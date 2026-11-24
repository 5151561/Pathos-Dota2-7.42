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
  /// DOTA2 7.42 英雄：斧王（Axe / 蒙哥可汗）
  /// 基于「声明式外壳 + Harmony Hook 核心逻辑」忠实还原 DOTA2 7.42 机制（0 Codex.Properties 占用）：
  /// - 先天技能「孤军奋战（One Man Army）」：
  ///   - 周围 4 格（700 码）内无存活友军时生效，友军靠近后粘滞 6 回合（3 秒）
  ///   - 获得超额防御（Defence - 10）50% 的等效力量加成，转化为近战额外伤害
  /// - Q：狂战士之吼（berserkers call）— 嘲讽周围 2 格（T4 右 3 格）敌方强制向斧王靠近并近战普攻、禁止施法，同时获得 +16/19/22/25 护甲（+6/+7/+7/+8 防御，T2 左封顶 +10），持续 4~6 回合
  /// - W：战斗饥渴（battle hunger）— 远程指向敌方（4~7 格，空放返蓝），24 回合（12s）纯粹持续伤害（12/16/20/24 DPS）与 16%/19%/22%/25% 减速；目标击杀任意单位即驱散
  /// - E：反击螺旋（counter helix）— 独立被动（Lv 1~4）：每受到 7/6/5/4 次敌方攻击（7.42：包含被高防/闪避 Miss 的攻击）触发 3×3 纯粹伤害旋斩（100/120/140/160 纯伤）
  /// - R：淘汰之刃（culling blade）— 近战指向（2 格，空放返蓝）：目标生命 ≤ 斩杀线（275/375/475 → 18/25/32 HP + 熟练度/天赋）直接无视减伤瞬间处决、100% 返还本次法力、给予周围 4 格友军 12 回合（T1 右 18 回合）+25% 移速与 +20/25/30 护甲战吼，并叠加永久护甲（DotaCharacterStorage 存档持久化）
  /// </summary>
  public sealed class AxeHero : IDotaHeroDefinition
  {
    private const string AxeClassName = "axe";
    private const string BerserkersCallSpellName = "berserkers call";
    private const string BattleHungerSpellName = "battle hunger";
    private const string CullingBladeSpellName = "culling blade";

    // 斧王技能加点槽位 ID（小技能 Lv 1/3/5/7，大招 Lv 6/12/18，外加 7 级全属性黄点）
    private const string SkillCallId = "axe.q.berserkers_call";
    private const string SkillHungerId = "axe.w.battle_hunger";
    private const string SkillHelixId = "axe.e.counter_helix";
    private const string SkillCullingId = "axe.r.culling_blade";

    // 斧王天赋节点 ID（DOTA2 7.42 原版节奏：Lv 10 / 15 / 20 / 25 解锁，Lv 27 / 28 / 29 / 30 双修补点）
    private const string TalentT1HungerMoveSpeed = "axe.t1.hunger_ms";
    private const string TalentT1CullingBuffDuration = "axe.t1.culling_buff_dur";
    private const string TalentT2CallArmor = "axe.t2.call_armor";
    private const string TalentT2HungerDps = "axe.t2.hunger_dps";
    private const string TalentT3HelixDamage = "axe.t3.helix_dmg";
    private const string TalentT3Strength = "axe.t3.strength";
    private const string TalentT4CullingDamage = "axe.t4.culling_dmg";
    private const string TalentT4CallAoe = "axe.t4.call_aoe";

    // 存档持久化键（存入 DotaCharacterStorage，单位：0.1 点 DOTA2 护甲，便于精确存储 +1.5 / +0.2 等小数）
    private const string StorageCullingArmorTenthsKey = "axe.cull_armor_x10";

    // DOTA2 7.42 斧王初始三维与成长：STR 25 (+2.7) / AGI 18 (+1.5, 7.42) / INT 18 (+1.6)
    private const int DotaBaseStr = 25;
    private const float DotaStrGain = 2.7f;
    private const int DotaBaseAgi = 18;
    private const float DotaAgiGain = 1.5f;
    private const int DotaBaseInt = 18;
    private const float DotaIntGain = 1.6f;

    private static readonly PathosStartingStats StartingStats =
      DotaMechanicsMapper.MapStartingStats(DotaBaseStr, DotaBaseAgi, DotaBaseInt, Charisma: 10);
    private static readonly PathosStatLimits StatLimits =
      DotaMechanicsMapper.MapStatLimits(DotaBaseStr, DotaStrGain, DotaBaseAgi, DotaAgiGain, DotaBaseInt, DotaIntGain, CharismaLimit: 20, StartingCha: 10);

    // 蓝耗（K_mana = 20）
    private static readonly int BerserkersCallManaCost = DotaMechanicsMapper.MapSkillManaCost(100); // 90/100/110/120 取中值 100 -> 5
    private static readonly int BattleHungerManaCost = DotaMechanicsMapper.MapSkillManaCost(60);    // 50/60/70/80 取中值 60 -> 3
    private static readonly int CullingBladeManaCost = DotaMechanicsMapper.MapSkillManaCost(125);   // 100/125/150 取中值 125 -> 6

    // 先天：孤军奋战
    private const int OneManArmyRadius = 4; // 700 码 ≈ 4 格
    private static readonly int OneManArmyLingerTurns = DotaMechanicsMapper.MapDurationTurns(3.0f); // 3s = 6 回合

    // Q：狂战士之吼（7.42：持续 2.1/2.4/2.7/3.0s，护甲 +16/19/22/25，T2 左 +10 护甲，T4 右 +70 范围）
    private static readonly float[] CallDotaDurations = { 2.1f, 2.4f, 2.7f, 3.0f };
    private static readonly float[] CallDotaArmor = { 16.0f, 19.0f, 22.0f, 25.0f };
    private const float CallTalentBonusArmor = 10.0f;

    // W：战斗饥渴（7.42：12s，12/16/20/24 纯伤 DPS，16%/19%/22%/25% 减速，T2 右 +8 DPS，T1 左每目标 +8% 移速）
    private static readonly int BattleHungerDurationTurns = DotaMechanicsMapper.MapDurationTurns(12.0f); // 24 回合
    private static readonly float[] HungerDotaDps = { 12.0f, 16.0f, 20.0f, 24.0f };
    private static readonly float[] HungerSlowRatios = { 0.16f, 0.19f, 0.22f, 0.25f };
    private const float HungerTalentBonusDps = 8.0f;
    private const float HungerTalentMsPerTarget = 0.08f;

    // E：反击螺旋（7.42：7/6/5/4 次受击触发含 Miss，100/120/140/160 纯伤，T3 左 +40 纯伤）
    private static readonly int[] HelixAttackThresholds = { 7, 6, 5, 4 };
    private static readonly float[] HelixDotaDamage = { 100.0f, 120.0f, 140.0f, 160.0f };
    private const float HelixTalentBonusDotaDamage = 40.0f;

    // R：淘汰之刃（7.42：275/375/475 斩杀线与纯伤，T4 左 +150，战吼 6s +25% 移速 +20/25/30 护甲，T1 右 +3s）
    private static readonly float[] CullingDotaThresholds = { 275.0f, 375.0f, 475.0f };
    private const float CullingTalentBonusDotaDamage = 150.0f;
    private static readonly int CullingBuffBaseTurns = DotaMechanicsMapper.MapDurationTurns(6.0f);    // 12 回合
    private static readonly int CullingBuffTalentExtraTurns = DotaMechanicsMapper.MapDurationTurns(3.0f); // +6 回合
    private const float CullingBuffMoveSpeedRatio = 0.25f;
    private static readonly float[] CullingBuffDotaArmor = { 20.0f, 25.0f, 30.0f };
    private static readonly int[] CullingEliteArmorTenths = { 10, 15, 20 }; // +1.0 / +1.5 / +2.0 DOTA2 护甲
    private static readonly int[] CullingCreepArmorTenths = { 2, 3, 4 };    // +0.2 / +0.3 / +0.4 DOTA2 护甲

    // T3 右天赋：7.42 +15 力量按 1:5 后期标尺 -> +3 STR/CON 与 +18 HP
    private const int TalentStrengthBonus = 3;
    private const int HpPerStrengthPoint = 6;

    private sealed class AxeRuntimeState
    {
      // 先天：孤军奋战
      public bool OneManArmyActive = true;
      public int OneManArmyLingerTurns;
      public int CachedOneManArmyBonusStr;

      // Q：狂战士之吼自身高甲状态
      public int CallRemainingTurns;
      public int CallBonusDefence;

      // E：反击螺旋受击计数
      public int HelixHitCounter;

      // R：淘汰之刃战吼增益（斧王自身与友军共用另一表，但此处保留快捷读取）
      public readonly HashSet<WeakReference<Character>> ActiveHungerTargets = new();
    }

    private sealed class TauntState
    {
      public WeakReference<Character> Taunter;
      public int RemainingTurns;
    }

    private sealed class BattleHungerState
    {
      public WeakReference<Character> Caster;
      public int RemainingTurns;
      public float DotaDps;
      public float SlowRatio;
      public float DamageAccumulator;
    }

    private sealed class CullingBuffState
    {
      public int RemainingTurns;
      public int BonusDefence;
    }

    private static readonly ConditionalWeakTable<Character, AxeRuntimeState> AxeStates = new();
    private static readonly ConditionalWeakTable<Character, TauntState> TauntedUnits = new();
    private static readonly ConditionalWeakTable<Character, BattleHungerState> HungerAfflicted = new();
    private static readonly ConditionalWeakTable<Character, CullingBuffState> CullingBuffs = new();

    [ThreadStatic]
    private static bool IsInsideDefenceQuery;

    [ThreadStatic]
    private static bool IsInsideTauntRedirect;

    [ThreadStatic]
    private static bool IsInsideHelixDamage;

    private Property OneManArmyTalentProperty;
    private Property CounterHelixTalentProperty;
    private Spell BerserkersCallSpellRef;
    private Spell BattleHungerSpellRef;
    private Spell CullingBladeSpellRef;

    private Glyph MogulKhanGlyph;
    private Glyph OneManArmyGlyph;
    private Glyph BerserkersCallGlyph;
    private Glyph BattleHungerGlyph;
    private Glyph CounterHelixGlyph;
    private Glyph CullingBladeGlyph;

    private Explosion BerserkersCallExplosion;
    private Explosion CounterHelixExplosion;
    private Strike BattleHungerStrike;
    private Strike CullingBladeStrike;

    public string Id => "axe";

    public void Register(DotaHeroContext Ctx)
    {
      // 1. 注册贴图（使用 "mogul khan" 避开官方内置物品 "axe"）
      MogulKhanGlyph = Ctx.RegisterGlyph("mogul khan");
      OneManArmyGlyph = Ctx.RegisterGlyph("one man army");
      BerserkersCallGlyph = Ctx.RegisterGlyph("berserkers call");
      BattleHungerGlyph = Ctx.RegisterGlyph("battle hunger");
      CounterHelixGlyph = Ctx.RegisterGlyph("counter helix");
      CullingBladeGlyph = Ctx.RegisterGlyph("culling blade");

      // 1.5 注册斧王专属自定义像素 VFX
      BerserkersCallExplosion = Ctx.RegisterExplosion3x3Vfx("berserkers call wave", "berserkers call fx");
      CounterHelixExplosion = Ctx.RegisterExplosion3x3Vfx("counter helix spin", "counter helix fx");
      BattleHungerStrike = Ctx.RegisterStrikeVfx("battle hunger mark", "battle hunger fx strike");
      CullingBladeStrike = Ctx.RegisterStrikeVfx("culling blade strike", "culling blade fx strike");

      // 2. 注册长效先天「一人之军」与被动「反击螺旋」Property（在原生天赋栏与百科可见，且支持点击状态栏查看说明）
      OneManArmyTalentProperty = Ctx.RegisterPassiveProperty(
        Name: "one man army",
        Description: "Innate (One Man Army): While no living allies are within 4 tiles (lingers 6 turns when an ally approaches), Axe gains bonus Strength equal to 50% of his excess Defence (Defence - 10), increasing melee attack damage.",
        Icon: OneManArmyGlyph);
      CounterHelixTalentProperty = Ctx.RegisterPassiveProperty(
        Name: "counter helix",
        Description: "Passive (Counter Helix): Every 7 / 6 / 5 / 4 incoming attacks (by Counter Helix skill level 1-4, including missed attacks) triggers a 3x3 crimson axe spin dealing 100 / 120 / 140 / 160 Pure damage (1d6+3 to 3d6).",
        Icon: CounterHelixGlyph);

      // 3. 注册 3 个主动技能外壳（Q / W / R）
      BerserkersCallSpellRef = RegisterBerserkersCallSpell(Ctx, BerserkersCallGlyph);
      BattleHungerSpellRef = RegisterBattleHungerSpell(Ctx, BattleHungerGlyph);
      CullingBladeSpellRef = RegisterCullingBladeSpell(Ctx, CullingBladeGlyph);

      // 4. 注册专属职业：斧王（axe）
      var AxeClass = RegisterAxeClass(Ctx, MogulKhanGlyph, OneManArmyTalentProperty);

      // 5. 注册预设英雄：Axe（官方兽人种族 + 斧王职业）
      Ctx.RegisterPresetHero("Axe", Ctx.Races.orc, AxeClass, Ctx.Genders.male, MogulKhanGlyph);

      // 6. 注册技能加点表（Q/W/E/R + 7 级全属性黄点，力量主属性；E 反击螺旋 0→1 授予原生被动专长）
      RegisterSkillBuild(Ctx, BerserkersCallSpellRef, BattleHungerSpellRef, CullingBladeSpellRef, CounterHelixTalentProperty);

      // 7. 注册 DOTA2 7.42 四层天赋树（Lv 10/15/20/25 + Lv 27~30 双修）
      RegisterTalentTree(Ctx);
    }

    private void RegisterSkillBuild(DotaHeroContext Ctx, Spell CallSpell, Spell HungerSpell, Spell CullingSpell, Property CounterHelixTalent)
    {
      var Build = HeroSkillBuild.CreateStandard(
        ClassName: AxeClassName,
        HeroDisplayName: "Axe",
        HeroId: "axe",
        IconGlyph: MogulKhanGlyph,
        SlotQ: DotaSkillSlot.CreateActive(
          Id: SkillCallId,
          Hotkey: "Q",
          Title: "Berserker's Call",
          IconGlyph: BerserkersCallGlyph,
          Spell: CallSpell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("axe.skill.q.desc_0")
            : DotaLocalization.Format(
                "axe.skill.q.desc_lv",
                GetCallBaseDurationTurns(Lv),
                DotaMechanicsMapper.MapArmorToDefence(GetCallBaseArmor(Lv)),
                Lv >= 4 ? DotaLocalization.T("axe.skill.desc_max_suffix") : DotaLocalization.T("axe.skill.q.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("axe.skill.q.title")),
        SlotW: DotaSkillSlot.CreateActive(
          Id: SkillHungerId,
          Hotkey: "W",
          Title: "Battle Hunger",
          IconGlyph: BattleHungerGlyph,
          Spell: HungerSpell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("axe.skill.w.desc_0")
            : DotaLocalization.Format(
                "axe.skill.w.desc_lv",
                $"{GetHungerBaseDps(Lv):0}",
                $"{GetHungerSlowRatio(Lv) * 100.0f:0}",
                Lv >= 4 ? DotaLocalization.T("axe.skill.desc_max_suffix") : DotaLocalization.T("axe.skill.w.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("axe.skill.w.title")),
        SlotE: DotaSkillSlot.CreatePassive(
          Id: SkillHelixId,
          Hotkey: "E",
          Title: "Counter Helix",
          IconGlyph: CounterHelixGlyph,
          PassiveProperty: CounterHelixTalent,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("axe.skill.e.desc_0")
            : DotaLocalization.Format(
                "axe.skill.e.desc_lv",
                GetHelixThresholdByLevel(Lv),
                DotaMechanicsMapper.MapSkillDamageDice(GetHelixBaseDotaDamage(Lv), 6),
                Lv < 4
                  ? DotaLocalization.Format("axe.skill.e.desc_next_suffix", GetHelixThresholdByLevel(Lv + 1))
                  : DotaLocalization.T("axe.skill.desc_max_suffix")),
          TitleResolver: () => DotaLocalization.T("axe.skill.e.title")),
        SlotR: DotaSkillSlot.CreateActive(
          Id: SkillCullingId,
          Hotkey: "R",
          Title: "Culling Blade",
          IconGlyph: CullingBladeGlyph,
          Spell: CullingSpell,
          IsUltimate: true,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("axe.skill.r.desc_0")
            : DotaLocalization.Format(
                "axe.skill.r.desc_lv",
                DotaMechanicsMapper.MapSkillDamage(GetCullingBaseDotaThreshold(Lv)),
                $"{CullingEliteArmorTenths[Math.Clamp(Lv - 1, 0, 2)] / 10.0f:0.#}",
                Lv >= 3 ? DotaLocalization.T("axe.skill.desc_max_suffix") : DotaLocalization.T("axe.skill.r.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("axe.skill.r.title")),
        HeroDisplayNameResolver: () => DotaLocalization.T("axe.hero_display"),
        PrimaryAttribute: DotaPrimaryAttribute.Strength);

      HeroSkillRegistry.Register(Ctx, Build);
    }

    private void RegisterTalentTree(DotaHeroContext Ctx)
    {
      var Tree = HeroTalentTree.CreateStandard(
        ClassName: AxeClassName,
        HeroDisplayNameResolver: () => DotaLocalization.T("axe.hero_display"),
        IconGlyph: MogulKhanGlyph,
        T1Left: new TalentNode(
          Id: TalentT1HungerMoveSpeed,
          TitleResolver: () => DotaLocalization.T("axe.talent.t1l.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t1l.desc")),
        T1Right: new TalentNode(
          Id: TalentT1CullingBuffDuration,
          TitleResolver: () => DotaLocalization.T("axe.talent.t1r.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t1r.desc")),
        T2Left: new TalentNode(
          Id: TalentT2CallArmor,
          TitleResolver: () => DotaLocalization.T("axe.talent.t2l.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t2l.desc")),
        T2Right: new TalentNode(
          Id: TalentT2HungerDps,
          TitleResolver: () => DotaLocalization.T("axe.talent.t2r.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t2r.desc")),
        T3Left: new TalentNode(
          Id: TalentT3HelixDamage,
          TitleResolver: () => DotaLocalization.T("axe.talent.t3l.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t3l.desc")),
        T3Right: new TalentNode(
          Id: TalentT3Strength,
          TitleResolver: () => DotaLocalization.T("axe.talent.t3r.title"),
          DescriptionResolver: () => DotaLocalization.Format("axe.talent.t3r.desc", TalentStrengthBonus, TalentStrengthBonus * HpPerStrengthPoint),
          OnLearn: (Engine, Ch) =>
          {
            if (Ch == null)
              return;

            int StrLimit = PathosEngineBridge.GetLimitAbilityScore(Ch, "strength");
            int ConLimit = PathosEngineBridge.GetLimitAbilityScore(Ch, "constitution");
            int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength");
            int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Ch, "constitution");
            PathosEngineBridge.SetPermanentAbilityScore(Ch, "strength", Math.Min(StrLimit, CurStr + TalentStrengthBonus));
            PathosEngineBridge.SetPermanentAbilityScore(Ch, "constitution", Math.Min(ConLimit, CurCon + TalentStrengthBonus));

            if (Ch.Life != null)
            {
              int HpGain = TalentStrengthBonus * HpPerStrengthPoint;
              PathosEngineBridge.SetMeter(Ch.Life, Ch.Life.Points + HpGain, Ch.Life.Total + HpGain);
            }
          }),
        T4Left: new TalentNode(
          Id: TalentT4CullingDamage,
          TitleResolver: () => DotaLocalization.T("axe.talent.t4l.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t4l.desc")),
        T4Right: new TalentNode(
          Id: TalentT4CallAoe,
          TitleResolver: () => DotaLocalization.T("axe.talent.t4r.title"),
          DescriptionResolver: () => DotaLocalization.T("axe.talent.t4r.desc")));

      TalentRegistry.Register(Ctx, Tree);
    }

    private static PathosStatLimits GetTalentStatLimitBonus(Character Character)
    {
      int StrConBonus = TalentService.HasTalent(Character, TalentT3Strength) ? TalentStrengthBonus : 0;
      return new PathosStatLimits(Str: StrConBonus, Dex: 0, Con: StrConBonus, Int: 0, Wis: 0, Cha: 0);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定斧王固定初始六维、30 级职业属性上限与 T3 右力量上限加成
      DotaMechanicsMapper.RegisterFixedStartingStats(AxeClassName, StartingStats);
      DotaMechanicsMapper.RegisterHeroStatLimits(AxeClassName, StatLimits);
      DotaMechanicsMapper.RegisterHeroStatLimitBonus(AxeClassName, GetTalentStatLimitBonus);

      // 开局立刻初始化先天「一人之军」数值，确保第 0 回合即可在状态栏看到加成
      DotaHookBus.RegisterAfterCreateActor("axe.init_one_man_army", Args =>
      {
        if (Args?.Character != null && IsAxe(Args.Character))
          RefreshOneManArmy(Args.Engine, Args.Character);
      });

      // 0.1 全射程选格（W 战斗饥渴 4~7 格选敌；R 淘汰之刃 2 格选敌）
      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: BattleHungerSpellName,
        DisplayTitle: "Battle Hunger",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: Caster => GetBattleHungerCastRange(Ctx, Caster),
        AllowSelf: false,
        ResolveDisplayTitle: () => DotaLocalization.T("axe.skill.w.title")));

      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: CullingBladeSpellName,
        DisplayTitle: "Culling Blade",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: _ => 2,
        AllowSelf: false,
        ResolveDisplayTitle: () => DotaLocalization.T("axe.skill.r.title")));

      // 0.2 左上角状态栏徽章（0 Codex.Properties 占用）
      RegisterStatusBadges();

      // 0.3 技能帮助面板实时数值与 5 阶熟练度详情
      RegisterSpellHelpSpecs(Ctx);

      // 1. 施法后置钩子：Q 狂战士之吼 / W 战斗饥渴 / R 淘汰之刃
      DotaHookBus.RegisterAfterCastSpell("axe.spell_casts", Args =>
      {
        var SpellName = Args.Spell?.Name;
        if (string.IsNullOrEmpty(SpellName))
          return;

        if (string.Equals(SpellName, BerserkersCallSpellName, StringComparison.OrdinalIgnoreCase))
        {
          HandleBerserkersCallCast(Ctx, Args);
        }
        else if (string.Equals(SpellName, BattleHungerSpellName, StringComparison.OrdinalIgnoreCase))
        {
          HandleBattleHungerCast(Ctx, Args);
        }
        else if (string.Equals(SpellName, CullingBladeSpellName, StringComparison.OrdinalIgnoreCase))
        {
          HandleCullingBladeCast(Ctx, Args);
        }
      });

      // 2. 嘲讽拦截：被狂战士之吼嘲讽的敌人禁止施法
      DotaHookBus.RegisterBeforeCastSpell("axe.taunt_silence", Args =>
      {
        if (Args.Character == null || !IsTauntedByLivingAxe(Args.Character, out _))
          return;

        Args.Cancel = true;
      });

      // 3. 嘲讽重定向：被嘲讽的敌人行动时强制靠近斧王或近战普攻斧王
      DotaHookBus.RegisterBeforeExecuteCommand("axe.taunt_redirect", Args =>
      {
        if (IsInsideTauntRedirect || Args.Character == null || Args.Character.Deceased)
          return;

        HandleTauntedUnitCommand(Args);
      });

      // 4. 每回合推进：孤军奋战、狂战士之吼计时、淘汰战吼计时、战斗饥渴每回合纯伤
      DotaHookBus.RegisterAfterExecuteCommand("axe.turn_ticks", Args =>
      {
        var Actor = Args.Character;
        if (Actor == null || Actor.Deceased)
          return;

        if (IsAxe(Actor))
          TickAxeTurn(Args.Engine, Actor);

        if (CullingBuffs.TryGetValue(Actor, out var Buff) && Buff.RemainingTurns > 0)
        {
          Buff.RemainingTurns--;
          if (Buff.RemainingTurns <= 0)
            CullingBuffs.Remove(Actor);
        }

        if (HungerAfflicted.TryGetValue(Actor, out var Hunger) && Hunger.RemainingTurns > 0)
          TickBattleHungerOnVictim(Args.Engine, Actor, Hunger);
      });

      // 5. E 反击螺旋（近战受击计次：7.42 包含高防/闪避 Miss 的攻击）
      DotaHookBus.RegisterAfterAttack("axe.counter_helix.melee", Args =>
      {
        var Attacker = Args.Character;
        if (Attacker == null || Attacker.Deceased || Attacker.Square == null || Args.Direction == Direction.Self)
          return;

        var TargetSq = Attacker.Square.Adjacent(Args.Direction, 1);
        var Target = TargetSq?.Character;
        if (Target == null && TargetSq != null && !TargetSq.IsBlocked(Vision: false))
          Target = Attacker.Square.Adjacent(Args.Direction, 2)?.Character;

        if (Target != null && IsAxe(Target) && DotaCombatHelper.IsHostile(Target, Attacker))
          AdvanceCounterHelix(Args.Engine, Target);
      });

      // 6. E 反击螺旋（远程/非近战物理受击计次）
      DotaHookBus.RegisterAfterDamage("axe.counter_helix.ranged", Args =>
      {
        if (IsInsideHelixDamage || Args.Defender == null || !IsAxe(Args.Defender))
          return;

        // 近战攻击已在 AfterAttack 中计数（含 Miss），此处仅补计远程物理/投掷攻击
        if (DotaHookBus.ActiveAttack != null || DotaHookBus.ActiveSpellCast != null)
          return;

        if (Args.Attacker != null && DotaCombatHelper.IsHostile(Args.Defender, Args.Attacker))
          AdvanceCounterHelix(Args.Engine, Args.Defender);
      });

      // 7. 先天「孤军奋战」近战增伤
      DotaHookBus.RegisterBeforeDamage("axe.one_man_army_damage", Args =>
      {
        if (Args.Attacker == null || !IsAxe(Args.Attacker))
          return;

        if (DotaHookBus.ActiveAttack == null || DotaHookBus.ActiveSpellCast != null)
          return;

        var State = GetAxeState(Args.Attacker);
        if (!State.OneManArmyActive || State.CachedOneManArmyBonusStr <= 0)
          return;

        int BonusDmg = Math.Max(1, State.CachedOneManArmyBonusStr / 2);
        Args.DamagePoints += BonusDmg;
      });

      // 8. 护甲（Defence）统一查询：狂战士之吼 + 淘汰之刃战吼 + 淘汰之刃永久护甲
      DotaHookBus.RegisterDefenceQuery("axe.defence_query", Args =>
      {
        if (IsInsideDefenceQuery || Args.Character == null || Args.Character.Deceased)
          return;

        int BonusDef = 0;

        if (IsAxe(Args.Character))
        {
          var State = GetAxeState(Args.Character);
          if (State.CallRemainingTurns > 0)
            BonusDef += State.CallBonusDefence;

          BonusDef += GetCullingPermanentDefenceBonus(Args.Character);
        }

        if (CullingBuffs.TryGetValue(Args.Character, out var Buff) && Buff.RemainingTurns > 0)
          BonusDef += Buff.BonusDefence;

        if (BonusDef != 0)
          Args.Defence += BonusDef;
      });

      // 9. 移动速度查询：战斗饥渴减速、T1 左饥渴移速加成、淘汰之刃 +25% 移速战吼
      DotaHookBus.RegisterSpeedQuery("axe.speed_query", Args =>
      {
        var Ch = Args.Character;
        if (Ch == null || Ch.Deceased || Args.Attacking)
          return;

        float Mult = 1.0f;

        if (HungerAfflicted.TryGetValue(Ch, out var Hunger) && Hunger.RemainingTurns > 0)
          Mult *= Math.Max(0.35f, 1.0f - Hunger.SlowRatio);

        if (CullingBuffs.TryGetValue(Ch, out var Buff) && Buff.RemainingTurns > 0)
          Mult *= (1.0f + CullingBuffMoveSpeedRatio);

        if (IsAxe(Ch) && TalentService.HasTalent(Ch, TalentT1HungerMoveSpeed))
        {
          int ActiveCount = CountActiveHungerTargets(Ch);
          if (ActiveCount > 0)
            Mult *= (1.0f + Math.Min(0.64f, ActiveCount * HungerTalentMsPerTarget));
        }

        if (MathF.Abs(Mult - 1.0f) > 0.001f)
          Args.Speed = ScaleSpeed(Args.Speed, Mult);
      });

      // 10. 战斗饥渴击杀驱散：中饥渴单位击杀任意目标时立即解除饥渴
      DotaHookBus.RegisterAfterKill("axe.battle_hunger_dispel", Args =>
      {
        var Killer = Args.Attacker;
        if (Killer == null)
          return;

        if (HungerAfflicted.TryGetValue(Killer, out var Hunger))
        {
          HungerAfflicted.Remove(Killer);
          if (Hunger.Caster != null && Hunger.Caster.TryGetTarget(out var Caster))
          {
            PathosEngineBridge.WriteInternalLog(
              Args.Engine,
              Caster,
              BattleHungerGlyph,
              DotaLocalization.Format("axe.log.hunger_dispelled", GetCharacterDisplayName(Killer)));
          }
        }
      });
    }

    private void RegisterStatusBadges()
    {
      // 1. 先天：一人之军
      DotaStatusBarBridge.RegisterProvider("axe.one_man_army", Ch =>
      {
        if (!IsAxe(Ch) || Ch.Deceased)
          return null;

        var State = GetAxeState(Ch);
        if (!State.OneManArmyActive || State.CachedOneManArmyBonusStr <= 0)
          return null;

        return new DotaStatusBadge
        {
          Key = "axe.one_man_army",
          Glyph = OneManArmyGlyph,
          Text = (OneManArmyTalentProperty != null
            ? PathosEngineBridge.GetLocalizedName(OneManArmyTalentProperty)
            : DotaLocalization.T("axe.status.one_man_army")).ToUpper(),
          Count = $"+{State.CachedOneManArmyBonusStr}",
          Colour = State.OneManArmyLingerTurns > 0 ? Colour.DarkOrange : Colour.DarkRed,
          HelpProperty = OneManArmyTalentProperty
        };
      });

      // 2. Q 狂战士之吼高甲状态
      DotaStatusBarBridge.RegisterProvider("axe.berserkers_call", Ch =>
      {
        if (!IsAxe(Ch) || Ch.Deceased)
          return null;

        var State = GetAxeState(Ch);
        if (State.CallRemainingTurns <= 0)
          return null;

        return new DotaStatusBadge
        {
          Key = "axe.berserkers_call",
          Glyph = BerserkersCallGlyph,
          Text = DotaLocalization.T("axe.status.call").ToUpper(),
          Count = $"{State.CallRemainingTurns}T",
          Colour = Colour.DarkRed,
          HelpSpell = BerserkersCallSpellRef
        };
      });

      // 3. E 反击螺旋受击计数
      DotaStatusBarBridge.RegisterProvider("axe.counter_helix", Ch =>
      {
        if (!IsAxe(Ch) || Ch.Deceased)
          return null;

        int HelixLv = HeroSkillService.GetSkillLevel(Ch, SkillHelixId);
        if (HelixLv <= 0)
          return null;

        var State = GetAxeState(Ch);
        if (State.HelixHitCounter <= 0)
          return null;

        int Need = GetHelixThresholdByLevel(HelixLv);
        return new DotaStatusBadge
        {
          Key = "axe.counter_helix",
          Glyph = CounterHelixGlyph,
          Text = (CounterHelixTalentProperty != null
            ? PathosEngineBridge.GetLocalizedName(CounterHelixTalentProperty)
            : DotaLocalization.T("axe.status.helix")).ToUpper(),
          Count = $"{State.HelixHitCounter}/{Need}",
          Colour = State.HelixHitCounter >= Need - 1 ? Colour.DarkOrange : Colour.Black,
          HelpProperty = CounterHelixTalentProperty
        };
      });

      // 4. W 战斗饥渴活跃目标数
      DotaStatusBarBridge.RegisterProvider("axe.battle_hunger", Ch =>
      {
        if (!IsAxe(Ch) || Ch.Deceased)
          return null;

        int Active = CountActiveHungerTargets(Ch);
        if (Active <= 0)
          return null;

        return new DotaStatusBadge
        {
          Key = "axe.battle_hunger",
          Glyph = BattleHungerGlyph,
          Text = DotaLocalization.T("axe.status.hunger").ToUpper(),
          Count = $"×{Active}",
          Colour = Colour.DarkRed,
          HelpSpell = BattleHungerSpellRef
        };
      });

      // 5. R 淘汰之刃永久护甲 / 战吼爆发
      DotaStatusBarBridge.RegisterProvider("axe.culling_blade", Ch =>
      {
        if (Ch == null || Ch.Deceased)
          return null;

        bool HasBuff = CullingBuffs.TryGetValue(Ch, out var Buff) && Buff.RemainingTurns > 0;
        int PermDef = IsAxe(Ch) ? GetCullingPermanentDefenceBonus(Ch) : 0;
        if (!HasBuff && PermDef <= 0)
          return null;

        string CountText = HasBuff
          ? (PermDef > 0 ? $"+{PermDef}/{Buff.RemainingTurns}T" : $"{Buff.RemainingTurns}T")
          : $"+{PermDef}";

        return new DotaStatusBadge
        {
          Key = "axe.culling_blade",
          Glyph = CullingBladeGlyph,
          Text = DotaLocalization.T("axe.status.culling").ToUpper(),
          Count = CountText,
          Colour = HasBuff ? Colour.Crimson : Colour.Black,
          HelpSpell = CullingBladeSpellRef
        };
      });
    }

    private void HandleBerserkersCallCast(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster == null || Caster.Deceased || Caster.Square == null)
        return;

      int SkillLv = Math.Clamp(HeroSkillService.GetSkillLevel(Caster, SkillCallId), 1, 4);
      int Tier = DotaCombatHelper.GetSkillTier(Ctx, Caster, Ctx.Skills.abjuration);
      int ExtraTurns = Tier >= 5 ? 2 : (Tier >= 3 ? 1 : 0);
      int DurationTurns = GetCallBaseDurationTurns(SkillLv) + ExtraTurns;

      bool HasArmorTalent = TalentService.HasTalent(Caster, TalentT2CallArmor);
      float DotaArmor = GetCallBaseArmor(SkillLv) + (HasArmorTalent ? CallTalentBonusArmor : 0.0f);
      int BonusDef = DotaMechanicsMapper.MapArmorToDefence(DotaArmor, MaxPositiveCap: HasArmorTalent ? 10 : 8);

      int Radius = TalentService.HasTalent(Caster, TalentT4CallAoe) ? 3 : 2;

      var State = GetAxeState(Caster);
      State.CallRemainingTurns = DurationTurns;
      State.CallBonusDefence = BonusDef;

      // 播放 3×3 猩红怒吼冲击波特效
      PathosEngineBridge.StoryExploding(Args.Engine, Delay.HalfSecond, Caster.Square, BerserkersCallExplosion);

      var Hostiles = DotaCombatHelper.FindHostilesInRadius(Caster, Caster.Square, Radius);
      foreach (var Enemy in Hostiles)
      {
        // 唤醒沉睡并移除恐惧，确保被嘲讽目标立刻转向斧王
        if (PathosEngineBridge.HasTransient(Enemy, Ctx.Properties.sleeping))
          PathosEngineBridge.RemoveTransient(Args.Engine, Caster, Enemy, Ctx.Properties.sleeping);
        if (PathosEngineBridge.HasTransient(Enemy, Ctx.Properties.fear))
          PathosEngineBridge.RemoveTransient(Args.Engine, Caster, Enemy, Ctx.Properties.fear);

        var TState = TauntedUnits.GetOrCreateValue(Enemy);
        TState.Taunter = new WeakReference<Character>(Caster);
        TState.RemainingTurns = DurationTurns;

        if (Enemy.Square != null)
          PathosEngineBridge.StoryCaption(Args.Engine, Enemy.Square, Colour.Crimson, DotaLocalization.T("axe.caption.taunted"), Delayed: false);
      }

      RefreshOneManArmy(Args.Engine, Caster);

      PathosEngineBridge.WriteInternalLog(
        Args.Engine,
        Caster,
        BerserkersCallGlyph,
        DotaLocalization.Format("axe.log.call_cast", Hostiles.Count, BonusDef, DurationTurns));
    }

    private void HandleBattleHungerCast(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster == null || Caster.Deceased || Caster.Square == null)
        return;

      int CastRange = GetBattleHungerCastRange(Ctx, Caster);
      var Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      var Target = DotaCombatHelper.ResolveHostileTarget(Caster, Args.Direction, CastRange, Pending, AllowDirectionFallback: true);

      if (Target == null)
      {
        DotaCombatHelper.RefundMana(Caster, BattleHungerManaCost);
        PathosEngineBridge.WriteInternalLog(Args.Engine, Caster, BattleHungerGlyph, DotaLocalization.T("axe.log.hunger_no_target"));
        return;
      }

      int SkillLv = Math.Clamp(HeroSkillService.GetSkillLevel(Caster, SkillHungerId), 1, 4);
      int Tier = DotaCombatHelper.GetSkillTier(Ctx, Caster, Ctx.Skills.enchantment);
      float BaseDps = GetHungerBaseDps(SkillLv) + (TalentService.HasTalent(Caster, TalentT2HungerDps) ? HungerTalentBonusDps : 0.0f);
      float StarAndTierMult = (1.0f + (SkillLv - 1) * 0.15f) * (1.0f + (Tier - 1) * 0.08f);
      float EffectiveDps = BaseDps * StarAndTierMult;
      float Slow = GetHungerSlowRatio(SkillLv);

      var HState = HungerAfflicted.GetOrCreateValue(Target);
      HState.Caster = new WeakReference<Character>(Caster);
      HState.RemainingTurns = BattleHungerDurationTurns;
      HState.DotaDps = EffectiveDps;
      HState.SlowRatio = Slow;
      HState.DamageAccumulator = 0.0f;

      TrackHungerTarget(Caster, Target);

      if (Target.Square != null)
        PathosEngineBridge.StoryStriking(Args.Engine, Delay.HalfSecond, Target.Square, BattleHungerStrike);

      // 施法当回合立即结算 1 次保底纯伤，确保即时反馈
      int FirstDmg = Math.Max(1, (int)MathF.Round((EffectiveDps / DotaMechanicsMapper.TurnsPerSecond) / DotaMechanicsMapper.DamageScale));
      PathosEngineBridge.StrikeCharacter(Args.Engine, Caster, Target, Args.Direction, FirstDmg, Delay.Zero, "battle hunger");

      PathosEngineBridge.WriteInternalLog(
        Args.Engine,
        Caster,
        BattleHungerGlyph,
        DotaLocalization.Format("axe.log.hunger_cast", GetCharacterDisplayName(Target), BattleHungerDurationTurns, $"{Slow * 100.0f:0}"));
    }

    private void HandleCullingBladeCast(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster == null || Caster.Deceased || Caster.Square == null)
        return;

      var Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      var Target = DotaCombatHelper.ResolveHostileTarget(Caster, Args.Direction, 2, Pending, AllowDirectionFallback: true);

      if (Target == null)
      {
        DotaCombatHelper.RefundMana(Caster, CullingBladeManaCost);
        PathosEngineBridge.WriteInternalLog(Args.Engine, Caster, CullingBladeGlyph, DotaLocalization.T("axe.log.culling_no_target"));
        return;
      }

      int SkillLv = Math.Clamp(HeroSkillService.GetSkillLevel(Caster, SkillCullingId), 1, 3);
      int Tier = DotaCombatHelper.GetSkillTier(Ctx, Caster, Ctx.Skills.evocation);
      int Threshold = GetCullingEffectiveThreshold(Caster, SkillLv, Tier);

      var TargetSq = Target.Square;
      string TargetName = GetCharacterDisplayName(Target);
      if (TargetSq != null)
        PathosEngineBridge.StoryStriking(Args.Engine, Delay.HalfSecond, TargetSq, CullingBladeStrike);

      int CurrentHp = Target.Life?.Points ?? 0;
      if (CurrentHp <= Threshold)
      {
        // 低于等于斩杀线：直接无视减伤处决！
        bool Killed = PathosEngineBridge.KillCharacter(Args.Engine, Caster, Target, "culling blade");
        if (!Killed || !Target.Deceased)
        {
          // 兜底：若目标仍未倒下，施加巨额纯粹斩击
          PathosEngineBridge.StrikeCharacter(Args.Engine, Caster, Target, Args.Direction, Math.Max(999, CurrentHp + 50), Delay.Zero, "culling blade");
        }

        // 100% 返还法力（对应 DOTA2 斩杀刷新冷却，支持连斩！）
        DotaCombatHelper.RefundMana(Caster, CullingBladeManaCost);

        // 给予斧王及周围 4 格友军战吼增益（+25% 移速 + 护甲）
        int BuffTurns = CullingBuffBaseTurns + (TalentService.HasTalent(Caster, TalentT1CullingBuffDuration) ? CullingBuffTalentExtraTurns : 0);
        int BuffDef = DotaMechanicsMapper.MapArmorToDefence(CullingBuffDotaArmor[SkillLv - 1]);

        ApplyCullingBuff(Caster, BuffTurns, BuffDef);
        foreach (var Sq in Caster.Square.GetAroundSquares(4))
        {
          var Ally = Sq?.Character;
          if (Ally != null && Ally != Caster && DotaCombatHelper.IsFriendly(Caster, Ally))
            ApplyCullingBuff(Ally, BuffTurns, BuffDef);
        }

        // 叠加永久护甲（精英/Boss/唯一怪 +1.0/1.5/2.0 甲，普通小怪 +0.2/0.3/0.4 甲，存档持久化）
        bool IsElite = Target.Entity != null && (Target.Entity.Level >= 5 || Target.Entity.IsUnique);
        int DeltaTenths = IsElite ? CullingEliteArmorTenths[SkillLv - 1] : CullingCreepArmorTenths[SkillLv - 1];
        int NewTotalTenths = DotaCharacterStorage.AddCustomInt(Caster, StorageCullingArmorTenthsKey, DeltaTenths);
        int PermDef = DotaMechanicsMapper.MapArmorToDefence(NewTotalTenths / 10.0f);

        if (TargetSq != null)
          PathosEngineBridge.StoryCaption(Args.Engine, TargetSq, Colour.Crimson, DotaLocalization.T("axe.caption.culled"), Delayed: false);

        RefreshOneManArmy(Args.Engine, Caster);

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Caster,
          CullingBladeGlyph,
          DotaLocalization.Format("axe.log.culling_execute", TargetName, $"{DeltaTenths / 10.0f:0.#}", PermDef, BuffTurns));
      }
      else
      {
        // 高于斩杀线：造成常规纯粹伤害（不返蓝、不触发战吼）
        float DotaDmg = GetCullingBaseDotaThreshold(SkillLv)
          + (Tier - 1) * 30.0f
          + (TalentService.HasTalent(Caster, TalentT4CullingDamage) ? CullingTalentBonusDotaDamage : 0.0f);
        Dice DmgDice = DotaMechanicsMapper.MapSkillDamageDice(DotaDmg, DieFaces: 6);
        int Rolled = DotaCombatHelper.RollDice(DmgDice);
        PathosEngineBridge.StrikeCharacter(Args.Engine, Caster, Target, Args.Direction, Rolled, Delay.Zero, "culling blade");

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Caster,
          CullingBladeGlyph,
          DotaLocalization.Format("axe.log.culling_damage", TargetName, Rolled, Threshold));
      }
    }

    private void HandleTauntedUnitCommand(CommandEventArgs Args)
    {
      var Unit = Args.Character;
      if (!IsTauntedByLivingAxe(Unit, out var Axe))
        return;

      if (!TauntedUnits.TryGetValue(Unit, out var TState))
        return;

      TState.RemainingTurns--;
      if (TState.RemainingTurns <= 0)
        TauntedUnits.Remove(Unit);

      if (Unit.Square == null || Axe.Square == null || Unit.Square.Map != Axe.Square.Map)
        return;

      int Dist = Unit.Square.AsRange(Axe.Square);
      var DirToAxe = Unit.Square.AsDirection(Axe.Square);

      // 若该怪物的原始指令已经是朝斧王挥砍，直接放行
      if (Dist <= 1 && PathosEngineBridge.IsAttackCommand(Args.Command))
      {
        var CmdDir = PathosEngineBridge.GetAttackCommandDirection(Args.Command);
        if (CmdDir == DirToAxe)
          return;
      }

      // 否则取消原指令（逃跑/施法/打友军），强制转向斧王！
      Args.Cancel = true;

      IsInsideTauntRedirect = true;
      try
      {
        if (Dist <= 1)
        {
          if (!PathosEngineBridge.ForceDirectorAttack(Args.Director, Unit, DirToAxe))
          {
            var AtkDelay = PathosEngineBridge.GetAttackDelay(Args.Engine, Unit);
            PathosEngineBridge.SetBusy(Args.Engine, Unit, AtkDelay);
            PathosEngineBridge.StoryAttacking(Args.Engine, Delay.FromTurns(10), Unit, DirToAxe);
            AdvanceCounterHelix(Args.Engine, Axe);
          }
        }
        else
        {
          var NextSq = FindStepToward(Unit.Square, Axe.Square);
          if (NextSq != null)
            PathosEngineBridge.DashCharacter(Args.Engine, Unit, NextSq);

          var StepDelay = PathosEngineBridge.GetAttackDelay(Args.Engine, Unit);
          PathosEngineBridge.SetBusy(Args.Engine, Unit, StepDelay);
        }
      }
      finally
      {
        IsInsideTauntRedirect = false;
      }
    }

    private void TickAxeTurn(object Engine, Character Axe)
    {
      var State = GetAxeState(Axe);

      if (State.CallRemainingTurns > 0)
      {
        State.CallRemainingTurns--;
        if (State.CallRemainingTurns <= 0)
          State.CallBonusDefence = 0;
      }

      RefreshOneManArmy(Engine, Axe);
    }

    private void RefreshOneManArmy(object Engine, Character Axe)
    {
      if (Axe == null || Axe.Deceased)
        return;

      var State = GetAxeState(Axe);
      bool HasNearbyAlly = false;

      if (Axe.Square != null)
      {
        foreach (var Sq in Axe.Square.GetAroundSquares(OneManArmyRadius))
        {
          var Cand = Sq?.Character;
          if (Cand != null && Cand != Axe && DotaCombatHelper.IsFriendly(Axe, Cand))
          {
            HasNearbyAlly = true;
            break;
          }
        }
      }

      if (!HasNearbyAlly)
      {
        State.OneManArmyActive = true;
        State.OneManArmyLingerTurns = 0;
      }
      else if (State.OneManArmyActive)
      {
        if (State.OneManArmyLingerTurns <= 0)
        {
          State.OneManArmyLingerTurns = OneManArmyLingerTurns;
        }
        else
        {
          State.OneManArmyLingerTurns--;
          if (State.OneManArmyLingerTurns <= 0)
            State.OneManArmyActive = false;
        }
      }

      if (State.OneManArmyActive && Engine != null)
      {
        int TotalDef = QueryDefenceSafe(Engine, Axe);
        int ExcessDef = Math.Max(0, TotalDef - 10);
        State.CachedOneManArmyBonusStr = ExcessDef > 0 ? Math.Max(1, (int)MathF.Floor(ExcessDef * 0.5f)) : 0;
      }
      else if (!State.OneManArmyActive)
      {
        State.CachedOneManArmyBonusStr = 0;
      }
    }

    private void TickBattleHungerOnVictim(object Engine, Character Victim, BattleHungerState Hunger)
    {
      Hunger.RemainingTurns--;
      if (Hunger.RemainingTurns <= 0)
        HungerAfflicted.Remove(Victim);

      Character Caster = null;
      Hunger.Caster?.TryGetTarget(out Caster);

      // 按 K_dmg = 15 与每回合 0.5 秒精确累加纯粹持续伤害
      float PerTurnFloat = (Hunger.DotaDps / DotaMechanicsMapper.TurnsPerSecond) / DotaMechanicsMapper.DamageScale;
      Hunger.DamageAccumulator += PerTurnFloat;

      int DmgToDeal = (int)MathF.Floor(Hunger.DamageAccumulator);
      if (DmgToDeal > 0)
      {
        Hunger.DamageAccumulator -= DmgToDeal;
        PathosEngineBridge.StrikeCharacter(Engine, Caster, Victim, Direction.Self, DmgToDeal, Delay.Zero, "battle hunger");
      }
    }

    private void AdvanceCounterHelix(object Engine, Character Axe)
    {
      if (Axe == null || Axe.Deceased || Axe.Square == null)
        return;

      int HelixLv = HeroSkillService.GetSkillLevel(Axe, SkillHelixId);
      if (HelixLv <= 0)
        return;

      var State = GetAxeState(Axe);
      State.HelixHitCounter++;

      int Threshold = GetHelixThresholdByLevel(HelixLv);
      if (State.HelixHitCounter < Threshold)
        return;

      State.HelixHitCounter = 0;

      float DotaDmg = GetHelixBaseDotaDamage(HelixLv)
        + (TalentService.HasTalent(Axe, TalentT3HelixDamage) ? HelixTalentBonusDotaDamage : 0.0f);
      Dice DmgDice = DotaMechanicsMapper.MapSkillDamageDice(DotaDmg, DieFaces: 6);

      PathosEngineBridge.StoryExploding(Engine, Delay.FromTurns(18), Axe.Square, CounterHelixExplosion);

      var Hostiles = DotaCombatHelper.FindHostilesInRadius(Axe, Axe.Square, 1);
      if (Hostiles.Count == 0)
        return;

      int TotalHits = 0;
      int LastRolled = 0;

      IsInsideHelixDamage = true;
      try
      {
        foreach (var Enemy in Hostiles)
        {
          if (Enemy == null || Enemy.Deceased)
            continue;

          int Rolled = DotaCombatHelper.RollDice(DmgDice);
          LastRolled = Rolled;
          TotalHits++;
          var Dir = Axe.Square.AsDirection(Enemy.Square);
          PathosEngineBridge.StrikeCharacter(Engine, Axe, Enemy, Dir, Rolled, Delay.Zero, "counter helix");
        }
      }
      finally
      {
        IsInsideHelixDamage = false;
      }

      if (TotalHits > 0)
      {
        PathosEngineBridge.WriteInternalLog(
          Engine,
          Axe,
          CounterHelixGlyph,
          DotaLocalization.Format("axe.log.helix_proc", TotalHits, LastRolled));
      }
    }

    private void RegisterSpellHelpSpecs(DotaHeroContext Ctx)
    {
      // Q：狂战士之吼帮助面板
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = BerserkersCallSpellName,
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.abjuration),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int SkillLv = Math.Clamp(HeroSkillService.GetSkillLevel(Ch, SkillCallId), 1, 4);
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.abjuration);
          int ExtraTurns = Tier >= 5 ? 2 : (Tier >= 3 ? 1 : 0);
          int Duration = GetCallBaseDurationTurns(SkillLv) + ExtraTurns;
          bool HasArmorTalent = TalentService.HasTalent(Ch, TalentT2CallArmor);
          float DotaArmor = GetCallBaseArmor(SkillLv) + (HasArmorTalent ? CallTalentBonusArmor : 0.0f);
          int BonusDef = DotaMechanicsMapper.MapArmorToDefence(DotaArmor, MaxPositiveCap: HasArmorTalent ? 10 : 8);
          int Radius = TalentService.HasTalent(Ch, TalentT4CallAoe) ? 3 : 2;

          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("axe.help.q.key_radius"), DotaHelpColor.Control(DotaLocalization.Format("axe.help.tiles_radius", Radius))),
            (DotaLocalization.T("axe.help.q.key_duration"), DotaHelpColor.Control(DotaLocalization.Format("axe.help.turns_val", Duration))),
            (DotaLocalization.T("axe.help.q.key_armor"), DotaHelpColor.Skill(DotaLocalization.Format("axe.help.q.val_armor", BonusDef, $"{DotaArmor:0}")))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("axe.help.q.mech_1"),
          DotaLocalization.T("axe.help.q.mech_2")
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int Extra = Tier >= 5 ? 2 : (Tier >= 3 ? 1 : 0);
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.Format("axe.help.q.tier_suffix", Extra),
              Bullets = new[] { DotaLocalization.Format("axe.help.q.tier_bullet", Extra) }
            });
          }
          return List;
        }
      });

      // W：战斗饥渴帮助面板
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = BattleHungerSpellName,
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.enchantment),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int SkillLv = Math.Clamp(HeroSkillService.GetSkillLevel(Ch, SkillHungerId), 1, 4);
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.enchantment);
          int Range = GetBattleHungerCastRange(Ctx, Ch);
          float BaseDps = GetHungerBaseDps(SkillLv) + (TalentService.HasTalent(Ch, TalentT2HungerDps) ? HungerTalentBonusDps : 0.0f);
          float Mult = (1.0f + (SkillLv - 1) * 0.15f) * (1.0f + (Tier - 1) * 0.08f);
          float EffDps = BaseDps * Mult;
          int EstTotal = Math.Max(1, (int)MathF.Round((EffDps * 12.0f) / DotaMechanicsMapper.DamageScale));
          float Slow = GetHungerSlowRatio(SkillLv) * 100.0f;

          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("axe.help.w.key_range"), DotaHelpColor.Control(DotaLocalization.Format("axe.help.tiles_val", Range))),
            (DotaLocalization.T("axe.help.w.key_dot"), DotaHelpColor.Physical(DotaLocalization.Format("axe.help.w.val_dot", $"{EffDps:0.#}", EstTotal, BattleHungerDurationTurns))),
            (DotaLocalization.T("axe.help.w.key_slow"), DotaHelpColor.Control($"-{Slow:0}%"))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("axe.help.w.mech_1"),
          DotaLocalization.T("axe.help.w.mech_2")
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int Range = GetBattleHungerRangeByTier(Tier);
            int BonusPct = (Tier - 1) * 8;
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.Format("axe.help.cast_range_suffix", Range),
              Bullets = new[] { DotaLocalization.Format("axe.help.w.tier_bullet", BonusPct) }
            });
          }
          return List;
        }
      });

      // R：淘汰之刃帮助面板
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = CullingBladeSpellName,
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.evocation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int SkillLv = Math.Clamp(HeroSkillService.GetSkillLevel(Ch, SkillCullingId), 1, 3);
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.evocation);
          int Threshold = GetCullingEffectiveThreshold(Ch, SkillLv, Tier);
          int BuffTurns = CullingBuffBaseTurns + (TalentService.HasTalent(Ch, TalentT1CullingBuffDuration) ? CullingBuffTalentExtraTurns : 0);
          int BuffDef = DotaMechanicsMapper.MapArmorToDefence(CullingBuffDotaArmor[SkillLv - 1]);
          float PermArmor = DotaCharacterStorage.GetCustomInt(Ch, StorageCullingArmorTenthsKey) / 10.0f;
          int PermDef = DotaMechanicsMapper.MapArmorToDefence(PermArmor);

          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("axe.help.r.key_threshold"), DotaHelpColor.Physical(DotaLocalization.Format("axe.help.r.val_threshold", Threshold))),
            (DotaLocalization.T("axe.help.r.key_buff"), DotaHelpColor.Skill(DotaLocalization.Format("axe.help.r.val_buff", BuffTurns, BuffDef))),
            (DotaLocalization.T("axe.help.r.key_perm"), DotaHelpColor.Passive(DotaLocalization.Format("axe.help.r.val_perm", $"{PermArmor:0.#}", PermDef)))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("axe.help.r.mech_1"),
          DotaLocalization.T("axe.help.r.mech_2")
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int BonusHp = (Tier - 1) * 2;
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = DotaLocalization.Format("axe.help.cast_range_suffix", 2),
              Bullets = new[] { DotaLocalization.Format("axe.help.r.tier_bullet", BonusHp) }
            });
          }
          return List;
        }
      });
    }

    private static Spell RegisterBerserkersCallSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = BerserkersCallSpellName;
        S.Description = "Roar to taunt enemies within 2 tiles (3 with Lv 25 talent) for 4 to 6 turns, silencing their spells and forcing them to march and attack Axe while granting +6 to +8 Defence (+10 with Lv 15 talent).";
        S.Glyph = Icon;
        S.School = Ctx.Schools.abjuration;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = DotaMechanicsMapper.MapSpellLevel(BerserkersCallManaCost);
        S.Mana = BerserkersCallManaCost;

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

    private static Spell RegisterBattleHungerSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = BattleHungerSpellName;
        S.Description = "Afflict a targeted enemy within 4 to 7 tiles for 24 turns with Pure damage over time (12/16/20/24 DPS mapped via K_dmg=15) and 16%/19%/22%/25% movement slow; dispelled if the target kills any unit.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.enchantment;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.fire);
        S.Level = DotaMechanicsMapper.MapSpellLevel(BattleHungerManaCost);
        S.Mana = BattleHungerManaCost;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.Nothing();
        }

        Configure(Ctx.Qualifications.proficient, 4);
        Configure(Ctx.Qualifications.specialist, 5);
        Configure(Ctx.Qualifications.expert, 5);
        Configure(Ctx.Qualifications.master, 6);
        Configure(Ctx.Qualifications.champion, 7);
      });
    }

    private static Spell RegisterCullingBladeSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = CullingBladeSpellName;
        S.Description = "Strike a nearby foe (range 2) with Pure force: instantly executes targets at or below 18/25/32 HP (+mastery/talent), refunding 100% mana, granting nearby allies +25% move speed and +7/+8/+8 Defence for 12 turns, and stacking permanent armor; otherwise deals 5d6+1 to 9d6 Pure damage.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.fire);
        S.Level = DotaMechanicsMapper.MapSpellLevel(CullingBladeManaCost);
        S.Mana = CullingBladeManaCost;

        void Configure(Qualification Qual)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(2));
          Adept.Apply.Nothing();
        }

        Configure(Ctx.Qualifications.proficient);
        Configure(Ctx.Qualifications.specialist);
        Configure(Ctx.Qualifications.expert);
        Configure(Ctx.Qualifications.master);
        Configure(Ctx.Qualifications.champion);
      });
    }

    private static Class RegisterAxeClass(DotaHeroContext Ctx, Glyph AvatarGlyph, Property OneManArmyTalent)
    {
      return Ctx.Classes.Add(C =>
      {
        C.Name = AxeClassName;
        C.Description = "A relentless DOTA2 crimson berserker who thrives alone with One Man Army, taunts entire rooms with Berserker's Call, carves attackers with Pure Counter Helix spins, and executes the weak with Culling Blade.";
        C.Backpack = Ctx.Items.Backpack;
        C.LifeAdvancement.Set(16, 1.d12());
        int ClassManaBase = DotaMechanicsMapper.ComputeClassManaBase(DotaBaseInt, RaceManaBase: 1, StartingStats.Wis);
        C.ManaAdvancement.Set(ClassManaBase, 1.d2());

        C.SetDistribution(
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.dexterity,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.charisma);

        Ctx.AddClassAvatars(C, AvatarGlyph);

        C.AddFeat(1, OneManArmyTalent);
        C.AddFeat(4, Ctx.Properties.free_action);
        C.AddFeat(8, Ctx.Properties.clarity);
        C.AddFeat(12, Ctx.Properties.quickness);

        C.Startup.SetSkill(Ctx.Qualifications.proficient,
          Ctx.Skills.literacy,
          Ctx.Skills.axe,
          Ctx.Skills.heavy_blade,
          Ctx.Skills.medium_armour,
          Ctx.Skills.heavy_armour,
          Ctx.Skills.abjuration,
          Ctx.Skills.enchantment,
          Ctx.Skills.evocation);

        C.Startup.Loot.AddKit(Chance.Always, Modifier.Plus1, Ctx.Items.axe);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.chain_mail);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_healing);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.food_ration);
      });
    }

    private static AxeRuntimeState GetAxeState(Character Character)
      => AxeStates.GetValue(Character, _ => new AxeRuntimeState());

    private static bool IsAxe(Character Character)
      => DotaCombatHelper.IsClass(Character, AxeClassName);

    private static int GetCallBaseDurationTurns(int SkillLevel)
      => DotaMechanicsMapper.MapDurationTurns(CallDotaDurations[Math.Clamp(SkillLevel - 1, 0, 3)]);

    private static float GetCallBaseArmor(int SkillLevel)
      => CallDotaArmor[Math.Clamp(SkillLevel - 1, 0, 3)];

    private static float GetHungerBaseDps(int SkillLevel)
      => HungerDotaDps[Math.Clamp(SkillLevel - 1, 0, 3)];

    private static float GetHungerSlowRatio(int SkillLevel)
      => HungerSlowRatios[Math.Clamp(SkillLevel - 1, 0, 3)];

    private static int GetHelixThresholdByLevel(int SkillLevel)
      => HelixAttackThresholds[Math.Clamp(SkillLevel - 1, 0, 3)];

    private static float GetHelixBaseDotaDamage(int SkillLevel)
      => HelixDotaDamage[Math.Clamp(SkillLevel - 1, 0, 3)];

    private static float GetCullingBaseDotaThreshold(int SkillLevel)
      => CullingDotaThresholds[Math.Clamp(SkillLevel - 1, 0, 2)];

    private static int GetCullingEffectiveThreshold(Character Caster, int SkillLevel, int Tier)
    {
      int BaseHp = DotaMechanicsMapper.MapSkillDamage(GetCullingBaseDotaThreshold(SkillLevel));
      int MasteryBonus = Math.Max(0, Tier - 1) * 2;
      int TalentBonus = TalentService.HasTalent(Caster, TalentT4CullingDamage)
        ? DotaMechanicsMapper.MapSkillDamage(CullingTalentBonusDotaDamage)
        : 0;
      return BaseHp + MasteryBonus + TalentBonus;
    }

    private static int GetBattleHungerRangeByTier(int Tier)
    {
      return Tier switch
      {
        <= 1 => 4,
        2 => 5,
        3 => 5,
        4 => 6,
        _ => 7
      };
    }

    private static int GetBattleHungerCastRange(DotaHeroContext Ctx, Character Caster)
    {
      int Tier = DotaCombatHelper.GetSkillTier(Ctx, Caster, Ctx.Skills.enchantment);
      return GetBattleHungerRangeByTier(Tier);
    }

    private static int GetCullingPermanentDefenceBonus(Character Axe)
    {
      int Tenths = DotaCharacterStorage.GetCustomInt(Axe, StorageCullingArmorTenthsKey);
      if (Tenths <= 0)
        return 0;

      return DotaMechanicsMapper.MapArmorToDefence(Tenths / 10.0f);
    }

    private static void ApplyCullingBuff(Character Target, int Turns, int BonusDef)
    {
      if (Target == null || Target.Deceased)
        return;

      var Buff = CullingBuffs.GetOrCreateValue(Target);
      Buff.RemainingTurns = Math.Max(Buff.RemainingTurns, Turns);
      Buff.BonusDefence = Math.Max(Buff.BonusDefence, BonusDef);
    }

    private static bool IsTauntedByLivingAxe(Character Unit, out Character Axe)
    {
      Axe = null;
      if (Unit == null || !TauntedUnits.TryGetValue(Unit, out var State) || State.RemainingTurns <= 0)
        return false;

      if (State.Taunter == null || !State.Taunter.TryGetTarget(out var T) || T == null || T.Deceased)
      {
        TauntedUnits.Remove(Unit);
        return false;
      }

      Axe = T;
      return true;
    }

    private static Square FindStepToward(Square From, Square To)
    {
      if (From == null || To == null || From.Map != To.Map)
        return null;

      Square Best = null;
      int BestDist = From.AsRange(To);
      foreach (var Adj in From.GetAroundSquares(1))
      {
        if (!DotaCombatHelper.IsOpenSquare(Adj))
          continue;

        int D = Adj.AsRange(To);
        if (D < BestDist)
        {
          BestDist = D;
          Best = Adj;
        }
      }
      return Best;
    }

    private static int QueryDefenceSafe(object Engine, Character Character)
    {
      if (IsInsideDefenceQuery)
        return 10;

      IsInsideDefenceQuery = true;
      try
      {
        return PathosEngineBridge.GetDefenceClass(Engine, Character);
      }
      finally
      {
        IsInsideDefenceQuery = false;
      }
    }

    private static void TrackHungerTarget(Character Caster, Character Victim)
    {
      if (Caster == null || Victim == null)
        return;

      var State = GetAxeState(Caster);
      State.ActiveHungerTargets.RemoveWhere(Wr => !Wr.TryGetTarget(out var Existing) || Existing == null || Existing.Deceased || Existing == Victim);
      State.ActiveHungerTargets.Add(new WeakReference<Character>(Victim));
    }

    private static int CountActiveHungerTargets(Character Caster)
    {
      if (Caster == null)
        return 0;

      var State = GetAxeState(Caster);
      int Count = 0;
      State.ActiveHungerTargets.RemoveWhere(Wr =>
      {
        if (!Wr.TryGetTarget(out var Victim) || Victim == null || Victim.Deceased)
          return true;

        if (!HungerAfflicted.TryGetValue(Victim, out var Hunger) || Hunger.RemainingTurns <= 0)
          return true;

        if (Hunger.Caster == null || !Hunger.Caster.TryGetTarget(out var Owner) || Owner != Caster)
          return true;

        Count++;
        return false;
      });
      return Count;
    }

    private static string GetCharacterDisplayName(Character Character)
    {
      if (Character == null)
        return DotaLocalization.T("axe.target_fallback");

      if (!string.IsNullOrWhiteSpace(Character.Name))
        return Character.Name;

      if (Character.Entity != null)
        return PathosEngineBridge.GetLocalizedName(Character.Entity);

      return DotaLocalization.T("axe.target_fallback");
    }

    private static Speed ScaleSpeed(Speed BaseSpeed, float Multiplier)
    {
      return Speed.FromRate(BaseSpeed.GetRate() * Multiplier);
    }
  }
}
