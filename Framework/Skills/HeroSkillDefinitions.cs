using System;
using System.Collections.Generic;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.Skills
{
  public enum DotaSkillSlotKind
  {
    Active,
    Passive,
    AttributeBonus
  }

  public enum DotaSkillUpgradeState
  {
    Available,
    Maxed,
    NoPoints,
    LevelLocked
  }

  /// <summary>
  /// 单个 DOTA2 英雄技能加点槽位定义（Q / W / E / R / 黄点）。
  /// - Active：0→1 级通过 Engine.LearnSpell 学会法术，1→MaxLevel 每次加点原生升星（★~★★★）并叠加英雄自定义数值增幅。
  /// - Passive：0→1 级通过 Engine.GainTalent 授予原生可见被动天赋，1→MaxLevel 由独立技能等级直接驱动被动数值。
  /// - AttributeBonus：全属性黄点（默认 7 级上限，每级永久全六维 +1）。
  /// </summary>
  public sealed class DotaSkillSlot
  {
    public static readonly int[] BasicUnlockLevels = { 1, 3, 5, 7 };
    public static readonly int[] UltimateUnlockLevels = { 6, 12, 18 };
    public static readonly int[] AttributeBonusUnlockLevels = { 1, 1, 1, 1, 1, 1, 1 };

    private readonly string StaticTitle;
    private readonly Func<string> TitleResolver;

    public DotaSkillSlot(
      string Id,
      string Hotkey,
      string Title,
      DotaSkillSlotKind Kind,
      int MaxLevel,
      IReadOnlyList<int> UnlockLevels,
      Glyph IconGlyph = null,
      IReadOnlyList<Spell> Spells = null,
      Property PassiveProperty = null,
      Func<int, string> DescribeLevel = null,
      Action<object, Character, int> OnUpgrade = null,
      Func<string> TitleResolver = null)
    {
      if (string.IsNullOrWhiteSpace(Id))
        throw new ArgumentException("DotaSkillSlot Id cannot be null or empty.", nameof(Id));
      if (MaxLevel <= 0)
        throw new ArgumentOutOfRangeException(nameof(MaxLevel), "MaxLevel must be positive.");

      this.Id = Id;
      this.Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? "•" : Hotkey;
      this.StaticTitle = string.IsNullOrWhiteSpace(Title) ? Id : Title;
      this.TitleResolver = TitleResolver;
      this.Kind = Kind;
      this.MaxLevel = MaxLevel;
      this.UnlockLevels = UnlockLevels ?? BuildDefaultUnlockLevels(MaxLevel);
      this.IconGlyph = IconGlyph;
      this.Spells = Spells ?? Array.Empty<Spell>();
      this.PassiveProperty = PassiveProperty;
      this.DescribeLevel = DescribeLevel;
      this.OnUpgrade = OnUpgrade;
    }

    public string Id { get; }
    public string Hotkey { get; }
    public string Title => TitleResolver?.Invoke() ?? StaticTitle;
    public DotaSkillSlotKind Kind { get; }
    public int MaxLevel { get; }
    public IReadOnlyList<int> UnlockLevels { get; }
    public Glyph IconGlyph { get; }
    public IReadOnlyList<Spell> Spells { get; }
    public Property PassiveProperty { get; }
    public Func<int, string> DescribeLevel { get; }
    public Action<object, Character, int> OnUpgrade { get; }

    public int GetRequiredHeroLevel(int TargetSkillLevel)
    {
      if (TargetSkillLevel <= 0)
        return 1;

      int Idx = Math.Clamp(TargetSkillLevel - 1, 0, UnlockLevels.Count - 1);
      return Math.Max(1, UnlockLevels[Idx]);
    }

    public static DotaSkillSlot CreateActive(
      string Id,
      string Hotkey,
      string Title,
      Glyph IconGlyph,
      Spell Spell,
      Func<int, string> DescribeLevel = null,
      bool IsUltimate = false,
      Action<object, Character, int> OnUpgrade = null,
      Func<string> TitleResolver = null)
    {
      return CreateActiveGroup(
        Id,
        Hotkey,
        Title,
        IconGlyph,
        Spell != null ? new[] { Spell } : Array.Empty<Spell>(),
        DescribeLevel,
        IsUltimate,
        OnUpgrade,
        TitleResolver);
    }

    public static DotaSkillSlot CreateActiveGroup(
      string Id,
      string Hotkey,
      string Title,
      Glyph IconGlyph,
      IReadOnlyList<Spell> Spells,
      Func<int, string> DescribeLevel = null,
      bool IsUltimate = false,
      Action<object, Character, int> OnUpgrade = null,
      Func<string> TitleResolver = null)
    {
      return new DotaSkillSlot(
        Id: Id,
        Hotkey: Hotkey,
        Title: Title,
        Kind: DotaSkillSlotKind.Active,
        MaxLevel: IsUltimate ? 3 : 4,
        UnlockLevels: IsUltimate ? UltimateUnlockLevels : BasicUnlockLevels,
        IconGlyph: IconGlyph,
        Spells: Spells,
        PassiveProperty: null,
        DescribeLevel: DescribeLevel,
        OnUpgrade: OnUpgrade,
        TitleResolver: TitleResolver);
    }

    public static DotaSkillSlot CreatePassive(
      string Id,
      string Hotkey,
      string Title,
      Glyph IconGlyph,
      Property PassiveProperty,
      Func<int, string> DescribeLevel = null,
      bool IsUltimate = false,
      Action<object, Character, int> OnUpgrade = null,
      Func<string> TitleResolver = null)
    {
      return new DotaSkillSlot(
        Id: Id,
        Hotkey: Hotkey,
        Title: Title,
        Kind: DotaSkillSlotKind.Passive,
        MaxLevel: IsUltimate ? 3 : 4,
        UnlockLevels: IsUltimate ? UltimateUnlockLevels : BasicUnlockLevels,
        IconGlyph: IconGlyph,
        Spells: null,
        PassiveProperty: PassiveProperty,
        DescribeLevel: DescribeLevel,
        OnUpgrade: OnUpgrade,
        TitleResolver: TitleResolver);
    }

    /// <summary>
    /// 创建标准 DOTA2 7.42 全属性黄点槽位（上限 7 级，每级永久提升全六维 +1，受 LimitForm 上限保护）。
    /// </summary>
    public static DotaSkillSlot CreateStandardAttributeBonus(string HeroId, Glyph IconGlyph = null)
    {
      return new DotaSkillSlot(
        Id: $"{HeroId}.attr_bonus",
        Hotkey: "+",
        Title: "Attribute Bonus",
        Kind: DotaSkillSlotKind.AttributeBonus,
        MaxLevel: 7,
        UnlockLevels: AttributeBonusUnlockLevels,
        IconGlyph: IconGlyph,
        DescribeLevel: Lv => Lv <= 0
          ? DotaLocalization.T("ui.skill.attr_bonus.desc_zero")
          : DotaLocalization.Format("ui.skill.attr_bonus.desc_lv", Lv),
        OnUpgrade: ApplyAllAttributesBonus,
        TitleResolver: () => DotaLocalization.T("ui.skill.attr_bonus.title"));
    }

    private static readonly string[] AllAttributeNames =
    {
      "strength",
      "dexterity",
      "constitution",
      "intelligence",
      "wisdom",
      "charisma"
    };

    private static void ApplyAllAttributesBonus(object Engine, Character Character, int NewLevel)
    {
      if (Character == null)
        return;

      int Increased = 0;
      foreach (var Attr in AllAttributeNames)
      {
        int Cur = PathosEngineBridge.GetCurrentAbilityScore(Character, Attr);
        int Limit = PathosEngineBridge.GetLimitAbilityScore(Character, Attr);
        if (Cur < Limit && PathosEngineBridge.SetPermanentAbilityScore(Character, Attr, Cur + 1))
          Increased++;
      }

      if (Increased > 0)
      {
        PathosEngineBridge.WriteInternalLog(
          Engine,
          Character,
          null,
          DotaLocalization.Format("ui.skill.attr_bonus.log", NewLevel, Increased));
      }
    }

    private static int[] BuildDefaultUnlockLevels(int MaxLevel)
    {
      var Levels = new int[MaxLevel];
      for (int i = 0; i < MaxLevel; i++)
        Levels[i] = 1 + i * 2;
      return Levels;
    }
  }

  /// <summary>
  /// 单个 DOTA2 英雄的完整技能加点配置（Q / W / E / R + 全属性黄点）。
  /// </summary>
  public sealed class HeroSkillBuild
  {
    private readonly string StaticHeroDisplayName;
    private readonly Func<string> HeroDisplayNameResolver;

    public HeroSkillBuild(
      string ClassName,
      string HeroDisplayName,
      IReadOnlyList<DotaSkillSlot> Slots,
      Glyph IconGlyph = null,
      Func<string> HeroDisplayNameResolver = null)
    {
      if (string.IsNullOrWhiteSpace(ClassName))
        throw new ArgumentException("ClassName cannot be null or empty.", nameof(ClassName));

      this.ClassName = ClassName;
      this.StaticHeroDisplayName = string.IsNullOrWhiteSpace(HeroDisplayName) ? ClassName : HeroDisplayName;
      this.HeroDisplayNameResolver = HeroDisplayNameResolver;
      this.Slots = Slots ?? throw new ArgumentNullException(nameof(Slots));
      this.IconGlyph = IconGlyph;
    }

    public string ClassName { get; }
    public string HeroDisplayName => HeroDisplayNameResolver?.Invoke() ?? StaticHeroDisplayName;
    public IReadOnlyList<DotaSkillSlot> Slots { get; }
    public Glyph IconGlyph { get; }

    public static HeroSkillBuild CreateStandard(
      string ClassName,
      string HeroDisplayName,
      string HeroId,
      Glyph IconGlyph,
      DotaSkillSlot SlotQ,
      DotaSkillSlot SlotW,
      DotaSkillSlot SlotE,
      DotaSkillSlot SlotR,
      Func<string> HeroDisplayNameResolver = null)
    {
      var Slots = new[]
      {
        SlotQ ?? throw new ArgumentNullException(nameof(SlotQ)),
        SlotW ?? throw new ArgumentNullException(nameof(SlotW)),
        SlotE ?? throw new ArgumentNullException(nameof(SlotE)),
        SlotR ?? throw new ArgumentNullException(nameof(SlotR)),
        DotaSkillSlot.CreateStandardAttributeBonus(HeroId, IconGlyph)
      };

      return new HeroSkillBuild(ClassName, HeroDisplayName, Slots, IconGlyph, HeroDisplayNameResolver);
    }
  }
}
