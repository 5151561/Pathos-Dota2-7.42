using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Inv;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Skills;
using Pathos.DotaHeroes.Framework.Talents;
using Pathos.DotaHeroes.Framework.Targeting;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Heroes
{
  /// <summary>
  /// DOTA2 7.42 英雄：变体精灵（Morphling / 水人）
  /// 基于「声明式外壳 + Harmony Hook 核心逻辑」忠实还原 DOTA2 7.42 百科机制：
  /// - 先天技能「潮涨潮落（Ebb and Flow）」（实时随属性生效，变形期间持续保留）：
  ///   - 敏捷（DEX）：提供额外移动速度与远程攻击距离
  ///   - 力量（STR）：提供额外施法距离与减速抗性
  /// - Q：波浪形态（waveform）— 闪烁突进 + 落地水波 AOE 伤害 + 短暂穿墙与闪避位移
  /// - W：变体打击（adaptive strike）— 单一技能合二为一：基础水伤 + 击退 + 眩晕，同时按实时 DEX/STR 动态缩放爆发伤害与眩晕时长
  /// - E1/E2：属性转换·敏捷 / 力量（morph agility / morph strength）—
  ///   - Hook 全权接管，同步修改 CurrentForm 与 MaximumForm，严格总量守恒且复活/恢复属性不再白嫖
  ///   - 忠实还原 DOTA2 血量联动：转力同步提升最大生命与当前生命；转敏同步降低最大生命与当前生命（最低保底 1 HP）
  /// - R1/R2：变形（morph）与解除变形（morph revert）— 化身敌人复制外形与技能，先天技能持续保留
  /// </summary>
  public sealed class MorphlingHero : IDotaHeroDefinition
  {
    private const string MorphlingClassName = "morphling";
    private const string WaveformSpellName = "waveform";
    private const string AdaptiveStrikeSpellName = "adaptive strike";
    private const string MorphAgilitySpellName = "morph agility";
    private const string MorphStrengthSpellName = "morph strength";
    private const string MorphSpellName = "morph";
    private const string MorphRevertSpellName = "morph revert";

    private const string SkillWaveformId = "morph.q.waveform";
    private const string SkillAdaptiveStrikeId = "morph.w.adaptive_strike";
    private const string SkillAttributeShiftId = "morph.e.attribute_shift";
    private const string SkillMorphId = "morph.r.morph";

    // 水人天赋节点 ID（DOTA2 7.42 原版节奏：Lv 10 / 15 / 20 / 25 解锁，Lv 27 / 28 / 29 / 30 双修补点）
    private const string TalentT1MorphDuration = "morph.t1.morph_duration";
    private const string TalentT1MagicResist = "morph.t1.magic_resist";
    private const string TalentT2AttackRange = "morph.t2.attack_range";
    private const string TalentT2WaveformRange = "morph.t2.waveform_range";
    private const string TalentT3Agility = "morph.t3.agility";
    private const string TalentT3AdaptiveStrikeMana = "morph.t3.adaptive_strike_mana";
    private const string TalentT4Strength = "morph.t4.strength";
    private const string TalentT4WaveformMana = "morph.t4.waveform_mana";

    private const int MinAttributeScore = 3;
    private const int MinMaxLifePoints = 10;
    private const int HpPerShiftedPoint = 6;
    private const int TalentMorphDefenceBonus = 2;
    private const int TalentAttackRangeBonus = 1;
    private const int TalentWaveformRangeBonus = 2;
    private const int TalentAgilityBonus = 3;   // 7.42 +15 敏捷按 1:5 后期属性标尺 -> +3 DEX（同步拓宽上限）
    private const int TalentStrengthBonus = 7;  // 7.42 +35 力量按 1:5 后期属性标尺 -> +7 STR/CON 与 +42 HP（同步拓宽上限）

    // DOTA2 7.42 变体精灵初始三维与成长：STR 16 (+2.8) / AGI 33 (+4.2) / INT 19 (+2.0)
    private const int DotaBaseStr = 16;
    private const float DotaStrGain = 2.8f;
    private const int DotaBaseAgi = 33;
    private const float DotaAgiGain = 4.2f;
    private const int DotaBaseInt = 19;
    private const float DotaIntGain = 2.0f;
    private static readonly PathosStartingStats StartingStats =
      DotaMechanicsMapper.MapStartingStats(DotaBaseStr, DotaBaseAgi, DotaBaseInt, Charisma: 10);
    private static readonly PathosStatLimits StatLimits =
      DotaMechanicsMapper.MapStatLimits(DotaBaseStr, DotaStrGain, DotaBaseAgi, DotaAgiGain, DotaBaseInt, DotaIntGain, CharismaLimit: 20, StartingCha: 10);

    private static readonly int WaveformManaCost = DotaMechanicsMapper.MapSkillManaCost(115);      // 6
    private static readonly int AdaptiveStrikeManaCost = DotaMechanicsMapper.MapSkillManaCost(70); // 7.42 满级 70 蓝 -> 4
    private static readonly int AttributeShiftManaCost = DotaMechanicsMapper.MapSkillManaCost(0);  // 0（忠实还原 7.42）
    private static readonly int MorphManaCost = DotaMechanicsMapper.MapSkillManaCost(120);         // 原 50 蓝，Pathos Impersonate 强度修正 -> 6
    private static readonly int MorphRevertManaCost = DotaMechanicsMapper.MapSkillManaCost(0);     // 0

    // 7.42 减冷却 / 覆盖率天赋 -> 减蓝耗换算系数
    private static readonly float MorphDurationTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(12.0f, 36.0f); // 24s -> 36s (+50% 覆盖率 = 33.3% 蓝耗折减)
    private static readonly float AdaptiveStrikeTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(3.0f, 8.0f);  // -3s / 8s 满级冷却 = 37.5%
    private const float WaveformTalentCdr = 0.40f; // -40% 波浪形态冷却

    private static readonly ConditionalWeakTable<Character, Square> WaveformOriginSquares = new();

    private Property EbbAndFlowTalentProperty;
    private Glyph MorphlingGlyph;
    private Glyph EbbAndFlowGlyph;
    private Glyph MorphGlyph;
    private Glyph MorphRevertGlyph;
    private Beam WaveformDashBeam;
    private Explosion WaveformSplashExplosion;
    private Strike AdaptiveStrikeImpactStrike;

    public string Id => "morphling";

    public void Register(DotaHeroContext Ctx)
    {
      // 1. 注册贴图（对应 Resources/Heroes/morphling/*.png）
      MorphlingGlyph = Ctx.RegisterGlyph("morphling");
      EbbAndFlowGlyph = Ctx.RegisterGlyph("ebb and flow");
      var WaveformGlyph = Ctx.RegisterGlyph("waveform");
      var AdaptiveStrikeGlyph = Ctx.RegisterGlyph("adaptive strike");
      var MorphAgilityGlyph = Ctx.RegisterGlyph("morph agility");
      var MorphStrengthGlyph = Ctx.RegisterGlyph("morph strength");
      MorphGlyph = Ctx.RegisterGlyph("morph");
      MorphRevertGlyph = Ctx.RegisterGlyph("morph revert");
      var TidalOrbGlyph = Ctx.RegisterGlyph("tidal orb");

      // 1.5 注册水人专属自定义像素 VFX（4 方向水浪残影 + 3×3 九宫格水爆 + 变体打击单格水爆）
      WaveformDashBeam = Ctx.RegisterBeamVfx(
        "waveform dash",
        "waveform fx beam h",
        "waveform fx beam v",
        "waveform fx beam f",
        "waveform fx beam b");
      WaveformSplashExplosion = Ctx.RegisterExplosion3x3Vfx("waveform splash", "waveform fx");
      AdaptiveStrikeImpactStrike = Ctx.RegisterStrikeVfx("adaptive strike impact", "adaptive strike fx strike");

      // 2. 注册水人先天技能天赋：潮涨潮落（显示在角色天赋栏与左上角状态栏，实际动态效果由 Hook 实时驱动）
      EbbAndFlowTalentProperty = RegisterEbbAndFlowTalent(Ctx, EbbAndFlowGlyph);

      // 3. 注册水人专属无限远程普攻：潮汐水弹（tidal orb）
      var TidalOrbItem = RegisterTidalOrbItem(Ctx, TidalOrbGlyph);

      // 4. 注册水人全套专属技能（Q / W / E1 / E2 / R1 / R2）
      var WaveformSpell = RegisterWaveformSpell(Ctx, WaveformGlyph);
      var AdaptiveStrikeSpell = RegisterAdaptiveStrikeSpell(Ctx, AdaptiveStrikeGlyph);
      var MorphAgilitySpell = RegisterMorphAgilitySpell(Ctx, MorphAgilityGlyph);
      var MorphStrengthSpell = RegisterMorphStrengthSpell(Ctx, MorphStrengthGlyph);
      var MorphSpell = RegisterMorphSpell(Ctx, MorphGlyph);
      var MorphRevertSpell = RegisterMorphRevertSpell(Ctx, MorphRevertGlyph);

      // 5. 注册专属种族：变体水灵（waterkin，含遇水回血回蓝与遇火蒸发反应）
      var WaterkinRace = RegisterWaterkinRace(Ctx, MorphlingGlyph);

      // 6. 注册水系环境专属交互（水药水吸收 + 喷泉汲取）
      RegisterWaterInteractions(Ctx);

      // 7. 注册专属职业：变体精灵（morphling）
      var MorphlingClass = RegisterMorphlingClass(
        Ctx,
        MorphlingGlyph,
        EbbAndFlowTalentProperty,
        TidalOrbItem,
        WaveformSpell,
        AdaptiveStrikeSpell,
        MorphAgilitySpell,
        MorphStrengthSpell,
        MorphSpell,
        MorphRevertSpell);

      // 8. 注册预设英雄：Morphling（可在选人界面一键选中）
      Ctx.RegisterPresetHero("Morphling", WaterkinRace, MorphlingClass, Ctx.Genders.male, MorphlingGlyph);

      // 9. 注册变体精灵技能加点表（Q/W/E/R + 7 级全属性黄点，主动升星与学派熟练度双重成长）
      RegisterSkillBuild(
        Ctx,
        MorphlingGlyph,
        WaveformGlyph,
        AdaptiveStrikeGlyph,
        MorphAgilityGlyph,
        MorphGlyph,
        WaveformSpell,
        AdaptiveStrikeSpell,
        MorphAgilitySpell,
        MorphStrengthSpell,
        MorphSpell,
        MorphRevertSpell);

      // 10. 注册变体精灵 4 层双修天赋树（DOTA2 7.42 Lv 10/15/20/25 + Lv 27~30 双修）
      RegisterTalentTree(Ctx, MorphlingGlyph);
    }

    private static void RegisterSkillBuild(
      DotaHeroContext Ctx,
      Glyph HeroGlyph,
      Glyph WaveformGlyph,
      Glyph AdaptiveStrikeGlyph,
      Glyph AttributeShiftGlyph,
      Glyph MorphSkillGlyph,
      Spell WaveformSpell,
      Spell AdaptiveStrikeSpell,
      Spell MorphAgilitySpell,
      Spell MorphStrengthSpell,
      Spell MorphSpell,
      Spell MorphRevertSpell)
    {
      var Build = HeroSkillBuild.CreateStandard(
        ClassName: MorphlingClassName,
        HeroDisplayName: "Morphling",
        HeroId: "morph",
        IconGlyph: HeroGlyph,
        SlotQ: DotaSkillSlot.CreateActive(
          Id: SkillWaveformId,
          Hotkey: "Q",
          Title: "Waveform",
          IconGlyph: WaveformGlyph,
          Spell: WaveformSpell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("morph.skill.q.desc_0")
            : DotaLocalization.Format(
                "morph.skill.q.desc_lv",
                $"{1.0f + (Lv - 1) * 0.15f:0.00}",
                Lv - 1,
                Lv < 4 ? DotaLocalization.T("jugg.skill.w.desc_next_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("morph.skill.q.title")),
        SlotW: DotaSkillSlot.CreateActive(
          Id: SkillAdaptiveStrikeId,
          Hotkey: "W",
          Title: "Adaptive Strike",
          IconGlyph: AdaptiveStrikeGlyph,
          Spell: AdaptiveStrikeSpell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("morph.skill.w.desc_0")
            : DotaLocalization.Format(
                "morph.skill.w.desc_lv",
                $"{1.0f + (Lv - 1) * 0.15f:0.00}",
                Lv - 1,
                Lv < 4 ? DotaLocalization.T("jugg.skill.w.desc_next_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("morph.skill.w.title")),
        SlotE: DotaSkillSlot.CreateActiveGroup(
          Id: SkillAttributeShiftId,
          Hotkey: "E",
          Title: "Attribute Shift (AGI/STR)",
          IconGlyph: AttributeShiftGlyph,
          Spells: new[] { MorphAgilitySpell, MorphStrengthSpell },
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("morph.skill.e.desc_0")
            : DotaLocalization.Format(
                "morph.skill.e.desc_lv",
                Lv - 1,
                Lv < 4 ? DotaLocalization.T("morph.skill.e.desc_next_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("morph.skill.e.title")),
        SlotR: DotaSkillSlot.CreateActiveGroup(
          Id: SkillMorphId,
          Hotkey: "R",
          Title: "Morph / Morph Revert",
          IconGlyph: MorphSkillGlyph,
          Spells: new[] { MorphSpell, MorphRevertSpell },
          IsUltimate: true,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("morph.skill.r.desc_0")
            : DotaLocalization.Format(
                "morph.skill.r.desc_lv",
                Lv - 1,
                Lv < 3 ? DotaLocalization.T("morph.skill.r.desc_next_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("morph.skill.r.title")),
        HeroDisplayNameResolver: () => DotaLocalization.T("morph.hero_display"),
        PrimaryAttribute: DotaPrimaryAttribute.Agility);

      HeroSkillRegistry.Register(Ctx, Build);
    }

    private static void RegisterTalentTree(DotaHeroContext Ctx, Glyph IconGlyph)
    {
      int ReducedMorphMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(MorphManaCost, MorphDurationTalentCdr);
      int ReducedAdaptiveMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(AdaptiveStrikeManaCost, AdaptiveStrikeTalentCdr);
      int ReducedWaveformMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(WaveformManaCost, WaveformTalentCdr);

      var Tree = HeroTalentTree.CreateStandard(
        ClassName: MorphlingClassName,
        HeroDisplayNameResolver: () => DotaLocalization.T("morph.hero_display"),
        IconGlyph: IconGlyph,
        T1Left: new TalentNode(
          Id: TalentT1MorphDuration,
          TitleResolver: () => DotaLocalization.T("morph.talent.t1l.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t1l.desc", MorphManaCost, ReducedMorphMana, TalentMorphDefenceBonus),
          ManaReductions: new[] { new SpellManaReductionRule(MorphSpellName, MorphDurationTalentCdr) }),
        T1Right: new TalentNode(
          Id: TalentT1MagicResist,
          TitleResolver: () => DotaLocalization.T("morph.talent.t1r.title"),
          DescriptionResolver: () => DotaLocalization.T("morph.talent.t1r.desc")),
        T2Left: new TalentNode(
          Id: TalentT2AttackRange,
          TitleResolver: () => DotaLocalization.T("morph.talent.t2l.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t2l.desc", TalentAttackRangeBonus)),
        T2Right: new TalentNode(
          Id: TalentT2WaveformRange,
          TitleResolver: () => DotaLocalization.T("morph.talent.t2r.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t2r.desc", TalentWaveformRangeBonus)),
        T3Left: new TalentNode(
          Id: TalentT3Agility,
          TitleResolver: () => DotaLocalization.T("morph.talent.t3l.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t3l.desc", TalentAgilityBonus),
          OnLearn: (Engine, Ch) =>
          {
            int CurDex = PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity");
            int MaxDex = PathosEngineBridge.GetLimitAbilityScore(Ch, "dexterity");
            int NextDex = Math.Min(MaxDex, CurDex + TalentAgilityBonus);
            PathosEngineBridge.SetPermanentAbilityScore(Ch, "dexterity", NextDex);
          }),
        T3Right: new TalentNode(
          Id: TalentT3AdaptiveStrikeMana,
          TitleResolver: () => DotaLocalization.T("morph.talent.t3r.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t3r.desc", AdaptiveStrikeManaCost, ReducedAdaptiveMana),
          ManaReductions: new[] { new SpellManaReductionRule(AdaptiveStrikeSpellName, AdaptiveStrikeTalentCdr) }),
        T4Left: new TalentNode(
          Id: TalentT4Strength,
          TitleResolver: () => DotaLocalization.T("morph.talent.t4l.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t4l.desc", TalentStrengthBonus, TalentStrengthBonus * HpPerShiftedPoint),
          OnLearn: (Engine, Ch) =>
          {
            int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength");
            int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Ch, "constitution");
            int MaxStr = PathosEngineBridge.GetLimitAbilityScore(Ch, "strength");
            int MaxCon = PathosEngineBridge.GetLimitAbilityScore(Ch, "constitution");
            PathosEngineBridge.SetPermanentAbilityScore(Ch, "strength", Math.Min(MaxStr, CurStr + TalentStrengthBonus));
            PathosEngineBridge.SetPermanentAbilityScore(Ch, "constitution", Math.Min(MaxCon, CurCon + TalentStrengthBonus));

            int HpGain = TalentStrengthBonus * HpPerShiftedPoint;
            if (Ch.Life != null)
              PathosEngineBridge.SetMeter(Ch.Life, Math.Min(Ch.Life.Total + HpGain, Ch.Life.Points + HpGain), Ch.Life.Total + HpGain);
          }),
        T4Right: new TalentNode(
          Id: TalentT4WaveformMana,
          TitleResolver: () => DotaLocalization.T("morph.talent.t4r.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t4r.desc", WaveformManaCost, ReducedWaveformMana),
          ManaReductions: new[] { new SpellManaReductionRule(WaveformSpellName, WaveformTalentCdr) }));

      TalentRegistry.Register(Ctx, Tree);
    }

    private static PathosStatLimits GetTalentStatLimitBonus(Character Character)
    {
      int DexBonus = TalentService.HasTalent(Character, TalentT3Agility) ? TalentAgilityBonus : 0;
      int StrConBonus = TalentService.HasTalent(Character, TalentT4Strength) ? TalentStrengthBonus : 0;
      return new PathosStatLimits(Str: StrConBonus, Dex: DexBonus, Con: StrConBonus, Int: 0, Wis: 0, Cha: 0);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定变体精灵固定初始六维（STR 10 / DEX 18 / CON 10 / INT 12 / WIS 12 / CHA 10）、1 级初始蓝量（15）与 30 级职业属性上限（27 / 34 / 27 / 25 / 25 / 20）
      //    同时注册动态属性上限加成（Lv 20 +15 敏捷 -> DEX+3 上限；Lv 25 +35 力量 -> STR/CON+7 上限）
      DotaMechanicsMapper.RegisterFixedStartingStats(MorphlingClassName, StartingStats);
      DotaMechanicsMapper.RegisterHeroStatLimits(MorphlingClassName, StatLimits);
      DotaMechanicsMapper.RegisterHeroStatLimitBonus(MorphlingClassName, GetTalentStatLimitBonus);

      // 0.1 注册 Pathos 原生选框扩距（波浪形态允许直接点空地或敌人格，自动穿身落位 / 变体打击 / 变形）
      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: WaveformSpellName,
        DisplayTitle: "Waveform",
        Mode: DotaTargetMode.PointAny,
        ResolveRange: Caster => GetWaveformRange(Ctx, Caster),
        AllowSelf: false,
        ResolveDisplayTitle: () => DotaLocalization.T("morph.skill.q.title")));

      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: AdaptiveStrikeSpellName,
        DisplayTitle: "Adaptive Strike",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: Caster => GetAdaptiveStrikeRange(Ctx, Caster),
        AllowSelf: false,
        ResolveDisplayTitle: () => DotaLocalization.T("morph.skill.w.title")));

      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: MorphSpellName,
        DisplayTitle: "Morph",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: Caster => GetMorphRange(Ctx, Caster),
        AllowSelf: false,
        ResolveDisplayTitle: () => DotaLocalization.T("morph.spell.morph.title")));

      // 0.2 注册左上角状态栏动态徽章：先天「潮涨潮落」实时倾向 & 变形状态
      DotaStatusBarBridge.RegisterProvider("morphling.ebb_flow", Character =>
      {
        if (!IsMorphling(Character) || Character.Deceased)
          return null;

        int Dex = PathosEngineBridge.GetCurrentAbilityScore(Character, "dexterity");
        int Str = PathosEngineBridge.GetCurrentAbilityScore(Character, "strength");
        if (Dex <= 10 && Str <= 10)
          return null;

        Colour BadgeColour = Dex > Str + 2
          ? Colour.DarkCyan
          : Str > Dex + 2
            ? Colour.DarkRed
            : Colour.Black;

        return new DotaStatusBadge
        {
          Key = "morphling.ebb_flow",
          Glyph = EbbAndFlowGlyph ?? MorphlingGlyph,
          Text = (EbbAndFlowTalentProperty != null
            ? PathosEngineBridge.GetLocalizedName(EbbAndFlowTalentProperty)
            : DotaLocalization.T("morph.status.ebb_fallback")).ToUpper(),
          Count = DotaLocalization.Format("morph.status.ebb_count", Dex, Str),
          Colour = BadgeColour,
          HelpProperty = EbbAndFlowTalentProperty
        };
      });

      DotaStatusBarBridge.RegisterProvider("morphling.morphed", Character =>
      {
        if (!IsMorphling(Character) || Character.Deceased || !PathosEngineBridge.IsPolymorphed(Character))
          return null;

        bool HasMorphTalent = TalentService.HasTalent(Character, TalentT1MorphDuration);
        return new DotaStatusBadge
        {
          Key = "morphling.morphed",
          Glyph = MorphGlyph ?? MorphlingGlyph,
          Text = DotaLocalization.T("morph.status.morphed").ToUpper(),
          Count = HasMorphTalent ? $"+{TalentMorphDefenceBonus} DEF" : string.Empty,
          Colour = HasMorphTalent ? Colour.DarkBlue : Colour.Black,
          HelpProperty = EbbAndFlowTalentProperty
        };
      });

      // 0.3 注册变体精灵主动技能的帮助面板数值与 5 阶熟练度详情（含游戏内实时面板预览）
      RegisterSpellHelpSpecs(Ctx);

      // Q 波浪形态施法前置：记录起点格子，供施法后绘制起止点 4 方向水浪残影 Beam
      DotaHookBus.RegisterBeforeCastSpell("morphling.before_cast", Args =>
      {
        if (Args.Character?.Square != null &&
            string.Equals(Args.Spell?.Name, WaveformSpellName, StringComparison.OrdinalIgnoreCase))
        {
          WaveformOriginSquares.Remove(Args.Character);
          WaveformOriginSquares.Add(Args.Character, Args.Character.Square);
        }
      });

      // Q/W 水系技能落点校准与专属像素 VFX（4 方向水浪残影 + 3×3 潮汐水爆）
      DotaHookBus.RegisterAfterCastSpell("morphling.range_and_vfx", Args =>
      {
        var Caster = Args.Character;
        var SpellName = Args.Spell?.Name;
        if (Caster?.Square == null || string.IsNullOrEmpty(SpellName))
          return;

        if (string.Equals(SpellName, WaveformSpellName, StringComparison.OrdinalIgnoreCase))
        {
          ExecuteWaveform(Ctx, Args);
        }
        else if (string.Equals(SpellName, AdaptiveStrikeSpellName, StringComparison.OrdinalIgnoreCase))
        {
          ExecuteAdaptiveStrike(Ctx, Args);
        }
        else if (string.Equals(SpellName, MorphSpellName, StringComparison.OrdinalIgnoreCase))
        {
          ExecuteMorph(Ctx, Args);
        }
      });

      // E1/E2：属性转换（总量守恒 + CurrentForm/MaximumForm 同步 + DOTA2 血量联动）
      DotaHookBus.RegisterAfterCastSpell("morphling.attribute_shift", Args =>
      {
        var SpellName = Args.Spell?.Name;
        bool IsAgilityShift = string.Equals(SpellName, MorphAgilitySpellName, StringComparison.OrdinalIgnoreCase);
        bool IsStrengthShift = string.Equals(SpellName, MorphStrengthSpellName, StringComparison.OrdinalIgnoreCase);
        if (!IsAgilityShift && !IsStrengthShift)
          return;

        ExecuteAttributeShift(Ctx, Args, ToAgility: IsAgilityShift);
      });

      // T1 左天赋（+12s 变形持续时间）：变形期间额外提供 +2 防御
      DotaHookBus.RegisterDefenceQuery("morphling.talent_morph_defence", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        if (PathosEngineBridge.IsPolymorphed(Args.Character) &&
            TalentService.HasTalent(Args.Character, TalentT1MorphDuration))
        {
          Args.Defence += TalentMorphDefenceBonus;
        }
      });

      // 先天技能「潮涨潮落」— 敏捷提升移动速度（变形期间持续保留）
      DotaHookBus.RegisterSpeedQuery("morphling.ebb_flow.speed", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        int Dex = PathosEngineBridge.GetCurrentAbilityScore(Args.Character, "dexterity");
        if (Dex <= 10)
          return;

        float BonusMultiplier = 1.0f + Math.Min(0.50f, (Dex - 10) * 0.02f);
        Args.Speed = Speed.FromRate(Args.Speed.GetRate() * BonusMultiplier);
      });

      // 先天技能「潮涨潮落」— 敏捷提升远程攻击距离 + T2 左天赋（+75 攻击距离 -> +1 格，变形为远程生物同样保留）
      DotaHookBus.RegisterAttackRangeQuery("morphling.ebb_flow.attack_range", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        int Dex = PathosEngineBridge.GetCurrentAbilityScore(Args.Character, "dexterity");
        int BonusRange = Math.Max(0, (Dex - 10) / 5);
        if (TalentService.HasTalent(Args.Character, TalentT2AttackRange))
          BonusRange += TalentAttackRangeBonus;

        if (BonusRange > 0)
          Args.Range += BonusRange;
      });

      // 先天技能「潮涨潮落」— 力量提升施法距离（含变形后窃取的技能）
      DotaHookBus.RegisterCastRange("morphling.ebb_flow.cast_range", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        int Str = PathosEngineBridge.GetCurrentAbilityScore(Args.Character, "strength");
        int BonusRange = Math.Max(0, (Str - 10) / 5);
        if (BonusRange > 0)
          Args.BonusRange += BonusRange;
      });

      // 先天技能「潮涨潮落」— 力量提供减速抗性（高力直接免疫，中力大幅缩短减速时间）
      DotaHookBus.RegisterBeforeApplyTransient("morphling.transient_hooks", Args =>
      {
        if (Args.Property == Ctx.Properties.slowness && IsMorphling(Args.Target))
        {
          int Str = PathosEngineBridge.GetCurrentAbilityScore(Args.Target, "strength");
          if (Str >= 24)
          {
            Args.Cancel = true;
            return;
          }

          if (Str > 10)
          {
            float Resist = Math.Min(0.75f, (Str - 10) * 0.045f);
            int AvgTurns = (int)MathF.Round(Args.Duration.Average() * (1.0f - Resist));
            if (AvgTurns <= 0)
            {
              Args.Cancel = true;
              return;
            }
            Args.Duration = Dice.Fixed(AvgTurns);
          }
        }
      });

      // T1 右天赋（+15% 魔法抗性）：对法术/元素/环境等非普通物理伤害减免 15%
      DotaHookBus.RegisterBeforeDamage("morphling.adaptive_strike", Args =>
      {
        if (Args.Defender != null &&
            Args.DamagePoints > 0 &&
            IsMorphling(Args.Defender) &&
            TalentService.HasTalent(Args.Defender, TalentT1MagicResist) &&
            DotaCombatHelper.IsNonBasicPhysicalIncoming(Args))
        {
          Args.DamagePoints = Math.Max(1, (int)MathF.Round(Args.DamagePoints * 0.85f));
        }
      });
    }

    private void ExecuteWaveform(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster?.Square == null)
        return;

      WaveformOriginSquares.TryGetValue(Caster, out var OriginSquare);
      WaveformOriginSquares.Remove(Caster);
      OriginSquare ??= Caster.Square;

      var Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      int MaxRange = GetWaveformRange(Ctx, Caster);
      Square ClickedSquare = null;
      Square DestSquare = null;

      if (Pending?.TargetSquare != null &&
          Pending.TargetSquare.Map == OriginSquare.Map &&
          OriginSquare.AsRange(Pending.TargetSquare) <= MaxRange)
      {
        ClickedSquare = Pending.TargetSquare;
        if (DotaCombatHelper.IsOpenSquare(ClickedSquare))
        {
          DestSquare = ClickedSquare;
        }
        else
        {
          // 点击到敌人、墙体或未探索虚空：
          // 1) 若点击格有角色，优先穿到目标身后 1 格；
          // 2) 否则像相位结束弹出一样，在目标格周围 1~2 格寻找最近的可站立安全空地；
          // 3) 若周围全是实心岩壁/虚空，则沿飞行路径退回撞墙前的最后一个安全空地，绝不卡死。
          if (ClickedSquare.Character != null)
          {
            var PushDir = OriginSquare.AsDirection(ClickedSquare);
            var Behind = ClickedSquare.Adjacent(PushDir, 1);
            if (Behind != null && Behind != ClickedSquare && DotaCombatHelper.IsOpenSquare(Behind))
              DestSquare = Behind;
          }

          DestSquare ??= FindNearestOpenSquare(ClickedSquare, MaxSearchRadius: 2);

          if (DestSquare == null)
          {
            var WalkBack = OriginSquare;
            int Steps = 0;
            while (WalkBack != null && WalkBack != ClickedSquare && Steps++ < MaxRange + 2)
            {
              if (WalkBack != OriginSquare && DotaCombatHelper.IsOpenSquare(WalkBack))
                DestSquare = WalkBack;
              var StepDir = WalkBack.AsDirection(ClickedSquare);
              var Next = WalkBack.Adjacent(StepDir, 1);
              if (Next == null || Next == WalkBack)
                break;
              WalkBack = Next;
            }
          }
        }
      }
      else
      {
        // 方向施法回退：沿施法方向突进至射程内最远可站立空地
        var Cursor = OriginSquare;
        for (int Step = 1; Step <= MaxRange; Step++)
        {
          var Next = Cursor.Adjacent(Args.Direction, 1);
          if (Next == null || Next == Cursor || Next.IsVoid())
            break;
          Cursor = Next;
          if (DotaCombatHelper.IsOpenSquare(Cursor))
            DestSquare = Cursor;
        }
      }

      if (DestSquare != null && DestSquare != Caster.Square)
        PathosEngineBridge.DashCharacter(Args.Engine, Caster, DestSquare);
      else
        DestSquare = Caster.Square;

      int Tier = DotaCombatHelper.GetSkillTier(Ctx, Caster, Ctx.Skills.transmutation);
      int Idx = Math.Clamp(Tier - 1, 0, WaveformDotaDamages.Length - 1);
      int Radius = WaveformRadii[Idx];
      int EvasionTurns = WaveformEvasionTurns[Idx];
      Dice BaseDice = DotaMechanicsMapper.MapSkillDamageDice(WaveformDotaDamages[Idx]);
      float StarMult = GetWaveformStarDamageMultiplier(Caster);

      // 施加短暂穿墙与幻影闪避
      PathosEngineBridge.ApplyTransient(Args.Engine, Caster, Caster, Ctx.Properties.phasing, Dice.Fixed(EvasionTurns));
      PathosEngineBridge.ApplyTransient(Args.Engine, Caster, Caster, Ctx.Properties.displacement, Dice.Fixed(EvasionTurns));

      // 收集「冲锋路径（含 1 格波宽） + 落点 Radius 格范围」内的全部存活敌方单位（去重、不伤自己与友军）
      var HitTargets = new HashSet<Character>();

      void CollectAround(Square Center, int R)
      {
        if (Center == null)
          return;
        if (DotaCombatHelper.IsHostile(Caster, Center.Character))
          HitTargets.Add(Center.Character);
        foreach (var Sq in Center.GetAroundSquares(R))
        {
          if (Sq?.Character != null && DotaCombatHelper.IsHostile(Caster, Sq.Character))
            HitTargets.Add(Sq.Character);
        }
      }

      // 1) 沿途路径步进扫描（还原 DOTA2 波浪形态直线冲锋刮怪）
      var Walk = OriginSquare;
      int Guard = 0;
      while (Walk != null && Walk != DestSquare && Guard++ < MaxRange + 4)
      {
        CollectAround(Walk, 1);
        var StepDir = Walk.AsDirection(DestSquare);
        var Next = Walk.Adjacent(StepDir, 1);
        if (Next == null || Next == Walk)
          break;
        Walk = Next;
      }

      // 2) 落点大范围水波 AOE（2~3 格半径）+ 若点在敌人身上确保目标格也纳入命中
      CollectAround(DestSquare, Radius);
      if (ClickedSquare != null && ClickedSquare != DestSquare)
        CollectAround(ClickedSquare, 1);

      // 播放水浪残影 Beam 与落点 3×3 潮汐水爆 VFX
      if (WaveformDashBeam != null && OriginSquare != DestSquare && OriginSquare.Map == DestSquare.Map)
        DotaCombatHelper.PlayBeamTrail(Args.Engine, OriginSquare, DestSquare, WaveformDashBeam);

      PathosEngineBridge.StoryEmitting(Args.Engine, DestSquare, Ctx.Sonics.water_splash, Audibility: 8);
      PathosEngineBridge.StoryExploding(Args.Engine, Delay.HalfSecond, DestSquare, WaveformSplashExplosion ?? Ctx.Explosions.watery);

      // 对全部命中敌人结算水波 AOE 伤害
      foreach (var Enemy in HitTargets)
      {
        if (Enemy == null || Enemy.Deceased || Enemy.Square == null)
          continue;

        int Rolled = DotaCombatHelper.RollDice(BaseDice);
        int FinalDmg = StarMult > 1.001f
          ? Math.Max(Rolled + 1, (int)MathF.Round(Rolled * StarMult))
          : Rolled;
        var HitDir = DestSquare.AsDirection(Enemy.Square);
        PathosEngineBridge.HarmCharacter(Args.Engine, Caster, Enemy, HitDir, Ctx.Elements.water, Dice.Fixed(FinalDmg), Modifier.Zero, "waveform");
      }
    }

    private static Square FindNearestOpenSquare(Square Center, int MaxSearchRadius)
    {
      if (Center == null || MaxSearchRadius <= 0)
        return null;

      Square Best = null;
      int BestDist = int.MaxValue;
      foreach (var Sq in Center.GetAroundSquares(MaxSearchRadius))
      {
        if (!DotaCombatHelper.IsOpenSquare(Sq))
          continue;

        int D = Center.AsRange(Sq);
        if (D < BestDist)
        {
          BestDist = D;
          Best = Sq;
        }
      }
      return Best;
    }

    private void ExecuteAdaptiveStrike(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster?.Square == null)
        return;

      var Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      int CastRange = GetAdaptiveStrikeRange(Ctx, Caster);
      var Target = DotaCombatHelper.ResolveHostileTarget(Caster, Args.Direction, CastRange, Pending, AllowDirectionFallback: true);

      if (Target == null || Target == Caster || Target.Deceased || Target.Square == null)
      {
        int Refund = Args.Spell?.Mana ?? AdaptiveStrikeManaCost;
        DotaCombatHelper.RefundMana(Caster, Refund);
        PathosEngineBridge.WriteInternalLog(Args.Engine, Caster, Args.Spell?.Glyph, DotaLocalization.T("morph.log.adaptive_no_target"));
        return;
      }

      int Tier = DotaCombatHelper.GetSkillTier(Ctx, Caster, Ctx.Skills.evocation);
      int Idx = Math.Clamp(Tier - 1, 0, AdaptiveStrikeDotaDamages.Length - 1);
      int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(Caster, "dexterity"));
      int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(Caster, "strength"));

      // 1. 基础水伤骰 + 敏捷比缩放爆发伤害（叠加升星增幅）
      Dice BaseDice = DotaMechanicsMapper.MapSkillDamageDice(AdaptiveStrikeDotaDamages[Idx]);
      int BaseRoll = DotaCombatHelper.RollDice(BaseDice);
      float DexScale = Math.Clamp((Dex / (float)Str - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
      float MaxAgiMult = GetAdaptiveStrikeMaxAgiMultiplier(Tier);
      float StarMult = GetAdaptiveStrikeStarDamageMultiplier(Caster);
      int BonusDamage = (int)MathF.Round(Dex * (AdaptiveStrikeMinAgiMult + (MaxAgiMult - AdaptiveStrikeMinAgiMult) * DexScale) * StarMult);
      int TotalDamage = Math.Max(1, BaseRoll + BonusDamage);

      Direction HitDir = Caster.Square.AsDirection(Target.Square);
      PathosEngineBridge.StoryEmitting(Args.Engine, Target.Square, Ctx.Sonics.water_splash, Audibility: 7);
      PathosEngineBridge.StoryStriking(Args.Engine, Delay.HalfSecond, Target.Square, AdaptiveStrikeImpactStrike ?? Ctx.Strikes.frost);
      PathosEngineBridge.HarmCharacter(Args.Engine, Caster, Target, HitDir, Ctx.Elements.water, Dice.Fixed(TotalDamage), Modifier.Zero, "adaptive strike");

      if (Target.Deceased || Target.Square == null)
        return;

      // 2. 力量比动态缩放眩晕（仅对敌方 Target 生效，绝不会眩晕施法者自己）
      float StrScale = Math.Clamp((Str / (float)Dex - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
      int MaxStun = GetAdaptiveStrikeBaseMaxStunTurns(Tier) + GetAdaptiveStrikeExtraMaxStun(Caster);
      int FinalStun = Math.Max(1, (int)MathF.Round(1.0f + (MaxStun - 1.0f) * StrScale));
      PathosEngineBridge.ApplyTransient(Args.Engine, Caster, Target, Ctx.Properties.stunned, Dice.Fixed(FinalStun));

      // 3. 单体安全击退（低力推 1 格，高力推 2 格，仅推入空地）
      int KnockSteps = StrScale >= 0.60f ? 2 : 1;
      for (int Step = 0; Step < KnockSteps; Step++)
      {
        var NextSq = Target.Square?.Adjacent(HitDir, 1);
        if (NextSq == null || NextSq == Target.Square || !DotaCombatHelper.IsOpenSquare(NextSq))
          break;
        PathosEngineBridge.DashCharacter(Args.Engine, Target, NextSq);
      }
    }

    private void ExecuteMorph(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster?.Square == null)
        return;

      var Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      int CastRange = GetMorphRange(Ctx, Caster);
      var Target = DotaCombatHelper.ResolveHostileTarget(Caster, Args.Direction, CastRange, Pending, AllowDirectionFallback: true);

      if (Target == null || Target == Caster || Target.Deceased || Target.Entity == null)
      {
        int Refund = Args.Spell?.Mana ?? MorphManaCost;
        DotaCombatHelper.RefundMana(Caster, Refund);
        PathosEngineBridge.WriteInternalLog(Args.Engine, Caster, Args.Spell?.Glyph ?? MorphGlyph, DotaLocalization.T("morph.log.morph_no_target"));
        return;
      }

      bool Applied = PathosEngineBridge.ImpersonateCharacter(Args.Engine, Caster, Target);
      if (!Applied || !PathosEngineBridge.IsPolymorphed(Caster))
        PathosEngineBridge.PolymorphCharacter(Args.Engine, Caster, Target.Entity);

      PathosEngineBridge.StoryEmitting(Args.Engine, Caster.Square, Ctx.Sonics.water_splash, Audibility: 6);
      if (WaveformSplashExplosion != null)
        PathosEngineBridge.StoryExploding(Args.Engine, Delay.HalfSecond, Caster.Square, WaveformSplashExplosion);
    }

    private static void ExecuteAttributeShift(DotaHeroContext Ctx, SpellCastEventArgs Args, bool ToAgility)
    {
      var Character = Args.Character;
      if (Character?.CurrentForm == null || Character.MaximumForm == null)
        return;

      int Step = DotaCombatHelper.GetSkillTier(Ctx, Character, Ctx.Skills.transmutation) +
                 GetAttributeShiftExtraStep(Character);

      int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Character, "strength");
      int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Character, "constitution");
      int CurDex = PathosEngineBridge.GetCurrentAbilityScore(Character, "dexterity");

      int MaxStr = PathosEngineBridge.GetLimitAbilityScore(Character, "strength");
      int MaxCon = PathosEngineBridge.GetLimitAbilityScore(Character, "constitution");
      int MaxDex = PathosEngineBridge.GetLimitAbilityScore(Character, "dexterity");

      int Shift;
      if (ToAgility)
      {
        int SourceAvail = Math.Min(CurStr - MinAttributeScore, CurCon - MinAttributeScore);
        int TargetRoom = MaxDex - CurDex;
        Shift = Math.Min(Step, Math.Min(SourceAvail, TargetRoom));

        if (Shift <= 0)
        {
          PathosEngineBridge.WriteInternalLog(
            Args.Engine,
            Character,
            Args.Spell.Glyph,
            DotaLocalization.T("morph.log.shift_agi_cap"));
          return;
        }

        PathosEngineBridge.SetPermanentAbilityScore(Character, "dexterity", CurDex + Shift);
        PathosEngineBridge.SetPermanentAbilityScore(Character, "strength", CurStr - Shift);
        PathosEngineBridge.SetPermanentAbilityScore(Character, "constitution", CurCon - Shift);

        // 血量联动：转敏仅降低最大生命值，当前生命值保持不变（仅在超过新上限时截断）
        int HpDelta = Shift * HpPerShiftedPoint;
        var Life = Character.Life;
        if (Life != null)
        {
          int NewTotal = Math.Max(MinMaxLifePoints, Life.Total - HpDelta);
          int NewPoints = Math.Clamp(Life.Points, 1, NewTotal);
          PathosEngineBridge.SetMeter(Life, NewPoints, NewTotal);
        }

        if (Character.Square != null)
        {
          PathosEngineBridge.StoryEmitting(Args.Engine, Character.Square, Ctx.Sonics.water_splash, Audibility: 4);
          PathosEngineBridge.StoryCaption(Args.Engine, Character.Square, Colour.DeepSkyBlue, DotaLocalization.Format("morph.caption.shift_agi", Shift), Delayed: false);
        }

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Character,
          Args.Spell.Glyph,
          DotaLocalization.Format("morph.log.shift_agi_ok", Shift, HpDelta));
      }
      else
      {
        int SourceAvail = CurDex - MinAttributeScore;
        int TargetRoom = Math.Min(MaxStr - CurStr, MaxCon - CurCon);
        Shift = Math.Min(Step, Math.Min(SourceAvail, TargetRoom));

        if (Shift <= 0)
        {
          PathosEngineBridge.WriteInternalLog(
            Args.Engine,
            Character,
            Args.Spell.Glyph,
            DotaLocalization.T("morph.log.shift_str_cap"));
          return;
        }

        PathosEngineBridge.SetPermanentAbilityScore(Character, "strength", CurStr + Shift);
        PathosEngineBridge.SetPermanentAbilityScore(Character, "constitution", CurCon + Shift);
        PathosEngineBridge.SetPermanentAbilityScore(Character, "dexterity", CurDex - Shift);

        // DOTA2 血量联动：转力同步提升最大生命与当前生命（1 血极限转力反杀）
        int HpDelta = Shift * HpPerShiftedPoint;
        var Life = Character.Life;
        if (Life != null)
        {
          int NewTotal = Life.Total + HpDelta;
          int NewPoints = Math.Min(NewTotal, Life.Points + HpDelta);
          PathosEngineBridge.SetMeter(Life, NewPoints, NewTotal);
        }

        if (Character.Square != null)
        {
          PathosEngineBridge.StoryEmitting(Args.Engine, Character.Square, Ctx.Sonics.water_splash, Audibility: 4);
          PathosEngineBridge.StoryCaption(Args.Engine, Character.Square, Colour.OrangeRed, DotaLocalization.Format("morph.caption.shift_str", Shift, HpDelta), Delayed: false);
        }

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Character,
          Args.Spell.Glyph,
          DotaLocalization.Format("morph.log.shift_str_ok", Shift, HpDelta));
      }
    }

    private static int GetStrRangeBonus(Character Character)
    {
      if (!IsMorphling(Character))
        return 0;
      int Str = PathosEngineBridge.GetCurrentAbilityScore(Character, "strength");
      return Math.Max(0, (Str - 10) / 5);
    }

    private static float GetWaveformStarDamageMultiplier(Character Character)
      => 1.0f + Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillWaveformId) - 1) * 0.15f;

    private static int GetWaveformExtraRange(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillWaveformId) - 1);

    private static float GetAdaptiveStrikeStarDamageMultiplier(Character Character)
      => 1.0f + Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillAdaptiveStrikeId) - 1) * 0.15f;

    private static int GetAdaptiveStrikeExtraMaxStun(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillAdaptiveStrikeId) - 1);

    private static int GetAttributeShiftExtraStep(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillAttributeShiftId) - 1);

    private static int GetMorphExtraRange(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillMorphId) - 1);

    private static int GetWaveformTalentRangeBonus(Character Character)
      => TalentService.HasTalent(Character, TalentT2WaveformRange) ? TalentWaveformRangeBonus : 0;

    private static int GetWaveformRange(DotaHeroContext Ctx, Character Caster)
    {
      var Qual = PathosEngineBridge.GetQualification(Caster, Ctx.Skills.transmutation);
      int BaseRange = Qual == Ctx.Qualifications.champion ? 11
        : Qual == Ctx.Qualifications.master ? 9
        : Qual == Ctx.Qualifications.expert ? 7
        : Qual == Ctx.Qualifications.specialist ? 6
        : 5;
      return BaseRange + GetStrRangeBonus(Caster) + GetWaveformExtraRange(Caster) + GetWaveformTalentRangeBonus(Caster);
    }

    private static int GetAdaptiveStrikeRange(DotaHeroContext Ctx, Character Caster)
    {
      var Qual = PathosEngineBridge.GetQualification(Caster, Ctx.Skills.evocation);
      int BaseRange = Qual == Ctx.Qualifications.champion ? 9
        : Qual == Ctx.Qualifications.master ? 8
        : Qual == Ctx.Qualifications.expert ? 7
        : Qual == Ctx.Qualifications.specialist ? 6
        : 5;
      return BaseRange + GetStrRangeBonus(Caster);
    }

    private static int GetMorphRange(DotaHeroContext Ctx, Character Caster)
    {
      var Qual = PathosEngineBridge.GetQualification(Caster, Ctx.Skills.transmutation);
      int BaseRange = Qual == Ctx.Qualifications.champion ? 10
        : Qual == Ctx.Qualifications.master ? 8
        : Qual == Ctx.Qualifications.expert ? 7
        : Qual == Ctx.Qualifications.specialist ? 6
        : 5;
      return BaseRange + GetStrRangeBonus(Caster) + GetMorphExtraRange(Caster);
    }

    private static bool IsMorphling(Character Character)
      => DotaCombatHelper.IsClass(Character, MorphlingClassName);

    private static readonly float[] WaveformDotaDamages = { 75.0f, 150.0f, 225.0f, 300.0f, 360.0f };
    private static readonly int[] WaveformBaseRanges = { 5, 6, 7, 9, 11 };
    private static readonly int[] WaveformRadii = { 2, 2, 2, 3, 3 };
    private static readonly int[] WaveformEvasionTurns = { 3, 4, 4, 5, 6 };

    // DOTA2 7.42 变体打击核实数值：基础 50/70/90/110（Champion 130）；敏捷倍率底限 0.35×、上限随熟练度 0.90/1.10/1.30/1.50/1.70×；最长眩晕 1.2/1.6/2.0/2.4s（Champion 2.8s）
    private static readonly float[] AdaptiveStrikeDotaDamages = { 50.0f, 70.0f, 90.0f, 110.0f, 130.0f };
    private const float AdaptiveStrikeMinAgiMult = 0.35f;
    private static readonly float[] AdaptiveStrikeMaxAgiMults = { 0.90f, 1.10f, 1.30f, 1.50f, 1.70f };
    private static readonly float[] AdaptiveStrikeMaxStunSeconds = { 1.2f, 1.6f, 2.0f, 2.4f, 2.8f };
    private static readonly int[] AdaptiveStrikeBaseRanges = { 5, 6, 7, 8, 9 };
    private static readonly int[] MorphBaseRanges = { 5, 6, 7, 8, 10 };

    private static float GetAdaptiveStrikeMaxAgiMultiplier(int Tier)
      => AdaptiveStrikeMaxAgiMults[Math.Clamp(Tier - 1, 0, AdaptiveStrikeMaxAgiMults.Length - 1)];

    private static int GetAdaptiveStrikeBaseMaxStunTurns(int Tier)
      => DotaMechanicsMapper.MapDurationTurns(AdaptiveStrikeMaxStunSeconds[Math.Clamp(Tier - 1, 0, AdaptiveStrikeMaxStunSeconds.Length - 1)]);

    private static void RegisterSpellHelpSpecs(DotaHeroContext Ctx)
    {
      // 1. Q：波浪形态
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "waveform",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation);
          int Idx = Math.Clamp(Tier - 1, 0, 4);
          int BaseRange = WaveformBaseRanges[Idx];
          int StrBonus = GetStrRangeBonus(Ch);
          int StarRange = GetWaveformExtraRange(Ch);
          int TalentRange = GetWaveformTalentRangeBonus(Ch);
          float StarMult = GetWaveformStarDamageMultiplier(Ch);
          Dice WaveDice = DotaMechanicsMapper.MapSkillDamageDice(WaveformDotaDamages[Idx]);
          int Radius = WaveformRadii[Idx];
          int Evasion = WaveformEvasionTurns[Idx];
          int TotalRange = BaseRange + StrBonus + StarRange + TalentRange;
          string ExtraTags =
            (StrBonus > 0 ? $" + {DotaHelpColor.Passive(DotaLocalization.Format("morph.help.ebb_tag", StrBonus))}" : string.Empty) +
            (StarRange > 0 ? $" + {DotaHelpColor.Skill(DotaLocalization.Format("morph.help.star_range_tag", StarRange))}" : string.Empty) +
            (TalentRange > 0 ? $" + {DotaHelpColor.Talent(DotaLocalization.Format("morph.help.talent_range_tag", TalentRange))}" : string.Empty);
          string RangeDetail = !string.IsNullOrEmpty(ExtraTags)
            ? DotaLocalization.Format(
                "morph.help.range_detail",
                DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", TotalRange)),
                BaseRange,
                ExtraTags,
                string.Empty)
            : DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", BaseRange));
          string StarDmgNote = StarMult > 1.001f
            ? DotaLocalization.Format("ui.common.comma_item", DotaHelpColor.Skill(DotaLocalization.Format("jugg.help.tag_star_mult", $"{StarMult:0.00}")))
            : string.Empty;

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.q.key_range"), RangeDetail),
            (DotaLocalization.T("morph.help.q.key_dmg"), DotaLocalization.Format(
              "morph.help.q.val_dmg",
              DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(WaveDice)),
              DotaHelpColor.Magic($"{WaveDice.Average() * StarMult:0.#}"),
              StarDmgNote,
              DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", Radius)))),
            (DotaLocalization.T("morph.help.q.key_mobility"), DotaLocalization.Format(
              "morph.help.q.val_mobility",
              DotaHelpColor.Control(DotaLocalization.Format("jugg.help.turns_val", Evasion))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.q.mech_1", DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name")))
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int Idx = Tier - 1;
            Dice WaveDice = DotaMechanicsMapper.MapSkillDamageDice(WaveformDotaDamages[Idx]);
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.Format("jugg.help.cast_range_suffix", WaveformBaseRanges[Idx]),
              Bullets = new[]
              {
                DotaLocalization.Format(
                  "morph.help.q.tier_bullet",
                  DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(WaveDice)),
                  DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", WaveformRadii[Idx])),
                  DotaHelpColor.Control(DotaLocalization.Format("jugg.help.turns_val", WaveformEvasionTurns[Idx])))
              }
            });
          }
          return List;
        }
      });

      // 2. W：变体打击
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "adaptive strike",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.evocation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.evocation);
          int Idx = Math.Clamp(Tier - 1, 0, 4);
          int BaseRange = AdaptiveStrikeBaseRanges[Idx];
          int StrBonus = GetStrRangeBonus(Ch);
          Dice BaseDice = DotaMechanicsMapper.MapSkillDamageDice(AdaptiveStrikeDotaDamages[Idx]);

          int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity"));
          int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength"));

          float DexScale = Math.Clamp((Dex / (float)Str - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
          float MaxAgiMult = GetAdaptiveStrikeMaxAgiMultiplier(Tier);
          float AdaptiveStarMult = GetAdaptiveStrikeStarDamageMultiplier(Ch);
          int BonusDmg = (int)MathF.Round(Dex * (AdaptiveStrikeMinAgiMult + (MaxAgiMult - AdaptiveStrikeMinAgiMult) * DexScale) * AdaptiveStarMult);

          float StrScale = Math.Clamp((Str / (float)Dex - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
          int MaxStun = GetAdaptiveStrikeBaseMaxStunTurns(Tier) + GetAdaptiveStrikeExtraMaxStun(Ch);
          int FinalStun = Math.Max(1, (int)MathF.Round(1.0f + (MaxStun - 1.0f) * StrScale));

          int TotalMin = DotaSpellHelpBridge.GetDiceMin(BaseDice) + BonusDmg;
          int TotalMax = DotaSpellHelpBridge.GetDiceMax(BaseDice) + BonusDmg;
          float TotalAvg = BaseDice.Average() + BonusDmg;

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.w.key_range"), StrBonus > 0
              ? DotaLocalization.Format(
                  "morph.help.range_detail",
                  DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", BaseRange + StrBonus)),
                  BaseRange,
                  $" + {DotaHelpColor.Passive(DotaLocalization.Format("morph.help.ebb_tag", StrBonus))}",
                  string.Empty)
              : DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", BaseRange))),
            (DotaLocalization.T("morph.help.w.key_dmg"), DotaLocalization.Format(
              "morph.help.w.val_dmg",
              DotaHelpColor.Magic($"{TotalMin}~{TotalMax}"),
              DotaHelpColor.Magic($"{TotalAvg:0.#}"),
              DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceNotation(BaseDice)),
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.w.agi_bonus", BonusDmg)))),
            (DotaLocalization.T("morph.help.w.key_stun"), DotaLocalization.Format(
              "morph.help.w.val_stun",
              DotaHelpColor.Control(DotaLocalization.Format("jugg.help.turns_val", FinalStun)),
              DotaHelpColor.Control(DotaLocalization.Format("jugg.help.turns_val", MaxStun)))),
            (DotaLocalization.T("morph.help.w.key_ratio"), DotaLocalization.Format(
              "morph.help.w.val_ratio",
              DotaHelpColor.Stat(DotaLocalization.Format("jugg.help.dex_val", Dex)),
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.str_val", Str))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "morph.help.w.mech_1",
            DotaHelpColor.Magic(DotaLocalization.T("morph.help.w.tag_base_water")),
            DotaHelpColor.Stat(DotaLocalization.T("morph.help.w.tag_agi_scale")),
            DotaHelpColor.Control(DotaLocalization.T("morph.help.w.tag_str_stun")))
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int Idx = Tier - 1;
            Dice BaseDice = DotaMechanicsMapper.MapSkillDamageDice(AdaptiveStrikeDotaDamages[Idx]);
            int MaxStun = GetAdaptiveStrikeBaseMaxStunTurns(Tier);
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.Format("jugg.help.cast_range_suffix", AdaptiveStrikeBaseRanges[Idx]),
              Bullets = new[]
              {
                DotaLocalization.Format(
                  "morph.help.w.tier_bullet",
                  DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(BaseDice)),
                  DotaHelpColor.Control(DotaLocalization.Format("jugg.help.turns_val", $"1~{MaxStun}")))
              }
            });
          }
          return List;
        }
      });

      // 3. E1：属性转换·敏捷
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "morph agility",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Step = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation) + GetAttributeShiftExtraStep(Ch);
          int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength");
          int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Ch, "constitution");
          int CurDex = PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity");
          int MaxDex = PathosEngineBridge.GetLimitAbilityScore(Ch, "dexterity");

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.e.key_shift"),
              $"{DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e1.str_con_minus", Step))} → {DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e1.agi_plus", Step))} ({DotaHelpColor.Mana(DotaLocalization.T("morph.help.e.zero_mana"))})"),
            (DotaLocalization.T("morph.help.e.key_hp"), DotaLocalization.Format(
              "morph.help.e1.val_hp",
              DotaHelpColor.Physical($"-{Step * HpPerShiftedPoint} HP"))),
            (DotaLocalization.T("morph.help.e.key_attrs"), DotaLocalization.Format(
              "morph.help.e1.val_attrs",
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.str_val", CurStr)),
              DotaHelpColor.Stat(DotaLocalization.Format("jugg.help.dex_val", CurDex)),
              MaxDex,
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.con_val", CurCon)),
              MinAttributeScore))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.e1.mech_1", MinAttributeScore, MinMaxLifePoints)
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.T("morph.help.self_zero_mana"),
              Bullets = new[]
              {
                DotaLocalization.Format(
                  "morph.help.e1.tier_bullet",
                  DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e.pts_val", Tier)),
                  DotaHelpColor.Physical($"-{Tier * HpPerShiftedPoint} HP"))
              }
            });
          }
          return List;
        }
      });

      // 4. E2：属性转换·力量
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "morph strength",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Step = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation) + GetAttributeShiftExtraStep(Ch);
          int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength");
          int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Ch, "constitution");
          int CurDex = PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity");
          int MaxStr = PathosEngineBridge.GetLimitAbilityScore(Ch, "strength");

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.e.key_shift"),
              $"{DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e2.agi_minus", Step))} → {DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e2.str_con_plus", Step))} ({DotaHelpColor.Mana(DotaLocalization.T("morph.help.e.zero_mana"))})"),
            (DotaLocalization.T("morph.help.e.key_hp"), DotaLocalization.Format(
              "morph.help.e2.val_hp",
              DotaHelpColor.Heal($"+{Step * HpPerShiftedPoint} HP"))),
            (DotaLocalization.T("morph.help.e.key_attrs"), DotaLocalization.Format(
              "morph.help.e2.val_attrs",
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.str_val", CurStr)),
              MaxStr,
              DotaHelpColor.Stat(DotaLocalization.Format("jugg.help.dex_val", CurDex)),
              MinAttributeScore,
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.con_val", CurCon))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.e2.mech_1", MinAttributeScore)
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.T("morph.help.self_zero_mana"),
              Bullets = new[]
              {
                DotaLocalization.Format(
                  "morph.help.e2.tier_bullet",
                  DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e.pts_val", Tier)),
                  DotaHelpColor.Heal($"+{Tier * HpPerShiftedPoint} HP"))
              }
            });
          }
          return List;
        }
      });

      // 5. R1：变形
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "morph",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation);
          int BaseRange = MorphBaseRanges[Math.Clamp(Tier - 1, 0, 4)];
          int StrBonus = GetStrRangeBonus(Ch);
          int StarRange = GetMorphExtraRange(Ch);
          int TotalRange = BaseRange + StrBonus + StarRange;

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.r.key_range"), (StrBonus > 0 || StarRange > 0)
              ? DotaLocalization.Format(
                  "morph.help.range_detail",
                  DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", TotalRange)),
                  BaseRange,
                  StrBonus > 0 ? $" + {DotaHelpColor.Passive(DotaLocalization.Format("morph.help.ebb_tag", StrBonus))}" : string.Empty,
                  StarRange > 0 ? $" + {DotaHelpColor.Skill(DotaLocalization.Format("morph.help.star_range_tag", StarRange))}" : string.Empty)
              : DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", BaseRange))),
            (DotaLocalization.T("morph.help.r.key_mech"), DotaLocalization.Format(
              "morph.help.r.val_mech",
              DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name"))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.r.mech_1", DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name")))
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.Format("jugg.help.cast_range_suffix", MorphBaseRanges[Tier - 1]),
              Bullets = new[]
              {
                DotaLocalization.Format(
                  "morph.help.r.tier_bullet",
                  DotaHelpColor.Control(DotaLocalization.Format("jugg.help.tiles_val", MorphBaseRanges[Tier - 1])),
                  DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name")))
              }
            });
          }
          return List;
        }
      });
    }

    private static Property RegisterEbbAndFlowTalent(DotaHeroContext Ctx, Glyph Icon)
      => Ctx.RegisterPassiveProperty(
        Name: "ebb and flow",
        Description: "Innate (Ebb & Flow): Agility grants bonus movement speed and ranged attack range; Strength grants bonus spell cast range and slow resistance. Persists while morphed.",
        Icon: Icon);

    private static Item RegisterTidalOrbItem(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Items.Add(I =>
      {
        I.Name = "tidal orb";
        I.Description = "A condensed sphere of living tidewater launched as infinite ranged ammunition from a sling.";
        I.Glyph = Icon;
        I.Stock = Ctx.Stocks.weapon;
        I.Type = ItemType.RangedMissile;
        I.Grade = Ctx.Grades.standard;
        I.Material = Ctx.Materials.water;
        I.Size = Size.Tiny;
        I.Weight = Weight.Zero;
        I.Price = Gold.Zero;
        I.Rarity = 0;
        I.Sonic = Ctx.Sonics.water_splash;
        I.BundleDice = Dice.One;
        I.InfiniteQuantity = true;

        I.SetEquip(EquipAction.Ready, Delay.OneSecond, Ctx.Sonics.water_splash, Audibility: 5);

        var W = I.SetOneHandedWeapon(
          Ctx.Skills.sling,
          Ctx.Sonics.water_splash,
          Ctx.Elements.water,
          DamageType.Bludgeon,
          1.d6(),
          _ => { });
        W.Ammunition = Ammunition.Pellet;
        W.FixedRange = 6;
      });
    }

    private static void RegisterWaterInteractions(DotaHeroContext Ctx)
    {
      // 水药水（potion_of_water）：水元素饮用（drink）直接吸收水分，回复生命与法力
      Ctx.Items.Edit(Ctx.Items.potion_of_water).AddObviousUse(
        Ctx.Motions.drink,
        Delay.OneSecond,
        Ctx.Sonics.quaff,
        U =>
        {
          U.Consume();
          U.SetCast().SetTargetSelf(true);
          U.Apply.WhenTargetHasMaterial(
            Ctx.Materials.water,
            Then =>
            {
              Then.HealEntity(2.d6(), Modifier.Zero);
              Then.EnergiseEntity(2.d6(), Modifier.Zero);
            },
            Else =>
            {
              Else.HealEntity(Dice.One, Modifier.Zero);
            });
        });

      // 喷泉（fountain）：水元素专属汲取（refill）动作——泡泉水大幅回血回蓝并获得短暂迅捷
      var FountainUse = Ctx.Features.Edit(Ctx.Features.fountain).AddUse(
        Ctx.Motions.refill,
        null,
        Delay.OneSecond,
        Ctx.Sonics.water_fountain,
        Audibility: 10);
      FountainUse.SetCast().SetTargetSelf(true);
      FountainUse.Apply.WhenTargetHasMaterial(
        Ctx.Materials.water,
        Then =>
        {
          Then.HealEntity(3.d6(), Modifier.Zero);
          Then.EnergiseEntity(3.d6(), Modifier.Zero);
          Then.ApplyTransient(Ctx.Properties.quickness, 1.d4());
        },
        Else =>
        {
          Else.Nothing();
        });
    }

    private static Spell RegisterWaveformSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "waveform";
        S.Description = "Dissolve into a surging wave, blinking forward, splashing water damage around you, and briefly gaining phasing and displacement.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Teleport);
        S.Level = DotaMechanicsMapper.MapSpellLevel(WaveformManaCost);
        S.Mana = WaveformManaCost;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          // 声明式仅保留全射程施法外壳，精准突进、路径+落点全范围敌方 AOE 伤害与穿墙/闪避状态由 Hook 全权结算
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.Nothing();
        }

        // DOTA2 7.42 波浪形态基础伤害：75 / 150 / 225 / 300（Champion 360）在 Hook 中经 K_dmg = 15 自动生成伤害骰
        Configure(Ctx.Qualifications.proficient, Range: 5);
        Configure(Ctx.Qualifications.specialist, Range: 6);
        Configure(Ctx.Qualifications.expert, Range: 7);
        Configure(Ctx.Qualifications.master, Range: 9);
        Configure(Ctx.Qualifications.champion, Range: 11);
      });
    }

    private static Spell RegisterAdaptiveStrikeSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "adaptive strike";
        S.Description = "Launch a surge of water dealing base damage, knocking back, and stunning the target. Bonus damage scales with Agility-to-Strength ratio while stun duration scales with Strength-to-Agility ratio.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.water);
        S.Level = DotaMechanicsMapper.MapSpellLevel(AdaptiveStrikeManaCost);
        S.Mana = AdaptiveStrikeManaCost;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          // 声明式仅保留全射程施法外壳，真实单体水伤/敏捷爆发/力量击退与眩晕由 Hook 精准锁定敌方目标结算，绝不误伤/自晕
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.Nothing();
        }

        // DOTA2 7.42 变体打击基础伤害：50 / 70 / 90 / 110（Champion 130）在 Hook 中经 K_dmg = 15 掷骰并叠加敏捷爆发
        Configure(Ctx.Qualifications.proficient, 5);
        Configure(Ctx.Qualifications.specialist, 6);
        Configure(Ctx.Qualifications.expert, 7);
        Configure(Ctx.Qualifications.master, 8);
        Configure(Ctx.Qualifications.champion, 9);
      });
    }

    private static Spell RegisterMorphAgilitySpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph agility";
        S.Description = "Shift Strength and Constitution into Agility (1 to 5 points by mastery), conserving total stats and lowering maximum HP without draining current HP.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = DotaMechanicsMapper.MapSpellLevel(AttributeShiftManaCost);
        S.Mana = AttributeShiftManaCost;

        // 声明式仅保留施法外壳，真实属性转换与血量联动由 Hook 全权执行
        void Configure(Qualification Qual)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.Nothing();
        }

        Configure(Ctx.Qualifications.proficient);
        Configure(Ctx.Qualifications.specialist);
        Configure(Ctx.Qualifications.expert);
        Configure(Ctx.Qualifications.master);
        Configure(Ctx.Qualifications.champion);
      });
    }

    private static Spell RegisterMorphStrengthSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph strength";
        S.Description = "Shift Agility into Strength and Constitution (1 to 5 points by mastery), conserving total stats while raising both maximum and current HP.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Healing);
        S.Level = DotaMechanicsMapper.MapSpellLevel(AttributeShiftManaCost);
        S.Mana = AttributeShiftManaCost;

        // 声明式仅保留施法外壳，真实属性转换与血量联动由 Hook 全权执行
        void Configure(Qualification Qual)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.Nothing();
        }

        Configure(Ctx.Qualifications.proficient);
        Configure(Ctx.Qualifications.specialist);
        Configure(Ctx.Qualifications.expert);
        Configure(Ctx.Qualifications.master);
        Configure(Ctx.Qualifications.champion);
      });
    }

    private static Spell RegisterMorphSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph";
        S.Description = "Assume the form of a targeted creature, copying its body, attributes, skills, and spells while retaining Ebb & Flow.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = DotaMechanicsMapper.MapSpellLevel(MorphManaCost);
        S.Mana = MorphManaCost;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          // 声明式仅保留施法外壳，由 Hook 精准解析全射程选中的敌方目标并调用 Engine.ImpersonateCharacter（含兜底 PolymorphCharacter）
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.Nothing();
        }

        Configure(Ctx.Qualifications.proficient, 5);
        Configure(Ctx.Qualifications.specialist, 6);
        Configure(Ctx.Qualifications.expert, 7);
        Configure(Ctx.Qualifications.master, 8);
        Configure(Ctx.Qualifications.champion, 10);
      });
    }

    private static Spell RegisterMorphRevertSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph revert";
        S.Description = "Release your morphed form and immediately return to your true waterkin body.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = DotaMechanicsMapper.MapSpellLevel(MorphRevertManaCost);
        S.Mana = MorphRevertManaCost;

        void Configure(Qualification Qual)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.UnpolymorphEntity();
        }

        Configure(Ctx.Qualifications.proficient);
        Configure(Ctx.Qualifications.specialist);
        Configure(Ctx.Qualifications.expert);
        Configure(Ctx.Qualifications.master);
        Configure(Ctx.Qualifications.champion);
      });
    }

    private static Race RegisterWaterkinRace(DotaHeroContext Ctx, Glyph AvatarGlyph)
    {
      var WaterkinEntity = Ctx.Entities.Add(E =>
      {
        E.Name = "waterkin";
        E.Description = "A sentient elemental forged from living tidewater, capable of reshaping its physical attributes at will.";
        E.Glyph = AvatarGlyph;
        E.Kind = Ctx.Kinds.elemental;
        E.IsBase = true;
        E.IsAnimate = false;
        E.Frequency = Frequency.None;
        E.Level = 0;
        E.Difficulty = 0;
        E.Challenge = 0;
        E.Size = Size.Medium;
        E.Speed = Speed.S5_0;
        E.Weight = Weight.OneHundredPounds;
        E.Defence = new Defence(10, 0, 0, 0);
        E.Strategy = Strategy.Attack;
        E.Sonic = Ctx.Sonics.water_splash;

        E.SetCorpse(Chance.Never, null, null);
        E.SetDiet(Ctx.Diets.omnivore);
        E.SetGender(Ctx.Genders.male, Ctx.Genders.female, Ctx.Genders.nonbinary);
        E.SetTerrain(Ctx.Materials.air, Ctx.Materials.water);

        E.Figure.Material = Ctx.Materials.water;
        E.Figure.CombatSkill = Ctx.Skills.unarmed_combat;
        E.Figure.Set(Ctx.Anatomies.amorphous);
        E.Figure.Set(Ctx.Anatomies.eyes);
        E.Figure.Set(Ctx.Anatomies.hands);
        E.Figure.Set(Ctx.Anatomies.limbs);
        E.Figure.Set(Ctx.Anatomies.mind);
        E.Figure.Set(Ctx.Anatomies.voice);

        Ctx.SetFormScores(
          E.DefaultForm,
          Str: StartingStats.Str,
          Dex: StartingStats.Dex,
          Con: StartingStats.Con,
          Int: StartingStats.Int,
          Wis: StartingStats.Wis,
          Cha: StartingStats.Cha);
        Ctx.SetFormScores(
          E.LimitForm,
          Str: StatLimits.Str,
          Dex: StatLimits.Dex,
          Con: StatLimits.Con,
          Int: StatLimits.Int,
          Wis: StatLimits.Wis,
          Cha: StatLimits.Cha);

        E.LifeAdvancement.Set(0, Dice.Zero);
        E.ManaAdvancement.Set(2, Dice.Fixed(2));

        E.Chemistry.SetWeakness(Ctx.Elements.shock);

        E.Startup.SetResistance(Ctx.Elements.water, Ctx.Elements.cold);
        E.Startup.SetTalent(Ctx.Properties.slippery);
        E.Startup.SetSkill(Ctx.Qualifications.proficient, Ctx.Skills.swimming, Ctx.Skills.unarmed_combat);

        E.AddAttack(Ctx.AttackTypes.splash, Ctx.Elements.water, 1.d6(), _ => { });

        E.AddReaction(Chance.Always, Ctx.Elements.water, A =>
        {
          A.HealEntity(1.d6(), Modifier.Zero);
          A.EnergiseEntity(1.d6(), Modifier.Zero);
        });
        E.AddReaction(Chance.OneIn2, Ctx.Elements.fire, A =>
        {
          A.DrainMana(Ctx.Elements.fire, 1.d4());
        });
      });

      var WaterkinRace = Ctx.Races.Add(R =>
      {
        R.Name = "waterkin";
        R.BaseEntity = WaterkinEntity;
        Ctx.AddRaceAvatars(R, AvatarGlyph);
      });

      Ctx.Entities.Edit(WaterkinEntity).Race = WaterkinRace;

      return WaterkinRace;
    }

    private static Class RegisterMorphlingClass(
      DotaHeroContext Ctx,
      Glyph AvatarGlyph,
      Property EbbAndFlowTalent,
      Item TidalOrbItem,
      Spell WaveformSpell,
      Spell AdaptiveStrikeSpell,
      Spell MorphAgilitySpell,
      Spell MorphStrengthSpell,
      Spell MorphSpell,
      Spell MorphRevertSpell)
    {
      return Ctx.Classes.Add(C =>
      {
        C.Name = MorphlingClassName;
        C.Description = "A fluid DOTA2 hero who commands surging waves, shifts freely between Strength and Agility, empowers himself through Ebb & Flow, and morphs into foes.";
        C.Backpack = Ctx.Items.Backpack;
        C.LifeAdvancement.Set(12, 1.d8());
        int ClassManaBase = DotaMechanicsMapper.ComputeClassManaBase(DotaBaseInt, RaceManaBase: 2, StartingStats.Wis);
        C.ManaAdvancement.Set(ClassManaBase, new Dice(1, 3, 0));

        C.SetDistribution(
          Ctx.Attributes.dexterity,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.charisma);

        Ctx.AddClassAvatars(C, AvatarGlyph);

        // Lv 1 先天技能天赋：在角色天赋栏与选人职业专长中直观显示（且变形期间依然保留）
        C.AddFeat(1, EbbAndFlowTalent);
        C.AddFeat(4, Ctx.Elements.water);
        C.AddFeat(6, Ctx.Properties.polymorph_control);
        C.AddFeat(8, Ctx.Properties.displacement);
        C.AddFeat(12, Ctx.Properties.clarity);

        C.Startup.SetSkill(Ctx.Qualifications.proficient,
          Ctx.Skills.literacy,
          Ctx.Skills.evocation,
          Ctx.Skills.transmutation,
          Ctx.Skills.swimming,
          Ctx.Skills.sling,
          Ctx.Skills.light_armour,
          Ctx.Skills.unarmed_combat);

        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.robe);
        C.Startup.Loot.AddKit(Chance.Always, Modifier.Plus1, Ctx.Items.sling);
        C.Startup.Loot.AddKit(Chance.Always, TidalOrbItem);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.sack);
        C.Startup.Loot.AddKit(Chance.Always, Dice.Two, Ctx.Items.potion_of_water);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
      });
    }
  }
}
