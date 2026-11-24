using System;
using System.Collections.Generic;

namespace Pathos.DotaHeroes.Framework.Talents
{
  public enum TalentBranch
  {
    Left,
    Right
  }

  public enum TalentNodeState
  {
    Locked,
    Available,
    Learned,
    OppositeBlocked
  }

  /// <summary>
  /// 声明式「减冷却 → 减蓝耗」规则。
  /// 当 SpellName 为 null 或空字符串时表示对全部法术生效；否则仅对指定法术生效（忽略大小写）。
  /// </summary>
  public readonly struct SpellManaReductionRule
  {
    public SpellManaReductionRule(string SpellName, float CdrRatio)
    {
      this.SpellName = SpellName;
      this.CdrRatio = Math.Clamp(CdrRatio, 0.0f, 1.0f);
    }

    public string SpellName { get; }
    public float CdrRatio { get; }

    public bool Matches(Spell Spell)
    {
      if (Spell == null)
        return false;

      if (string.IsNullOrWhiteSpace(SpellName))
        return true;

      return string.Equals(Spell.Name, SpellName, StringComparison.OrdinalIgnoreCase);
    }
  }

  /// <summary>
  /// 单个天赋节点定义。
  /// </summary>
  public sealed class TalentNode
  {
    public TalentNode(
      string Id,
      string Title,
      string Description,
      IReadOnlyList<SpellManaReductionRule> ManaReductions = null,
      Action<object, Character> OnLearn = null)
    {
      if (string.IsNullOrWhiteSpace(Id))
        throw new ArgumentException("TalentNode Id cannot be null or empty.", nameof(Id));

      this.Id = Id;
      this.Title = Title ?? Id;
      this.Description = Description ?? string.Empty;
      this.ManaReductions = ManaReductions ?? Array.Empty<SpellManaReductionRule>();
      this.OnLearn = OnLearn;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<SpellManaReductionRule> ManaReductions { get; }
    public Action<object, Character> OnLearn { get; }

    /// <summary>
    /// 对应 Pathos 原生 Codex.Properties 的内部标记（用于随 Character.AcquiredTalentSet 自动存档与读档）。
    /// </summary>
    internal Property BackingProperty { get; set; }
  }

  /// <summary>
  /// 单层天赋定义（左右二选一，达到 DualUnlockLevel 后可补点另一侧）。
  /// </summary>
  public sealed class TalentTier
  {
    public TalentTier(int TierIndex, int UnlockLevel, int DualUnlockLevel, TalentNode Left, TalentNode Right)
    {
      this.TierIndex = TierIndex;
      this.UnlockLevel = UnlockLevel;
      this.DualUnlockLevel = DualUnlockLevel;
      this.Left = Left ?? throw new ArgumentNullException(nameof(Left));
      this.Right = Right ?? throw new ArgumentNullException(nameof(Right));
    }

    public int TierIndex { get; }
    public int UnlockLevel { get; }
    public int DualUnlockLevel { get; }
    public TalentNode Left { get; }
    public TalentNode Right { get; }

    public TalentNode GetNode(TalentBranch Branch) => Branch == TalentBranch.Left ? Left : Right;
    public TalentNode GetOpposite(TalentBranch Branch) => Branch == TalentBranch.Left ? Right : Left;
  }

  /// <summary>
  /// 单个 DOTA2 英雄的完整 4 层天赋树定义。
  /// DOTA2 原版解锁阶梯：Lv 10 / 15 / 20 / 25（四层二选一），Lv 27 / 28 / 29 / 30（依次补点另一侧）。
  /// </summary>
  public sealed class HeroTalentTree
  {
    public static readonly int[] DefaultUnlockLevels = { 10, 15, 20, 25 };
    public static readonly int[] DefaultDualUnlockLevels = { 27, 28, 29, 30 };

    public HeroTalentTree(
      string ClassName,
      string HeroDisplayName,
      IReadOnlyList<TalentTier> Tiers,
      Glyph IconGlyph = null)
    {
      if (string.IsNullOrWhiteSpace(ClassName))
        throw new ArgumentException("ClassName cannot be null or empty.", nameof(ClassName));

      this.ClassName = ClassName;
      this.HeroDisplayName = string.IsNullOrWhiteSpace(HeroDisplayName) ? ClassName : HeroDisplayName;
      this.Tiers = Tiers ?? throw new ArgumentNullException(nameof(Tiers));
      this.IconGlyph = IconGlyph;
    }

    public string ClassName { get; }
    public string HeroDisplayName { get; }
    public IReadOnlyList<TalentTier> Tiers { get; }
    public Glyph IconGlyph { get; }

    public static HeroTalentTree CreateStandard(
      string ClassName,
      string HeroDisplayName,
      Glyph IconGlyph,
      TalentNode T1Left, TalentNode T1Right,
      TalentNode T2Left, TalentNode T2Right,
      TalentNode T3Left, TalentNode T3Right,
      TalentNode T4Left, TalentNode T4Right)
    {
      var Tiers = new[]
      {
        new TalentTier(0, DefaultUnlockLevels[0], DefaultDualUnlockLevels[0], T1Left, T1Right),
        new TalentTier(1, DefaultUnlockLevels[1], DefaultDualUnlockLevels[1], T2Left, T2Right),
        new TalentTier(2, DefaultUnlockLevels[2], DefaultDualUnlockLevels[2], T3Left, T3Right),
        new TalentTier(3, DefaultUnlockLevels[3], DefaultDualUnlockLevels[3], T4Left, T4Right),
      };

      return new HeroTalentTree(ClassName, HeroDisplayName, Tiers, IconGlyph);
    }
  }
}
