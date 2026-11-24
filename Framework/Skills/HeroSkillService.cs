using System;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.Skills
{
  /// <summary>
  /// DOTA2 英雄技能加点核心业务服务：
  /// - 1 级自带 1 点技能点，每升 1 级获得 1 点（与天赋树完全独立）。
  /// - 主动技能 0→1 级学会法术，1→MaxLevel 每次加点调用 Pathos 原生 Engine.LearnSpell 升星（★~★★★）。
  /// - 被动技能 0→1 级授予原生可见专长，1→MaxLevel 由独立技能等级驱动数值（彻底脱离武器/学派熟练度）。
  /// - 所有等级进度以 dota_skill_* 写入 Character.AcquiredTalentSet，100% 兼容原生存档。
  /// </summary>
  public static class HeroSkillService
  {
    public static void EnsureHooksRegistered()
    {
      DotaHookBus.RegisterAfterLevelUp("dota.skills.level_up_notify", HandleAfterLevelUp);
    }

    public static bool HasSkillBuild(Character Character)
      => HeroSkillRegistry.TryGetBuild(Character, out _);

    public static int GetSkillLevel(Character Character, string SkillId)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SkillId))
        return 0;

      if (!HeroSkillRegistry.TryGetSlot(SkillId, out var Slot))
        return 0;

      return GetSkillLevel(Character, Slot);
    }

    public static int GetSkillLevel(Character Character, DotaSkillSlot Slot)
    {
      if (Character == null || Slot == null)
        return 0;

      int Stored = DotaCharacterStorage.GetSkillLevel(Character, Slot.Id);
      if (Slot.Kind == DotaSkillSlotKind.Active && Slot.Spells != null && Slot.Spells.Count > 0)
      {
        int NativeLevel = PathosEngineBridge.GetSpellMasteryLevel(Character, Slot.Spells[0]);
        if (NativeLevel > Stored)
          Stored = NativeLevel;
      }

      return Math.Clamp(Stored, 0, Slot.MaxLevel);
    }

    /// <summary>
    /// 统计该英雄已消耗的全部技能点总数。
    /// </summary>
    public static int GetTotalSpentSkillPoints(Character Character)
    {
      if (Character == null || !HeroSkillRegistry.TryGetBuild(Character, out var Build))
        return 0;

      int Spent = 0;
      foreach (var Slot in Build.Slots)
        Spent += GetSkillLevel(Character, Slot);

      return Spent;
    }

    /// <summary>
    /// 计算该英雄当前可用于加点的剩余技能点数（不超过所有未满槽位剩余可点上限之和）。
    /// </summary>
    public static int GetAvailableSkillPoints(Character Character)
    {
      if (Character == null || !HeroSkillRegistry.TryGetBuild(Character, out var Build))
        return 0;

      int TotalMax = 0;
      int TotalSpent = 0;
      foreach (var Slot in Build.Slots)
      {
        TotalMax += Slot.MaxLevel;
        TotalSpent += GetSkillLevel(Character, Slot);
      }

      int RemainingCapacity = Math.Max(0, TotalMax - TotalSpent);
      if (RemainingCapacity <= 0)
        return 0;

      int EarnedPoints = Math.Max(1, Character.Level);
      return Math.Min(RemainingCapacity, Math.Max(0, EarnedPoints - TotalSpent));
    }

    /// <summary>
    /// 判断指定技能槽位当前是否可升级，并输出下一级所需的英雄等级门槛。
    /// </summary>
    public static DotaSkillUpgradeState GetUpgradeState(
      Character Character,
      DotaSkillSlot Slot,
      out int RequiredHeroLevel)
    {
      RequiredHeroLevel = 1;
      if (Character == null || Slot == null)
        return DotaSkillUpgradeState.NoPoints;

      int CurrentLv = GetSkillLevel(Character, Slot);
      if (CurrentLv >= Slot.MaxLevel)
        return DotaSkillUpgradeState.Maxed;

      int NextLv = CurrentLv + 1;
      RequiredHeroLevel = Slot.GetRequiredHeroLevel(NextLv);
      if (Character.Level < RequiredHeroLevel)
        return DotaSkillUpgradeState.LevelLocked;

      if (GetAvailableSkillPoints(Character) <= 0)
        return DotaSkillUpgradeState.NoPoints;

      return DotaSkillUpgradeState.Available;
    }

    /// <summary>
    /// 返回主动技能当前原生星级文本（例如 "0★" / "★" / "★★" / "★★★"；未学会返回空字符串）。
    /// </summary>
    public static string GetActiveStarsBadge(Character Character, DotaSkillSlot Slot)
    {
      if (Character == null || Slot == null || Slot.Kind != DotaSkillSlotKind.Active)
        return string.Empty;

      int SkillLv = GetSkillLevel(Character, Slot);
      if (SkillLv <= 0)
        return string.Empty;

      int Stars = Math.Max(0, SkillLv - 1);
      if (Slot.Spells != null && Slot.Spells.Count > 0)
      {
        int NativeLevel = PathosEngineBridge.GetSpellMasteryLevel(Character, Slot.Spells[0]);
        if (NativeLevel >= 1)
          Stars = Math.Max(Stars, NativeLevel - 1);
      }

      return Stars > 0 ? new string('★', Stars) : DotaLocalization.T("ui.skill.learned_short");
    }

    /// <summary>
    /// 执行一次技能升级加点（不可撤销）：
    /// - 写入 dota_skill_* 持久化标记
    /// - 主动技能同步调用 Engine.LearnSpell（0→1 学会，1→N 原生升星）
    /// - 被动技能 0→1 级授予原生可见专长
    /// - 触发自定义 OnUpgrade 回调并写入日志
    /// </summary>
    public static bool TryUpgradeSkill(object Engine, Character Character, DotaSkillSlot Slot)
    {
      if (Engine == null || Character == null || Slot == null)
        return false;

      if (GetUpgradeState(Character, Slot, out _) != DotaSkillUpgradeState.Available)
        return false;

      int CurrentLv = GetSkillLevel(Character, Slot);
      int NextLv = CurrentLv + 1;
      if (NextLv < 1 || NextLv > Slot.MaxLevel)
        return false;

      if (!DotaCharacterStorage.SetSkillLevel(Character, Slot.Id, NextLv))
        return false;

      if (Slot.Kind == DotaSkillSlotKind.Active && Slot.Spells != null)
      {
        foreach (var Spell in Slot.Spells)
        {
          if (Spell != null)
            PathosEngineBridge.LearnSpell(Engine, Character, Spell);
        }
      }
      else if (Slot.Kind == DotaSkillSlotKind.Passive && NextLv == 1 && Slot.PassiveProperty != null)
      {
        if (!PathosEngineBridge.HasAcquiredTalent(Character, Slot.PassiveProperty))
          PathosEngineBridge.GainTalent(Engine, Character, Slot.PassiveProperty);
      }

      try
      {
        Slot.OnUpgrade?.Invoke(Engine, Character, NextLv);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"Skill '{Slot.Id}' OnUpgrade failed: {Ex}");
      }

      Glyph LogIcon = Slot.IconGlyph ?? (HeroSkillRegistry.TryGetBuild(Character, out var Build) ? Build.IconGlyph : null);
      string StarNote = Slot.Kind == DotaSkillSlotKind.Active && NextLv > 1
        ? DotaLocalization.Format("ui.skill.star_note", new string('★', NextLv - 1))
        : string.Empty;

      string ActionLabel = NextLv == 1 && Slot.Kind != DotaSkillSlotKind.AttributeBonus
        ? DotaLocalization.Format("ui.skill.log.learned", Slot.Hotkey, Slot.Title)
        : DotaLocalization.Format("ui.skill.log.upgraded", Slot.Hotkey, Slot.Title, NextLv, Slot.MaxLevel, StarNote);

      PathosEngineBridge.WriteInternalLog(Engine, Character, LogIcon, ActionLabel);
      return true;
    }

    private static void HandleAfterLevelUp(LevelUpEventArgs Args)
    {
      if (Args?.Character == null || Args.NewLevel <= Args.PreviousLevel)
        return;

      if (!HeroSkillRegistry.TryGetBuild(Args.Character, out var Build))
        return;

      int Points = GetAvailableSkillPoints(Args.Character);
      if (Points <= 0)
        return;

      bool UnlockedUltimate = false;
      foreach (var Slot in Build.Slots)
      {
        if (!string.Equals(Slot.Hotkey, "R", StringComparison.OrdinalIgnoreCase))
          continue;

        for (int Lv = 1; Lv <= Slot.MaxLevel; Lv++)
        {
          int Req = Slot.GetRequiredHeroLevel(Lv);
          if (Args.PreviousLevel < Req && Args.NewLevel >= Req)
          {
            UnlockedUltimate = true;
            break;
          }
        }
      }

      string Msg = UnlockedUltimate
        ? DotaLocalization.Format("ui.skill.notify.ultimate", Points)
        : DotaLocalization.Format("ui.skill.notify.point", Points);

      PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, Build.IconGlyph, Msg);
    }
  }
}
