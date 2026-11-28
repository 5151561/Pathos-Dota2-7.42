using System;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.Talents
{
  /// <summary>
  /// 天赋系统核心业务服务：
  /// - 查询角色已学天赋、节点状态、可用天赋点数（含满级后补点另一侧逻辑）。
  /// - 执行学习（不可洗点）：写入 DotaCharacterStorage（随存档持久化），结算 StatBonus 后触发 OnLearn 回调。
  /// - 自动订阅 SpellCostQuery（统一处理所有英雄的「减冷却 → 减蓝耗」规则）与 AfterLevelUp（升级解锁提示）。
  /// </summary>
  public static class TalentService
  {
    public static void EnsureHooksRegistered()
    {
      DotaHookBus.RegisterSpellCostQuery("dota.talents.spell_cost", HandleSpellCostQuery);
      DotaHookBus.RegisterAfterLevelUp("dota.talents.level_up_notify", HandleAfterLevelUp);
    }

    public static bool HasTalentTree(Character Character)
    {
      return TalentRegistry.TryGetTree(Character, out _);
    }

    public static bool HasTalent(Character Character, string TalentId)
    {
      if (Character == null || string.IsNullOrWhiteSpace(TalentId))
        return false;

      return DotaCharacterStorage.HasTalent(Character, TalentId);
    }

    public static TalentNodeState GetNodeState(Character Character, TalentTier Tier, TalentBranch Branch)
    {
      if (Character == null || Tier == null)
        return TalentNodeState.Locked;

      var Self = Tier.GetNode(Branch);
      var Opposite = Tier.GetOpposite(Branch);

      if (HasTalent(Character, Self.Id))
        return TalentNodeState.Learned;

      if (Character.Level < Tier.UnlockLevel)
        return TalentNodeState.Locked;

      bool HasOpposite = HasTalent(Character, Opposite.Id);
      if (!HasOpposite)
        return TalentNodeState.Available;

      return Character.Level >= Tier.DualUnlockLevel
        ? TalentNodeState.Available
        : TalentNodeState.OppositeBlocked;
    }

    /// <summary>
    /// 计算角色当前可分配的天赋点数（包含常规四层首点与高等级双修补点）。
    /// </summary>
    public static int GetAvailableTalentPoints(Character Character)
    {
      if (Character == null || !TalentRegistry.TryGetTree(Character, out var Tree))
        return 0;

      int Available = 0;
      foreach (var Tier in Tree.Tiers)
      {
        int LearnedInTier =
          (HasTalent(Character, Tier.Left.Id) ? 1 : 0) +
          (HasTalent(Character, Tier.Right.Id) ? 1 : 0);

        int MaxAllowedInTier = 0;
        if (Character.Level >= Tier.DualUnlockLevel)
          MaxAllowedInTier = 2;
        else if (Character.Level >= Tier.UnlockLevel)
          MaxAllowedInTier = 1;

        if (MaxAllowedInTier > LearnedInTier)
          Available += (MaxAllowedInTier - LearnedInTier);
      }

      return Available;
    }

    /// <summary>
    /// 学习指定层与分支的天赋（不可反悔 / 不可洗点）。
    /// </summary>
    public static bool TryLearnTalent(object Engine, Character Character, TalentTier Tier, TalentBranch Branch)
    {
      if (Engine == null || Character == null || Tier == null)
        return false;

      if (GetNodeState(Character, Tier, Branch) != TalentNodeState.Available)
        return false;

      var Node = Tier.GetNode(Branch);
      if (Node == null)
        return false;

      if (!DotaCharacterStorage.AddTalent(Character, Node.Id))
        return false;

      try
      {
        ApplyStatBonus(Character, Node.StatBonus);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"Talent '{Node.Id}' StatBonus failed: {Ex}");
      }

      try
      {
        Node.OnLearn?.Invoke(Engine, Character);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"Talent '{Node.Id}' OnLearn failed: {Ex}");
      }

      Glyph Icon = TalentRegistry.TryGetTree(Character, out var Tree) ? Tree.IconGlyph : null;
      string Detail = string.IsNullOrWhiteSpace(Node.Description)
        ? Node.Title
        : DotaLocalization.Format("ui.talent.log.learned_detail", Node.Title, Node.Description);
      PathosEngineBridge.WriteInternalLog(
        Engine,
        Character,
        Icon,
        DotaLocalization.Format("ui.talent.log.learned", Tier.UnlockLevel, Detail));

      return true;
    }

    /// <summary>
    /// 汇总角色已学天赋节点的声明式属性加成（<see cref="TalentNode.StatBonus"/>），供属性上限查询叠加。
    /// </summary>
    public static PathosStatLimits GetLearnedStatBonus(Character Character)
    {
      var Total = default(PathosStatLimits);
      if (Character == null || !TalentRegistry.TryGetTree(Character, out var Tree))
        return Total;

      foreach (var Tier in Tree.Tiers)
      {
        if (!Tier.Left.StatBonus.IsZero && HasTalent(Character, Tier.Left.Id))
          Total += Tier.Left.StatBonus;
        if (!Tier.Right.StatBonus.IsZero && HasTalent(Character, Tier.Right.Id))
          Total += Tier.Right.StatBonus;
      }

      return Total;
    }

    /// <summary>
    /// 学习瞬间结算属性加成：此时节点已写入存档，上限已包含本次加成，属性按 Min(上限, 当前 + 加成) 永久提升；
    /// 力量加成按每点 <see cref="DotaMechanicsMapper.HpPerStrengthPoint"/> HP 同步提升最大与当前生命（当前值夹取到新上限）。
    /// </summary>
    private static void ApplyStatBonus(Character Character, PathosStatLimits Bonus)
    {
      if (Character == null || Bonus.IsZero)
        return;

      DotaStats.RaisePermanentClamped(Character, DotaStat.Str, Bonus.Str);
      DotaStats.RaisePermanentClamped(Character, DotaStat.Dex, Bonus.Dex);
      DotaStats.RaisePermanentClamped(Character, DotaStat.Con, Bonus.Con);
      DotaStats.RaisePermanentClamped(Character, DotaStat.Int, Bonus.Int);
      DotaStats.RaisePermanentClamped(Character, DotaStat.Wis, Bonus.Wis);
      DotaStats.RaisePermanentClamped(Character, DotaStat.Cha, Bonus.Cha);

      if (Bonus.Str > 0)
        DotaLife.AdjustMaxByStrength(Character, Bonus.Str, DotaLifeAdjustMode.RaiseBoth);
    }

    private static void HandleSpellCostQuery(SpellCostQueryEventArgs Args)
    {
      if (Args?.Character == null || Args.Spell == null || Args.ManaCost <= 0)
        return;

      if (!TalentRegistry.TryGetTree(Args.Character, out var Tree))
        return;

      // 乘算聚合当前角色所有已学天赋中匹配该技能的减冷却（→减蓝耗）比例
      float RemainingMultiplier = 1.0f;
      bool HasAnyRule = false;

      foreach (var Tier in Tree.Tiers)
      {
        AccumulateNodeManaReduction(Args.Character, Tier.Left, Args.Spell, ref RemainingMultiplier, ref HasAnyRule);
        AccumulateNodeManaReduction(Args.Character, Tier.Right, Args.Spell, ref RemainingMultiplier, ref HasAnyRule);
      }

      if (!HasAnyRule)
        return;

      float CombinedCdrRatio = 1.0f - RemainingMultiplier;
      Args.ManaCost = DotaMechanicsMapper.MapCooldownReductionToManaCost(Args.ManaCost, CombinedCdrRatio);
    }

    /// <summary>
    /// 汇总角色所有已学天赋对指定法术的冷却缩减（固定减少秒数 + 乘算百分比倍率，百分比缩减封顶 60%）。
    /// </summary>
    public static (float FlatSeconds, float PercentMultiplier) GetCooldownReduction(Character Character, Spell Spell)
    {
      if (Character == null || Spell == null || !TalentRegistry.TryGetTree(Character, out var Tree))
        return (0.0f, 1.0f);

      float FlatSeconds = 0.0f;
      float RemainingMultiplier = 1.0f;

      foreach (var Tier in Tree.Tiers)
      {
        AccumulateNodeCooldownReduction(Character, Tier.Left, Spell, ref FlatSeconds, ref RemainingMultiplier);
        AccumulateNodeCooldownReduction(Character, Tier.Right, Spell, ref FlatSeconds, ref RemainingMultiplier);
      }

      return (FlatSeconds, Math.Max(0.40f, RemainingMultiplier));
    }

    private static void AccumulateNodeCooldownReduction(
      Character Character,
      TalentNode Node,
      Spell Spell,
      ref float FlatSeconds,
      ref float RemainingMultiplier)
    {
      if (Node == null || Node.CooldownReductions == null || Node.CooldownReductions.Count == 0)
        return;

      if (!HasTalent(Character, Node.Id))
        return;

      foreach (var Rule in Node.CooldownReductions)
      {
        if (!Rule.Matches(Spell))
          continue;

        if (Rule.FlatSeconds > 0.0f)
          FlatSeconds += Rule.FlatSeconds;
        if (Rule.PercentRatio > 0.0f)
          RemainingMultiplier *= (1.0f - Rule.PercentRatio);
      }
    }

    private static void AccumulateNodeManaReduction(
      Character Character,
      TalentNode Node,
      Spell Spell,
      ref float RemainingMultiplier,
      ref bool HasAnyRule)
    {
      if (Node == null || Node.ManaReductions == null || Node.ManaReductions.Count == 0)
        return;

      if (!HasTalent(Character, Node.Id))
        return;

      foreach (var Rule in Node.ManaReductions)
      {
        if (Rule.CdrRatio > 0.0f && Rule.Matches(Spell))
        {
          RemainingMultiplier *= (1.0f - Rule.CdrRatio);
          HasAnyRule = true;
        }
      }
    }

    private static void HandleAfterLevelUp(LevelUpEventArgs Args)
    {
      if (Args?.Character == null || Args.NewLevel <= Args.PreviousLevel)
        return;

      if (!TalentRegistry.TryGetTree(Args.Character, out var Tree))
        return;

      bool Unlocked = false;

      foreach (var Tier in Tree.Tiers)
      {
        if ((Args.PreviousLevel < Tier.UnlockLevel && Args.NewLevel >= Tier.UnlockLevel) ||
            (Args.PreviousLevel < Tier.DualUnlockLevel && Args.NewLevel >= Tier.DualUnlockLevel))
        {
          Unlocked = true;
          break;
        }
      }

      if (!Unlocked)
        return;

      int Points = GetAvailableTalentPoints(Args.Character);
      if (Points <= 0)
        return;

      string Msg = DotaLocalization.Format("ui.talent.notify.point", Points);
      PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, Tree.IconGlyph, Msg);
    }
  }
}
