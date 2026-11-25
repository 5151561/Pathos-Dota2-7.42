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
  /// DOTA2 7.42 英雄：主宰（Juggernaut / 尤涅若）
  /// 基于「声明式外壳 + Harmony Hook 核心逻辑」忠实还原 DOTA2 7.42 机制：
  /// - 先天技能「剑心犹在（Bladeform）」：
  ///   - 每连续 2 回合未受到伤害叠加 1 层（最高 10 层）
  ///   - 每层提供 +1% 移动速度（满层 +10%）与 +2.5% 等效基础敏捷加成（攻速 + 近战/无敌斩额外伤害）
  ///   - 受到任意伤害后保留 2 回合粘滞期，期满后消散；无敌斩期间全程无敌，稳定保持层数
  /// - Q：剑刃风暴（blade fury）— 基础驱散 + 减益免疫 + 80% 魔法/元素减伤 + 每回合周围 3x3 魔法旋转伤害（可暴击），期间普通物理普攻不造成常规伤害
  /// - W：治疗守卫（healing ward）— 召唤 1 HP 可移动友方随从，每回合为周围 2 格友方回复 4%~8% 最大生命值（同时上限 1 个）
  /// - E：剑舞（blade dance）— 被动天赋：近战攻击、无敌斩与剑刃风暴有 20%~35% 概率触发 1.8x 致命一击
  /// - R：无敌斩（omnislash）— 突进并在周围 3 格敌方目标间连续瞬移斩击 5~12 次，全程无敌，每次斩击均可触发剑舞暴击与剑心犹在加成
  /// </summary>
  public sealed class JuggernautHero : IDotaHeroDefinition
  {
    private const string JuggernautClassName = "juggernaut";
    private const string HealingWardEntityName = "healing ward";
    private const string BladeFurySpellName = "blade fury";
    private const string HealingWardSpellName = "healing ward";
    private const string OmnislashSpellName = "omnislash";

    // 主宰技能加点槽位 ID（小技能 Lv 1/3/5/7，大招 Lv 6/12/18，外加 7 级全属性黄点）
    private const string SkillBladeFuryId = "jugg.q.blade_fury";
    private const string SkillHealingWardId = "jugg.w.healing_ward";
    private const string SkillBladeDanceId = "jugg.e.blade_dance";
    private const string SkillOmnislashId = "jugg.r.omnislash";

    // 主宰天赋节点 ID（DOTA2 原版节奏：Lv 10 / 15 / 20 / 25 解锁，Lv 27 / 28 / 29 / 30 双修补点）
    private const string TalentT1BladeformInterval = "jugg.t1.bladeform_interval";
    private const string TalentT1HealingWardMana = "jugg.t1.healing_ward_mana";
    private const string TalentT2BladeFurySpeed = "jugg.t2.blade_fury_speed";
    private const string TalentT2OmnislashMana = "jugg.t2.omnislash_mana";
    private const string TalentT3BladeFuryDamage = "jugg.t3.blade_fury_damage";
    private const string TalentT3BladeDanceCrit = "jugg.t3.blade_dance_crit";
    private const string TalentT4BladeDanceLifesteal = "jugg.t4.blade_dance_lifesteal";
    private const string TalentT4OmnislashSlashes = "jugg.t4.omnislash_slashes";

    private static readonly float HealingWardTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(12.0f, 60.0f); // -12s / 60s = 20%
    private static readonly float OmnislashTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(15.0f, 120.0f);  // -15s / 120s = 12.5%

    private const int BladeformMaxStacks = 10;
    private static readonly int BladeformIntervalTurns = DotaMechanicsMapper.MapDurationTurns(2.0f); // 2 秒 = 4 回合
    private static readonly int BladeformLingerTurns = DotaMechanicsMapper.MapDurationTurns(2.0f);   // 2 秒 = 4 回合

    // DOTA2 7.42 主宰初始三维与成长：STR 20 (+2.0) / AGI 32 (+2.8) / INT 14 (+1.4)
    private static readonly DotaHeroProfile Profile = new(
      JuggernautClassName,
      BaseStr: 20, StrGain: 2.0f,
      BaseAgi: 32, AgiGain: 2.8f,
      BaseInt: 14, IntGain: 1.4f);

    private static readonly int BladeFuryManaCost = DotaMechanicsMapper.MapSkillManaCost(110);
    private static readonly int HealingWardManaCost = DotaMechanicsMapper.MapSkillManaCost(140);
    private static readonly int OmnislashManaCost = DotaMechanicsMapper.MapSkillManaCost(250); // 200/250/300 取中值

    // DOTA2 7.42 技能伤害基准（统一经 DotaMechanicsMapper K_dmg = 15 与 1s = 2 回合折算）
    // 剑刃风暴 7.41b/7.42 每秒伤害：85 / 115 / 145 / 175（Champion 延展至 205），每回合合并结算 1 次并受敏捷/剑心犹在攻速频率缩放
    private static readonly float[] BladeFuryDotaDps = { 85.0f, 115.0f, 145.0f, 175.0f, 205.0f };
    private const float BladeFuryTalentBonusDotaDps = 120.0f; // T3 左天赋：原 +120 每秒伤害
    // 无敌斩 7.42 额外攻击力：25 / 30 / 35（5 阶熟练度对应 25 / 30 / 35 / 40 / 45 → Pathos +2 / +2 / +2 / +3 / +3）
    private static readonly float[] OmnislashBonusDotaDamage = { 25.0f, 30.0f, 35.0f, 40.0f, 45.0f };

    // 注：剑心犹在是「无持续时间的永久叠层计数器」（受击后 4 回合粘滞仅是其内部子状态），
    // 且徽章颜色随粘滞期动态变化、层数被剑刃风暴频率/增伤在 juggernaut.turn_ticks 同一回合内读取，
    // 故刻意不迁移到 DotaModifierService（框架 Modifier 必须有剩余回合、徽章颜色固定、计时由独立 Hook 推进）。
    private sealed class BladeformState
    {
      public int Stacks;
      public int CleanTurns;
      public int LingerTurns;
    }

    private sealed class HealingWardState
    {
      public Character Owner;
      public int RemainingTurns;
      public float HealPercent;
    }

    private sealed class OmnislashSession
    {
      public int RemainingSlashes;
      public int TotalSlashes;
      public int SlashesDone;
      public int StatBonus;
      public int OmniBonusDamage;
      public Dice WeaponDice;
    }

    private static readonly ConditionalWeakTable<Character, BladeformState> BladeformStates = new();
    private static readonly ConditionalWeakTable<Character, HealingWardState> WardStates = new();
    private static readonly ConditionalWeakTable<Character, WeakReference<Character>> OwnerActiveWard = new();
    private static readonly ConditionalWeakTable<Character, OmnislashSession> ActiveOmnislashSessions = new();

    private static readonly DotaCharacterFlagSet ActiveOmnislashCasters = new();
    private static readonly DotaCharacterFlagSet SkipNextBladeFuryTick = new();
    private static readonly DotaCharacterFlagSet SkipNextOmnislashTick = new();

    private static DotaAbility BladeFuryAbility;
    private static DotaAbility HealingWardAbility;
    private static DotaAbility OmnislashAbility;

    private Property BladeformTalentProperty;
    private Property BladeFuryStatusProperty;
    private Property OmnislashStatusProperty;
    private Entity HealingWardEntity;
    private Glyph JuggernautGlyph;
    private Glyph BladeformGlyph;
    private Glyph BladeFuryGlyph;
    private Glyph BladeDanceGlyph;
    private Glyph OmnislashGlyph;
    private Explosion BladeFuryExplosion;
    private Strike OmnislashStrike;
    private Strike BladeDanceCritStrike;
    private Strike HealingWardPulseStrike;
    private Beam OmnislashDashBeam;
    private bool LastOmnislashStrikeCritted;

    public string Id => "juggernaut";

    public void Register(DotaHeroContext Ctx)
    {
      // 1. 注册贴图（对应 Resources/Heroes/juggernaut/<tileset>/*.png；使用 "yurnero" 避免与官方内置怪物贴图 "juggernaut" 重名冲突）
      JuggernautGlyph = Ctx.RegisterGlyph("yurnero");
      BladeformGlyph = Ctx.RegisterGlyph("bladeform");
      BladeFuryGlyph = Ctx.RegisterGlyph("blade fury");
      var HealingWardGlyph = Ctx.RegisterGlyph("healing ward");
      var HealingWardTotemGlyph = Ctx.RegisterGlyph("healing ward totem");
      BladeDanceGlyph = Ctx.RegisterGlyph("blade dance");
      OmnislashGlyph = Ctx.RegisterGlyph("omnislash");

      // 1.5 注册主宰专属自绘像素特效（走 Pathos 原生 Explosion / Strike / Beam 引擎管线）
      BladeFuryExplosion = Ctx.RegisterExplosion3x3Vfx("blade fury spin", "blade fury fx");
      OmnislashStrike = Ctx.RegisterStrikeVfx("omnislash slash", "omnislash fx strike");
      BladeDanceCritStrike = Ctx.RegisterStrikeVfx("blade dance crit", "blade dance fx crit");
      HealingWardPulseStrike = Ctx.RegisterStrikeVfx("healing ward pulse", "healing ward fx pulse");
      OmnislashDashBeam = Ctx.RegisterBeamVfx(
        "omnislash dash",
        "omnislash fx beam h",
        "omnislash fx beam v",
        "omnislash fx beam f",
        "omnislash fx beam b");

      // 2. 注册长效天赋（先天「剑心犹在」/ 被动「剑舞」）与短期主动技能状态（剑刃风暴 / 无敌斩，自动在原生天赋面板与百科隐藏）
      BladeformTalentProperty = Ctx.RegisterPassiveProperty(
        Name: "bladeform",
        Description: "Innate (Bladeform): Gain 1 stack every 4 turns without taking damage (up to 10 stacks). Each stack grants +1% movement speed and +2.5% agility strike power and attack speed; lingers 4 turns after taking damage.",
        Icon: BladeformGlyph);
      var BladeDanceTalent = Ctx.RegisterPassiveProperty(
        Name: "blade dance",
        Description: "Passive (Blade Dance): Melee attacks, Omnislash strikes, and Blade Fury spins have a 20% / 25% / 30% / 35% chance (by Blade Dance skill level 1-4) to critically strike for 1.8x damage.",
        Icon: BladeDanceGlyph);
      BladeFuryStatusProperty = Ctx.RegisterSkillStatusProperty(
        Name: "blade fury",
        Description: "Spinning in a destructive storm of blades: immune to debuffs, 80% magical damage reduction, and dealing magical damage around you each turn.",
        Icon: BladeFuryGlyph);
      OmnislashStatusProperty = Ctx.RegisterSkillStatusProperty(
        Name: "omnislash",
        Description: "Leaping between nearby enemies in a blur of invulnerable strikes: immune to all damage and debuffs while slashing a nearby foe each turn.",
        Icon: OmnislashGlyph);

      // 3. 注册治疗守卫召唤物实体（使用透明背景的木雕翡翠火焰图腾贴图）
      HealingWardEntity = RegisterHealingWardEntity(Ctx, HealingWardTotemGlyph);

      // 4. 注册主宰主动技能（Q / W / R）
      RegisterAbilities(Ctx, BladeFuryGlyph, HealingWardGlyph, OmnislashGlyph);
      var BladeFurySpell = BladeFuryAbility.Spell;
      var HealingWardSpell = HealingWardAbility.Spell;
      var OmnislashSpell = OmnislashAbility.Spell;

      // 5. 注册专属职业：主宰（juggernaut）
      var JuggernautClass = RegisterJuggernautClass(
        Ctx,
        JuggernautGlyph,
        BladeformTalentProperty,
        BladeDanceTalent,
        BladeFurySpell,
        HealingWardSpell,
        OmnislashSpell);

      // 6. 注册预设英雄：Juggernaut（人类种族 + 主宰职业）
      Ctx.RegisterPresetHero("Juggernaut", Ctx.Races.human, JuggernautClass, Ctx.Genders.male, JuggernautGlyph);

      // 7. 注册主宰技能加点表（Q/W/E/R + 7 级全属性黄点，主动升星与学派熟练度双重成长，剑舞被动独立升级）
      RegisterSkillBuild(
        Ctx,
        JuggernautGlyph,
        BladeFuryGlyph,
        HealingWardGlyph,
        BladeDanceGlyph,
        OmnislashGlyph,
        BladeFurySpell,
        HealingWardSpell,
        BladeDanceTalent,
        OmnislashSpell);

      // 8. 注册主宰 4 层天赋树（DOTA2 原版 Lv 10/15/20/25 + Lv 27~30 双修，含减冷却 → 减蓝耗声明式映射）
      RegisterTalentTree(Ctx, JuggernautGlyph);
    }

    private static void RegisterSkillBuild(
      DotaHeroContext Ctx,
      Glyph HeroGlyph,
      Glyph BladeFuryGlyph,
      Glyph HealingWardGlyph,
      Glyph BladeDanceGlyph,
      Glyph OmnislashGlyph,
      Spell BladeFurySpell,
      Spell HealingWardSpell,
      Property BladeDanceTalent,
      Spell OmnislashSpell)
    {
      var Build = HeroSkillBuild.CreateStandard(
        ClassName: JuggernautClassName,
        HeroDisplayName: "Juggernaut",
        HeroId: "jugg",
        IconGlyph: HeroGlyph,
        SlotQ: DotaSkillSlot.CreateActive(
          Id: SkillBladeFuryId,
          Hotkey: "Q",
          Title: "Blade Fury",
          IconGlyph: BladeFuryGlyph,
          Spell: BladeFurySpell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("jugg.skill.q.desc_0")
            : DotaLocalization.Format(
                "jugg.skill.q.desc_lv",
                $"{HeroSkillService.GetStarMultiplierForLevel(Lv):0.00}",
                Lv >= 4 ? DotaLocalization.T("jugg.skill.q.desc_max_suffix") : DotaLocalization.T("jugg.skill.q.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("jugg.skill.q.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.damage_mult", Lv => Lv > 0 ? $"×{HeroSkillService.GetStarMultiplierForLevel(Lv):0.00}" : null),
            DotaSkillStat.Of("ui.stat.bonus_duration", Lv => Lv > 0 ? "+" + DotaHelpFormat.Turns(Lv >= 4 ? 2 : 0) : null)
          }),
        SlotW: DotaSkillSlot.CreateActive(
          Id: SkillHealingWardId,
          Hotkey: "W",
          Title: "Healing Ward",
          IconGlyph: HealingWardGlyph,
          Spell: HealingWardSpell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("jugg.skill.w.desc_0")
            : DotaLocalization.Format(
                "jugg.skill.w.desc_lv",
                $"{(Lv - 1) * 0.5f:0.#}",
                (Lv - 1) * 4,
                Lv < 4 ? DotaLocalization.T("ui.skill.desc_next_star_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("jugg.skill.w.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.bonus_heal_per_turn", Lv => Lv > 0 ? $"+{(Lv - 1) * 0.5f:0.#}%" : null),
            DotaSkillStat.Of("ui.stat.bonus_duration", Lv => Lv > 0 ? "+" + DotaHelpFormat.Turns((Lv - 1) * 4) : null)
          }),
        SlotE: DotaSkillSlot.CreatePassive(
          Id: SkillBladeDanceId,
          Hotkey: "E",
          Title: "Blade Dance",
          IconGlyph: BladeDanceGlyph,
          PassiveProperty: BladeDanceTalent,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("jugg.skill.e.desc_0")
            : DotaLocalization.Format(
                "jugg.skill.e.desc_lv",
                $"{GetBladeDanceCritChanceByLevel(Lv) * 100:0}",
                Lv < 4
                  ? DotaLocalization.Format("jugg.skill.e.desc_next_suffix", $"{GetBladeDanceCritChanceByLevel(Lv + 1) * 100:0}")
                  : DotaLocalization.T("jugg.skill.e.desc_max_suffix")),
          TitleResolver: () => DotaLocalization.T("jugg.skill.e.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.crit_chance", Lv => Lv > 0 ? $"{GetBladeDanceCritChanceByLevel(Lv) * 100:0}%" : null)
          }),
        SlotR: DotaSkillSlot.CreateActive(
          Id: SkillOmnislashId,
          Hotkey: "R",
          Title: "Omnislash",
          IconGlyph: OmnislashGlyph,
          Spell: OmnislashSpell,
          IsUltimate: true,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("jugg.skill.r.desc_0")
            : DotaLocalization.Format(
                "jugg.skill.r.desc_lv",
                (Lv - 1) * 2,
                (Lv - 1) * 2,
                Lv < 3 ? DotaLocalization.T("jugg.skill.r.desc_next_suffix") : string.Empty),
          TitleResolver: () => DotaLocalization.T("jugg.skill.r.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.bonus_slashes", Lv => Lv > 0 ? $"+{(Lv - 1) * 2}" : null),
            DotaSkillStat.Of("ui.stat.bonus_slash_attack", Lv => Lv > 0 ? $"+{(Lv - 1) * 2}" : null)
          }),
        HeroDisplayNameResolver: () => DotaLocalization.T("jugg.hero_display"),
        PrimaryAttribute: DotaPrimaryAttribute.Agility);

      HeroSkillRegistry.Register(Ctx, Build);
    }

    private static void RegisterTalentTree(DotaHeroContext Ctx, Glyph IconGlyph)
    {
      int ReducedWardMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(HealingWardManaCost, HealingWardTalentCdr);
      int ReducedOmniMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(OmnislashManaCost, OmnislashTalentCdr);

      var Tree = HeroTalentTree.CreateStandard(
        ClassName: JuggernautClassName,
        HeroDisplayNameResolver: () => DotaLocalization.T("jugg.hero_display"),
        IconGlyph: IconGlyph,
        T1Left: new TalentNode(
          Id: TalentT1BladeformInterval,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t1l.title"),
          DescriptionResolver: () => DotaLocalization.T("jugg.talent.t1l.desc")),
        T1Right: new TalentNode(
          Id: TalentT1HealingWardMana,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t1r.title"),
          DescriptionResolver: () => DotaLocalization.Format("jugg.talent.t1r.desc", HealingWardManaCost, ReducedWardMana),
          ManaReductions: new[] { new SpellManaReductionRule(HealingWardSpellName, HealingWardTalentCdr) }),
        T2Left: new TalentNode(
          Id: TalentT2BladeFurySpeed,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t2l.title")),
        T2Right: new TalentNode(
          Id: TalentT2OmnislashMana,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t2r.title"),
          DescriptionResolver: () => DotaLocalization.Format("jugg.talent.t2r.desc", OmnislashManaCost, ReducedOmniMana),
          ManaReductions: new[] { new SpellManaReductionRule(OmnislashSpellName, OmnislashTalentCdr) }),
        T3Left: new TalentNode(
          Id: TalentT3BladeFuryDamage,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t3l.title"),
          DescriptionResolver: () => DotaLocalization.T("jugg.talent.t3l.desc")),
        T3Right: new TalentNode(
          Id: TalentT3BladeDanceCrit,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t3r.title"),
          DescriptionResolver: () => DotaLocalization.T("jugg.talent.t3r.desc")),
        T4Left: new TalentNode(
          Id: TalentT4BladeDanceLifesteal,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t4l.title")),
        T4Right: new TalentNode(
          Id: TalentT4OmnislashSlashes,
          TitleResolver: () => DotaLocalization.T("jugg.talent.t4r.title"),
          DescriptionResolver: () => DotaLocalization.T("jugg.talent.t4r.desc")));

      TalentRegistry.Register(Ctx, Tree);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定主宰固定初始六维（STR 12 / DEX 17 / CON 12 / INT 9 / WIS 9 / CHA 10）、1 级初始蓝量（12）与 30 级职业属性上限（25 / 29 / 25 / 22 / 22 / 20）
      Profile.RegisterStats();

      // 0.1 全射程选格与施法分发由 DotaAbility 自动注册（无敌斩 4~7 格敌方目标选定；治疗守卫 3~5 格点地召唤）
      // 0.2 注册「剑心犹在」左上角状态栏动态叠层显示（叠层 > 0 时展示层数，受击粘滞期以橙色高亮提示）
      DotaStatusBarBridge.RegisterProvider("juggernaut.bladeform", JuggernautClassName, Character =>
      {
        var State = GetBladeformState(Character);
        if (State.Stacks <= 0)
          return null;

        return DotaStatusBadge.ForPassive(
          "juggernaut.bladeform",
          BladeformGlyph,
          BladeformTalentProperty,
          "jugg.status.bladeform_fallback",
          Count: $"×{State.Stacks}",
          Colour: State.LingerTurns > 0 ? Colour.DarkOrange : Colour.Black);
      });

      // 0.3 注册主宰 3 个主动技能的帮助面板数值与 5 阶熟练度详情（含游戏内实时面板预览）
      RegisterSpellHelpSpecs(Ctx);
      RegisterPassiveHelpSpecs();

      // 0.4 注册无敌斩「无敌/不可选中」引擎级保护（免疫敌方攻击命中、狂犬病/病痛、地面陷阱、敌方/环境法术与负面状态）
      DotaHookBus.RegisterInvulnerableQuery("juggernaut.omnislash_invulnerable", IsInOmnislash);

      // 0.5 剑刃风暴期间免疫一切减益 Modifier（如战斗饥渴等 Hook 实现的减益；原生 Transient 减益由下方 BeforeApplyTransient 拦截）
      DotaModifierService.RegisterDebuffImmunity(
        "juggernaut.blade_fury_debuff_immunity",
        Ch => BladeFuryStatusProperty != null && PathosEngineBridge.HasTransient(Ch, BladeFuryStatusProperty));

      // 2. 行动完成钩子：驱动先天「剑心犹在」叠层/粘滞、剑刃风暴每回合转伤、治疗守卫每回合光环回血
      DotaHookBus.RegisterAfterExecuteCommand("juggernaut.turn_ticks", Args =>
      {
        var Actor = Args.Character;
        if (Actor == null || Actor.Deceased)
          return;

        if (IsJuggernaut(Actor))
          TickBladeform(Args.Engine, Actor);

        if (BladeFuryStatusProperty != null && PathosEngineBridge.HasTransient(Actor, BladeFuryStatusProperty))
        {
          if (SkipNextBladeFuryTick.Remove(Actor))
          {
            // 刚施放剑刃风暴的当回合已在 AfterCastSpell 中触发过首次旋转，跳过重复结算
          }
          else
          {
            TriggerBladeFuryPulse(Ctx, Args.Engine, Actor);
          }
        }

        if (IsHealingWard(Actor))
          TickHealingWard(Ctx, Args.Engine, Actor);

        // 方案 A：无敌斩每回合自动跳斩下一名 3 格内敌人，呈现一刀一跳、刀光交错的连斩节奏
        if (ActiveOmnislashSessions.TryGetValue(Actor, out var OmniSession) && OmniSession.RemainingSlashes > 0)
        {
          if (SkipNextOmnislashTick.Remove(Actor))
          {
            // 施法当回合已斩出第 1 刀，跳过同一指令的重复结算
          }
          else
          {
            TickOmnislash(Ctx, Args.Engine, Actor, OmniSession);
          }
        }
      });

      // 3. 状态免疫与满级剑刃风暴持续时间加成钩子
      DotaHookBus.RegisterBeforeApplyTransient("juggernaut.blade_fury_debuff_immunity", Args =>
      {
        if (Args.Target == null || Args.Property == null)
          return;

        // 满级（Lv 4 / ★★★）剑刃风暴额外 +2 回合持续时间
        if (Args.Property == BladeFuryStatusProperty &&
            IsJuggernaut(Args.Target) &&
            BladeFuryAbility.Level(Args.Target) >= 4)
        {
          int BaseTurns = Math.Max(1, (int)MathF.Round(Args.Duration.Average()));
          Args.Duration = Dice.Fixed(BaseTurns + 2);
          return;
        }

        if (!DotaCombatHelper.IsNegativeDebuff(Ctx, Args.Property))
          return;

        if ((BladeFuryStatusProperty != null && PathosEngineBridge.HasTransient(Args.Target, BladeFuryStatusProperty)) ||
            IsInOmnislash(Args.Target))
          Args.Cancel = true;
      });

      // 4. 速度钩子：先天技能「剑心犹在」提供移动速度（每层 +1%）与攻击速度（每层 +2.5%），以及 T2 剑刃风暴移速天赋（+15%）
      DotaHookBus.RegisterSpeedQuery("juggernaut.bladeform_speed", Args =>
      {
        if (!IsJuggernaut(Args.Character))
          return;

        float Bonus = 0.0f;
        var State = GetBladeformState(Args.Character);
        if (State.Stacks > 0)
          Bonus += State.Stacks * (Args.Attacking ? 0.025f : 0.01f);

        if (!Args.Attacking &&
            BladeFuryStatusProperty != null &&
            PathosEngineBridge.HasTransient(Args.Character, BladeFuryStatusProperty) &&
            TalentService.HasTalent(Args.Character, TalentT2BladeFurySpeed))
        {
          Bonus += 0.15f;
        }

        if (Bonus > 0.0f)
          Args.Speed = Speed.FromRate(Args.Speed.GetRate() * (1.0f + Bonus));
      });

      // 5. 伤害前置钩子（Normal 阶段）：剑刃风暴期间普攻置零、剑心犹在敏捷增伤
      //    无敌斩无敌由 InvulnerableQuery 在引擎层拦截；剑刃风暴 80% 魔抗与剑舞暴击走 DotaDamage 管线（见下方 5.1 / 5.2）
      DotaHookBus.RegisterBeforeDamage("juggernaut.damage_modifiers", Args =>
      {
        var Attacker = Args.Attacker;
        if (Attacker == null || Args.Defender == Attacker || Args.DamagePoints <= 0 || Args.Info == null)
          return;

        bool IsBasicAttack = Args.Info.Origin == DotaDamageOrigin.BasicAttack;
        bool IsOmnislash = Args.Info.IsFrom(OmnislashSpellName);

        // (a) 剑刃风暴期间普通物理普攻不造成常规伤害
        if (IsBasicAttack &&
            BladeFuryStatusProperty != null &&
            PathosEngineBridge.HasTransient(Attacker, BladeFuryStatusProperty))
        {
          Args.Cancel = true;
          return;
        }

        // (b) 先天「剑心犹在」：近战普攻与无敌斩按层数追加等效基础敏捷伤害（每层 +2.5% DEX）
        if ((IsBasicAttack || IsOmnislash) && IsJuggernaut(Attacker))
        {
          int BonusDmg = GetBladeformBonusDamage(Attacker);
          if (BonusDmg > 0)
            Args.DamagePoints += BonusDmg;
        }
      });

      // 5.1 剑刃风暴：80% 魔法抗性（只作用于魔法/未知伤害，纯粹与物理伤害不减免）
      DotaDamage.RegisterMagicResist("juggernaut.blade_fury", Args =>
        BladeFuryStatusProperty != null && PathosEngineBridge.HasTransient(Args.Defender, BladeFuryStatusProperty) ? 0.80f : 0.0f);

      // 5.2 被动「剑舞」：由独立技能等级（1~4 级：20%/25%/30%/35%）驱动，可暴击伤害（普攻、无敌斩、剑刃风暴旋风）触发 1.8x（T3 右天赋 1.95x）致命一击
      DotaDamage.RegisterCritSource("juggernaut.blade_dance", Args =>
      {
        var Attacker = Args.Attacker;
        if (!IsJuggernaut(Attacker) || Args.Info == null || !Args.Info.Has(DotaDamageFlags.CanCrit))
          return null;

        float CritChance = GetBladeDanceCritChance(Attacker);
        if (CritChance <= 0.0f || Random.Shared.NextSingle() >= CritChance)
          return null;

        float CritMultiplier = GetBladeDanceCritMultiplier(Attacker);
        return new DotaCritRoll(CritMultiplier, OnCrit: CritArgs =>
        {
          bool IsOmnislash = CritArgs.Info.IsFrom(OmnislashSpellName);
          bool IsBladeFurySpin = CritArgs.Info.IsFrom(BladeFurySpellName);
          string LifestealNote = string.Empty;
          if (TalentService.HasTalent(Attacker, TalentT4BladeDanceLifesteal) &&
              Attacker.Life != null &&
              Attacker.Life.Points < Attacker.Life.Total)
          {
            int HealAmt = Math.Max(1, (int)MathF.Round(CritArgs.DamagePoints * 0.40f));
            var SelfDir = Attacker.Square != null ? Attacker.Square.AsDirection(Attacker.Square) : default;
            PathosEngineBridge.HealCharacter(CritArgs.Engine, Attacker, Attacker, SelfDir, HealAmt);
            LifestealNote = DotaLocalization.Format("jugg.log.crit_lifesteal", HealAmt);
          }

          if (IsOmnislash)
          {
            LastOmnislashStrikeCritted = true;
          }
          else if (CritArgs.Defender?.Square != null)
          {
            PathosEngineBridge.StoryStriking(CritArgs.Engine, Delay.HalfSecond, CritArgs.Defender.Square, BladeDanceCritStrike ?? Ctx.Strikes.flame);
            PathosEngineBridge.StoryCaption(CritArgs.Engine, CritArgs.Defender.Square, Colour.OrangeRed, DotaLocalization.T("ui.caption.crit"), Delayed: false);
            PathosEngineBridge.StoryEmitting(CritArgs.Engine, CritArgs.Defender.Square, Ctx.Sonics.sever, Audibility: 6);
          }

          string SourceTag = IsOmnislash
            ? DotaLocalization.T("jugg.log.crit_src_omni")
            : IsBladeFurySpin
              ? DotaLocalization.T("jugg.log.crit_src_fury")
              : string.Empty;
          // 保留英雄专属回退 Key（各语言词表中带语法格的译文）
          string TargetName = DotaCombatHelper.GetDisplayName(CritArgs.Defender, "jugg.log.crit_target_fallback");
          PathosEngineBridge.WriteInternalLog(
            CritArgs.Engine,
            Attacker,
            BladeDanceGlyph,
            DotaLocalization.Format("jugg.log.blade_dance_crit", SourceTag, CritArgs.DamagePoints, TargetName, LifestealNote));
        });
      });

      // 6. 伤害后置钩子：主宰受伤害后开启「剑心犹在」2 回合粘滞倒计时
      DotaHookBus.RegisterAfterDamage("juggernaut.bladeform_on_hit", Args =>
      {
        if (Args.DamagePoints <= 0 || !IsJuggernaut(Args.Defender))
          return;

        var State = GetBladeformState(Args.Defender);
        State.CleanTurns = 0;
        if (State.Stacks > 0 && State.LingerTurns <= 0)
          State.LingerTurns = BladeformLingerTurns;
      });
    }

    private void TickBladeform(object Engine, Character Juggernaut)
    {
      var State = GetBladeformState(Juggernaut);

      if (State.LingerTurns > 0)
      {
        State.LingerTurns--;
        if (State.LingerTurns == 0 && State.Stacks > 0)
        {
          State.Stacks = 0;
          State.CleanTurns = 0;
          PathosEngineBridge.WriteInternalLog(
            Engine,
            Juggernaut,
            BladeformGlyph,
            DotaLocalization.T("jugg.log.bladeform_lost"));
        }
        return;
      }

      int EffectiveInterval = TalentService.HasTalent(Juggernaut, TalentT1BladeformInterval)
        ? Math.Max(1, BladeformIntervalTurns / 2)
        : BladeformIntervalTurns;

      State.CleanTurns++;
      if (State.CleanTurns >= EffectiveInterval)
      {
        State.CleanTurns = 0;
        if (State.Stacks < BladeformMaxStacks)
        {
          State.Stacks++;
          if (State.Stacks == BladeformMaxStacks)
          {
            PathosEngineBridge.WriteInternalLog(
              Engine,
              Juggernaut,
              BladeformGlyph,
              DotaLocalization.T("jugg.log.bladeform_maxed"));
          }
        }
      }
    }

    private void HandleBladeFuryCast(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster == null)
        return;

      // 基础驱散自身常见负面状态
      DotaCombatHelper.DispelNegativeDebuffs(Ctx, Args.Engine, Caster);

      // 仅在开转瞬间播放一次利刃破风声，避免每回合持续旋转重复噪音
      if (Caster.Square != null)
        PathosEngineBridge.StoryEmitting(Args.Engine, Caster.Square, Ctx.Sonics.sever, Audibility: 8);

      // 施法当回合立即结算一次旋转伤害，并标记跳过同一指令的 Postfix 重复结算
      SkipNextBladeFuryTick.Add(Caster);
      TriggerBladeFuryPulse(Ctx, Args.Engine, Caster);
    }

    private void TriggerBladeFuryPulse(DotaHeroContext Ctx, object Engine, Character Caster)
    {
      if (Caster?.Square == null || Caster.Deceased)
        return;

      Dice SpinDice = ComputeBladeFurySpin(Caster).SpinDice;

      var Targets = DotaCombatHelper.FindHostilesInRadius(Caster, Caster.Square, Radius: 1);

      // 播放自定义 3×3 剑刃风暴金橙旋风贴图（Pathos 原生 Explosion 管线，对齐原版火球爆炸 0.5 秒持续时长），精准标出 3×3 生效范围
      PathosEngineBridge.StoryExploding(Engine, Delay.HalfSecond, Caster.Square, BladeFuryExplosion ?? Ctx.Explosions.fiery);

      if (Targets.Count == 0)
        return;

      // 魔法伤害 + 可暴击（剑舞）
      var SpinInfo = DotaDamageInfo.Magical(BladeFurySpellName, DotaDamageFlags.Spell | DotaDamageFlags.CanCrit);
      foreach (var Target in Targets)
      {
        if (Target.Deceased || Target.Square == null)
          continue;

        var Dir = Caster.Square.AsDirection(Target.Square);
        DotaDamage.Harm(Engine, Caster, Target, Dir, Ctx.Elements.magical, SpinDice, SpinInfo);
      }
    }

    private void HandleHealingWardCast(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster?.Square == null)
        return;

      int Duration = GetHealingWardDuration(Caster, Cast.Tier);
      float HealPct = GetHealingWardHealPct(Caster, Cast.Tier);

      // 1. 若之前已有存活的治疗守卫，先彻底消散旧守卫（上限同时存在 1 个，避免占用盟友上限或残留旧单位）
      if (OwnerActiveWard.TryGetValue(Caster, out var OldRef) &&
          OldRef.TryGetTarget(out var OldWard) &&
          OldWard != null &&
          !OldWard.Deceased)
      {
        PathosEngineBridge.DespawnSummon(Args.Engine, OldWard, "replaced");
      }

      // 2. 确定召唤落点：优先玩家通过全射程选格指示器指定的格子，否则回退至施法方向相邻空位或自身周围空位
      var PlacementSq = Cast.ResolveOpenPointTarget();
      if (PlacementSq == null || HealingWardEntity == null)
        return;

      // 3. 通过原生 Reactor.SummonEntity 精确召唤 1 个治疗守卫
      var NewWard = PathosEngineBridge.SummonAllyEntity(
        Args.Engine,
        Caster,
        PlacementSq,
        HealingWardEntity,
        Constructed: true,
        Cause: HealingWardSpellName);

      if (NewWard == null)
        return;

      // 仅在召唤瞬间轻播一次风铃提示音（守卫实体本身静音，避免每回合移动或环境音反复响动）
      if (NewWard.Square != null)
        PathosEngineBridge.StoryEmitting(Args.Engine, NewWard.Square, Ctx.Sonics.chime, Audibility: 6);

      WardStates.AddOrUpdate(NewWard, new HealingWardState
      {
        Owner = Caster,
        RemainingTurns = Duration,
        HealPercent = HealPct
      });
      OwnerActiveWard.AddOrUpdate(Caster, new WeakReference<Character>(NewWard));
    }

    private void TickHealingWard(DotaHeroContext Ctx, object Engine, Character Ward)
    {
      if (Ward?.Square == null || Ward.Deceased)
        return;

      var State = WardStates.GetValue(Ward, _ => new HealingWardState
      {
        Owner = null,
        RemainingTurns = 10,
        HealPercent = 0.05f
      });

      var SquaresToCheck = new List<Square> { Ward.Square };
      SquaresToCheck.AddRange(Ward.Square.GetAroundSquares(2));
      var HealedSquares = new List<Square>();

      foreach (var Sq in SquaresToCheck)
      {
        var Ally = Sq?.Character;
        if (Ally == null || Ally == Ward || Ally.Deceased || Ally.Life == null)
          continue;

        if (!DotaCombatHelper.IsFriendly(Ward, Ally))
          continue;

        if (Ally.Life.Points < Ally.Life.Total)
        {
          int HealAmount = Math.Max(1, (int)MathF.Round(Ally.Life.Total * State.HealPercent));
          var Dir = Ward.Square.AsDirection(Ally.Square);
          PathosEngineBridge.HealCharacter(Engine, Ward, Ally, Dir, HealAmount);
          HealedSquares.Add(Ally.Square);
        }
      }

      if (HealedSquares.Count > 0)
      {
        // Pathos 原生 2 格范围脉冲 + 自定义翡翠治疗光环贴图（0.5 秒完整渐隐）
        PathosEngineBridge.StoryDetecting(Engine, Ward.Square, 2, Colour.LimeGreen);
        foreach (var Sq in HealedSquares)
          PathosEngineBridge.StoryStriking(Engine, Delay.HalfSecond, Sq, HealingWardPulseStrike ?? Ctx.Strikes.boost);
      }

      State.RemainingTurns--;
      if (State.RemainingTurns <= 0)
        PathosEngineBridge.DespawnSummon(Engine, Ward, "expired");
    }

    private void HandleOmnislashCast(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster?.Square == null || Caster.Deceased)
        return;

      // 优先使用 DOTA2 全射程选格指示器锁定的精确敌方目标（含 1 格智能吸附、方向射线与最近敌人回退）；空大按本次实际扣蓝返还
      if (!Cast.TryResolveHostileTarget(out var InitialTarget, "jugg.log.omni_no_target"))
        return;

      var Omni = ComputeOmnislash(Ctx, Args.Engine, Caster, Cast.Tier);

      var Session = new OmnislashSession
      {
        RemainingSlashes = Omni.TotalSlashes,
        TotalSlashes = Omni.TotalSlashes,
        SlashesDone = 0,
        StatBonus = Omni.StatBonus,
        OmniBonusDamage = Omni.OmniBonusDamage,
        WeaponDice = Omni.WeaponDice
      };

      // 方案 A：当回合先瞬移斩出第 1 刀，剩余斩数自动推进回合逐斩跳跃（全程无敌、无需手动操作）
      ExecuteSingleOmnislashStep(Ctx, Args.Engine, Caster, Session, InitialTarget);

      if (Session.RemainingSlashes > 0 && !Caster.Deceased && DotaCombatHelper.PickRandomNearbyHostile(Caster, Radius: 3) != null)
      {
        ActiveOmnislashSessions.AddOrUpdate(Caster, Session);
        SkipNextOmnislashTick.Add(Caster);
        if (OmnislashStatusProperty != null)
          PathosEngineBridge.ApplyTransient(Args.Engine, Caster, Caster, OmnislashStatusProperty, Dice.Fixed(Session.RemainingSlashes + 1));

        // 自动排入下一回合 Wait，驱动引擎播完当前斩击动画后自动跳斩下一刀
        PathosEngineBridge.QueueWaitDecision(Caster);
      }
      else
      {
        FinishOmnislashSession(Ctx, Args.Engine, Caster, Session);
      }
    }

    private void TickOmnislash(DotaHeroContext Ctx, object Engine, Character Caster, OmnislashSession Session)
    {
      if (Caster == null || Caster.Deceased || Caster.Square == null || Session == null || Session.RemainingSlashes <= 0)
      {
        FinishOmnislashSession(Ctx, Engine, Caster, Session);
        return;
      }

      var NextTarget = DotaCombatHelper.PickRandomNearbyHostile(Caster, Radius: 3);
      if (NextTarget == null)
      {
        FinishOmnislashSession(Ctx, Engine, Caster, Session);
        return;
      }

      ExecuteSingleOmnislashStep(Ctx, Engine, Caster, Session, NextTarget);

      if (Session.RemainingSlashes > 0 && !Caster.Deceased && DotaCombatHelper.PickRandomNearbyHostile(Caster, Radius: 3) != null)
      {
        // 继续自动推进下一回合，直到斩数耗尽或周围 3 格内无敌方目标
        PathosEngineBridge.QueueWaitDecision(Caster);
      }
      else
      {
        FinishOmnislashSession(Ctx, Engine, Caster, Session);
      }
    }

    private void ExecuteSingleOmnislashStep(
      DotaHeroContext Ctx,
      object Engine,
      Character Caster,
      OmnislashSession Session,
      Character Target)
    {
      if (Caster?.Square == null || Target?.Square == null || Caster.Deceased || Target.Deceased)
        return;

      var FromSq = Caster.Square;
      var TargetSq = Target.Square;
      var DestSquare = DotaCombatHelper.FindOpenAdjacentSquare(Caster, TargetSq);

      ActiveOmnislashCasters.Add(Caster);
      try
      {
        if (DestSquare != null && DestSquare != Caster.Square)
        {
          DotaCombatHelper.PlayBeamTrail(Engine, FromSq, DestSquare, OmnislashDashBeam, BaseDelay: Delay.FromTurns(10), StepIncrementTurns: 1);
          // 使用静音突进位移，彻底避免 Engine.TeleportCharacter 每刀反复播放刺耳的传送音效
          PathosEngineBridge.DashCharacter(Engine, Caster, DestSquare);
        }
        else if (FromSq != TargetSq)
        {
          DotaCombatHelper.PlayBeamTrail(Engine, FromSq, TargetSq, OmnislashDashBeam, BaseDelay: Delay.FromTurns(10), StepIncrementTurns: 1);
        }

        var ToSq = Caster.Square;
        int SlashDamage = DotaCombatHelper.RollDice(Session.WeaponDice) + Session.StatBonus + Session.OmniBonusDamage;
        var Dir = ToSq.AsDirection(TargetSq);

        LastOmnislashStrikeCritted = false;
        // 物理攻击类伤害（可触发剑舞暴击与剑心犹在加成）
        DotaDamage.Strike(Engine, Caster, Target, Dir, SlashDamage, DotaDamageInfo.Physical(OmnislashSpellName));

        // 播放 Pathos 原生挥砍动作（0.25 秒紧凑连斩节奏）+ 自定义无敌斩金白交叉斩击贴图（0.5 秒完整放大渐隐，暴击时切换炽红新月暴击贴图）
        PathosEngineBridge.StoryAttacking(Engine, Delay.FromTurns(15), Caster, Dir);
        PathosEngineBridge.StoryStriking(
          Engine,
          Delay.HalfSecond,
          TargetSq,
          LastOmnislashStrikeCritted
            ? (BladeDanceCritStrike ?? Ctx.Strikes.flame)
            : (OmnislashStrike ?? Ctx.Strikes.sever));

        if (LastOmnislashStrikeCritted)
        {
          PathosEngineBridge.StoryCaption(Engine, TargetSq, Colour.OrangeRed, DotaLocalization.T("ui.caption.crit"), Delayed: false);
          PathosEngineBridge.StoryEmitting(Engine, TargetSq, Ctx.Sonics.sever, Audibility: 6);
        }

        Session.SlashesDone++;
        Session.RemainingSlashes = Math.Max(0, Session.RemainingSlashes - 1);
      }
      finally
      {
        ActiveOmnislashCasters.Remove(Caster);
      }
    }

    private void FinishOmnislashSession(DotaHeroContext Ctx, object Engine, Character Caster, OmnislashSession Session)
    {
      if (Caster == null)
        return;

      ActiveOmnislashSessions.Remove(Caster);
      SkipNextOmnislashTick.Remove(Caster);

      if (OmnislashStatusProperty != null && PathosEngineBridge.HasTransient(Caster, OmnislashStatusProperty))
        PathosEngineBridge.RemoveTransient(Engine, Caster, Caster, OmnislashStatusProperty);

      if (Session != null && Session.SlashesDone > 0 && !Caster.Deceased)
      {
        PathosEngineBridge.ApplyTransient(Engine, Caster, Caster, Ctx.Properties.displacement, Dice.One);
        PathosEngineBridge.WriteInternalLog(
          Engine,
          Caster,
          OmnislashGlyph,
          DotaLocalization.Format("jugg.log.omni_finished", Session.SlashesDone));
      }
    }

    private static bool IsInOmnislash(Character Character)
    {
      if (Character == null)
        return false;
      if (ActiveOmnislashCasters.Contains(Character))
        return true;
      return ActiveOmnislashSessions.TryGetValue(Character, out var Session) && Session.RemainingSlashes > 0;
    }

    // 5 阶熟练度数值表（1 Proficient .. 5 Champion）
    private static readonly DotaTable<float> HealingWardDurationSeconds = DotaTable.Of(18.0f, 20.0f, 22.0f, 24.0f, 24.0f);
    private static readonly DotaTable<float> HealingWardHealPctPerSecond = DotaTable.Of(0.02f, 0.03f, 0.04f, 0.05f, 0.065f);
    private static readonly DotaTable<float> BladeFuryDurationSeconds = DotaTable.Of(5.0f, 5.0f, 5.0f, 5.0f, 6.0f); // Champion 对应 +1 秒天赋

    private static int GetHealingWardDurationByTier(int Tier)
      => DotaMechanicsMapper.MapDurationTurns(HealingWardDurationSeconds[Tier]);

    private static float GetHealingWardHealPctByTier(int Tier)
      => HealingWardHealPctPerSecond[Tier] / DotaMechanicsMapper.TurnsPerSecond;

    private static int GetBladeFuryDurationByTier(int Tier)
      => DotaMechanicsMapper.MapDurationTurns(BladeFuryDurationSeconds[Tier]);

    private static float GetBladeFuryStarDamageMultiplier(Character Character)
      => BladeFuryAbility.StarMultiplier(Character);

    private static int GetBladeFuryExtraTurns(Character Character)
      => BladeFuryAbility.Level(Character) >= 4 ? 2 : 0;

    private static int GetHealingWardExtraTurns(Character Character)
      => HealingWardAbility.StarSteps(Character) * 4;

    private static float GetHealingWardExtraHealPct(Character Character)
      => HealingWardAbility.StarSteps(Character) * 0.005f;

    private static int GetOmnislashExtraSlashes(Character Character)
      => OmnislashAbility.StarSteps(Character) * 2;

    private static int GetOmnislashExtraDamage(Character Character)
      => OmnislashAbility.StarSteps(Character) * 2;

    private static float GetBladeDanceCritChanceByLevel(int SkillLevel) => SkillLevel switch
    {
      <= 0 => 0.0f,
      1 => 0.20f,
      2 => 0.25f,
      3 => 0.30f,
      _ => 0.35f
    };

    private static float GetBladeDanceCritChance(Character Character)
      => GetBladeDanceCritChanceByLevel(HeroSkillService.GetSkillLevel(Character, SkillBladeDanceId));

    // ---- 施法结算与帮助面板共用的数值计算（单一来源，防止两处公式漂移） ----

    /// <summary>剑舞暴击倍率（T3 右天赋 1.95×，否则 1.8×）。</summary>
    private static float GetBladeDanceCritMultiplier(Character Character)
      => TalentService.HasTalent(Character, TalentT3BladeDanceCrit) ? 1.95f : 1.80f;

    /// <summary>剑心犹在：近战普攻 / 无敌斩每层追加 2.5% DEX 伤害。</summary>
    private static int GetBladeformBonusDamage(Character Character)
    {
      int Stacks = IsJuggernaut(Character) ? GetBladeformState(Character).Stacks : 0;
      return Stacks > 0 ? (int)MathF.Round(DotaStats.Dex(Character) * 0.025f * Stacks) : 0;
    }

    private readonly record struct BladeFurySpinStats(Dice SpinDice, int Dex, int BladeformStacks, float Cadence, float StarMult, bool HasDamageTalent);

    /// <summary>
    /// 剑刃风暴每回合旋转伤害骰：DOTA2 7.42 基础 DPS 85/115/145/175（Champion 205，T3 左天赋 +120 DPS），
    /// 合并为每回合 1 次结算（0.5s），并按敏捷与剑心犹在攻速频率缩放（还原 7.36+/7.42 攻速缩短跳伤间隔），再乘升星倍率。
    /// </summary>
    private static BladeFurySpinStats ComputeBladeFurySpin(Character Caster)
      => ComputeBladeFurySpin(Caster, BladeFuryAbility.Tier(Caster));

    /// <summary>指定熟练度阶数下的剑刃风暴旋转伤害（帮助面板各阶按当前属性/升星/天赋计算）。</summary>
    private static BladeFurySpinStats ComputeBladeFurySpin(Character Caster, int Tier)
    {
      int Dex = DotaStats.Dex(Caster);
      int BladeformStacks = IsJuggernaut(Caster) ? GetBladeformState(Caster).Stacks : 0;

      float BaseDps = BladeFuryDotaDps[Math.Clamp(Tier - 1, 0, BladeFuryDotaDps.Length - 1)];
      bool HasDamageTalent = TalentService.HasTalent(Caster, TalentT3BladeFuryDamage);
      if (HasDamageTalent)
        BaseDps += BladeFuryTalentBonusDotaDps;

      float Cadence = 1.0f + Math.Max(0, Dex - 10) * 0.05f + BladeformStacks * 0.025f;
      float StarMult = GetBladeFuryStarDamageMultiplier(Caster);
      Dice SpinDice = DotaMechanicsMapper.MapDotDamageDicePerTurn(BaseDps * Cadence * StarMult, DieFaces: 4);
      return new BladeFurySpinStats(SpinDice, Dex, BladeformStacks, Cadence, StarMult, HasDamageTalent);
    }

    /// <summary>治疗守卫持续回合（熟练度 + 升星每星 +4 回合）。</summary>
    private static int GetHealingWardDuration(Character Caster, int Tier)
      => GetHealingWardDurationByTier(Tier) + GetHealingWardExtraTurns(Caster);

    /// <summary>治疗守卫每回合回复最大生命百分比（熟练度 + 升星每星 +0.5%）。</summary>
    private static float GetHealingWardHealPct(Character Caster, int Tier)
      => GetHealingWardHealPctByTier(Tier) + GetHealingWardExtraHealPct(Caster);

    /// <summary>无敌斩基础斩数（熟练度 + 敏捷，5~12）。</summary>
    private static int GetOmnislashBaseSlashes(int Tier, int Dex)
      => Math.Clamp(4 + Tier + Math.Max(0, (Dex - 14) / 6), 5, 12);

    /// <summary>无敌斩熟练度额外攻击力（DOTA2 25/30/35/40/45 → K_dmg = 15）。</summary>
    private static int GetOmnislashTierBonusDamage(int Tier)
      => DotaMechanicsMapper.MapSkillDamage(OmnislashBonusDotaDamage[Math.Clamp(Tier - 1, 0, OmnislashBonusDotaDamage.Length - 1)]);

    private readonly record struct OmnislashStats(int BaseSlashes, int TotalSlashes, bool HasSlashTalent, Dice WeaponDice, int StatBonus, int OmniBonusDamage);

    /// <summary>无敌斩：总斩数（基础 + 升星 + T4 右天赋 +3）与单斩伤害构成（武器骰 + 属性修正 + 技能附加攻击力）。</summary>
    private static OmnislashStats ComputeOmnislash(DotaHeroContext Ctx, object Engine, Character Caster, int Tier)
    {
      int Dex = DotaStats.Dex(Caster);
      int Str = DotaStats.Str(Caster);
      int BaseSlashes = GetOmnislashBaseSlashes(Tier, Dex);
      bool HasSlashTalent = TalentService.HasTalent(Caster, TalentT4OmnislashSlashes);
      int TotalSlashes = BaseSlashes + GetOmnislashExtraSlashes(Caster) + (HasSlashTalent ? 3 : 0);

      var WeaponAsset = DotaCombatHelper.GetEquippedWeapon(Ctx, Caster);
      var WeaponDice = PathosEngineBridge.GetMeleeDamageDice(Engine, Caster, WeaponAsset);
      int StatBonus = Math.Max(0, (Math.Max(Str, Dex) - 10) / 2);
      int OmniBonusDamage = GetOmnislashTierBonusDamage(Tier) + GetOmnislashExtraDamage(Caster);
      return new OmnislashStats(BaseSlashes, TotalSlashes, HasSlashTalent, WeaponDice, StatBonus, OmniBonusDamage);
    }

    private static void RegisterSpellHelpSpecs(DotaHeroContext Ctx)
    {
      // 1. Q：剑刃风暴
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = BladeFurySpellName,
        ResolveCurrentTier = BladeFuryAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = BladeFuryAbility.Tier(Ch);
          int ExtraTurns = GetBladeFuryExtraTurns(Ch);
          int Turns = GetBladeFuryDurationByTier(Tier) + ExtraTurns;
          var Spin = ComputeBladeFurySpin(Ch);
          int Dex = Spin.Dex;
          int BladeformStacks = Spin.BladeformStacks;
          bool HasDmgTalent = Spin.HasDamageTalent;
          float Cadence = Spin.Cadence;
          float StarMult = Spin.StarMult;
          Dice SpinDice = Spin.SpinDice;

          float CritChance = GetBladeDanceCritChance(Ch);
          bool HasCritTalent = TalentService.HasTalent(Ch, TalentT3BladeDanceCrit);
          float CritMult = GetBladeDanceCritMultiplier(Ch);
          bool HasSpeedTalent = TalentService.HasTalent(Ch, TalentT2BladeFurySpeed);

          string TalentDmgNote = HasDmgTalent
            ? DotaLocalization.Format("ui.common.comma_item", DotaHelpColor.Talent(DotaLocalization.T("jugg.help.q.tag_t3_dps")))
            : string.Empty;
          string StarDmgNote = StarMult > 1.001f
            ? DotaLocalization.Format("ui.common.comma_item", DotaHelpColor.Skill(DotaLocalization.Format("ui.help.tag_star_mult", $"{StarMult:0.00}")))
            : string.Empty;
          string TalentSpeedNote = HasSpeedTalent
            ? $" + {DotaHelpColor.Talent(DotaLocalization.T("jugg.help.q.tag_t2_speed"))}"
            : string.Empty;
          string CritMultText = HasCritTalent
            ? DotaHelpColor.Talent($"{CritMult:0.##}×")
            : DotaHelpColor.Passive($"{CritMult:0.##}×");

          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("jugg.help.q.key_duration"), DotaLocalization.Format(
              "jugg.help.q.val_duration",
              DotaHelpColor.Control(DotaHelpFormat.Turns(Turns)),
              ExtraTurns > 0 ? DotaLocalization.Format("jugg.help.q.max_star_extra_turns", ExtraTurns) : string.Empty)),
            (DotaLocalization.T("jugg.help.q.key_area"), DotaLocalization.Format(
              "jugg.help.q.val_area",
              DotaHelpColor.Control(DotaLocalization.T("jugg.help.tiles_3x3")),
              DotaHelpColor.Control(DotaHelpFormat.Tiles(1)))),
            (DotaLocalization.T("jugg.help.q.key_dmg"), DotaLocalization.Format(
              "jugg.help.q.val_dmg",
              DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(SpinDice)),
              DotaHelpColor.Magic($"{SpinDice.Average():0.#}"))),
            (DotaLocalization.T("jugg.help.q.key_scale"), DotaLocalization.Format(
              "jugg.help.q.val_scale",
              DotaHelpColor.Stat($"×{Cadence * StarMult:0.##}"),
              DotaHelpColor.Stat(DotaHelpFormat.Dex(Dex)),
              DotaHelpColor.Passive(DotaLocalization.Format("jugg.help.bladeform_stacks", BladeformStacks)),
              StarDmgNote,
              TalentDmgNote)),
            (DotaLocalization.T("jugg.help.q.key_def_crit"), DotaLocalization.Format(
              "jugg.help.q.val_def_crit",
              DotaHelpColor.Control(DotaLocalization.T("jugg.help.q.magic_dr_tag")),
              TalentSpeedNote,
              DotaHelpColor.Passive(DotaLocalization.Format("jugg.help.q.blade_dance_pct", $"{CritChance * 100:0}")),
              CritMultText))
          };
          return Rows;
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("jugg.help.q.mech_1", DotaHelpColor.Control(DotaLocalization.T("jugg.help.q.magic_elem_dr_tag"))),
          DotaLocalization.Format(
            "jugg.help.q.mech_2",
            DotaHelpColor.Control(DotaLocalization.T("jugg.help.tiles_3x3")),
            DotaHelpColor.Stat(DotaLocalization.T("jugg.help.word.dex")),
            DotaHelpColor.Passive(DotaLocalization.T("jugg.help.word.bladeform")),
            DotaHelpColor.Passive(DotaLocalization.T("jugg.help.word.blade_dance_crit")))
        },
        // 各阶数值按当前升星 / 属性 / 天赋实算（当前阶与实时面板一致；百科无角色时为基础值）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaLocalization.Format("jugg.help.q.tier_suffix", GetBladeFuryDurationByTier(Tier) + GetBladeFuryExtraTurns(HelpCtx.Character)),
          Tier => DotaLocalization.Format(
            "jugg.help.q.tier_bullet_full",
            DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(ComputeBladeFurySpin(HelpCtx.Character, Tier).SpinDice))))
      });

      // 2. W：治疗守卫
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = HealingWardSpellName,
        ResolveCurrentTier = HealingWardAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = HealingWardAbility.Tier(Ch);
          int Range = HealingWardAbility.BaseRange(Tier);
          int Duration = GetHealingWardDuration(Ch, Tier);
          float HealPct = GetHealingWardHealPct(Ch, Tier);
          bool HasManaTalent = TalentService.HasTalent(Ch, TalentT1HealingWardMana);
          int ActualMana = DotaHelpFormat.ActualMana(Ch, HealingWardAbility);

          int MaxHp = Ch.Life?.Total ?? 0;
          int SelfHealPerTurn = MaxHp > 0 ? Math.Max(1, (int)MathF.Round(MaxHp * HealPct)) : 1;

          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("jugg.help.key_actual_mana"), HasManaTalent
              ? DotaLocalization.Format(
                  "jugg.help.mana_with_talent",
                  DotaHelpColor.Mana($"{ActualMana}"),
                  HealingWardManaCost,
                  DotaHelpColor.Talent(DotaLocalization.T("jugg.help.w.t1_mana_tag")))
              : DotaHelpColor.Mana($"{ActualMana}")),
            (DotaLocalization.T("jugg.help.w.key_cast_aura"), DotaLocalization.Format(
              "jugg.help.w.val_cast_aura",
              DotaHelpColor.Control(DotaHelpFormat.Tiles(Range)),
              DotaHelpColor.Control(DotaHelpFormat.Tiles(2)))),
            (DotaLocalization.T("jugg.help.q.key_duration"), DotaLocalization.Format(
              "jugg.help.w.val_duration",
              DotaHelpColor.Control(DotaHelpFormat.Turns(Duration)))),
            (DotaLocalization.T("jugg.help.w.key_heal"), DotaLocalization.Format(
              "jugg.help.w.val_heal",
              DotaHelpColor.Heal($"{HealPct * 100:0.##}%"),
              DotaHelpColor.Heal(DotaLocalization.Format("jugg.help.hp_per_turn", SelfHealPerTurn)),
              DotaHelpColor.Heal(DotaLocalization.Format("jugg.help.hp_total", SelfHealPerTurn * Duration))))
          };

          return Rows;
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "jugg.help.w.mech_1",
            DotaHelpColor.Control(DotaHelpFormat.Tiles(2)),
            DotaHelpColor.Heal("1 HP"))
        },
        // 各阶数值含当前升星加成（当前阶与实时面板一致；百科无角色时为基础值）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(HealingWardAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier => DotaLocalization.Format(
            "jugg.help.w.tier_bullet",
            DotaHelpColor.Control(DotaHelpFormat.Turns(GetHealingWardDuration(HelpCtx.Character, Tier))),
            DotaHelpColor.Heal(DotaLocalization.Format("jugg.help.w.pct_max_hp", $"{GetHealingWardHealPct(HelpCtx.Character, Tier) * 100:0.##}"))))
      });

      // 3. R：无敌斩
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = OmnislashSpellName,
        ResolveCurrentTier = OmnislashAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = OmnislashAbility.Tier(Ch);
          int CastRange = OmnislashAbility.BaseRange(Tier);

          var Omni = ComputeOmnislash(Ctx, HelpCtx.Engine, Ch, Tier);
          int BaseSlashes = Omni.BaseSlashes;
          bool HasSlashTalent = Omni.HasSlashTalent;
          int TotalSlashes = Omni.TotalSlashes;

          bool HasManaTalent = TalentService.HasTalent(Ch, TalentT2OmnislashMana);
          int ActualMana = DotaHelpFormat.ActualMana(Ch, OmnislashAbility);

          var WeaponDice = Omni.WeaponDice;
          int StatBonus = Omni.StatBonus;
          int OmniBonus = Omni.OmniBonusDamage;
          int BladeformBonus = GetBladeformBonusDamage(Ch);

          int FlatBonus = StatBonus + OmniBonus + BladeformBonus;
          int MinSlash = DotaSpellHelpBridge.GetDiceMin(WeaponDice) + FlatBonus;
          int MaxSlash = DotaSpellHelpBridge.GetDiceMax(WeaponDice) + FlatBonus;
          float AvgSlash = WeaponDice.Average() + FlatBonus;

          float CritChance = GetBladeDanceCritChance(Ch);
          bool HasCritTalent = TalentService.HasTalent(Ch, TalentT3BladeDanceCrit);
          float CritMult = GetBladeDanceCritMultiplier(Ch);

          string WeaponPart = DotaLocalization.Format("jugg.help.r.part_weapon", DotaHelpColor.Physical(DotaSpellHelpBridge.FormatDiceNotation(WeaponDice)));
          string StatPart = DotaLocalization.Format("jugg.help.r.part_stat", DotaHelpColor.Stat($"+{StatBonus}"));
          string SkillPart = DotaLocalization.Format("jugg.help.r.part_skill", DotaHelpColor.Skill($"+{OmniBonus}"));
          string Breakdown = BladeformBonus > 0
            ? $"{WeaponPart} {StatPart} {SkillPart} {DotaLocalization.Format("jugg.help.r.part_bladeform", DotaHelpColor.Passive($"+{BladeformBonus}"))}"
            : $"{WeaponPart} {StatPart} {SkillPart}";

          string CritMultText = HasCritTalent
            ? DotaHelpColor.Talent($"{CritMult:0.##}×")
            : DotaHelpColor.Passive($"{CritMult:0.##}×");

          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("jugg.help.key_actual_mana"), HasManaTalent
              ? DotaLocalization.Format(
                  "jugg.help.mana_with_talent",
                  DotaHelpColor.Mana($"{ActualMana}"),
                  OmnislashManaCost,
                  DotaHelpColor.Talent(DotaLocalization.T("jugg.help.r.t2_mana_tag")))
              : DotaHelpColor.Mana($"{ActualMana}")),
            (DotaLocalization.T("jugg.help.r.key_cast_jump"), DotaLocalization.Format(
              "jugg.help.r.val_cast_jump",
              DotaHelpColor.Control(DotaHelpFormat.Tiles(CastRange)),
              DotaHelpColor.Control(DotaHelpFormat.Tiles(3)))),
            (DotaLocalization.T("jugg.help.r.key_slashes"), HasSlashTalent
              ? DotaLocalization.Format(
                  "jugg.help.r.val_slashes_talent",
                  DotaHelpColor.Skill(DotaLocalization.Format("jugg.help.slashes_val", TotalSlashes)),
                  DotaHelpColor.Skill($"{BaseSlashes}"),
                  DotaHelpColor.Talent(DotaLocalization.T("jugg.help.r.t4_slashes_tag")))
              : DotaLocalization.Format(
                  "jugg.help.r.val_slashes_dex",
                  DotaHelpColor.Skill(DotaLocalization.Format("jugg.help.slashes_val", TotalSlashes)),
                  DotaHelpColor.Stat(DotaLocalization.T("jugg.help.word.dex")),
                  DotaHelpColor.Skill(DotaLocalization.Format("jugg.help.slashes_val", 12)))),
            (DotaLocalization.T("jugg.help.r.key_per_slash"), DotaLocalization.Format(
              "jugg.help.r.val_per_slash",
              DotaHelpColor.Physical($"{MinSlash}~{MaxSlash}"),
              DotaHelpColor.Physical($"{AvgSlash:0.#}"),
              Breakdown)),
            (DotaLocalization.T("jugg.help.r.key_total"), DotaLocalization.Format(
              "jugg.help.r.val_total",
              DotaHelpColor.Physical($"{MinSlash * TotalSlashes}~{MaxSlash * TotalSlashes}"),
              DotaHelpColor.Passive(DotaLocalization.Format("jugg.help.q.blade_dance_pct", $"{CritChance * 100:0}")),
              CritMultText))
          };

          return Rows;
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "jugg.help.r.mech_1",
            DotaHelpColor.Physical(DotaLocalization.T("jugg.help.r.tag_weapon_dice")),
            DotaHelpColor.Stat(DotaLocalization.T("jugg.help.r.tag_stat_mod")),
            DotaHelpColor.Skill(DotaLocalization.T("jugg.help.r.tag_skill_atk")),
            DotaHelpColor.Control(DotaHelpFormat.Tiles(3))),
          DotaLocalization.Format(
            "jugg.help.r.mech_2",
            DotaHelpColor.Stat(DotaLocalization.T("jugg.help.word.dex")),
            DotaHelpColor.Stat(DotaLocalization.T("jugg.help.r.tag_plus_1_slash")),
            DotaHelpColor.Talent(DotaLocalization.T("jugg.help.r.tag_t4_plus_3"))),
          DotaLocalization.Format(
            "jugg.help.r.mech_3",
            DotaHelpColor.Passive(DotaLocalization.T("jugg.help.word.blade_dance_crit")),
            DotaHelpColor.Passive(DotaLocalization.T("jugg.help.word.bladeform")))
        },
        // 游戏内：各阶斩数与额外攻击力按当前敏捷 / 升星 / 天赋实算（当前阶与实时面板一致）；百科无角色时列基础区间
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(OmnislashAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier =>
          {
            if (HelpCtx.Character == null)
              return DotaLocalization.Format(
                "jugg.help.r.tier_bullet",
                DotaHelpColor.Skill(DotaLocalization.Format("jugg.help.slashes_val", $"{Math.Clamp(4 + Tier, 5, 12)}~12")),
                DotaHelpColor.Skill($"+{GetOmnislashTierBonusDamage(Tier)}"));

            var Omni = ComputeOmnislash(Ctx, HelpCtx.Engine, HelpCtx.Character, Tier);
            return DotaLocalization.Format(
              "jugg.help.r.tier_bullet_full",
              DotaHelpColor.Skill(DotaLocalization.Format("jugg.help.slashes_val", Omni.TotalSlashes)),
              DotaHelpColor.Skill($"+{Omni.OmniBonusDamage}"));
          })
      });
    }

    /// <summary>被动「剑舞」与先天「剑心犹在」的 Property 帮助面板（实时数值 + 按等级明细）。</summary>
    private static void RegisterPassiveHelpSpecs()
    {
      // E：剑舞（被动，技能等级 1~4）
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        PropertyName = "blade dance",
        ResolveCurrentLevel = Ch => IsJuggernaut(Ch) ? HeroSkillService.GetSkillLevel(Ch, SkillBladeDanceId) : 0,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (!IsJuggernaut(Ch))
            return null;

          int Level = HeroSkillService.GetSkillLevel(Ch, SkillBladeDanceId);
          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("ui.help.key_skill_level"), Level > 0
              ? DotaHelpColor.Skill(DotaLocalization.Format("ui.help.val_skill_level", Level, 4))
              : DotaLocalization.T("ui.help.not_learned"))
          };
          if (Level <= 0)
            return Rows;

          float Chance = GetBladeDanceCritChance(Ch);
          float Mult = GetBladeDanceCritMultiplier(Ch);
          bool HasCritTalent = TalentService.HasTalent(Ch, TalentT3BladeDanceCrit);
          string MultText = HasCritTalent
            ? DotaHelpColor.Talent($"{Mult:0.##}×") + DotaLocalization.Format("ui.common.paren_item", DotaHelpColor.Talent(DotaLocalization.T("jugg.help.e.t3_tag")))
            : DotaHelpColor.Passive($"{Mult:0.##}×");

          Rows.Add((DotaLocalization.T("jugg.help.e.key_crit"), DotaLocalization.Format(
            "jugg.help.e.val_crit",
            DotaHelpColor.Passive($"{Chance * 100:0}%"),
            MultText)));
          Rows.Add((DotaLocalization.T("jugg.help.e.key_expected"), DotaLocalization.Format(
            "jugg.help.e.val_expected",
            DotaHelpColor.Physical($"×{1.0f + Chance * (Mult - 1.0f):0.###}"))));
          if (TalentService.HasTalent(Ch, TalentT4BladeDanceLifesteal))
            Rows.Add((DotaLocalization.T("jugg.help.e.key_lifesteal"), DotaLocalization.Format(
              "jugg.help.e.val_lifesteal",
              DotaHelpColor.Heal("40%"),
              DotaHelpColor.Talent(DotaLocalization.T("jugg.help.e.t4_tag")))));
          return Rows;
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "jugg.help.e.mech_1",
            DotaHelpColor.Skill(DotaLocalization.T("jugg.skill.r.title")),
            DotaHelpColor.Skill(DotaLocalization.T("jugg.skill.q.title"))),
          DotaLocalization.T("jugg.help.e.mech_2")
        },
        BuildLevels = _ => DotaSpellHelpBridge.Levels(
          4,
          Lv => DotaLocalization.Format("jugg.help.e.level_suffix", $"{GetBladeDanceCritChanceByLevel(Lv) * 100:0}"),
          Lv => DotaLocalization.Format(
            "jugg.help.e.val_crit",
            DotaHelpColor.Passive($"{GetBladeDanceCritChanceByLevel(Lv) * 100:0}%"),
            DotaHelpColor.Passive("1.8×")))
      });

      // 先天：剑心犹在（无等级）
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        PropertyName = "bladeform",
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (!IsJuggernaut(Ch))
            return null;

          var State = GetBladeformState(Ch);
          bool HasIntervalTalent = TalentService.HasTalent(Ch, TalentT1BladeformInterval);
          int Interval = HasIntervalTalent ? Math.Max(1, BladeformIntervalTurns / 2) : BladeformIntervalTurns;

          string LingerNote = State.LingerTurns > 0
            ? DotaLocalization.Format("jugg.help.bf.linger_note", DotaHelpColor.Control(DotaHelpFormat.Turns(State.LingerTurns)))
            : string.Empty;
          string TalentNote = HasIntervalTalent
            ? DotaLocalization.Format("ui.common.paren_item", DotaHelpColor.Talent(DotaLocalization.T("jugg.help.bf.t1_tag")))
            : string.Empty;

          return new List<(string, string)>
          {
            (DotaLocalization.T("jugg.help.bf.key_stacks"), DotaLocalization.Format(
              "jugg.help.bf.val_stacks",
              DotaHelpColor.Passive($"{State.Stacks}"),
              BladeformMaxStacks,
              LingerNote)),
            (DotaLocalization.T("jugg.help.bf.key_gain"), DotaLocalization.Format(
              "jugg.help.bf.val_gain",
              DotaHelpColor.Control(DotaHelpFormat.Turns(Interval)),
              TalentNote)),
            (DotaLocalization.T("jugg.help.bf.key_bonus"), DotaLocalization.Format(
              "jugg.help.bf.val_bonus",
              DotaHelpColor.Control($"+{State.Stacks * 1.0f:0.#}%"),
              DotaHelpColor.Stat($"+{State.Stacks * 2.5f:0.#}%"),
              DotaHelpColor.Physical($"+{GetBladeformBonusDamage(Ch)}")))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "jugg.help.bf.mech_per_stack",
            DotaHelpColor.Control("+1%"),
            DotaHelpColor.Stat("+2.5%"),
            DotaHelpColor.Physical(DotaLocalization.T("jugg.help.bf.dex_2_5_pct"))),
          DotaLocalization.Format(
            "jugg.help.bf.mech_linger",
            DotaHelpColor.Control(DotaHelpFormat.Turns(BladeformLingerTurns)),
            BladeformMaxStacks)
        }
      });
    }

    private static BladeformState GetBladeformState(Character Character)
      => BladeformStates.GetValue(Character, _ => new BladeformState());

    private static bool IsJuggernaut(Character Character)
      => DotaCombatHelper.IsClass(Character, JuggernautClassName);

    private static bool IsHealingWard(Character Character)
      => DotaCombatHelper.IsEntity(Character, HealingWardEntityName);

    private static Entity RegisterHealingWardEntity(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Entities.Add(E =>
      {
        E.Name = HealingWardEntityName;
        E.Description = "A floating restorative totem summoned by Juggernaut that heals nearby allies for a percentage of their maximum HP each turn.";
        E.Glyph = Icon;
        E.Kind = Ctx.Kinds.light;
        E.IsBase = false;
        E.IsAnimate = true;
        E.Frequency = Frequency.None;
        E.Level = 1;
        E.Difficulty = 1;
        E.Challenge = 1;
        E.Size = Size.Small;
        E.Speed = Speed.S5_0;
        E.Weight = Weight.Zero;
        E.Defence = new Defence(10, 0, 0, 0);
        E.Strategy = Strategy.Attack;
        // 保持召唤物实体本身静音（仅在召唤瞬间播放一次柔和风铃音），避免每回合移动/环境音持续吵闹

        E.SetCorpse(Chance.Never, null, null);
        E.SetDiet(Ctx.Diets.inediate);
        E.SetGender(Ctx.Genders.nonbinary);
        E.SetTerrain(Ctx.Materials.air, Ctx.Materials.water);

        E.Figure.Material = Ctx.Materials.wood;
        E.Figure.CombatSkill = Ctx.Skills.unarmed_combat;
        E.Figure.Set(Ctx.Anatomies.amorphous);

        Ctx.SetFormScores(E.DefaultForm, Str: 4, Dex: 10, Con: 4, Int: 6, Wis: 12, Cha: 10);
        Ctx.SetFormScores(E.LimitForm, Str: 6, Dex: 14, Con: 6, Int: 8, Wis: 14, Cha: 12);

        E.LifeAdvancement.Set(1, Dice.Zero);
        E.ManaAdvancement.Set(0, Dice.Zero);
      });
    }

    private void RegisterAbilities(DotaHeroContext Ctx, Glyph BladeFuryIcon, Glyph HealingWardIcon, Glyph OmnislashIcon)
    {
      // Q：剑刃风暴（自身施放，声明式施加原生 blade fury Transient：DOTA2 全等级 5 秒，Champion 对应 +1 秒 -> 6 秒）
      BladeFuryAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = BladeFurySpellName,
        Description = "Dispel debuffs and spin for 10 to 12 turns: debuff immunity, 80% magical DR, and 1d4 to 2d4+2 base magical damage per turn (85-205 DPS mapped via K_dmg=15, scaled by DEX & Bladeform attack speed) to adjacent 3x3 foes; can crit with Blade Dance.",
        Icon = BladeFuryIcon,
        School = Ctx.Schools.evocation,
        Skill = Ctx.Skills.evocation,
        Precept = new Precept(Purpose.Buff),
        ManaCost = BladeFuryManaCost,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillBladeFuryId,
        ConfigureApply = (Tier, Apply) => Apply.ApplyTransient(BladeFuryStatusProperty, Dice.Fixed(GetBladeFuryDurationByTier(Tier))),
        OnCast = Cast => HandleBladeFuryCast(Ctx, Cast)
      });

      // W：治疗守卫（点地召唤，3~5 格随牧师系熟练度；声明式射线仅触发选格与扣蓝，唯一召唤由 HandleHealingWardCast 精确执行）
      HealingWardAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = HealingWardSpellName,
        DisplayTitle = "Healing Ward",
        TitleResolver = () => DotaLocalization.T("jugg.skill.w.title"),
        Description = "Summon a fragile movable Healing Ward (1 HP, max 1 active) for 36 to 48 turns that heals all allies within 2 tiles for 1% to 3.25% of their maximum HP each turn.",
        Icon = HealingWardIcon,
        School = Ctx.Schools.clerical,
        Skill = Ctx.Skills.clerical,
        Precept = new Precept(Purpose.Healing),
        ManaCost = HealingWardManaCost,
        Targeting = DotaAbilityTargeting.PointOpen,
        Range = DotaTable.Of(3, 3, 4, 4, 5),
        AllowSelfTarget = true,
        SkillSlotId = SkillHealingWardId,
        OnCast = Cast => HandleHealingWardCast(Ctx, Cast)
      });

      // R：无敌斩（指向敌方，4~7 格随变化系熟练度）
      OmnislashAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = OmnislashSpellName,
        DisplayTitle = "Omnislash",
        TitleResolver = () => DotaLocalization.T("jugg.skill.r.title"),
        Description = "Leap to a targeted enemy and unleash 5 to 12 rapid invulnerable weapon slashes (weapon + stat + 2 to 3 bonus damage per slash) across nearby foes, triggering Blade Dance crits and Bladeform bonuses.",
        Icon = OmnislashIcon,
        School = Ctx.Schools.transmutation,
        Skill = Ctx.Skills.transmutation,
        Precept = new Precept(Purpose.Teleport),
        ManaCost = OmnislashManaCost,
        Targeting = DotaAbilityTargeting.UnitHostile,
        Range = DotaTable.Of(4, 5, 5, 6, 7),
        SkillSlotId = SkillOmnislashId,
        OnCast = Cast => HandleOmnislashCast(Ctx, Cast)
      });
    }
    private static Class RegisterJuggernautClass(
      DotaHeroContext Ctx,
      Glyph AvatarGlyph,
      Property BladeformTalent,
      Property BladeDanceTalent,
      Spell BladeFurySpell,
      Spell HealingWardSpell,
      Spell OmnislashSpell)
    {
      return Ctx.Classes.Add(C =>
      {
        Profile.ConfigureClass(
          Ctx,
          C,
          Description: "A masked DOTA2 blademaster who builds unstoppable momentum through Bladeform, carves through magic with Blade Fury, sustains allies with Healing Ward, and cuts down foes with Blade Dance and Omnislash.",
          AvatarGlyph: AvatarGlyph,
          LifeBase: 14,
          LifeDice: 1.d10(),
          RaceManaBase: 1,
          ManaDice: 1.d2());

        C.SetDistribution(
          Ctx.Attributes.dexterity,
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.charisma);

        C.AddFeat(1, BladeformTalent);
        C.AddFeat(4, Ctx.Properties.free_action);
        C.AddFeat(8, Ctx.Properties.displacement);
        C.AddFeat(12, Ctx.Properties.quickness);

        C.Startup.SetSkill(Ctx.Qualifications.proficient,
          Ctx.Skills.literacy,
          Ctx.Skills.medium_blade,
          Ctx.Skills.heavy_blade,
          Ctx.Skills.light_armour,
          Ctx.Skills.evocation,
          Ctx.Skills.clerical,
          Ctx.Skills.transmutation);

        C.Startup.Loot.AddKit(Chance.Always, Modifier.Plus1, Ctx.Items.katana);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.leather_armour);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_healing);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.food_ration);
      });
    }
  }
}
