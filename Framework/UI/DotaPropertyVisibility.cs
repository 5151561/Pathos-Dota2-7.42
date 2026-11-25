using System;
using System.Collections.Concurrent;

namespace Pathos.DotaHeroes.Framework.UI
{
  /// <summary>
  /// 原生 TalentPanel（含大师模式）与百科「天赋/特性」栏目的 Property 隐藏注册表。
  /// 目前只登记主动技能的短期状态（Transient-only，如剑刃风暴、无敌斩），避免被误展示为永久天赋；
  /// 由 <see cref="DotaHeroContext.RegisterSkillStatusProperty"/> 自动调用。
  /// 天赋树与技能等级不再占用 Property（见 <see cref="DotaCharacterStorage"/>），无需在此登记。
  /// </summary>
  public static class DotaPropertyVisibility
  {
    private static readonly ConcurrentDictionary<string, byte> TransientOnlyPropertyNames =
      new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 声明一个仅用于主动技能短期状态（Transient）的 Property，
    /// 使其在原生 TalentPanel（尤其是大师模式）与百科「天赋/特性」栏目中自动隐藏。
    /// </summary>
    public static void RegisterTransientOnlyProperty(Property Property)
    {
      if (Property == null || string.IsNullOrEmpty(Property.Name))
        return;

      TransientOnlyPropertyNames[Property.Name] = 0;
    }

    public static bool IsTransientOnlyProperty(Property Property)
    {
      var Name = Property?.Name;
      return !string.IsNullOrEmpty(Name) && TransientOnlyPropertyNames.ContainsKey(Name);
    }

    /// <summary>
    /// 判断指定 Property 是否应从原生 TalentPanel 与百科「天赋/特性」栏目中隐藏。
    /// </summary>
    public static bool IsHiddenFromTalentPanel(Property Property)
      => IsTransientOnlyProperty(Property);
  }
}
