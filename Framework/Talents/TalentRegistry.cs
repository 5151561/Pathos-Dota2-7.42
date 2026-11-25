using System;
using System.Collections.Concurrent;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework.Talents
{
  /// <summary>
  /// 全局英雄天赋树注册表：
  /// - 按职业名（Class.Name）索引各英雄天赋树与节点。
  /// - 已学天赋由 <see cref="DotaCharacterStorage"/>（Character.Dialogue.Tag）随原生存档持久化，
  ///   不注册任何 Codex.Properties，不占用 64 位 Property 名额。
  /// </summary>
  public static class TalentRegistry
  {
    private static readonly ConcurrentDictionary<string, HeroTalentTree> TreesByClass =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, TalentNode> NodesById =
      new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册一棵英雄天赋树。必须在 IDotaHeroDefinition.Register 阶段调用（Campaign 构建期）。
    /// </summary>
    public static void Register(DotaHeroContext Ctx, HeroTalentTree Tree)
    {
      if (Ctx == null)
        throw new ArgumentNullException(nameof(Ctx));
      if (Tree == null)
        throw new ArgumentNullException(nameof(Tree));

      foreach (var Tier in Tree.Tiers)
      {
        NodesById[Tier.Left.Id] = Tier.Left;
        NodesById[Tier.Right.Id] = Tier.Right;
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
    /// 判断指定 Property 是否应从原生 TalentPanel 与百科「天赋/特性」列表中隐藏（主动技能短期状态）。
    /// </summary>
    public static bool IsHiddenFromTalentPanel(Property Property)
      => DotaPropertyVisibility.IsHiddenFromTalentPanel(Property);
  }
}
