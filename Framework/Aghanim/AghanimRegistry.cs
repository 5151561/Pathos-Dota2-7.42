using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Pathos.DotaHeroes.Framework.Aghanim
{
  /// <summary>
  /// 按职业名与关联技能索引所有英雄的阿哈利姆神杖/魔晶定义。
  /// </summary>
  public static class AghanimRegistry
  {
    private static readonly ConcurrentDictionary<string, HeroAghanimSpec> ByClass =
      new(StringComparer.OrdinalIgnoreCase);

    public static void Register(HeroAghanimSpec Spec)
    {
      if (Spec == null || string.IsNullOrWhiteSpace(Spec.ClassName))
        return;

      ByClass[Spec.ClassName] = Spec;
    }

    public static bool TryGetSpec(Character Character, out HeroAghanimSpec Spec)
    {
      Spec = null;
      string ClassName = Character?.Class?.Name;
      return !string.IsNullOrWhiteSpace(ClassName) && ByClass.TryGetValue(ClassName, out Spec);
    }

    public static bool TryGetSpec(string ClassName, out HeroAghanimSpec Spec)
    {
      Spec = null;
      return !string.IsNullOrWhiteSpace(ClassName) && ByClass.TryGetValue(ClassName, out Spec);
    }

    public static bool HasHeroSpec(Character Character) => TryGetSpec(Character, out _);

    /// <summary>
    /// 按法术名查找关联的神杖与魔晶升级（用于在技能帮助面板自动渲染神杖/魔晶区块）。
    /// </summary>
    public static (AghanimUpgradeSpec Scepter, AghanimUpgradeSpec Shard) FindBySpell(string SpellName)
    {
      if (string.IsNullOrWhiteSpace(SpellName))
        return (null, null);

      AghanimUpgradeSpec Scepter = null;
      AghanimUpgradeSpec Shard = null;
      foreach (var Entry in ByClass.Values)
      {
        if (Scepter == null && Entry.Scepter != null &&
            string.Equals(Entry.Scepter.AffectedSpellName, SpellName, StringComparison.OrdinalIgnoreCase))
          Scepter = Entry.Scepter;

        if (Shard == null && Entry.Shard != null &&
            string.Equals(Entry.Shard.AffectedSpellName, SpellName, StringComparison.OrdinalIgnoreCase))
          Shard = Entry.Shard;
      }

      return (Scepter, Shard);
    }

    /// <summary>
    /// 按被动帮助 ID 查找关联的神杖与魔晶升级。
    /// </summary>
    public static (AghanimUpgradeSpec Scepter, AghanimUpgradeSpec Shard) FindByPassive(string PassiveId)
    {
      if (string.IsNullOrWhiteSpace(PassiveId))
        return (null, null);

      AghanimUpgradeSpec Scepter = null;
      AghanimUpgradeSpec Shard = null;
      foreach (var Entry in ByClass.Values)
      {
        if (Scepter == null && Entry.Scepter != null &&
            string.Equals(Entry.Scepter.AffectedPassiveId, PassiveId, StringComparison.OrdinalIgnoreCase))
          Scepter = Entry.Scepter;

        if (Shard == null && Entry.Shard != null &&
            string.Equals(Entry.Shard.AffectedPassiveId, PassiveId, StringComparison.OrdinalIgnoreCase))
          Shard = Entry.Shard;
      }

      return (Scepter, Shard);
    }

    public static IEnumerable<HeroAghanimSpec> All => ByClass.Values;
  }
}
