using System;
using System.Collections.Concurrent;

namespace Pathos.DotaHeroes.Framework.Skills
{
  /// <summary>
  /// 全局英雄技能加点注册表：
  /// - 按职业名（Class.Name）索引各英雄的 Q/W/E/R/+ 技能加点配置。
  /// - 技能等级由 DotaCharacterStorage（Character.Dialogue.Tag）持久化，主动技能星级由原生 Knowledge.MasteryList 保存，
  ///   不注册任何 Codex.Properties。
  /// </summary>
  public static class HeroSkillRegistry
  {
    private static readonly ConcurrentDictionary<string, HeroSkillBuild> BuildsByClass =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DotaSkillSlot> SlotsById =
      new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个英雄的技能加点配置。
    /// 必须在 IDotaHeroDefinition.Register 阶段调用（Campaign 构建期）。
    /// </summary>
    public static void Register(DotaHeroContext Ctx, HeroSkillBuild Build)
    {
      if (Ctx == null)
        throw new ArgumentNullException(nameof(Ctx));
      if (Build == null)
        throw new ArgumentNullException(nameof(Build));

      foreach (var Slot in Build.Slots)
        SlotsById[Slot.Id] = Slot;

      BuildsByClass[Build.ClassName] = Build;
      HeroSkillService.EnsureHooksRegistered();
    }

    public static bool TryGetBuild(Character Character, out HeroSkillBuild Build)
    {
      Build = null;
      var ClassName = Character?.Class?.Name;
      if (string.IsNullOrEmpty(ClassName))
        return false;

      return BuildsByClass.TryGetValue(ClassName, out Build);
    }

    public static bool TryGetSlot(string SkillId, out DotaSkillSlot Slot)
    {
      Slot = null;
      if (string.IsNullOrWhiteSpace(SkillId))
        return false;

      return SlotsById.TryGetValue(SkillId, out Slot);
    }
  }
}
