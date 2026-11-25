using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.UI
{
  /// <summary>
  /// 技能帮助面板中单个熟练度阶梯（熟练/精通/专家/大师/冠军）的展示数据。
  /// </summary>
  public sealed class DotaSpellHelpTier
  {
    public int Tier { get; init; }
    public string HeaderSuffix { get; init; } = string.Empty;
    public IReadOnlyList<string> Bullets { get; init; } = Array.Empty<string>();
  }

  /// <summary>
  /// 技能帮助面板渲染上下文（在游戏内查看技能帮助时 Character 与 Engine 均非空；百科中 Character 为 null）。
  /// </summary>
  public sealed class DotaSpellHelpContext
  {
    public Character Character { get; init; }
    public Spell Spell { get; init; }
    public object Engine { get; init; }
  }

  /// <summary>
  /// 单个 DOTA2 技能的帮助面板数值与阶梯说明定义。
  /// </summary>
  public sealed class DotaSpellHelpSpec
  {
    public string SpellName { get; init; }
    public Func<Character, int> ResolveCurrentTier { get; init; }
    public Func<DotaSpellHelpContext, IReadOnlyList<(string Key, string Value)>> BuildLiveStats { get; init; }
    public Func<DotaSpellHelpContext, IReadOnlyList<string>> BuildMechanicsNotes { get; init; }
    public Func<DotaSpellHelpContext, IReadOnlyList<DotaSpellHelpTier>> BuildTiers { get; init; }
  }

  /// <summary>
  /// 被动 / 先天 Property 帮助面板渲染上下文（原生 HelpContext.CompileProperty 不传角色，
  /// Character 取自当前冒险的主控玩家；主菜单百科中为 null）。
  /// </summary>
  public sealed class DotaPassiveHelpContext
  {
    public Character Character { get; init; }
    public Property Property { get; init; }
    public object Engine { get; init; }
  }

  /// <summary>
  /// 单个被动 / 先天 Property 的帮助面板定义：在原生描述后追加「当前实时面板」「技能机制」与按技能等级的数值明细。
  /// </summary>
  public sealed class DotaPassiveHelpSpec
  {
    public string PropertyName { get; init; }
    /// <summary>当前技能等级（0 = 未学，不高亮任何等级）；为空则不高亮。</summary>
    public Func<Character, int> ResolveCurrentLevel { get; init; }
    /// <summary>实时数值行；返回 null / 空表示该角色不适用（如非本英雄）。</summary>
    public Func<DotaPassiveHelpContext, IReadOnlyList<(string Key, string Value)>> BuildLiveStats { get; init; }
    public Func<DotaPassiveHelpContext, IReadOnlyList<string>> BuildMechanicsNotes { get; init; }
    /// <summary>按技能等级的明细（Tier 字段即等级 1..N）；先天技能可为空。</summary>
    public Func<DotaPassiveHelpContext, IReadOnlyList<DotaSpellHelpTier>> BuildLevels { get; init; }
  }

  /// <summary>
  /// 技能帮助面板语义着色工具：
  /// 按数值来源为同一行内的不同数字着以高对比度语义色，帮助玩家一眼区分伤害/属性/技能/天赋/被动/治疗/蓝耗来源。
  /// </summary>
  public static class DotaHelpColor
  {
    /// <summary>物理 / 武器伤害（暖红橙）</summary>
    public static string Physical(string Text) => DotaSpellHelpBridge.Colorize("phys", Text);
    /// <summary>魔法 / 元素 / 水系技能伤害（天蓝）</summary>
    public static string Magic(string Text) => DotaSpellHelpBridge.Colorize("mag", Text);
    /// <summary>属性修正 / 力量敏捷加成 / 攻速频率缩放（翠绿）</summary>
    public static string Stat(string Text) => DotaSpellHelpBridge.Colorize("stat", Text);
    /// <summary>技能基础 / 附加攻击力 / 斩击次数（明金）</summary>
    public static string Skill(string Text) => DotaSpellHelpBridge.Colorize("skill", Text);
    /// <summary>天赋树加成（亮紫）</summary>
    public static string Talent(string Text) => DotaSpellHelpBridge.Colorize("talent", Text);
    /// <summary>先天 / 被动状态加成，如剑心犹在、剑舞暴击、潮涨潮落（琥珀橙）</summary>
    public static string Passive(string Text) => DotaSpellHelpBridge.Colorize("passive", Text);
    /// <summary>治疗 / 生命值回复 / 血量联动（翡翠绿）</summary>
    public static string Heal(string Text) => DotaSpellHelpBridge.Colorize("heal", Text);
    /// <summary>法力值 / 蓝耗（浅宝蓝）</summary>
    public static string Mana(string Text) => DotaSpellHelpBridge.Colorize("mana", Text);
    /// <summary>控制时长 / 持续回合 / 射程与范围 / 防御减伤（浅金黄）</summary>
    public static string Control(string Text) => DotaSpellHelpBridge.Colorize("cc", Text);
  }

  /// <summary>
  /// 技能帮助面板常用数值文本（共享 <c>ui.help.*</c> 本地化 Key，避免各英雄重复拼接 Format）。
  /// 参数为 object，可直接传入区间文本（如 <c>"1~4"</c>）。
  /// </summary>
  public static class DotaHelpFormat
  {
    /// <summary>「N 格」</summary>
    public static string Tiles(object Value) => DotaLocalization.Format("ui.help.tiles_val", Value);

    /// <summary>「N 回合」</summary>
    public static string Turns(object Value) => DotaLocalization.Format("ui.help.turns_val", Value);

    /// <summary>熟练度阶梯标题后缀「施法距离 N」。</summary>
    public static string CastRangeSuffix(object Range) => DotaLocalization.Format("ui.help.cast_range_suffix", Range);

    /// <summary>「敏捷 N」</summary>
    public static string Dex(object Value) => DotaLocalization.Format("ui.help.dex_val", Value);

    /// <summary>
    /// 施法者释放该技能的真实蓝耗（原生 Character.SpellCost，含天赋「减冷却 → 减蓝耗」），
    /// 与实际扣蓝及原生法术面板一致；切勿在英雄代码中手动重算 MapCooldownReductionToManaCost。
    /// </summary>
    public static int ActualMana(Character Character, DotaAbility Ability)
      => Ability == null ? 0 : PathosEngineBridge.GetSpellManaCost(Character, Ability.Spell, Ability.Spec.Skill);
  }

  /// <summary>
  /// Pathos 技能帮助面板（HelpContext.CompileSpell）桥接层：
  /// 解决 Hook 驱动的 DOTA2 技能在原生帮助面板中显示「什么也不发生」或缺失实际伤害/斩数/缩放数值的问题。
  /// - 保留原生头部（技能描述、学派、等级、基础蓝耗）。
  /// - 游戏内查看（Character != null）时额外渲染「当前实时数值」区块（装备武器骰、属性修正、天赋加成、实际蓝耗、预计伤害等）。
  /// - 支持同行多色渲染（DotaHelpColor），直观区分物理、魔法、属性、技能、天赋、被动、治疗与蓝耗来源。
  /// - 以原生风格渲染 5 阶熟练度完整数值明细，并以橙色高亮当前熟练度阶梯。
  /// - 同时覆盖游戏内技能帮助弹窗与主菜单百科（HelpLibrary）。
  /// </summary>
  public static class DotaSpellHelpBridge
  {
    private const char ColorOpen = '\u27E6';  // ⟦
    private const char ColorClose = '\u27E7'; // ⟧

    private const string LayoutSectionPrefix = "\u27E8S\u27E9"; // ⟨S⟩ 区块/当前阶高亮横条（段间距）
    private const string LayoutLiveRowPrefix = "\u27E8L\u27E9"; // ⟨L⟩ 实时面板属性行（左缩进 + 行间距）
    private const string LayoutTierHdrPrefix = "\u27E8T\u27E9"; // ⟨T⟩ 非当前阶梯/小节标题（段间距）
    private const string LayoutBulletPrefix = "\u27E8B\u27E9";  // ⟨B⟩ 缩进列表项（左缩进 + 行间距）

    private static readonly Dictionary<string, Colour> SemanticPalette = new(StringComparer.Ordinal)
    {
      ["phys"] = Colour.Coral,
      ["mag"] = Colour.DeepSkyBlue,
      ["stat"] = Colour.LightGreen,
      ["skill"] = Colour.Gold,
      ["talent"] = Colour.Orchid,
      ["passive"] = Colour.Orange,
      ["heal"] = Colour.SpringGreen,
      ["mana"] = Colour.LightSkyBlue,
      ["cc"] = Colour.Khaki
    };

    private static readonly ConcurrentDictionary<string, DotaSpellHelpSpec> Specs =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, DotaPassiveHelpSpec> PassiveSpecs =
      new(StringComparer.OrdinalIgnoreCase);

    private static FieldInfo HelpContextShellField;
    private static PropertyInfo ShellEngineProp;

    private static FieldInfo HelpEntryItemListField;
    private static PropertyInfo HelpItemTypeProp;
    private static MethodInfo HelpEntryHighlightMethod;
    private static MethodInfo HelpEntryPropertyMethod;
    private static MethodInfo HelpEntryInformationMethod;

    private static FieldInfo HelpRenderSearchField;
    private static PropertyInfo HelpPropertyKeyProp;
    private static PropertyInfo HelpPropertyValueProp;
    private static PropertyInfo HelpBlockBaseProp;
    private static PropertyInfo HelpBlockMarginProp;
    private static PropertyInfo HelpBlockPaddingProp;
    private static MethodInfo HelpBlockRemoveRunsMethod;
    private static MethodInfo HelpBlockAddRunMethod;

    private static readonly MethodInfo TranslationGetQualificationNameMethod =
      typeof(Expansion).Assembly.GetType("Pathos.Translation")?.GetMethod(
        "GetName",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] { typeof(Qualification) },
        null);

    private static string[] GetFallbackTierNames() => new[]
    {
      DotaLocalization.T("help.tier.1"),
      DotaLocalization.T("help.tier.2"),
      DotaLocalization.T("help.tier.3"),
      DotaLocalization.T("help.tier.4"),
      DotaLocalization.T("help.tier.5")
    };

    /// <summary>
    /// 使用内部语义色标记包装文本，渲染阶段由 HelpRender 补丁拆分为多色 HelpBlock Run。
    /// </summary>
    internal static string Colorize(string Tag, string Text)
    {
      if (string.IsNullOrEmpty(Text))
        return string.Empty;

      return $"{ColorOpen}{Tag}|{Text}{ColorClose}";
    }

    /// <summary>
    /// 按技能内部名称幂等注册自定义帮助面板规格。
    /// </summary>
    public static void RegisterSpell(DotaSpellHelpSpec Spec)
    {
      if (Spec == null || string.IsNullOrWhiteSpace(Spec.SpellName))
        return;

      Specs[Spec.SpellName] = Spec;
    }

    /// <summary>
    /// 注册被动 / 先天 Property 的帮助面板（按 Property.Name 匹配，覆盖原生天赋栏、技能面板被动与百科中的 Property 帮助）。
    /// </summary>
    public static void RegisterPassive(DotaPassiveHelpSpec Spec)
    {
      if (Spec == null || string.IsNullOrWhiteSpace(Spec.PropertyName))
        return;

      PassiveSpecs[Spec.PropertyName] = Spec;
    }

    /// <summary>按技能等级 1..<paramref name="MaxLevel"/> 构建被动明细（标题后缀 + 单条说明）。</summary>
    public static IReadOnlyList<DotaSpellHelpTier> Levels(int MaxLevel, Func<int, string> HeaderSuffix, Func<int, string> Bullet)
    {
      var List = new List<DotaSpellHelpTier>(MaxLevel);
      for (int Lv = 1; Lv <= MaxLevel; Lv++)
      {
        string Line = Bullet?.Invoke(Lv);
        List.Add(new DotaSpellHelpTier
        {
          Tier = Lv,
          HeaderSuffix = HeaderSuffix?.Invoke(Lv) ?? string.Empty,
          Bullets = string.IsNullOrWhiteSpace(Line) ? Array.Empty<string>() : new[] { Line }
        });
      }
      return List;
    }

    /// <summary>熟练度阶数（1 Proficient .. 5 Champion）。</summary>
    public const int TierCount = 5;

    /// <summary>
    /// 构建 5 阶熟练度明细（Tier 字段由本方法填写，<paramref name="Build"/> 中无需再设置）。
    /// </summary>
    public static IReadOnlyList<DotaSpellHelpTier> Tiers(Func<int, DotaSpellHelpTier> Build)
    {
      var List = new List<DotaSpellHelpTier>(TierCount);
      for (int Tier = 1; Tier <= TierCount; Tier++)
      {
        var Entry = Build(Tier);
        List.Add(new DotaSpellHelpTier
        {
          Tier = Tier,
          HeaderSuffix = Entry?.HeaderSuffix ?? string.Empty,
          Bullets = Entry?.Bullets ?? Array.Empty<string>()
        });
      }
      return List;
    }

    /// <summary>
    /// 构建 5 阶熟练度明细的常见形态：每阶 1 个标题后缀 + 1 条说明。
    /// </summary>
    public static IReadOnlyList<DotaSpellHelpTier> Tiers(Func<int, string> HeaderSuffix, Func<int, string> Bullet)
      => Tiers(Tier => new DotaSpellHelpTier
      {
        HeaderSuffix = HeaderSuffix?.Invoke(Tier) ?? string.Empty,
        Bullets = new[] { Bullet(Tier) }
      });

    /// <summary>
    /// 计算 Dice 的最小可能点数（Number * 1 + Bonus）。
    /// </summary>
    public static int GetDiceMin(Dice Dice)
    {
      if (Dice.IsZero())
        return 0;

      return Dice.Number > 0 && Dice.Base > 0
        ? Dice.Number + Dice.Bonus
        : Dice.Bonus;
    }

    /// <summary>
    /// 计算 Dice 的最大可能点数（Number * Base + Bonus）。
    /// </summary>
    public static int GetDiceMax(Dice Dice)
    {
      if (Dice.IsZero())
        return 0;

      return Dice.Number > 0 && Dice.Base > 0
        ? Dice.Number * Dice.Base + Dice.Bonus
        : Dice.Bonus;
    }

    /// <summary>
    /// 将 Dice 格式化为玩家易读的「NdF+B（最小~最大）」字符串。
    /// </summary>
    public static string FormatDiceWithRange(Dice Dice)
    {
      if (Dice.IsZero())
        return "0";

      string Notation = FormatDiceNotation(Dice);
      int Min = GetDiceMin(Dice);
      int Max = GetDiceMax(Dice);
      if (Min == Max)
        return Min.ToString();

      return DotaLocalization.Format("help.dice_range", Notation, Min, Max);
    }

    /// <summary>
    /// 将 Dice 格式化为标准骰子表达式（如 1d4、2d4+1、6）。
    /// </summary>
    public static string FormatDiceNotation(Dice Dice)
    {
      if (Dice.IsZero())
        return "0";

      if (Dice.Number <= 0 || Dice.Base <= 1)
        return GetDiceMin(Dice).ToString();

      string Core = $"{Dice.Number}d{Dice.Base}";
      if (Dice.Bonus > 0)
        return $"{Core}+{Dice.Bonus}";
      if (Dice.Bonus < 0)
        return $"{Core}{Dice.Bonus}";
      return Core;
    }

    internal static void ApplyPatches(Harmony Harmony)
    {
      try
      {
        var ClientAsm = PathosClientReflection.FindClientAssembly();
        if (ClientAsm == null)
        {
          HookBootstrap.WriteDebug("Pathos.dll not loaded; skipping DotaSpellHelpBridge patches.");
          return;
        }

        const BindingFlags AnyInstance = PathosClientReflection.AnyInstance;

        var HelpContextType = ClientAsm.GetType("Pathos.HelpContext");
        var HelpEntryType = ClientAsm.GetType("Pathos.HelpEntry");
        var HelpItemType = ClientAsm.GetType("Pathos.HelpItem");
        var HelpRenderType = ClientAsm.GetType("Pathos.HelpRender");
        var HelpPropertyType = ClientAsm.GetType("Pathos.HelpProperty");
        var HelpBlockType = ClientAsm.GetType("Pathos.HelpBlock");
        var HelpSearchType = ClientAsm.GetType("Pathos.HelpSearch");
        var ShellType = ClientAsm.GetType("Pathos.Shell");

        if (HelpContextType == null || HelpEntryType == null || HelpItemType == null)
        {
          HookBootstrap.WriteError("DotaSpellHelpBridge: required Pathos client types not found.");
          return;
        }

        HelpContextShellField = HelpContextType.GetField("Shell", AnyInstance);
        ShellEngineProp = ShellType?.GetProperty("Engine", AnyInstance);

        HelpEntryItemListField = HelpEntryType.GetField("ItemList", AnyInstance);
        HelpItemTypeProp = HelpItemType.GetProperty("Type", AnyInstance);
        HelpEntryHighlightMethod = HelpEntryType.GetMethod("Highlight", AnyInstance, null, new[] { typeof(string), typeof(Colour) }, null);
        HelpEntryPropertyMethod = HelpEntryType.GetMethod("Property", AnyInstance, null, new[] { typeof(string), typeof(string) }, null);
        HelpEntryInformationMethod = HelpEntryType.GetMethod("Information", AnyInstance, null, new[] { typeof(string) }, null);

        PathosClientReflection.PatchSafe(
          Harmony,
          HelpContextType,
          "CompileSpell",
          new[] { typeof(Character), typeof(Spell) },
          typeof(DotaSpellHelpBridge),
          Postfix: nameof(CompileSpellPostfix),
          Tag: "SpellHelp");

        PathosClientReflection.PatchSafe(
          Harmony,
          HelpContextType,
          "CompileProperty",
          new[] { typeof(Property) },
          typeof(DotaSpellHelpBridge),
          Postfix: nameof(CompilePropertyPostfix),
          Tag: "PassiveHelp");

        if (HelpRenderType != null && HelpPropertyType != null && HelpBlockType != null && HelpSearchType != null)
        {
          HelpRenderSearchField = HelpRenderType.GetField("Search", AnyInstance);
          HelpPropertyKeyProp = HelpPropertyType.GetProperty("Key", AnyInstance);
          HelpPropertyValueProp = HelpPropertyType.GetProperty("Value", AnyInstance);
          HelpBlockBaseProp = HelpBlockType.GetProperty("Base", AnyInstance);
          HelpBlockMarginProp = HelpBlockType.GetProperty("Margin", AnyInstance);
          HelpBlockPaddingProp = HelpBlockType.GetProperty("Padding", AnyInstance);
          HelpBlockRemoveRunsMethod = HelpBlockType.GetMethod("RemoveRuns", AnyInstance, null, Type.EmptyTypes, null);
          HelpBlockAddRunMethod = HelpBlockType.GetMethod(
            "AddRun",
            AnyInstance,
            null,
            new[] { typeof(string), HelpSearchType, typeof(Action<Inv.Font>) },
            null);

          PathosClientReflection.PatchSafe(
            Harmony,
            HelpRenderType,
            "Highlight",
            new[] { typeof(string), typeof(Colour) },
            typeof(DotaSpellHelpBridge),
            Postfix: nameof(HelpRenderHighlightPostfix),
            Tag: "SpellHelpLayoutHighlight");

          PathosClientReflection.PatchSafe(
            Harmony,
            HelpRenderType,
            "Property",
            new[] { HelpPropertyType },
            typeof(DotaSpellHelpBridge),
            Postfix: nameof(HelpRenderPropertyPostfix),
            Tag: "SpellHelpColorProperty");

          PathosClientReflection.PatchSafe(
            Harmony,
            HelpRenderType,
            "Information",
            new[] { typeof(string) },
            typeof(DotaSpellHelpBridge),
            Postfix: nameof(HelpRenderInformationPostfix),
            Tag: "SpellHelpColorInfo");
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaSpellHelpBridge.ApplyPatches failed: {Ex}");
      }
    }

    private static void HelpRenderHighlightPostfix(object __instance, string Text, Colour Colour, object __result)
    {
      if (__result == null || string.IsNullOrEmpty(Text) || !Text.StartsWith(LayoutSectionPrefix, StringComparison.Ordinal))
        return;

      if (HelpBlockRemoveRunsMethod == null || HelpBlockAddRunMethod == null)
        return;

      try
      {
        string CleanText = Text.Substring(LayoutSectionPrefix.Length);
        Colour DefaultColour = ExtractFirstSpanColour(__result, Colour.Black);
        object Search = HelpRenderSearchField?.GetValue(__instance);

        HelpBlockRemoveRunsMethod.Invoke(__result, null);
        AppendParsedColorRuns(__result, CleanText, Search, DefaultColour);

        // 区块/当前阶横条：明显段间距 + 内边距呼吸感
        ApplyBlockMargin(__result, Left: 0, Top: 14, Right: 0, Bottom: 6);
        ApplyBlockPadding(__result, Left: 8, Top: 4, Right: 8, Bottom: 4);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaSpellHelpBridge.HelpRenderHighlightPostfix failed: {Ex}");
      }
    }

    private static void HelpRenderPropertyPostfix(object __instance, object Property, object __result)
    {
      if (__result == null || Property == null || HelpBlockRemoveRunsMethod == null || HelpBlockAddRunMethod == null)
        return;

      try
      {
        string Key = HelpPropertyKeyProp?.GetValue(Property) as string;
        string Value = HelpPropertyValueProp?.GetValue(Property) as string;

        bool IsLiveRow = !string.IsNullOrEmpty(Key) && Key.StartsWith(LayoutLiveRowPrefix, StringComparison.Ordinal);
        bool IsTierHeader = !string.IsNullOrEmpty(Key) && Key.StartsWith(LayoutTierHdrPrefix, StringComparison.Ordinal);
        bool HasColor = !string.IsNullOrEmpty(Value) && Value.IndexOf(ColorOpen) >= 0;

        if (!IsLiveRow && !IsTierHeader && !HasColor)
          return;

        string CleanKey = IsLiveRow
          ? Key.Substring(LayoutLiveRowPrefix.Length)
          : IsTierHeader
            ? Key.Substring(LayoutTierHdrPrefix.Length)
            : Key;

        Colour KeyColour = ExtractFirstSpanColour(__result, Colour.CornflowerBlue);
        Colour DefaultValueColour = ExtractSpanColour(__result, 1, Colour.WhiteSmoke);
        object Search = HelpRenderSearchField?.GetValue(__instance);

        HelpBlockRemoveRunsMethod.Invoke(__result, null);

        if (!string.IsNullOrEmpty(CleanKey))
          AddColoredRun(__result, CleanKey + " ", Search, KeyColour);

        if (!string.IsNullOrEmpty(Value))
          AppendParsedColorRuns(__result, Value, Search, DefaultValueColour);

        if (IsLiveRow)
        {
          // 实时面板条目：左缩进 12px + 舒适行间距
          ApplyBlockMargin(__result, Left: 12, Top: 3, Right: 0, Bottom: 3);
        }
        else if (IsTierHeader)
        {
          // 阶梯/小节标题：上段间距 12px + 下间距 4px
          ApplyBlockMargin(__result, Left: 0, Top: 12, Right: 0, Bottom: 4);
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaSpellHelpBridge.HelpRenderPropertyPostfix failed: {Ex}");
      }
    }

    private static void HelpRenderInformationPostfix(object __instance, string Text, object __result)
    {
      if (__result == null || string.IsNullOrEmpty(Text))
        return;

      bool IsBullet = Text.StartsWith(LayoutBulletPrefix, StringComparison.Ordinal);
      bool HasColor = Text.IndexOf(ColorOpen) >= 0;
      if (!IsBullet && !HasColor)
        return;

      if (HelpBlockRemoveRunsMethod == null || HelpBlockAddRunMethod == null)
        return;

      try
      {
        string CleanText = IsBullet ? Text.Substring(LayoutBulletPrefix.Length) : Text;
        Colour DefaultColour = ExtractFirstSpanColour(__result, Colour.WhiteSmoke);
        object Search = HelpRenderSearchField?.GetValue(__instance);

        HelpBlockRemoveRunsMethod.Invoke(__result, null);
        AppendParsedColorRuns(__result, CleanText, Search, DefaultColour);

        if (IsBullet)
        {
          // 缩进列表项：左缩进 14px + 舒适行间距
          ApplyBlockMargin(__result, Left: 14, Top: 2, Right: 0, Bottom: 4);
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaSpellHelpBridge.HelpRenderInformationPostfix failed: {Ex}");
      }
    }

    private static void ApplyBlockMargin(object HelpBlock, int Left, int Top, int Right, int Bottom)
    {
      if (HelpBlockMarginProp?.GetValue(HelpBlock) is Inv.Edge Edge)
        Edge.Set(Left, Top, Right, Bottom);
    }

    private static void ApplyBlockPadding(object HelpBlock, int Left, int Top, int Right, int Bottom)
    {
      if (HelpBlockPaddingProp?.GetValue(HelpBlock) is Inv.Edge Edge)
        Edge.Set(Left, Top, Right, Bottom);
    }

    private static Colour ExtractFirstSpanColour(object HelpBlock, Colour Fallback)
      => ExtractSpanColour(HelpBlock, 0, Fallback);

    private static Colour ExtractSpanColour(object HelpBlock, int SpanIndex, Colour Fallback)
    {
      try
      {
        if (HelpBlockBaseProp?.GetValue(HelpBlock) is Inv.Block BaseBlock &&
            BaseBlock.Spans != null &&
            SpanIndex >= 0 &&
            SpanIndex < BaseBlock.Spans.Count)
        {
          var SpanColour = BaseBlock.Spans[SpanIndex]?.Font?.Colour;
          if (SpanColour != null)
            return SpanColour;
        }
      }
      catch { }

      return Fallback;
    }

    private static void AppendParsedColorRuns(object HelpBlock, string RichText, object Search, Colour DefaultColour)
    {
      int Pos = 0;
      int Len = RichText.Length;

      while (Pos < Len)
      {
        int OpenIdx = RichText.IndexOf(ColorOpen, Pos);
        if (OpenIdx < 0)
        {
          AddColoredRun(HelpBlock, RichText.Substring(Pos), Search, DefaultColour);
          break;
        }

        if (OpenIdx > Pos)
          AddColoredRun(HelpBlock, RichText.Substring(Pos, OpenIdx - Pos), Search, DefaultColour);

        int CloseIdx = RichText.IndexOf(ColorClose, OpenIdx + 1);
        if (CloseIdx < 0)
        {
          AddColoredRun(HelpBlock, RichText.Substring(OpenIdx), Search, DefaultColour);
          break;
        }

        int SepIdx = RichText.IndexOf('|', OpenIdx + 1);
        if (SepIdx > OpenIdx && SepIdx < CloseIdx)
        {
          string Tag = RichText.Substring(OpenIdx + 1, SepIdx - OpenIdx - 1);
          string Content = RichText.Substring(SepIdx + 1, CloseIdx - SepIdx - 1);
          Colour RunColour = SemanticPalette.TryGetValue(Tag, out var Mapped) ? Mapped : DefaultColour;
          if (!string.IsNullOrEmpty(Content))
            AddColoredRun(HelpBlock, Content, Search, RunColour);
        }
        else
        {
          string Content = RichText.Substring(OpenIdx + 1, CloseIdx - OpenIdx - 1);
          if (!string.IsNullOrEmpty(Content))
            AddColoredRun(HelpBlock, Content, Search, DefaultColour);
        }

        Pos = CloseIdx + 1;
      }
    }

    private static void AddColoredRun(object HelpBlock, string Text, object Search, Colour RunColour)
    {
      Action<Inv.Font> FontAction = F => F.Colour = RunColour;
      HelpBlockAddRunMethod.Invoke(HelpBlock, new object[] { Text, Search, FontAction });
    }

    private static void CompileSpellPostfix(object __instance, Character Character, Spell Spell, ref object __result)
    {
      if (__result == null || Spell == null || string.IsNullOrEmpty(Spell.Name))
        return;

      if (!Specs.TryGetValue(Spell.Name, out var Spec))
        return;

      try
      {
        if (HelpEntryItemListField?.GetValue(__result) is not IList ItemList)
          return;

        // 1. 保留原生头部：Description + 前 3 条 Property（研究/学派、等级、魔法值）
        int KeepCount = ComputeHeaderKeepCount(ItemList);
        while (ItemList.Count > KeepCount)
          ItemList.RemoveAt(ItemList.Count - 1);

        object Shell = HelpContextShellField?.GetValue(__instance);
        object Engine = Shell != null ? ShellEngineProp?.GetValue(Shell) : null;

        var HelpCtx = new DotaSpellHelpContext
        {
          Character = Character,
          Spell = Spell,
          Engine = Engine
        };

        // 2. 游戏内查看时渲染「当前实时数值」区块
        if (Character != null && Spec.BuildLiveStats != null)
        {
          var LiveRows = Spec.BuildLiveStats(HelpCtx);
          if (LiveRows != null && LiveRows.Count > 0)
          {
            InvokeHighlight(__result, LayoutSectionPrefix + DotaLocalization.T("help.section.live_stats"), Colour.Gold);
            foreach (var (Key, Value) in LiveRows)
            {
              if (!string.IsNullOrEmpty(Key) && !string.IsNullOrEmpty(Value))
                InvokeProperty(__result, LayoutLiveRowPrefix + Key, Value);
            }
          }
        }

        // 2.5 渲染跨阶共享的「技能机制」区块（避免在 5 阶熟练度中重复刷屏长句）
        if (Spec.BuildMechanicsNotes != null)
        {
          var Notes = Spec.BuildMechanicsNotes(HelpCtx);
          if (Notes != null && Notes.Count > 0)
          {
            InvokeProperty(__result, LayoutTierHdrPrefix + DotaLocalization.T("help.section.mechanics"), DotaLocalization.T("help.section.all_tiers"));
            foreach (var Note in Notes)
            {
              if (!string.IsNullOrWhiteSpace(Note))
                InvokeInformation(__result, $"{LayoutBulletPrefix}· {Note}");
            }
          }
        }

        // 3. 渲染 5 阶熟练度完整数值明细
        var Tiers = Spec.BuildTiers?.Invoke(HelpCtx);
        if (Tiers == null || Tiers.Count == 0)
          return;

        int CurrentTier = Character != null && Spec.ResolveCurrentTier != null
          ? Spec.ResolveCurrentTier(Character)
          : 0;

        var TierQualNames = ResolveLocalizedTierNames(Spell);

        foreach (var Tier in Tiers)
        {
          if (Tier == null)
            continue;

          int ClampedIdx = Math.Clamp(Tier.Tier - 1, 0, TierQualNames.Length - 1);
          string TierName = TierQualNames[ClampedIdx];
          string Suffix = Tier.HeaderSuffix ?? string.Empty;

          if (CurrentTier > 0 && Tier.Tier == CurrentTier)
          {
            string HighlightHeader = string.IsNullOrEmpty(Suffix)
              ? DotaLocalization.Format("help.tier.current_no_suffix", TierName)
              : DotaLocalization.Format("help.tier.current_with_suffix", TierName, Suffix);
            InvokeHighlight(__result, LayoutSectionPrefix + HighlightHeader, Colour.DarkOrange);
          }
          else
          {
            InvokeProperty(__result, LayoutTierHdrPrefix + TierName, string.IsNullOrEmpty(Suffix) ? DotaLocalization.T("help.tier.effect_fallback") : Suffix);
          }

          if (Tier.Bullets != null)
          {
            foreach (var Bullet in Tier.Bullets)
            {
              if (!string.IsNullOrWhiteSpace(Bullet))
                InvokeInformation(__result, $"{LayoutBulletPrefix}· {Bullet}");
            }
          }
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaSpellHelpBridge.CompileSpellPostfix failed for '{Spell.Name}': {Ex}");
      }
    }

    private static void CompilePropertyPostfix(object __instance, Property Property, ref object __result)
    {
      if (__result == null || Property == null || string.IsNullOrEmpty(Property.Name))
        return;

      if (!PassiveSpecs.TryGetValue(Property.Name, out var Spec))
        return;

      try
      {
        object Shell = HelpContextShellField?.GetValue(__instance);
        object Engine = Shell != null ? ShellEngineProp?.GetValue(Shell) : null;

        var HelpCtx = new DotaPassiveHelpContext
        {
          Character = ResolveMainCharacter(Engine),
          Property = Property,
          Engine = Engine
        };

        // 1. 原生条目（标题 / 图标 / 描述）全部保留，其后追加「当前实时面板」
        if (HelpCtx.Character != null && Spec.BuildLiveStats != null)
        {
          var LiveRows = Spec.BuildLiveStats(HelpCtx);
          if (LiveRows != null && LiveRows.Count > 0)
          {
            InvokeHighlight(__result, LayoutSectionPrefix + DotaLocalization.T("help.section.live_stats"), Colour.Gold);
            foreach (var (Key, Value) in LiveRows)
            {
              if (!string.IsNullOrEmpty(Key) && !string.IsNullOrEmpty(Value))
                InvokeProperty(__result, LayoutLiveRowPrefix + Key, Value);
            }
          }
        }

        // 2. 技能机制
        var Notes = Spec.BuildMechanicsNotes?.Invoke(HelpCtx);
        if (Notes != null && Notes.Count > 0)
        {
          InvokeProperty(__result, LayoutTierHdrPrefix + DotaLocalization.T("help.section.mechanics"), DotaLocalization.T("help.section.all_levels"));
          foreach (var Note in Notes)
          {
            if (!string.IsNullOrWhiteSpace(Note))
              InvokeInformation(__result, $"{LayoutBulletPrefix}· {Note}");
          }
        }

        // 3. 按技能等级的数值明细（当前等级橙色高亮）
        var Levels = Spec.BuildLevels?.Invoke(HelpCtx);
        if (Levels == null || Levels.Count == 0)
          return;

        int CurrentLevel = HelpCtx.Character != null && Spec.ResolveCurrentLevel != null
          ? Spec.ResolveCurrentLevel(HelpCtx.Character)
          : 0;

        foreach (var Level in Levels)
        {
          if (Level == null)
            continue;

          string LevelName = DotaLocalization.Format("help.level.n", Level.Tier);
          string Suffix = Level.HeaderSuffix ?? string.Empty;

          if (CurrentLevel > 0 && Level.Tier == CurrentLevel)
          {
            string HighlightHeader = string.IsNullOrEmpty(Suffix)
              ? DotaLocalization.Format("help.tier.current_no_suffix", LevelName)
              : DotaLocalization.Format("help.tier.current_with_suffix", LevelName, Suffix);
            InvokeHighlight(__result, LayoutSectionPrefix + HighlightHeader, Colour.DarkOrange);
          }
          else
          {
            InvokeProperty(__result, LayoutTierHdrPrefix + LevelName, string.IsNullOrEmpty(Suffix) ? DotaLocalization.T("help.tier.effect_fallback") : Suffix);
          }

          if (Level.Bullets != null)
          {
            foreach (var Bullet in Level.Bullets)
            {
              if (!string.IsNullOrWhiteSpace(Bullet))
                InvokeInformation(__result, $"{LayoutBulletPrefix}· {Bullet}");
            }
          }
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaSpellHelpBridge.CompilePropertyPostfix failed for '{Property.Name}': {Ex}");
      }
    }

    private static PropertyInfo EngineAdventureProp;

    /// <summary>取当前冒险第一个玩家的主控角色（单人地牢即玩家英雄）；主菜单百科等无冒险场景返回 null。</summary>
    private static Character ResolveMainCharacter(object Engine)
    {
      if (Engine == null)
        return null;

      try
      {
        EngineAdventureProp ??= Engine.GetType().GetProperty("Adventure", PathosClientReflection.AnyInstance);
        if (EngineAdventureProp?.GetValue(Engine) is not Adventure Adventure || Adventure.PlayerList == null)
          return null;

        foreach (var Player in Adventure.PlayerList)
        {
          var Character = Player?.Main?.Character;
          if (Character != null)
            return Character;
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteDebug($"DotaSpellHelpBridge.ResolveMainCharacter failed: {Ex.Message}");
      }

      return null;
    }

    private static int ComputeHeaderKeepCount(IList ItemList)
    {
      int Kept = 0;
      int PropertyCount = 0;

      for (int I = 0; I < ItemList.Count; I++)
      {
        var Item = ItemList[I];
        string TypeName = HelpItemTypeProp?.GetValue(Item)?.ToString();

        if (TypeName == "Description" || TypeName == "Inscription" || TypeName == "Title")
        {
          Kept = I + 1;
          continue;
        }

        if (TypeName == "Property" && PropertyCount < 3)
        {
          PropertyCount++;
          Kept = I + 1;
          continue;
        }

        break;
      }

      return Kept;
    }

    private static string[] ResolveLocalizedTierNames(Spell Spell)
    {
      var Names = GetFallbackTierNames();

      try
      {
        int Idx = 0;
        foreach (var Adept in Spell.GetAdepts())
        {
          if (Idx >= Names.Length)
            break;

          var Qual = Adept?.Qualification;
          if (Qual != null)
          {
            if (TranslationGetQualificationNameMethod?.Invoke(null, new object[] { Qual }) is string Localized &&
                !string.IsNullOrWhiteSpace(Localized))
            {
              Names[Idx] = Localized;
            }
            else if (!string.IsNullOrWhiteSpace(Qual.Name))
            {
              Names[Idx] = Qual.Name;
            }
          }

          Idx++;
        }
      }
      catch
      {
        // 回退至默认中文阶梯名
      }

      return Names;
    }

    private static void InvokeHighlight(object HelpEntry, string Text, Colour Colour)
    {
      HelpEntryHighlightMethod?.Invoke(HelpEntry, new object[] { Text, Colour });
    }

    private static void InvokeProperty(object HelpEntry, string Key, string Value)
    {
      HelpEntryPropertyMethod?.Invoke(HelpEntry, new object[] { Key, Value });
    }

    private static void InvokeInformation(object HelpEntry, string Text)
    {
      HelpEntryInformationMethod?.Invoke(HelpEntry, new object[] { Text });
    }
  }
}
