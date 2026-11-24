using System;
using System.Collections.Concurrent;

namespace Pathos.DotaHeroes.Framework.UI
{
  /// <summary>
  /// 全局 Property 可见性注册表：
  /// 统一管理应从 Pathos 原生 TalentPanel（含大师模式）与百科「天赋/特性」栏目中隐藏的 Property，
  /// 包含两类：
  /// 1. 内部持久化标记（如天赋树节点的 dota_talent_*）。
  /// 2. 主动技能的短期状态（Transient-only，如剑刃风暴、无敌斩），避免被误展示为永久天赋。
  /// </summary>
  public static class DotaPropertyVisibility
  {
    private static readonly ConcurrentDictionary<string, byte> InternalPropertyNames =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> InternalPrefixes =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> TransientOnlyPropertyNames =
      new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一个内部持久化标记前缀（如 "dota_talent_"），匹配该前缀的 Property 会自动在原生天赋面板与百科中隐藏。
    /// </summary>
    public static void RegisterInternalPrefix(string Prefix)
    {
      if (string.IsNullOrWhiteSpace(Prefix))
        return;

      InternalPrefixes[Prefix] = 0;
    }

    /// <summary>
    /// 注册一个内部持久化标记名称，使其在原生天赋面板与百科中隐藏。
    /// </summary>
    public static void RegisterInternalPropertyName(string PropertyName)
    {
      if (string.IsNullOrWhiteSpace(PropertyName))
        return;

      InternalPropertyNames[PropertyName] = 0;
    }

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

    public static bool IsInternalProperty(Property Property)
    {
      var Name = Property?.Name;
      if (string.IsNullOrEmpty(Name))
        return false;

      if (InternalPropertyNames.ContainsKey(Name))
        return true;

      foreach (var Prefix in InternalPrefixes.Keys)
      {
        if (Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
          return true;
      }

      return false;
    }

    /// <summary>
    /// 判断指定 Property 是否应从原生 TalentPanel 与百科「天赋/特性」栏目中隐藏。
    /// </summary>
    public static bool IsHiddenFromTalentPanel(Property Property)
    {
      return IsInternalProperty(Property) || IsTransientOnlyProperty(Property);
    }
  }
}
