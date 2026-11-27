using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Inv;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Aghanim;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Presentation;
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
    // 属性转换每点联动的生命值统一为 DotaMechanicsMapper.HpPerStrengthPoint；转敏后最大生命保底为 DotaLife.MinMaxLife
    private const int TalentMorphDefenceBonus = 2;
    private const int TalentAttackRangeBonus = 1;
    private const int TalentWaveformRangeBonus = 2;
    private const int TalentAgilityBonus = 3;   // 7.42 +15 敏捷按 1:5 后期属性标尺 -> +3 DEX（同步拓宽上限）
    private const int TalentStrengthBonus = 7;  // 7.42 +35 力量按 1:5 后期属性标尺 -> +7 STR/CON 与 +42 HP（同步拓宽上限）

    // DOTA2 7.42 变体精灵初始三维与成长：STR 16 (+2.8) / AGI 33 (+4.2) / INT 19 (+2.0)
    private static readonly DotaHeroProfile Profile = new(
      MorphlingClassName,
      BaseStr: 16, StrGain: 2.8f,
      BaseAgi: 33, AgiGain: 4.2f,
      BaseInt: 19, IntGain: 2.0f);

    private static readonly int WaveformManaCost = DotaMechanicsMapper.MapSkillManaCost(115);      // 6
    private static readonly int AdaptiveStrikeManaCost = DotaMechanicsMapper.MapSkillManaCost(70); // 7.42 满级 70 蓝 -> 4
    private static readonly int AttributeShiftManaCost = DotaMechanicsMapper.MapToggleManaPerTurn(10.0f); // 7.42a：转换期间每秒 10 蓝 -> 每次（1 回合）1 蓝
    private static readonly int MorphManaCost = DotaMechanicsMapper.MapSkillManaCost(120);         // 原 50 蓝，Pathos Impersonate 强度修正 -> 6
    private static readonly int MorphRevertManaCost = DotaMechanicsMapper.MapSkillManaCost(0);     // 0

    // 7.42 减冷却 / 覆盖率天赋 -> 减蓝耗换算系数
    private static readonly float MorphDurationTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(12.0f, 36.0f); // 24s -> 36s (+50% 覆盖率 = 33.3% 蓝耗折减)
    private static readonly float AdaptiveStrikeTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(3.0f, 8.0f);  // -3s / 8s 满级冷却 = 37.5%
    private const float WaveformTalentCdr = 0.40f; // -40% 波浪形态冷却

    private static readonly ConditionalWeakTable<Character, Square> WaveformOriginSquares = new();
    private const string ScepterEchoSequenceKey = "morphling.scepter_echo";
    private const float ScepterEchoDelaySeconds = 0.2f;
    private const int ScepterEchoRetargetRadius = 3; // 7.42: 450 码 ≈ 3 格
    private static readonly float[] ScepterEchoDamageRatios = { 0.50f, 0.25f }; // 7.42: 50% / 25%

    [ThreadStatic]
    private static bool IsInsideScepterEcho;

    private sealed class MorphSpellEchoCapture
    {
      public Spell Spell;
      public Character PrimaryTarget;
      public int CapturedDamage;
    }

    private static readonly ConditionalWeakTable<Character, MorphSpellEchoCapture> MorphEchoCaptures = new();

    private static DotaAbility WaveformAbility;
    private static DotaAbility AdaptiveStrikeAbility;
    private static DotaAbility MorphAgilityAbility;
    private static DotaAbility MorphStrengthAbility;
    private static DotaAbility MorphAbility;
    private static DotaAbility MorphRevertAbility;

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

      // 1.6 VFX 渲染样式：水流残影半透明淡出，水爆由内向外溅开
      DotaVfx.Style(WaveformDashBeam, new DotaVfxStyle { FadeIn = 0.1f, FadeOutStart = 0.3f, PeakOpacity = 0.8f });
      DotaVfx.Style(WaveformSplashExplosion, new DotaVfxStyle { FadeIn = 0.05f, FadeOutStart = 0.4f, ScaleFrom = 0.55f, ScaleTo = 1.1f });
      DotaVfx.Style(AdaptiveStrikeImpactStrike, new DotaVfxStyle { FadeIn = 0.05f, FadeOutStart = 0.4f, ScaleFrom = 0.8f, ScaleTo = 1.2f });

      // 2. 注册水人专属无限远程普攻：潮汐水弹（tidal orb）；先天「潮涨潮落」零 Property，由 Hook 与状态栏驱动
      var TidalOrbItem = RegisterTidalOrbItem(Ctx, TidalOrbGlyph);

      // 3. 注册水人全套专属技能（Q / W / E1 / E2 / R1 / R2）
      //    （DotaAbility 自动生成 5 阶法术外壳、全射程选格与按法术名的施法分发）
      RegisterAbilities(Ctx, WaveformGlyph, AdaptiveStrikeGlyph, MorphAgilityGlyph, MorphStrengthGlyph, MorphGlyph, MorphRevertGlyph);
      var WaveformSpell = WaveformAbility.Spell;
      var AdaptiveStrikeSpell = AdaptiveStrikeAbility.Spell;
      var MorphAgilitySpell = MorphAgilityAbility.Spell;
      var MorphStrengthSpell = MorphStrengthAbility.Spell;
      var MorphSpell = MorphAbility.Spell;
      var MorphRevertSpell = MorphRevertAbility.Spell;

      // 4. 注册专属种族：变体水灵（waterkin，含遇水回血回蓝与遇火蒸发反应）
      var WaterkinRace = RegisterWaterkinRace(Ctx, MorphlingGlyph);

      // 5. 注册水系环境专属交互（水药水吸收 + 喷泉汲取）
      RegisterWaterInteractions(Ctx);

      // 6. 注册专属职业：变体精灵（morphling）
      var MorphlingClass = RegisterMorphlingClass(
        Ctx,
        MorphlingGlyph,
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

      // 11. 注册变体精灵 DOTA2 7.42 阿哈利姆神杖（变形水影回响）与魔晶（属性转换免蓝+硬控可用）
      RegisterAghanimUpgrades(MorphAgilityGlyph);
    }

    private void RegisterAghanimUpgrades(Glyph AttributeShiftGlyph)
    {
      AghanimRegistry.Register(new HeroAghanimSpec
      {
        ClassName = MorphlingClassName,
        Scepter = new AghanimUpgradeSpec
        {
          AffectedSpellName = MorphSpellName,
          IconGlyph = MorphGlyph,
          TitleResolver = () => DotaLocalization.T("morph.aghanim.scepter.title"),
          SummaryResolver = () => DotaLocalization.T("morph.aghanim.scepter.summary"),
          BuildDetailBullets = _ => new[]
          {
            DotaLocalization.Format("morph.aghanim.scepter.b1", DotaHelpColor.Magic("50%"), DotaHelpColor.Magic("25%")),
            DotaLocalization.Format("morph.aghanim.scepter.b2", DotaHelpColor.Control(DotaHelpFormat.Tiles(ScepterEchoRetargetRadius)))
          }
        },
        Shard = new AghanimUpgradeSpec
        {
          AffectedSpellName = MorphAgilitySpellName,
          IconGlyph = AttributeShiftGlyph,
          TitleResolver = () => DotaLocalization.T("morph.aghanim.shard.title"),
          SummaryResolver = () => DotaLocalization.T("morph.aghanim.shard.summary"),
          BuildDetailBullets = _ => new[]
          {
            DotaLocalization.T("morph.aghanim.shard.b1"),
            DotaLocalization.Format("morph.aghanim.shard.b2", DotaHelpColor.Mana("0"))
          }
        }
      });
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
                $"{HeroSkillService.GetStarMultiplierForLevel(Lv):0.00}",
                GetWaveformExtraRangeForLevel(Lv),
                Lv < 4 ? DotaLocalization.T("ui.skill.desc_next_star_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("morph.skill.q.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.damage_mult", Lv => Lv > 0 ? $"×{HeroSkillService.GetStarMultiplierForLevel(Lv):0.00}" : null),
            DotaSkillStat.Of("ui.stat.bonus_range", Lv => Lv > 0 ? "+" + DotaHelpFormat.Tiles(GetWaveformExtraRangeForLevel(Lv)) : null)
          }),
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
                $"{HeroSkillService.GetStarMultiplierForLevel(Lv):0.00}",
                Lv - 1,
                Lv < 4 ? DotaLocalization.T("ui.skill.desc_next_star_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("morph.skill.w.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.damage_mult", Lv => Lv > 0 ? $"×{HeroSkillService.GetStarMultiplierForLevel(Lv):0.00}" : null),
            DotaSkillStat.Of("ui.stat.bonus_max_stun", Lv => Lv > 0 ? "+" + DotaHelpFormat.Turns(Lv - 1) : null)
          }),
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
          TitleResolver: () => DotaLocalization.T("morph.skill.e.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.bonus_shift_step", Lv => Lv > 0 ? $"+{Lv - 1}" : null)
          }),
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
          TitleResolver: () => DotaLocalization.T("morph.skill.r.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.bonus_cast_range", Lv => Lv > 0 ? "+" + DotaHelpFormat.Tiles(Lv - 1) : null)
          }),
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
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t3l.desc", TalentAgilityBonus))
        {
          StatBonus = PathosStatLimits.Bonus(Dex: TalentAgilityBonus)
        },
        T3Right: new TalentNode(
          Id: TalentT3AdaptiveStrikeMana,
          TitleResolver: () => DotaLocalization.T("morph.talent.t3r.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t3r.desc", AdaptiveStrikeManaCost, ReducedAdaptiveMana),
          ManaReductions: new[] { new SpellManaReductionRule(AdaptiveStrikeSpellName, AdaptiveStrikeTalentCdr) }),
        T4Left: new TalentNode(
          Id: TalentT4Strength,
          TitleResolver: () => DotaLocalization.T("morph.talent.t4l.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t4l.desc", TalentStrengthBonus, TalentStrengthBonus * DotaMechanicsMapper.HpPerStrengthPoint))
        {
          // 上限拓宽 / 永久提升 / +42 HP 生命联动由天赋框架统一结算
          StatBonus = PathosStatLimits.Bonus(Str: TalentStrengthBonus, Con: TalentStrengthBonus)
        },
        T4Right: new TalentNode(
          Id: TalentT4WaveformMana,
          TitleResolver: () => DotaLocalization.T("morph.talent.t4r.title"),
          DescriptionResolver: () => DotaLocalization.Format("morph.talent.t4r.desc", WaveformManaCost, ReducedWaveformMana),
          ManaReductions: new[] { new SpellManaReductionRule(WaveformSpellName, WaveformTalentCdr) }));

      TalentRegistry.Register(Ctx, Tree);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定变体精灵固定初始六维（STR 10 / DEX 18 / CON 10 / INT 12 / WIS 12 / CHA 10）、1 级初始蓝量（15）与 30 级职业属性上限（27 / 34 / 27 / 25 / 25 / 20）
      //    天赋属性上限加成（Lv 20 +15 敏捷 -> DEX+3 上限；Lv 25 +35 力量 -> STR/CON+7 上限）由 TalentNode.StatBonus 自动叠加
      Profile.RegisterStats();

      // 0.1 全射程选格与施法分发由 DotaAbility 自动注册（波浪形态点任意格 / 变体打击 / 变形选敌；属性转换与解除变形自身施放）
      // 0.2 注册左上角状态栏动态徽章：先天「潮涨潮落」实时倾向 & 变形状态
      DotaStatusBarBridge.RegisterProvider("morphling.ebb_flow", MorphlingClassName, Character =>
      {
        int Dex = DotaStats.Dex(Character);
        int Str = DotaStats.Str(Character);
        if (Dex <= 10 && Str <= 10)
          return null;

        Colour BadgeColour = Dex > Str + 2
          ? Colour.DarkCyan
          : Str > Dex + 2
            ? Colour.DarkRed
            : Colour.Black;

        return DotaStatusBadge.ForPassive(
          "morphling.ebb_flow",
          EbbAndFlowGlyph ?? MorphlingGlyph,
          "morphling.ebb_flow",
          "morph.status.ebb_fallback",
          Count: DotaLocalization.Format("morph.status.ebb_count", Dex, Str),
          Colour: BadgeColour);
      });

      DotaStatusBarBridge.RegisterProvider("morphling.morphed", MorphlingClassName, Character =>
      {
        if (!PathosEngineBridge.IsPolymorphed(Character))
          return null;

        bool HasMorphTalent = TalentService.HasTalent(Character, TalentT1MorphDuration);
        return new DotaStatusBadge
        {
          Key = "morphling.morphed",
          Glyph = MorphGlyph ?? MorphlingGlyph,
          Text = DotaStatusBadge.Title("morph.status.morphed"),
          Count = HasMorphTalent ? $"+{TalentMorphDefenceBonus} DEF" : string.Empty,
          Colour = HasMorphTalent ? Colour.DarkBlue : Colour.Black,
          HelpSpell = MorphAbility.Spell
        };
      });

      // 0.3 注册变体精灵主动技能的帮助面板数值与 5 阶熟练度详情（含游戏内实时面板预览）
      RegisterSpellHelpSpecs(Ctx);
      RegisterPassiveHelpSpecs();

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

        float Bonus = GetEbbSpeedBonus(DotaStats.Dex(Args.Character));
        if (Bonus <= 0.0f)
          return;

        Args.Speed = Speed.FromRate(Args.Speed.GetRate() * (1.0f + Bonus));
      });

      // 先天技能「潮涨潮落」— 敏捷提升远程攻击距离 + T2 左天赋（+75 攻击距离 -> +1 格，变形为远程生物同样保留）
      DotaHookBus.RegisterAttackRangeQuery("morphling.ebb_flow.attack_range", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        int BonusRange = GetEbbAttackRangeBonus(Args.Character);

        if (BonusRange > 0)
          Args.Range += BonusRange;
      });

      // 先天技能「潮涨潮落」— 力量提升施法距离（含变形后窃取的技能）
      DotaHookBus.RegisterCastRange("morphling.ebb_flow.cast_range", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        int BonusRange = GetStrRangeBonus(Args.Character);
        if (BonusRange > 0)
          Args.BonusRange += BonusRange;
      });

      // 先天技能「潮涨潮落」— 力量提供减速抗性（高力直接免疫，中力大幅缩短减速时间）
      DotaHookBus.RegisterBeforeApplyTransient("morphling.transient_hooks", Args =>
      {
        if (Args.Property == Ctx.Properties.slowness && IsMorphling(Args.Target))
        {
          float Resist = GetEbbSlowResist(DotaStats.Str(Args.Target));
          if (Resist >= 1.0f)
          {
            Args.Cancel = true;
            return;
          }

          if (Resist > 0.0f)
          {
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

      // T1 右天赋（+15% 魔法抗性）：魔法/未知来源伤害减免 15%（走 DotaDamage 魔抗管线，与其他魔抗乘算叠加，不减免纯粹/物理伤害）
      DotaDamage.RegisterMagicResist("morphling.talent_magic_resist", Args =>
        IsMorphling(Args.Defender) && TalentService.HasTalent(Args.Defender, TalentT1MagicResist) ? 0.15f : 0.0f);

      // 7.42 魔晶：属性转换（E1/E2）蓝耗降至 0，且在眩晕/硬控状态仍可施放
      DotaHookBus.RegisterSpellCostQuery("morphling.shard_shift_mana", Args =>
      {
        if (Args.Character == null || Args.Spell == null || !IsMorphling(Args.Character) || !AghanimService.HasShard(Args.Character))
          return;

        if (string.Equals(Args.Spell.Name, MorphAgilitySpellName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Args.Spell.Name, MorphStrengthSpellName, StringComparison.OrdinalIgnoreCase))
        {
          Args.ManaCost = 0;
        }
      });

      DotaHookBus.RegisterForceAllowCastQuery("morphling.shard_shift_stunned", (Ch, Spell) =>
      {
        if (Ch == null || !IsMorphling(Ch) || !AghanimService.HasShard(Ch))
          return false;

        if (Spell == null)
          return true;

        return string.Equals(Spell.Name, MorphAgilitySpellName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Spell.Name, MorphStrengthSpellName, StringComparison.OrdinalIgnoreCase);
      });

      // 7.42 神杖（变形）：变形期间施放任意指向性伤害法术，捕获其主目标与伤害并召唤 2 个水影复制体（0.2s 间隔，50% / 25% 伤害）
      DotaHookBus.RegisterAfterDamage("morphling.scepter_echo_capture", Args =>
      {
        if (IsInsideScepterEcho || Args.Attacker == null || Args.Defender == null || Args.Defender == Args.Attacker || Args.DamagePoints <= 0)
          return;

        var ActiveCast = DotaHookBus.ActiveSpellCast;
        if (ActiveCast == null || ActiveCast.Character != Args.Attacker || ActiveCast.Spell == null)
          return;

        if (!IsMorphling(Args.Attacker) || !PathosEngineBridge.IsPolymorphed(Args.Attacker) || !AghanimService.HasScepter(Args.Attacker))
          return;

        string SpellName = ActiveCast.Spell.Name;
        if (string.Equals(SpellName, WaveformSpellName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SpellName, MorphAgilitySpellName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SpellName, MorphStrengthSpellName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SpellName, MorphSpellName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SpellName, MorphRevertSpellName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(SpellName, AdaptiveStrikeSpellName, StringComparison.OrdinalIgnoreCase))
          return;

        if (!DotaCombatHelper.IsHostile(Args.Attacker, Args.Defender))
          return;

        var Capture = MorphEchoCaptures.GetOrCreateValue(Args.Attacker);
        if (Capture.Spell != ActiveCast.Spell)
        {
          Capture.Spell = ActiveCast.Spell;
          Capture.PrimaryTarget = Args.Defender;
          Capture.CapturedDamage = Args.DamagePoints;
        }
      });

      DotaHookBus.RegisterAfterCastSpell("morphling.scepter_echo_trigger", Args =>
      {
        if (IsInsideScepterEcho || Args.Character == null || Args.Spell == null)
          return;

        if (!MorphEchoCaptures.TryGetValue(Args.Character, out var Capture) || Capture.Spell != Args.Spell)
          return;

        var Primary = Capture.PrimaryTarget;
        int BaseDamage = Capture.CapturedDamage;
        Capture.Spell = null;
        Capture.PrimaryTarget = null;
        Capture.CapturedDamage = 0;

        if (Primary == null || BaseDamage <= 0 || !PathosEngineBridge.IsPolymorphed(Args.Character) || !AghanimService.HasScepter(Args.Character))
          return;

        ScheduleMorphScepterEchoes(Ctx, Args.Engine, Args.Character, Primary, BaseDamage, Args.Spell.Name, StunTurns: 0);
      });
    }

    private void ScheduleMorphScepterEchoes(
      DotaHeroContext Ctx,
      object Engine,
      Character Caster,
      Character PrimaryTarget,
      int BaseDamage,
      string SourceSpellName,
      int StunTurns)
    {
      if (Engine == null || Caster == null || PrimaryTarget == null || BaseDamage <= 0)
        return;

      var AnchorSquare = PrimaryTarget.Square ?? Caster.Square;
      var Seq = DotaPresentation.Sequence(Engine, Caster, ScepterEchoSequenceKey);
      for (int i = 0; i < ScepterEchoDamageRatios.Length; i++)
      {
        float Ratio = ScepterEchoDamageRatios[i];
        int EchoIndex = i + 1;
        Seq.At(ScepterEchoDelaySeconds * EchoIndex, SeqCtx =>
        {
          if (Caster.Deceased || Caster.Square == null)
            return;

          var RefSq = !PrimaryTarget.Deceased && PrimaryTarget.Square != null ? PrimaryTarget.Square : AnchorSquare;
          if (RefSq == null)
            return;

          // 优先选原目标 3 格内的其他随机敌人；无其他敌人时回退对原目标再次施放
          var Candidates = DotaCombatHelper.FindHostilesInRadius(Caster, RefSq, ScepterEchoRetargetRadius);
          var Others = new List<Character>();
          foreach (var C in Candidates)
          {
            if (C != null && !C.Deceased && C != PrimaryTarget)
              Others.Add(C);
          }

          Character EchoTarget = Others.Count > 0
            ? Others[Random.Shared.Next(Others.Count)]
            : (!PrimaryTarget.Deceased ? PrimaryTarget : (Candidates.Count > 0 ? Candidates[0] : null));

          if (EchoTarget == null || EchoTarget.Deceased || EchoTarget.Square == null)
            return;

          int EchoDmg = Math.Max(1, (int)MathF.Round(BaseDamage * Ratio));
          var HitDir = Caster.Square.AsDirection(EchoTarget.Square);

          IsInsideScepterEcho = true;
          try
          {
            DotaVfx.Strike(SeqCtx.Engine, EchoTarget.Square, AdaptiveStrikeImpactStrike ?? Ctx.Strikes.frost, 0.35f);
            PathosEngineBridge.StoryCaption(SeqCtx.Engine, EchoTarget.Square, Colour.DeepSkyBlue, DotaLocalization.T("morph.caption.scepter_echo"), Delayed: false);
            DotaDamage.Harm(SeqCtx.Engine, Caster, EchoTarget, HitDir, Ctx.Elements.water, Dice.Fixed(EchoDmg), DotaDamageInfo.Magical(SourceSpellName));
            if (StunTurns > 0 && !EchoTarget.Deceased)
            {
              int EchoStun = Math.Max(1, (int)MathF.Round(StunTurns * Ratio));
              PathosEngineBridge.ApplyTransient(SeqCtx.Engine, Caster, EchoTarget, Ctx.Properties.stunned, Dice.Fixed(EchoStun));
            }
          }
          finally
          {
            IsInsideScepterEcho = false;
          }
        });
      }
      Seq.Play();
    }

    private void ExecuteWaveform(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster?.Square == null)
        return;

      WaveformOriginSquares.TryGetValue(Caster, out var OriginSquare);
      WaveformOriginSquares.Remove(Caster);
      OriginSquare ??= Caster.Square;

      var Pending = Cast.ConsumePendingTarget();
      int MaxRange = Cast.Range;
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

      if (DestSquare == null)
        DestSquare = Caster.Square;

      var Engine = Args.Engine;
      int Tier = Cast.Tier;
      int Radius = GetWaveformRadius(Tier);
      int EvasionTurns = GetWaveformEvasionTurns(Tier);
      Dice BaseDice = GetWaveformDice(Tier);
      float StarMult = GetWaveformStarDamageMultiplier(Caster);

      // 冲锋路径（含首尾格）：沿途 1 格波宽刮怪，落点 Radius 格水爆
      var Path = OriginSquare.Map == DestSquare.Map
        ? DotaVfx.LinePath(OriginSquare, DestSquare, MaxRange + 4)
        : new List<Square> { DestSquare };
      if (Path.Count == 0)
        Path.Add(DestSquare);
      int LandStep = Path.Count - 1;

      // 收集命中目标及其「被浪头扫到」的路径步序（去重、不伤自己与友军）
      var HitStep = new Dictionary<Character, int>();

      void CollectAround(Square Center, int R, int Step)
      {
        if (Center == null)
          return;
        if (DotaCombatHelper.IsHostile(Caster, Center.Character))
          HitStep.TryAdd(Center.Character, Step);
        foreach (var Sq in Center.GetAroundSquares(R))
        {
          if (Sq?.Character != null && DotaCombatHelper.IsHostile(Caster, Sq.Character))
            HitStep.TryAdd(Sq.Character, Step);
        }
      }

      // DOTA2 7.42a：波浪形态只对路径上 width = 200（半宽 ≈ 1 格）的敌人造成伤害，落点没有额外的大范围水爆
      for (int Step = 0; Step < LandStep; Step++)
        CollectAround(Path[Step], Radius, Step);
      CollectAround(DestSquare, Radius, LandStep);
      if (ClickedSquare != null && ClickedSquare != DestSquare)
        CollectAround(ClickedSquare, 1, LandStep);

      // 逻辑位置立即落到终点（路径中的单位不会阻挡），贴图由 Flinging 按 DOTA2 弹道速度化作水流飞过去
      if (DestSquare != Caster.Square)
        PathosEngineBridge.DashCharacter(Engine, Caster, DestSquare);

      // 施加短暂穿墙与幻影闪避
      PathosEngineBridge.ApplyTransient(Engine, Caster, Caster, Ctx.Properties.phasing, Dice.Fixed(EvasionTurns));
      PathosEngineBridge.ApplyTransient(Engine, Caster, Caster, Ctx.Properties.displacement, Dice.Fixed(EvasionTurns));

      // 演出：DOTA2 7.42a 波浪形态速度 1250，按「DOTA 射程 / 本阶基础格数」折算每格飞行时间（约 0.08~0.11 秒/格），
      // 播放期间冻结世界，浪头经过哪里、哪里的敌人才吃到伤害；落地时水爆 + 落点 AOE
      float PerTile = GetWaveformSecondsPerTile(Tier);
      float Travel = PerTile * LandStep;
      var Dir = OriginSquare.AsDirection(DestSquare);

      void HitTargetsAt(int Step, Square From)
      {
        foreach (var (Enemy, EnemyStep) in HitStep)
        {
          if (EnemyStep != Step || Enemy == null || Enemy.Deceased || Enemy.Square == null)
            continue;

          int Rolled = DotaCombatHelper.RollDice(BaseDice);
          int FinalDmg = ScaleWaveformRoll(Rolled, StarMult);
          var HitDir = From.AsDirection(Enemy.Square);
          DotaDamage.Harm(Engine, Caster, Enemy, HitDir, Ctx.Elements.water, Dice.Fixed(FinalDmg), DotaDamageInfo.Magical(WaveformSpellName));
          DotaVfx.Strike(Engine, Enemy.Square, AdaptiveStrikeImpactStrike, 0.3f);
        }
      }

      if (LandStep > 0)
        PathosEngineBridge.StoryFlinging(Engine, DotaPresentation.Seconds(Travel), OriginSquare, DestSquare, Caster);

      var Sequence = DotaPresentation.Sequence(Engine, Caster, WaveformSequenceKey).FreezeWorld();
      for (int Step = 0; Step < LandStep; Step++)
      {
        var Sq = Path[Step];
        int Captured = Step;
        Sequence.At(Step * PerTile, _ =>
        {
          // 浪头经过的格子留下一段水流残影（0.5 秒淡出），并结算这一格扫到的敌人
          DotaVfx.Beam(Engine, Sq, Dir, WaveformDashBeam, 0.5f);
          HitTargetsAt(Captured, Sq);
        });
      }

      Sequence.At(Travel, _ =>
      {
        PathosEngineBridge.StoryEmitting(Engine, DestSquare, Ctx.Sonics.water_splash, Audibility: 8);
        DotaVfx.Explode(Engine, DestSquare, WaveformSplashExplosion ?? Ctx.Explosions.watery, 0.55f);
        HitTargetsAt(LandStep, DestSquare);
      });

      Sequence.Play();
    }

    /// <summary>
    /// 波浪形态每格飞行时间（秒）：DOTA2 7.42a 速度 1250 码/秒，按 175 码/格 → 每格 0.14 秒（初始 4 格约 0.56 秒）。
    /// </summary>
    private static float GetWaveformSecondsPerTile(int Tier)
      => DotaUnitsPerTile / WaveformDotaSpeed;

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

    private void ExecuteAdaptiveStrike(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster?.Square == null)
        return;

      if (!Cast.TryResolveHostileTarget(out var Target, "morph.log.adaptive_no_target"))
        return;

      var StrikeStats = ComputeAdaptiveStrike(Caster, Cast.Tier);

      // 1. 基础水伤骰 + 敏捷比缩放爆发伤害（叠加升星增幅）
      int BaseRoll = DotaCombatHelper.RollDice(StrikeStats.BaseDice);
      int TotalDamage = Math.Max(1, BaseRoll + StrikeStats.BonusDamage);

      Direction HitDir = Caster.Square.AsDirection(Target.Square);
      PathosEngineBridge.StoryEmitting(Args.Engine, Target.Square, Ctx.Sonics.water_splash, Audibility: 7);
      PathosEngineBridge.StoryStriking(Args.Engine, Delay.HalfSecond, Target.Square, AdaptiveStrikeImpactStrike ?? Ctx.Strikes.frost);
      DotaDamage.Harm(Args.Engine, Caster, Target, HitDir, Ctx.Elements.water, Dice.Fixed(TotalDamage), DotaDamageInfo.Magical(AdaptiveStrikeSpellName));

      if (Target.Deceased || Target.Square == null)
        return;

      // 2. 力量比动态缩放眩晕（仅对敌方 Target 生效，绝不会眩晕施法者自己）
      PathosEngineBridge.ApplyTransient(Args.Engine, Caster, Target, Ctx.Properties.stunned, Dice.Fixed(StrikeStats.FinalStun));

      // 3. 单体安全击退（低力推 1 格，高力推 2 格，仅推入空地）
      int KnockSteps = StrikeStats.StrScale >= 0.60f ? 2 : 1;
      for (int Step = 0; Step < KnockSteps; Step++)
      {
        var NextSq = Target.Square?.Adjacent(HitDir, 1);
        if (NextSq == null || NextSq == Target.Square || !DotaCombatHelper.IsOpenSquare(NextSq))
          break;
        PathosEngineBridge.DashCharacter(Args.Engine, Target, NextSq);
      }

      // 4. 7.42 神杖：变形期间施放变体打击，召唤 2 个水影复制体依次回响（50% / 25%）
      if (!IsInsideScepterEcho && PathosEngineBridge.IsPolymorphed(Caster) && AghanimService.HasScepter(Caster))
        ScheduleMorphScepterEchoes(Ctx, Args.Engine, Caster, Target, TotalDamage, AdaptiveStrikeSpellName, StrikeStats.FinalStun);
    }

    private void ExecuteMorph(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster?.Square == null)
        return;

      if (!Cast.TryResolveHostileTarget(out var Target, "morph.log.morph_no_target"))
        return;

      if (Target.Entity == null)
      {
        Cast.RefundAndLog("morph.log.morph_no_target");
        return;
      }

      bool Applied = PathosEngineBridge.ImpersonateCharacter(Args.Engine, Caster, Target);
      if (!Applied || !PathosEngineBridge.IsPolymorphed(Caster))
        PathosEngineBridge.PolymorphCharacter(Args.Engine, Caster, Target.Entity);

      PathosEngineBridge.StoryEmitting(Args.Engine, Caster.Square, Ctx.Sonics.water_splash, Audibility: 6);
      if (WaveformSplashExplosion != null)
        PathosEngineBridge.StoryExploding(Args.Engine, Delay.HalfSecond, Caster.Square, WaveformSplashExplosion);
    }

    private static void ExecuteAttributeShift(DotaHeroContext Ctx, DotaAbilityCast Cast, bool ToAgility)
    {
      var Args = Cast.Args;
      var Character = Cast.Caster;
      if (Character?.CurrentForm == null || Character.MaximumForm == null)
        return;

      int Step = Cast.Tier + GetAttributeShiftExtraStep(Character);

      int CurStr = DotaStats.Str(Character);
      int CurCon = DotaStats.Con(Character);
      int CurDex = DotaStats.Dex(Character);

      int MaxStr = DotaStats.Limit(Character, DotaStat.Str);
      int MaxCon = DotaStats.Limit(Character, DotaStat.Con);
      int MaxDex = DotaStats.Limit(Character, DotaStat.Dex);

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

        DotaStats.SetPermanent(Character, DotaStat.Dex, CurDex + Shift);
        DotaStats.SetPermanent(Character, DotaStat.Str, CurStr - Shift);
        DotaStats.SetPermanent(Character, DotaStat.Con, CurCon - Shift);

        // 血量联动：转敏仅降低最大生命值，当前生命值保持不变（仅在超过新上限时截断）
        int HpDelta = Shift * DotaMechanicsMapper.HpPerStrengthPoint;
        DotaLife.AdjustMax(Character, -HpDelta, DotaLifeAdjustMode.ClampCurrent);

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

        DotaStats.SetPermanent(Character, DotaStat.Str, CurStr + Shift);
        DotaStats.SetPermanent(Character, DotaStat.Con, CurCon + Shift);
        DotaStats.SetPermanent(Character, DotaStat.Dex, CurDex - Shift);

        // DOTA2 血量联动：转力同步提升最大生命与当前生命（1 血极限转力反杀）
        int HpDelta = Shift * DotaMechanicsMapper.HpPerStrengthPoint;
        DotaLife.AdjustMax(Character, HpDelta, DotaLifeAdjustMode.RaiseBoth);

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
      int Str = DotaStats.Str(Character);
      return Math.Max(0, (Str - 10) / 5);
    }

    // ---- 先天「潮涨潮落」公式（Hook 结算与帮助面板共用） ----

    /// <summary>敏捷移速加成比例：每点超过 10 的敏捷 +2%，上限 +50%。</summary>
    private static float GetEbbSpeedBonus(int Dex)
      => Dex > 10 ? Math.Min(0.50f, (Dex - 10) * 0.02f) : 0.0f;

    /// <summary>远程攻击距离加成：每 5 点超过 10 的敏捷 +1 格，T2 左天赋再 +1 格。</summary>
    private static int GetEbbAttackRangeBonus(Character Character)
      => Math.Max(0, (DotaStats.Dex(Character) - 10) / 5)
        + (TalentService.HasTalent(Character, TalentT2AttackRange) ? TalentAttackRangeBonus : 0);

    /// <summary>减速抗性：力量 ≥ 24 返回 1（免疫）；否则每点超过 10 的力量 +4.5%，上限 75%。</summary>
    private static float GetEbbSlowResist(int Str)
      => Str >= 24 ? 1.0f : Str > 10 ? Math.Min(0.75f, (Str - 10) * 0.045f) : 0.0f;

    private static float GetWaveformStarDamageMultiplier(Character Character)
      => WaveformAbility.StarMultiplier(Character);

    private static int GetWaveformExtraRange(Character Character)
      => GetWaveformExtraRangeForLevel(WaveformAbility.Level(Character));

    /// <summary>波浪形态升星射程：每升 1 星 +1 格（Lv 1~4 → +0 / +1 / +2 / +3 格）。</summary>
    private static int GetWaveformExtraRangeForLevel(int Lv)
      => Math.Max(0, Lv - 1);

    private static float GetAdaptiveStrikeStarDamageMultiplier(Character Character)
      => AdaptiveStrikeAbility.StarMultiplier(Character);

    private static int GetAdaptiveStrikeExtraMaxStun(Character Character)
      => AdaptiveStrikeAbility.StarSteps(Character);

    private static int GetAttributeShiftExtraStep(Character Character)
      => MorphAgilityAbility.StarSteps(Character);

    private static int GetMorphExtraRange(Character Character)
      => MorphAbility.StarSteps(Character);

    private static int GetWaveformTalentRangeBonus(Character Character)
      => TalentService.HasTalent(Character, TalentT2WaveformRange) ? TalentWaveformRangeBonus : 0;

    private static bool IsMorphling(Character Character)
      => DotaCombatHelper.IsClass(Character, MorphlingClassName);

    // DOTA2 7.42a 官方数据：Waveform speed 1250，cast range 700/775/850/925（Champion 阶沿用 925）
    private const float WaveformDotaSpeed = 1250.0f;
    // 距离换算 175 码/格（与淘汰之刃、无敌斩跳斩一致）：初始射程 700 码 → 4 格
    private const float DotaUnitsPerTile = 175.0f;
    private const string WaveformSequenceKey = "morphling.waveform";

    private static readonly float[] WaveformDotaDamages = { 75.0f, 150.0f, 225.0f, 300.0f, 360.0f };
    // DOTA2 7.42a width = 200（各级相同）→ 波宽半径 1 格（3 格宽浪头），落点不再有 2~3 格大范围水爆
    private static readonly int[] WaveformRadii = { 1, 1, 1, 1, 1 };
    private static readonly int[] WaveformEvasionTurns = { 3, 4, 4, 5, 6 };

    // DOTA2 7.42 变体打击核实数值：基础 50/70/90/110（Champion 130）；敏捷倍率底限 0.35×、上限随熟练度 0.90/1.10/1.30/1.50/1.70×；最长眩晕 1.4/1.8/2.2/2.6s（7.42a，Champion 外推 3.0s）
    private static readonly float[] AdaptiveStrikeDotaDamages = { 50.0f, 70.0f, 90.0f, 110.0f, 130.0f };
    private const float AdaptiveStrikeMinAgiMult = 0.35f;
    private static readonly float[] AdaptiveStrikeMaxAgiMults = { 0.90f, 1.10f, 1.30f, 1.50f, 1.70f };
    private static readonly float[] AdaptiveStrikeMaxStunSeconds = { 1.4f, 1.8f, 2.2f, 2.6f, 3.0f }; // DOTA2 7.42a stun_max（Champion 阶按 +0.4s 外推）

    private static float GetAdaptiveStrikeMaxAgiMultiplier(int Tier)
      => AdaptiveStrikeMaxAgiMults[Math.Clamp(Tier - 1, 0, AdaptiveStrikeMaxAgiMults.Length - 1)];

    private static int GetAdaptiveStrikeBaseMaxStunTurns(int Tier)
      => DotaMechanicsMapper.MapDurationTurns(AdaptiveStrikeMaxStunSeconds[Math.Clamp(Tier - 1, 0, AdaptiveStrikeMaxStunSeconds.Length - 1)]);

    // ---- 施法结算与帮助面板共用的数值计算（单一来源，防止两处公式漂移） ----

    private static int WaveformIndex(int Tier) => Math.Clamp(Tier - 1, 0, WaveformDotaDamages.Length - 1);

    /// <summary>Q 波浪形态基础水伤骰（DOTA2 75/150/225/300/360 → K_dmg = 15）。</summary>
    private static Dice GetWaveformDice(int Tier) => DotaMechanicsMapper.MapSkillDamageDice(WaveformDotaDamages[WaveformIndex(Tier)]);

    /// <summary>Q 单次掷骰按升星倍率放大（升星时至少 +1），施法结算与帮助面板共用。</summary>
    private static int ScaleWaveformRoll(int Rolled, float StarMult)
      => StarMult > 1.001f ? Math.Max(Rolled + 1, (int)MathF.Round(Rolled * StarMult)) : Rolled;

    /// <summary>Q 水波伤害文本：未升星为「骰（最小~最大）」，升星后为「骰 ×倍率（放大后最小~最大）」。</summary>
    private static string FormatWaveformDamage(Dice BaseDice, float StarMult)
    {
      if (StarMult <= 1.001f)
        return DotaSpellHelpBridge.FormatDiceWithRange(BaseDice);

      return DotaLocalization.Format(
        "help.dice_range",
        $"{DotaSpellHelpBridge.FormatDiceNotation(BaseDice)} ×{StarMult:0.00}",
        ScaleWaveformRoll(DotaSpellHelpBridge.GetDiceMin(BaseDice), StarMult),
        ScaleWaveformRoll(DotaSpellHelpBridge.GetDiceMax(BaseDice), StarMult));
    }

    private static int GetWaveformRadius(int Tier) => WaveformRadii[WaveformIndex(Tier)];

    private static int GetWaveformEvasionTurns(int Tier) => WaveformEvasionTurns[WaveformIndex(Tier)];

    private static Dice GetAdaptiveStrikeBaseDice(int Tier)
      => DotaMechanicsMapper.MapSkillDamageDice(AdaptiveStrikeDotaDamages[Math.Clamp(Tier - 1, 0, AdaptiveStrikeDotaDamages.Length - 1)]);

    private readonly record struct AdaptiveStrikeStats(int Dex, int Str, Dice BaseDice, int BonusDamage, float StrScale, int MaxStun, int FinalStun);

    /// <summary>
    /// W 变体打击：基础水伤骰 + 敏捷比缩放爆发（0.35× ~ 熟练度上限倍率 × 升星）与力量比缩放眩晕（1 ~ 最长眩晕回合）。
    /// </summary>
    private static AdaptiveStrikeStats ComputeAdaptiveStrike(Character Caster, int Tier)
    {
      int Dex = Math.Max(1, DotaStats.Dex(Caster));
      int Str = Math.Max(1, DotaStats.Str(Caster));

      float DexScale = Math.Clamp((Dex / (float)Str - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
      float MaxAgiMult = GetAdaptiveStrikeMaxAgiMultiplier(Tier);
      float StarMult = GetAdaptiveStrikeStarDamageMultiplier(Caster);
      int BonusDamage = (int)MathF.Round(Dex * (AdaptiveStrikeMinAgiMult + (MaxAgiMult - AdaptiveStrikeMinAgiMult) * DexScale) * StarMult);

      float StrScale = Math.Clamp((Str / (float)Dex - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
      int MaxStun = GetAdaptiveStrikeBaseMaxStunTurns(Tier) + GetAdaptiveStrikeExtraMaxStun(Caster);
      int FinalStun = Math.Max(1, (int)MathF.Round(1.0f + (MaxStun - 1.0f) * StrScale));

      return new AdaptiveStrikeStats(Dex, Str, GetAdaptiveStrikeBaseDice(Tier), BonusDamage, StrScale, MaxStun, FinalStun);
    }

    private static void RegisterSpellHelpSpecs(DotaHeroContext Ctx)
    {
      // 1. Q：波浪形态
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = WaveformSpellName,
        ResolveCurrentTier = WaveformAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = WaveformAbility.Tier(Ch);
          int BaseRange = WaveformAbility.BaseRange(Tier);
          int StrBonus = GetStrRangeBonus(Ch);
          int StarRange = GetWaveformExtraRange(Ch);
          int TalentRange = GetWaveformTalentRangeBonus(Ch);
          float StarMult = GetWaveformStarDamageMultiplier(Ch);
          Dice WaveDice = GetWaveformDice(Tier);
          int Radius = GetWaveformRadius(Tier);
          int Evasion = GetWaveformEvasionTurns(Tier);
          int TotalRange = BaseRange + StrBonus + StarRange + TalentRange;
          string ExtraTags =
            (StrBonus > 0 ? $" + {DotaHelpColor.Passive(DotaLocalization.Format("morph.help.ebb_tag", StrBonus))}" : string.Empty) +
            (StarRange > 0 ? $" + {DotaHelpColor.Skill(DotaLocalization.Format("morph.help.star_range_tag", StarRange))}" : string.Empty) +
            (TalentRange > 0 ? $" + {DotaHelpColor.Talent(DotaLocalization.Format("morph.help.talent_range_tag", TalentRange))}" : string.Empty);
          string RangeDetail = !string.IsNullOrEmpty(ExtraTags)
            ? DotaLocalization.Format(
                "morph.help.range_detail",
                DotaHelpColor.Control(DotaHelpFormat.Tiles(TotalRange)),
                BaseRange,
                ExtraTags,
                string.Empty)
            : DotaHelpColor.Control(DotaHelpFormat.Tiles(BaseRange));
          string StarDmgNote = StarMult > 1.001f
            ? DotaLocalization.Format("ui.common.comma_item", DotaHelpColor.Skill(DotaLocalization.Format("ui.help.tag_star_mult", $"{StarMult:0.00}")))
            : string.Empty;

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.q.key_range"), RangeDetail),
            (DotaLocalization.T("morph.help.q.key_dmg"), DotaLocalization.Format(
              "morph.help.q.val_dmg",
              DotaHelpColor.Magic(FormatWaveformDamage(WaveDice, StarMult)),
              DotaHelpColor.Magic($"{WaveDice.Average() * StarMult:0.#}"),
              StarDmgNote,
              DotaHelpColor.Control(DotaHelpFormat.Tiles(Radius)))),
            (DotaLocalization.T("morph.help.q.key_mobility"), DotaLocalization.Format(
              "morph.help.q.val_mobility",
              DotaHelpColor.Control(DotaHelpFormat.Turns(Evasion))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.q.mech_1", DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name")))
        },
        // 各阶射程与伤害含当前力量 / 升星 / 天赋加成（当前阶与实时面板一致；百科无角色时为基础值）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(WaveformAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier => DotaLocalization.Format(
            "morph.help.q.tier_bullet",
            DotaHelpColor.Magic(FormatWaveformDamage(GetWaveformDice(Tier), GetWaveformStarDamageMultiplier(HelpCtx.Character))),
            DotaHelpColor.Control(DotaHelpFormat.Tiles(GetWaveformRadius(Tier))),
            DotaHelpColor.Control(DotaHelpFormat.Turns(GetWaveformEvasionTurns(Tier)))))
      });

      // 2. W：变体打击
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = AdaptiveStrikeSpellName,
        ResolveCurrentTier = AdaptiveStrikeAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = AdaptiveStrikeAbility.Tier(Ch);
          int BaseRange = AdaptiveStrikeAbility.BaseRange(Tier);
          int StrBonus = GetStrRangeBonus(Ch);

          var StrikeStats = ComputeAdaptiveStrike(Ch, Tier);
          Dice BaseDice = StrikeStats.BaseDice;
          int Dex = StrikeStats.Dex;
          int Str = StrikeStats.Str;
          int BonusDmg = StrikeStats.BonusDamage;
          int MaxStun = StrikeStats.MaxStun;
          int FinalStun = StrikeStats.FinalStun;

          int TotalMin = DotaSpellHelpBridge.GetDiceMin(BaseDice) + BonusDmg;
          int TotalMax = DotaSpellHelpBridge.GetDiceMax(BaseDice) + BonusDmg;
          float TotalAvg = BaseDice.Average() + BonusDmg;

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.w.key_range"), StrBonus > 0
              ? DotaLocalization.Format(
                  "morph.help.range_detail",
                  DotaHelpColor.Control(DotaHelpFormat.Tiles(BaseRange + StrBonus)),
                  BaseRange,
                  $" + {DotaHelpColor.Passive(DotaLocalization.Format("morph.help.ebb_tag", StrBonus))}",
                  string.Empty)
              : DotaHelpColor.Control(DotaHelpFormat.Tiles(BaseRange))),
            (DotaLocalization.T("morph.help.w.key_dmg"), DotaLocalization.Format(
              "morph.help.w.val_dmg",
              DotaHelpColor.Magic($"{TotalMin}~{TotalMax}"),
              DotaHelpColor.Magic($"{TotalAvg:0.#}"),
              DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceNotation(BaseDice)),
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.w.agi_bonus", BonusDmg)))),
            (DotaLocalization.T("morph.help.w.key_stun"), DotaLocalization.Format(
              "morph.help.w.val_stun",
              DotaHelpColor.Control(DotaHelpFormat.Turns(FinalStun)),
              DotaHelpColor.Control(DotaHelpFormat.Turns(MaxStun)))),
            (DotaLocalization.T("morph.help.w.key_ratio"), DotaLocalization.Format(
              "morph.help.w.val_ratio",
              DotaHelpColor.Stat(DotaHelpFormat.Dex(Dex)),
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
        // 游戏内：各阶总伤与眩晕按当前力敏 / 升星实算（当前阶与实时面板一致）；百科无角色时列基础值
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(AdaptiveStrikeAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier =>
          {
            if (HelpCtx.Character == null)
              return DotaLocalization.Format(
                "morph.help.w.tier_bullet",
                DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(GetAdaptiveStrikeBaseDice(Tier))),
                DotaHelpColor.Control(DotaHelpFormat.Turns($"1~{GetAdaptiveStrikeBaseMaxStunTurns(Tier)}")));

            var Strike = ComputeAdaptiveStrike(HelpCtx.Character, Tier);
            int Min = DotaSpellHelpBridge.GetDiceMin(Strike.BaseDice) + Strike.BonusDamage;
            int Max = DotaSpellHelpBridge.GetDiceMax(Strike.BaseDice) + Strike.BonusDamage;
            return DotaLocalization.Format(
              "morph.help.w.tier_bullet_full",
              DotaHelpColor.Magic($"{Min}~{Max}"),
              DotaHelpColor.Control(DotaHelpFormat.Turns(Strike.FinalStun)),
              DotaHelpColor.Control(DotaHelpFormat.Turns(Strike.MaxStun)));
          })
      });

      // 3. E1：属性转换·敏捷
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = MorphAgilitySpellName,
        ResolveCurrentTier = MorphAgilityAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Step = MorphAgilityAbility.Tier(Ch) + GetAttributeShiftExtraStep(Ch);
          int CurStr = DotaStats.Str(Ch);
          int CurCon = DotaStats.Con(Ch);
          int CurDex = DotaStats.Dex(Ch);
          int MaxDex = DotaStats.Limit(Ch, DotaStat.Dex);

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.e.key_shift"),
              $"{DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e1.str_con_minus", Step))} → {DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e1.agi_plus", Step))} ({DotaHelpColor.Mana(DotaLocalization.T("morph.help.e.zero_mana"))})"),
            (DotaLocalization.T("morph.help.e.key_hp"), DotaLocalization.Format(
              "morph.help.e1.val_hp",
              DotaHelpColor.Physical($"-{Step * DotaMechanicsMapper.HpPerStrengthPoint} HP"))),
            (DotaLocalization.T("morph.help.e.key_attrs"), DotaLocalization.Format(
              "morph.help.e1.val_attrs",
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.str_val", CurStr)),
              DotaHelpColor.Stat(DotaHelpFormat.Dex(CurDex)),
              MaxDex,
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.con_val", CurCon)),
              MinAttributeScore))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.e1.mech_1", MinAttributeScore, DotaLife.MinMaxLife)
        },
        // 各阶单次转换步进含当前升星加成（每星 +1，当前阶与实时面板一致）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          _ => DotaLocalization.T("morph.help.self_zero_mana"),
          Tier =>
          {
            int Step = Tier + GetAttributeShiftExtraStep(HelpCtx.Character);
            return DotaLocalization.Format(
              "morph.help.e1.tier_bullet",
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e.pts_val", Step)),
              DotaHelpColor.Physical($"-{Step * DotaMechanicsMapper.HpPerStrengthPoint} HP"));
          })
      });

      // 4. E2：属性转换·力量
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = MorphStrengthSpellName,
        ResolveCurrentTier = MorphStrengthAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Step = MorphStrengthAbility.Tier(Ch) + GetAttributeShiftExtraStep(Ch);
          int CurStr = DotaStats.Str(Ch);
          int CurCon = DotaStats.Con(Ch);
          int CurDex = DotaStats.Dex(Ch);
          int MaxStr = DotaStats.Limit(Ch, DotaStat.Str);

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.e.key_shift"),
              $"{DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e2.agi_minus", Step))} → {DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e2.str_con_plus", Step))} ({DotaHelpColor.Mana(DotaLocalization.T("morph.help.e.zero_mana"))})"),
            (DotaLocalization.T("morph.help.e.key_hp"), DotaLocalization.Format(
              "morph.help.e2.val_hp",
              DotaHelpColor.Heal($"+{Step * DotaMechanicsMapper.HpPerStrengthPoint} HP"))),
            (DotaLocalization.T("morph.help.e.key_attrs"), DotaLocalization.Format(
              "morph.help.e2.val_attrs",
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.str_val", CurStr)),
              MaxStr,
              DotaHelpColor.Stat(DotaHelpFormat.Dex(CurDex)),
              MinAttributeScore,
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.con_val", CurCon))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.e2.mech_1", MinAttributeScore)
        },
        // 各阶单次转换步进含当前升星加成（每星 +1，当前阶与实时面板一致）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          _ => DotaLocalization.T("morph.help.self_zero_mana"),
          Tier =>
          {
            int Step = Tier + GetAttributeShiftExtraStep(HelpCtx.Character);
            return DotaLocalization.Format(
              "morph.help.e2.tier_bullet",
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.e.pts_val", Step)),
              DotaHelpColor.Heal($"+{Step * DotaMechanicsMapper.HpPerStrengthPoint} HP"));
          })
      });

      // 5. R1：变形
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = MorphSpellName,
        ResolveCurrentTier = MorphAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = MorphAbility.Tier(Ch);
          int BaseRange = MorphAbility.BaseRange(Tier);
          int StrBonus = GetStrRangeBonus(Ch);
          int StarRange = GetMorphExtraRange(Ch);
          int TotalRange = BaseRange + StrBonus + StarRange;

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.r.key_range"), (StrBonus > 0 || StarRange > 0)
              ? DotaLocalization.Format(
                  "morph.help.range_detail",
                  DotaHelpColor.Control(DotaHelpFormat.Tiles(TotalRange)),
                  BaseRange,
                  StrBonus > 0 ? $" + {DotaHelpColor.Passive(DotaLocalization.Format("morph.help.ebb_tag", StrBonus))}" : string.Empty,
                  StarRange > 0 ? $" + {DotaHelpColor.Skill(DotaLocalization.Format("morph.help.star_range_tag", StarRange))}" : string.Empty)
              : DotaHelpColor.Control(DotaHelpFormat.Tiles(BaseRange))),
            (DotaLocalization.T("morph.help.r.key_mech"), DotaLocalization.Format(
              "morph.help.r.val_mech",
              DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name"))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("morph.help.r.mech_1", DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name")))
        },
        // 各阶射程含当前力量 / 升星加成（当前阶与实时面板一致；百科无角色时为基础射程）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(MorphAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier => DotaLocalization.Format(
            "morph.help.r.tier_bullet_full",
            DotaHelpColor.Control(DotaHelpFormat.Tiles(MorphAbility.RangeAt(HelpCtx.Character, Tier))),
            DotaHelpColor.Passive(DotaLocalization.T("morph.help.ebb_name"))))
      });
    }

    /// <summary>先天「潮涨潮落」帮助面板（零 Property，点击状态栏徽章弹出）：按当前力敏实算移速 / 攻击距离 / 施法距离 / 减速抗性。</summary>
    private void RegisterPassiveHelpSpecs()
    {
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        Id = "morphling.ebb_flow",
        IconGlyph = EbbAndFlowGlyph ?? MorphlingGlyph,
        TitleResolver = () => DotaLocalization.T("morph.status.ebb_fallback"),
        DescriptionResolver = () => DotaLocalization.T("morph.help.ebb.desc"),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (!IsMorphling(Ch))
            return null;

          int Dex = DotaStats.Dex(Ch);
          int Str = DotaStats.Str(Ch);
          bool HasRangeTalent = TalentService.HasTalent(Ch, TalentT2AttackRange);
          float SlowResist = GetEbbSlowResist(Str);
          string SlowText = SlowResist >= 1.0f
            ? DotaHelpColor.Control(DotaLocalization.T("morph.help.ebb.slow_immune"))
            : DotaHelpColor.Control($"{SlowResist * 100:0.#}%");

          return new List<(string, string)>
          {
            (DotaLocalization.T("morph.help.e.key_attrs"), DotaLocalization.Format(
              "morph.help.ebb.val_attrs",
              DotaHelpColor.Stat(DotaHelpFormat.Dex(Dex)),
              DotaHelpColor.Stat(DotaLocalization.Format("morph.help.str_val", Str)))),
            (DotaLocalization.T("morph.help.ebb.key_agi"), DotaLocalization.Format(
              "morph.help.ebb.val_agi",
              DotaHelpColor.Control($"+{GetEbbSpeedBonus(Dex) * 100:0}%"),
              DotaHelpColor.Control(DotaHelpFormat.Tiles($"+{GetEbbAttackRangeBonus(Ch)}")),
              HasRangeTalent ? DotaLocalization.Format("ui.common.paren_item", DotaHelpColor.Talent(DotaLocalization.T("morph.help.ebb.t2_tag"))) : string.Empty)),
            (DotaLocalization.T("morph.help.ebb.key_str"), DotaLocalization.Format(
              "morph.help.ebb.val_str",
              DotaHelpColor.Control(DotaHelpFormat.Tiles($"+{GetStrRangeBonus(Ch)}")),
              SlowText))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "morph.help.ebb.mech_agi",
            DotaHelpColor.Control("+2%"),
            DotaHelpColor.Control("50%"),
            DotaHelpColor.Control(DotaHelpFormat.Tiles("+1"))),
          DotaLocalization.Format(
            "morph.help.ebb.mech_str",
            DotaHelpColor.Control(DotaHelpFormat.Tiles("+1")),
            DotaHelpColor.Control("4.5%"),
            DotaHelpColor.Control("75%"),
            DotaHelpColor.Stat("24")),
          DotaLocalization.T("morph.help.ebb.mech_morph")
        }
      });
    }

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

    private void RegisterAbilities(
      DotaHeroContext Ctx,
      Glyph WaveformIcon,
      Glyph AdaptiveStrikeIcon,
      Glyph MorphAgilityIcon,
      Glyph MorphStrengthIcon,
      Glyph MorphIcon,
      Glyph MorphRevertIcon)
    {
      // Q：波浪形态（点任意格：空地 / 敌人身后 / 墙边自动寻找落点；DOTA2 7.42 伤害 75/150/225/300，Champion 360 在 OnCast 中经 K_dmg = 15 生成伤害骰）
      WaveformAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = WaveformSpellName,
        DisplayTitle = "Waveform",
        TitleResolver = () => DotaLocalization.T("morph.skill.q.title"),
        Description = "Dissolve into a surging wave, blinking forward, splashing water damage around you, and briefly gaining phasing and displacement.",
        Icon = WaveformIcon,
        School = Ctx.Schools.transmutation,
        Skill = Ctx.Skills.transmutation,
        Precept = new Precept(Purpose.Teleport),
        ManaCost = WaveformManaCost,
        Targeting = DotaAbilityTargeting.PointAny,
        Range = DotaTable.Of(4, 5, 6, 8, 10), // 初始射程按 DOTA2 7.42a 700 码 ÷ 175 码/格 = 4 格，随熟练度成长
        RangeBonus = Ch => GetStrRangeBonus(Ch) + GetWaveformExtraRange(Ch) + GetWaveformTalentRangeBonus(Ch),
        SkillSlotId = SkillWaveformId,
        OnCast = Cast => ExecuteWaveform(Ctx, Cast)
      });

      // W：变体打击（指向敌方；基础水伤 + 敏捷爆发 + 力量眩晕/击退）
      AdaptiveStrikeAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = AdaptiveStrikeSpellName,
        DisplayTitle = "Adaptive Strike",
        TitleResolver = () => DotaLocalization.T("morph.skill.w.title"),
        Description = "Launch a surge of water dealing base damage, knocking back, and stunning the target. Bonus damage scales with Agility-to-Strength ratio while stun duration scales with Strength-to-Agility ratio.",
        Icon = AdaptiveStrikeIcon,
        School = Ctx.Schools.evocation,
        Skill = Ctx.Skills.evocation,
        Precept = new Precept(Purpose.Blast, Ctx.Elements.water),
        ManaCost = AdaptiveStrikeManaCost,
        Targeting = DotaAbilityTargeting.UnitHostile,
        Range = DotaTable.Of(5, 6, 7, 8, 9),
        RangeBonus = GetStrRangeBonus,
        SkillSlotId = SkillAdaptiveStrikeId,
        OnCast = Cast => ExecuteAdaptiveStrike(Ctx, Cast)
      });

      // E1 / E2：属性转换（自身施放，7.42a 每秒 10 蓝 -> 每次 1 蓝；两个法术共享同一技能槽）
      MorphAgilityAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = MorphAgilitySpellName,
        Description = "Shift Strength and Constitution into Agility (1 to 5 points by mastery), conserving total stats and lowering maximum HP without draining current HP.",
        Icon = MorphAgilityIcon,
        School = Ctx.Schools.transmutation,
        Skill = Ctx.Skills.transmutation,
        Precept = new Precept(Purpose.Buff),
        ManaCost = AttributeShiftManaCost,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillAttributeShiftId,
        OnCast = Cast => ExecuteAttributeShift(Ctx, Cast, ToAgility: true)
      });

      MorphStrengthAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = MorphStrengthSpellName,
        Description = "Shift Agility into Strength and Constitution (1 to 5 points by mastery), conserving total stats while raising both maximum and current HP.",
        Icon = MorphStrengthIcon,
        School = Ctx.Schools.transmutation,
        Skill = Ctx.Skills.transmutation,
        Precept = new Precept(Purpose.Healing),
        ManaCost = AttributeShiftManaCost,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillAttributeShiftId,
        OnCast = Cast => ExecuteAttributeShift(Ctx, Cast, ToAgility: false)
      });

      // R1：变形（指向敌方，调用 Engine.ImpersonateCharacter，兜底 PolymorphCharacter）
      MorphAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = MorphSpellName,
        DisplayTitle = "Morph",
        TitleResolver = () => DotaLocalization.T("morph.spell.morph.title"),
        Description = "Assume the form of a targeted creature, copying its body, attributes, skills, and spells while retaining Ebb & Flow.",
        Icon = MorphIcon,
        School = Ctx.Schools.transmutation,
        Skill = Ctx.Skills.transmutation,
        Precept = new Precept(Purpose.Buff),
        ManaCost = MorphManaCost,
        Targeting = DotaAbilityTargeting.UnitHostile,
        Range = DotaTable.Of(5, 6, 7, 8, 10),
        RangeBonus = Ch => GetStrRangeBonus(Ch) + MorphAbility.StarSteps(Ch),
        SkillSlotId = SkillMorphId,
        OnCast = Cast => ExecuteMorph(Ctx, Cast)
      });

      // R2：解除变形（纯声明式 UnpolymorphEntity，无需 OnCast）
      MorphRevertAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = MorphRevertSpellName,
        Description = "Release your morphed form and immediately return to your true waterkin body.",
        Icon = MorphRevertIcon,
        School = Ctx.Schools.transmutation,
        Skill = Ctx.Skills.transmutation,
        Precept = new Precept(Purpose.Buff),
        ManaCost = MorphRevertManaCost,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillMorphId,
        ConfigureApply = (_, Apply) => Apply.UnpolymorphEntity()
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
          Str: Profile.StartingStats.Str,
          Dex: Profile.StartingStats.Dex,
          Con: Profile.StartingStats.Con,
          Int: Profile.StartingStats.Int,
          Wis: Profile.StartingStats.Wis,
          Cha: Profile.StartingStats.Cha);
        Ctx.SetFormScores(
          E.LimitForm,
          Str: Profile.StatLimits.Str,
          Dex: Profile.StatLimits.Dex,
          Con: Profile.StatLimits.Con,
          Int: Profile.StatLimits.Int,
          Wis: Profile.StatLimits.Wis,
          Cha: Profile.StatLimits.Cha);

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
        Profile.ConfigureClass(
          Ctx,
          C,
          Description: "A fluid DOTA2 hero who commands surging waves, shifts freely between Strength and Agility, empowers himself through Ebb & Flow, and morphs into foes.",
          AvatarGlyph: AvatarGlyph,
          LifeBase: 12,
          LifeDice: 1.d8(),
          RaceManaBase: 2,
          ManaDice: new Dice(1, 3, 0));

        C.SetDistribution(
          Ctx.Attributes.dexterity,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.charisma);

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
