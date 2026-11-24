using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Pathos.DotaHeroes.Framework.Talents
{
  /// <summary>
  /// 全局英雄天赋树注册表：
  /// - 按职业名（Class.Name）索引各英雄天赋树。
  /// - 自动为每个天赋节点在 Codex.Properties 注册持久化标记（dota_talent_*），
  ///   由 Pathos 原生 AcquiredTalentSet 负责自动存档与读档，并在原生 TalentPanel 中隐藏。
  /// </summary>
  public static class TalentRegistry
  {
    public const string InternalPropertyPrefix = "dota_talent_";

    private static readonly ConcurrentDictionary<string, HeroTalentTree> TreesByClass =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, TalentNode> NodesById =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> InternalPropertyNames =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> TransientOnlyPropertyNames =
      new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一棵英雄天赋树，并为其全部节点生成 Codex 持久化 Property 标记。
    /// 必须在 IDotaHeroDefinition.Register 阶段调用（Campaign 构建期）。
    /// </summary>
    public static void Register(DotaHeroContext Ctx, HeroTalentTree Tree)
    {
      if (Ctx == null)
        throw new ArgumentNullException(nameof(Ctx));
      if (Tree == null)
        throw new ArgumentNullException(nameof(Tree));

      foreach (var Tier in Tree.Tiers)
      {
        BindNodeProperty(Ctx, Tree, Tier.Left);
        BindNodeProperty(Ctx, Tree, Tier.Right);
      }

      TreesByClass[Tree.ClassName] = Tree;
      TalentService.EnsureHooksRegistered();
    }

    public static bool TryGetTree(Character Character, out HeroTalentTree Tree)
    {
      Tree = null;
      var ClassName = Character?.Class?.Name;
      if (string.IsNullOrEmpty(ClassName))
        return false;

      return TreesByClass.TryGetValue(ClassName, out Tree);
    }

    public static bool TryGetNode(string TalentId, out TalentNode Node)
    {
      Node = null;
      if (string.IsNullOrWhiteSpace(TalentId))
        return false;

      return NodesById.TryGetValue(TalentId, out Node);
    }

    /// <summary>
    /// 声明一个仅用于主动技能短期状态（Transient）的 Property，
    /// 避免它在原生 TalentPanel（尤其是大师模式）与百科「天赋/特性」栏目中被误展示为永久天赋。
    /// </summary>
    public static void RegisterTransientOnlyProperty(Property Property)
    {
      if (Property == null || string.IsNullOrEmpty(Property.Name))
        return;

      TransientOnlyPropertyNames[Property.Name] = 0;
    }

    /// <summary>
    /// 判断指定 Property 是否为仅用于技能短期状态的属性。
    /// </summary>
    public static bool IsTransientOnlyProperty(Property Property)
    {
      var Name = Property?.Name;
      return !string.IsNullOrEmpty(Name) && TransientOnlyPropertyNames.ContainsKey(Name);
    }

    /// <summary>
    /// 判断指定 Property 是否为天赋树内部持久化标记（用于在原生 TalentPanel 中过滤隐藏）。
    /// </summary>
    public static bool IsInternalTalentProperty(Property Property)
    {
      var Name = Property?.Name;
      if (string.IsNullOrEmpty(Name))
        return false;

      return Name.StartsWith(InternalPropertyPrefix, StringComparison.OrdinalIgnoreCase) ||
             InternalPropertyNames.ContainsKey(Name);
    }

    /// <summary>
    /// 判断指定 Property 是否应从原生 TalentPanel 与百科「天赋/特性」列表中隐藏
    /// （包括内部天赋树持久化标记与主动技能短期状态）。
    /// </summary>
    public static bool IsHiddenFromTalentPanel(Property Property)
    {
      return IsInternalTalentProperty(Property) || IsTransientOnlyProperty(Property);
    }

    private static void BindNodeProperty(DotaHeroContext Ctx, HeroTalentTree Tree, TalentNode Node)
    {
      string PropName = InternalPropertyPrefix + SanitizeIdentifier(Node.Id);
      InternalPropertyNames[PropName] = 0;

      // 若同一 Campaign 内重复查找，优先复用已存在的 Property，避免重名注册异常
      var Existing = Ctx.Properties.List?.FirstOrDefault(P =>
        string.Equals(P?.Name, PropName, StringComparison.OrdinalIgnoreCase));

      if (Existing != null)
      {
        Node.BackingProperty = Existing;
      }
      else
      {
        Node.BackingProperty = Ctx.Properties.Add(P =>
        {
          P.Name = PropName;
          P.Description = $"[DOTA2 Talent] {Node.Title}: {Node.Description}";
          P.Glyph = Tree.IconGlyph;
        });
      }

      NodesById[Node.Id] = Node;
    }

    private static string SanitizeIdentifier(string RawId)
    {
      var Chars = new char[RawId.Length];
      for (int i = 0; i < RawId.Length; i++)
      {
        char C = char.ToLowerInvariant(RawId[i]);
        Chars[i] = (char.IsLetterOrDigit(C) || C == '_') ? C : '_';
      }
      return new string(Chars);
    }
  }
}
