using System;
using System.Collections.Generic;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.Skills
{
  public enum DotaPrimaryAttribute
  {
    Strength,
    Agility,
    Intelligence,
    Universal
  }

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
  /// 技能面板「升级效果预览」的一行数值（如 持续 3 回合 → 6 回合）。
  /// Value(Lv) 返回该技能等级下的显示文本；返回 null 表示该等级没有此项（显示为「—」，两端都为 null 时整行隐藏）。
  /// 必须与 DescribeLevel / 施法共用同一套按等级函数，避免面板与实际结算不一致。
  /// </summary>
  public sealed class DotaSkillStat
  {
    public DotaSkillStat(Func<string> Label, Func<int, string> Value)
    {
      this.Label = Label ?? throw new ArgumentNullException(nameof(Label));
      this.Value = Value ?? throw new ArgumentNullException(nameof(Value));
    }

    public Func<string> Label { get; }
    public Func<int, string> Value { get; }

    /// <summary>以本地化 Key 作为标签声明一行预览数值。</summary>
    public static DotaSkillStat Of(string LabelKey, Func<int, string> Value)
      => new(() => DotaLocalization.T(LabelKey), Value);
  }

  /// <summary>
  /// 单个 DOTA2 英雄技能加点槽位定义（Q / W / E / R / 黄点）。
  /// - Active：0→1 级通过 Engine.LearnSpell 学会法术，1→MaxLevel 每次加点原生升星（★~★★★）并叠加英雄自定义数值增幅。
  /// - Passive：0→1 级通过 Engine.GainTalent 授予原生可见被动天赋，1→MaxLevel 由独立技能等级直接驱动被动数值。
  /// - AttributeBonus：全属性黄点（默认 7 级上限，按力/敏/智三系轮转提升，满级核心属性各 +2、CHA +1、主属性额外 +1）。
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
      Func<string> TitleResolver = null,
      DotaPrimaryAttribute PrimaryAttribute = DotaPrimaryAttribute.Universal,
      IReadOnlyList<DotaSkillStat> Stats = null)
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
      this.PrimaryAttribute = PrimaryAttribute;
      this.Stats = Stats ?? Array.Empty<DotaSkillStat>();
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
    public DotaPrimaryAttribute PrimaryAttribute { get; }

    /// <summary>技能面板升级预览数值行（当前等级 → 下一级）。</summary>
    public IReadOnlyList<DotaSkillStat> Stats { get; }

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
      Func<string> TitleResolver = null,
      IReadOnlyList<DotaSkillStat> Stats = null)
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
        TitleResolver,
        Stats);
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
      Func<string> TitleResolver = null,
      IReadOnlyList<DotaSkillStat> Stats = null)
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
        TitleResolver: TitleResolver,
        Stats: Stats);
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
      Func<string> TitleResolver = null,
      IReadOnlyList<DotaSkillStat> Stats = null)
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
        TitleResolver: TitleResolver,
        Stats: Stats);
    }

    /// <summary>
    /// 创建标准 DOTA2 7.42 全属性黄点槽位（上限 7 级，按力/敏/智三系轮转提升）：
    /// - Lv 1 / 4：STR +1, CON +1
    /// - Lv 2 / 5：DEX +1
    /// - Lv 3 / 6：INT +1, WIS +1
    /// - Lv 7：CHA +1 + 主属性 +1（总计 +12 点 Pathos 属性，契合 DOTA2 14 全属性 × 0.12 ≈ +2 的换算标尺）
    /// </summary>
    public static DotaSkillSlot CreateStandardAttributeBonus(
      string HeroId,
      Glyph IconGlyph = null,
      DotaPrimaryAttribute PrimaryAttribute = DotaPrimaryAttribute.Universal)
    {
      return new DotaSkillSlot(
        Id: $"{HeroId}.attr_bonus",
        Hotkey: "+",
        Title: "Attribute Bonus",
        Kind: DotaSkillSlotKind.AttributeBonus,
        MaxLevel: 7,
        UnlockLevels: AttributeBonusUnlockLevels,
        IconGlyph: IconGlyph,
        DescribeLevel: Lv => DescribeAttributeBonusLevel(Lv, PrimaryAttribute),
        OnUpgrade: (Engine, Character, NewLevel) => ApplyAttributeBonusForLevel(Engine, Character, NewLevel, PrimaryAttribute),
        TitleResolver: () => DotaLocalization.T("ui.skill.attr_bonus.title"),
        PrimaryAttribute: PrimaryAttribute,
        Stats: BuildAttributeBonusStats(PrimaryAttribute));
    }

    private static IReadOnlyList<DotaSkillStat> BuildAttributeBonusStats(DotaPrimaryAttribute PrimaryAttribute)
    {
      string[] Attrs = { "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" };
      var Result = new DotaSkillStat[Attrs.Length];
      for (int i = 0; i < Attrs.Length; i++)
      {
        string Attr = Attrs[i];
        Result[i] = new DotaSkillStat(
          () => GetLocalizedAttributeName(Attr),
          Lv =>
          {
            int Total = 0;
            for (int L = 1; L <= Math.Min(Lv, 7); L++)
              foreach (var Granted in GetAttributeBonusGrantForLevel(L, PrimaryAttribute))
                if (Granted == Attr)
                  Total++;
            return Total > 0 ? $"+{Total}" : null;
          });
      }
      return Result;
    }

    /// <summary>
    /// 计算指定黄点等级累计提供的六维属性加成（同时用于抬高职业 LimitForm 上限）。
    /// </summary>
    public PathosStatLimits GetAttributeBonusStatLimits(int CurrentLevel)
    {
      if (Kind != DotaSkillSlotKind.AttributeBonus || CurrentLevel <= 0)
        return default;

      int Str = 0, Dex = 0, Con = 0, Int = 0, Wis = 0, Cha = 0;
      int Clamped = Math.Clamp(CurrentLevel, 0, MaxLevel);
      for (int Lv = 1; Lv <= Clamped; Lv++)
      {
        foreach (var Attr in GetAttributeBonusGrantForLevel(Lv, PrimaryAttribute))
        {
          switch (Attr)
          {
            case "strength": Str++; break;
            case "dexterity": Dex++; break;
            case "constitution": Con++; break;
            case "intelligence": Int++; break;
            case "wisdom": Wis++; break;
            case "charisma": Cha++; break;
          }
        }
      }

      return new PathosStatLimits(Str: Str, Dex: Dex, Con: Con, Int: Int, Wis: Wis, Cha: Cha);
    }

    private static string[] GetAttributeBonusGrantForLevel(int Level, DotaPrimaryAttribute PrimaryAttribute)
    {
      return Level switch
      {
        1 or 4 => new[] { "strength", "constitution" },
        2 or 5 => new[] { "dexterity" },
        3 or 6 => new[] { "intelligence", "wisdom" },
        7 => new[] { "charisma", GetPrimaryPathosAttribute(PrimaryAttribute) },
        _ => Array.Empty<string>()
      };
    }

    private static string GetPrimaryPathosAttribute(DotaPrimaryAttribute PrimaryAttribute)
    {
      return PrimaryAttribute switch
      {
        DotaPrimaryAttribute.Strength => "strength",
        DotaPrimaryAttribute.Agility => "dexterity",
        DotaPrimaryAttribute.Intelligence => "intelligence",
        _ => "constitution"
      };
    }

    private static string GetLocalizedAttributeName(string AttrName)
    {
      return AttrName switch
      {
        "strength" => DotaLocalization.T("ui.attr.str"),
        "dexterity" => DotaLocalization.T("ui.attr.dex"),
        "constitution" => DotaLocalization.T("ui.attr.con"),
        "intelligence" => DotaLocalization.T("ui.attr.int"),
        "wisdom" => DotaLocalization.T("ui.attr.wis"),
        "charisma" => DotaLocalization.T("ui.attr.cha"),
        _ => AttrName.ToUpperInvariant()
      };
    }

    private static string FormatAttributeGrant(IReadOnlyList<string> AttrNames)
    {
      if (AttrNames == null || AttrNames.Count == 0)
        return string.Empty;

      string Separator = DotaLocalization.IsCjkActive() ? "、" : ", ";
      var Parts = new string[AttrNames.Count];
      for (int i = 0; i < AttrNames.Count; i++)
        Parts[i] = $"{GetLocalizedAttributeName(AttrNames[i])} +1";
      return string.Join(Separator, Parts);
    }

    private static string FormatCumulativeBonus(int CurrentLevel, DotaPrimaryAttribute PrimaryAttribute)
    {
      int Str = 0, Dex = 0, Con = 0, Int = 0, Wis = 0, Cha = 0;
      int Clamped = Math.Clamp(CurrentLevel, 0, 7);
      for (int Lv = 1; Lv <= Clamped; Lv++)
      {
        foreach (var Attr in GetAttributeBonusGrantForLevel(Lv, PrimaryAttribute))
        {
          switch (Attr)
          {
            case "strength": Str++; break;
            case "dexterity": Dex++; break;
            case "constitution": Con++; break;
            case "intelligence": Int++; break;
            case "wisdom": Wis++; break;
            case "charisma": Cha++; break;
          }
        }
      }

      var Parts = new List<string>(6);
      if (Str > 0) Parts.Add($"{GetLocalizedAttributeName("strength")}+{Str}");
      if (Dex > 0) Parts.Add($"{GetLocalizedAttributeName("dexterity")}+{Dex}");
      if (Con > 0) Parts.Add($"{GetLocalizedAttributeName("constitution")}+{Con}");
      if (Int > 0) Parts.Add($"{GetLocalizedAttributeName("intelligence")}+{Int}");
      if (Wis > 0) Parts.Add($"{GetLocalizedAttributeName("wisdom")}+{Wis}");
      if (Cha > 0) Parts.Add($"{GetLocalizedAttributeName("charisma")}+{Cha}");
      return string.Join(" ", Parts);
    }

    private static string DescribeAttributeBonusLevel(int CurrentLevel, DotaPrimaryAttribute PrimaryAttribute)
    {
      if (CurrentLevel >= 7)
        return DotaLocalization.Format("ui.skill.attr_bonus.desc_max", FormatCumulativeBonus(CurrentLevel, PrimaryAttribute));

      int NextLv = Math.Max(1, CurrentLevel + 1);
      string Next = FormatAttributeGrant(GetAttributeBonusGrantForLevel(NextLv, PrimaryAttribute));
      return DotaLocalization.Format("ui.skill.attr_bonus.desc_next", Next);
    }

    private static void ApplyAttributeBonusForLevel(
      object Engine,
      Character Character,
      int NewLevel,
      DotaPrimaryAttribute PrimaryAttribute)
    {
      if (Character == null)
        return;

      var Grants = GetAttributeBonusGrantForLevel(NewLevel, PrimaryAttribute);
      var Applied = new List<string>(Grants.Length);
      foreach (var Attr in Grants)
      {
        int Cur = PathosEngineBridge.GetCurrentAbilityScore(Character, Attr);
        int Limit = PathosEngineBridge.GetLimitAbilityScore(Character, Attr);
        if (Cur < Limit && PathosEngineBridge.SetPermanentAbilityScore(Character, Attr, Cur + 1))
          Applied.Add($"{GetLocalizedAttributeName(Attr)} +1");
      }

      if (Applied.Count > 0)
      {
        string Separator = DotaLocalization.IsCjkActive() ? "、" : ", ";
        PathosEngineBridge.WriteInternalLog(
          Engine,
          Character,
          null,
          DotaLocalization.Format("ui.skill.attr_bonus.log", NewLevel, string.Join(Separator, Applied)));
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
      Func<string> HeroDisplayNameResolver = null,
      DotaPrimaryAttribute PrimaryAttribute = DotaPrimaryAttribute.Universal)
    {
      var Slots = new[]
      {
        SlotQ ?? throw new ArgumentNullException(nameof(SlotQ)),
        SlotW ?? throw new ArgumentNullException(nameof(SlotW)),
        SlotE ?? throw new ArgumentNullException(nameof(SlotE)),
        SlotR ?? throw new ArgumentNullException(nameof(SlotR)),
        DotaSkillSlot.CreateStandardAttributeBonus(HeroId, IconGlyph, PrimaryAttribute)
      };

      return new HeroSkillBuild(ClassName, HeroDisplayName, Slots, IconGlyph, HeroDisplayNameResolver);
    }
  }
}
