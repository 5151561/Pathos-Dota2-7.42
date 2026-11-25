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
    // 一人之军动态同步到 Life.Total 的生命上限增量（存档持久化，防止读档重复叠血或切状态丢血）
    private const string StorageOneManArmyAppliedHpKey = "axe.oma_applied_hp";

    // DOTA2 7.42 斧王初始三维与成长：STR 25 (+2.7) / AGI 18 (+1.5, 7.42) / INT 18 (+1.6)
    private static readonly DotaHeroProfile Profile = new(
      AxeClassName,
      BaseStr: 25, StrGain: 2.7f,
      BaseAgi: 18, AgiGain: 1.5f,
      BaseInt: 18, IntGain: 1.6f);

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

    // T3 右天赋：7.42 +15 力量按 1:5 后期标尺 -> +3 STR/CON 与 +18 HP（HP 按 DotaMechanicsMapper.HpPerStrengthPoint 联动）
    private const int TalentStrengthBonus = 3;

    private sealed class AxeRuntimeState
    {
      // 先天：孤军奋战
      public bool OneManArmyActive = true;
      public int OneManArmyLingerTurns;
      public int CachedOneManArmyBonusStr;

      // E：反击螺旋受击计数
      public int HelixHitCounter;
    }

    private static readonly ConditionalWeakTable<Character, AxeRuntimeState> AxeStates = new();

    // 带回合数的增益/减益统一走 DotaModifierService（回合递减、护甲/移速汇总、持续伤害、驱散与状态栏徽章由框架负责）
    private DotaModifierSpec CallArmorModifier;     // Q：狂战士之吼自身高甲
    private DotaModifierSpec BattleHungerModifier;  // W：战斗饥渴（纯粹 DoT + 减速，可驱散减益）
    private DotaModifierSpec CullingBuffModifier;   // R：淘汰之刃战吼（+移速 +护甲，自身与友军）

    [ThreadStatic]
    private static bool IsInsideDefenceQuery;

    private Property OneManArmyTalentProperty;
    private Property CounterHelixTalentProperty;
    private DotaAbility CallAbility;
    private DotaAbility HungerAbility;
    private DotaAbility CullingAbility;

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
      RegisterAbilities(Ctx);

      // 3.5 声明技能附带的增益/减益 Modifier
      RegisterModifiers();

      // 4. 注册专属职业：斧王（axe）
      var AxeClass = RegisterAxeClass(Ctx, MogulKhanGlyph, OneManArmyTalentProperty);

      // 5. 注册预设英雄：Axe（官方兽人种族 + 斧王职业）
      Ctx.RegisterPresetHero("Axe", Ctx.Races.orc, AxeClass, Ctx.Genders.male, MogulKhanGlyph);

      // 6. 注册技能加点表（Q/W/E/R + 7 级全属性黄点，力量主属性；E 反击螺旋 0→1 授予原生被动专长）
      RegisterSkillBuild(Ctx, CallAbility.Spell, HungerAbility.Spell, CullingAbility.Spell, CounterHelixTalentProperty);

      // 7. 注册 DOTA2 7.42 四层天赋树（Lv 10/15/20/25 + Lv 27~30 双修）
      RegisterTalentTree(Ctx);
    }

    private void RegisterModifiers()
    {
      CallArmorModifier = DotaModifierService.Define(new DotaModifierSpec
      {
        Key = "axe.berserkers_call",
        IsDebuff = false,
        IsDispellable = false,
        Stacking = DotaModifierStacking.Replace,
        BadgeGlyph = BerserkersCallGlyph,
        BadgeTitle = () => DotaLocalization.T("axe.status.call"),
        BadgeColour = Colour.DarkRed,
        HelpSpell = CallAbility.Spell
      });

      BattleHungerModifier = DotaModifierService.Define(new DotaModifierSpec
      {
        Key = "axe.battle_hunger",
        IsDebuff = true,
        IsDispellable = true,
        Stacking = DotaModifierStacking.Replace,
        DotElement = null, // 纯粹伤害
        DotCause = BattleHungerSpellName
      });

      // 战吼徽章与永久护甲合并显示，由 RegisterStatusBadges 中的组合徽章负责
      CullingBuffModifier = DotaModifierService.Define(new DotaModifierSpec
      {
        Key = "axe.culling_buff",
        IsDebuff = false,
        IsDispellable = true,
        Stacking = DotaModifierStacking.KeepStrongest
      });
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
          TitleResolver: () => DotaLocalization.T("axe.skill.q.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.duration", Lv => Lv > 0 ? DotaHelpFormat.Turns(GetCallBaseDurationTurns(Lv)) : null),
            DotaSkillStat.Of("ui.stat.defence", Lv => Lv > 0 ? $"+{DotaMechanicsMapper.MapArmorToDefence(GetCallBaseArmor(Lv))}" : null)
          }),
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
          TitleResolver: () => DotaLocalization.T("axe.skill.w.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.pure_dps", Lv => Lv > 0 ? $"{GetHungerBaseDps(Lv):0}" : null),
            DotaSkillStat.Of("ui.stat.slow", Lv => Lv > 0 ? $"{GetHungerSlowRatio(Lv) * 100.0f:0}%" : null)
          }),
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
          TitleResolver: () => DotaLocalization.T("axe.skill.e.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.trigger_hits", Lv => Lv > 0 ? $"{GetHelixThresholdByLevel(Lv)}" : null),
            DotaSkillStat.Of("ui.stat.pure_damage", Lv => Lv > 0 ? $"{DotaMechanicsMapper.MapSkillDamageDice(GetHelixBaseDotaDamage(Lv), 6)}" : null)
          }),
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
          TitleResolver: () => DotaLocalization.T("axe.skill.r.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.kill_threshold", Lv => Lv > 0 ? $"{DotaMechanicsMapper.MapSkillDamage(GetCullingBaseDotaThreshold(Lv))} HP" : null),
            DotaSkillStat.Of("ui.stat.elite_armor", Lv => Lv > 0 ? $"+{CullingEliteArmorTenths[Math.Clamp(Lv - 1, 0, 2)] / 10.0f:0.#}" : null)
          }),
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
          DescriptionResolver: () => DotaLocalization.Format("axe.talent.t3r.desc", TalentStrengthBonus, TalentStrengthBonus * DotaMechanicsMapper.HpPerStrengthPoint))
        {
          // 上限拓宽 / 永久提升 / 生命联动由天赋框架统一结算
          StatBonus = PathosStatLimits.Bonus(Str: TalentStrengthBonus, Con: TalentStrengthBonus)
        },
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

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定斧王固定初始六维与 30 级职业属性上限（T3 右力量上限加成由 TalentNode.StatBonus 自动叠加）
      Profile.RegisterStats();

      // 开局立刻初始化先天「一人之军」数值，确保第 0 回合即可在状态栏看到加成
      DotaHookBus.RegisterAfterCreateActor("axe.init_one_man_army", Args =>
      {
        if (Args?.Character != null && IsAxe(Args.Character))
        {
          if (Args.IsReroll)
            DotaCharacterStorage.SetCustomInt(Args.Character, StorageOneManArmyAppliedHpKey, 0);

          RefreshOneManArmy(Args.Engine, Args.Character);
        }
      });

      // 0.1 全射程选格与施法分发由 DotaAbility 自动注册（W 战斗饥渴 4~7 格选敌；R 淘汰之刃 2 格选敌）

      // 0.2 左上角状态栏徽章（0 Codex.Properties 占用）
      RegisterStatusBadges();

      // 0.3 技能帮助面板实时数值与 5 阶熟练度详情
      RegisterSpellHelpSpecs(Ctx);
      RegisterPassiveHelpSpecs();

      // 2. 初始化通用嘲讽基建（禁魔拦截 + 强制转向/攻击 + 回合递减）
      DotaTauntService.EnsureInitialized();

      // 4. 每回合推进：孤军奋战（狂战士之吼 / 淘汰战吼 / 战斗饥渴的计时与持续伤害由 DotaModifierService 统一推进）
      DotaHookBus.RegisterAfterExecuteCommand("axe.turn_ticks", Args =>
      {
        var Actor = Args.Character;
        if (Actor == null || Actor.Deceased)
          return;

        if (IsAxe(Actor))
          RefreshOneManArmy(Args.Engine, Actor);
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

      // 6. E 反击螺旋（非近战普攻的受击计次）：近战普攻已在 AfterAttack 中计数（含 Miss），
      //    此处补计远程投射物等无法归类的原生攻击，以及带 Attack 标记的脚本攻击（如敌方无敌斩斩击）；
      //    技能/持续伤害（魔法、纯粹 DoT，含斧王自身反击螺旋）均带标注，不再被误计为「受到攻击」
      DotaHookBus.RegisterAfterDamage("axe.counter_helix.ranged", Args =>
      {
        if (Args.Defender == null || Args.Info == null || !IsAxe(Args.Defender))
          return;

        bool CountsAsAttack =
          Args.Info.Origin == DotaDamageOrigin.Unclassified ||
          (Args.Info.Origin == DotaDamageOrigin.Scripted && Args.Info.Has(DotaDamageFlags.Attack));
        if (!CountsAsAttack)
          return;

        if (Args.Attacker != null && DotaCombatHelper.IsHostile(Args.Defender, Args.Attacker))
          AdvanceCounterHelix(Args.Engine, Args.Defender);
      });

      // 7. 先天「孤军奋战」真实力量加成（DOTA2 力量 = Pathos 力量 STR + 体质 CON，直接同步至面板、攻击命中、近战伤害与负重）
      DotaHookBus.RegisterActiveFormQuery("axe.one_man_army_str", Args =>
      {
        if (Args.Character == null || !IsAxe(Args.Character))
          return;

        var State = GetAxeState(Args.Character);
        if (!State.OneManArmyActive || State.CachedOneManArmyBonusStr <= 0)
          return;

        Args.BonusStr += State.CachedOneManArmyBonusStr;
        Args.BonusCon += State.CachedOneManArmyBonusStr;
      });

      // 8. 护甲（Defence）：淘汰之刃永久护甲（狂战士之吼 / 淘汰战吼的临时护甲由 Modifier 自动汇总）
      DotaHookBus.RegisterDefenceQuery("axe.defence_query", Args =>
      {
        if (Args.Character == null || Args.Character.Deceased || !IsAxe(Args.Character))
          return;

        int PermDef = GetCullingPermanentDefenceBonus(Args.Character);
        if (PermDef != 0)
          Args.Defence += PermDef;
      });

      // 9. 移动速度：T1 左天赋按活跃饥渴目标数加速（饥渴减速与战吼 +25% 移速由 Modifier 自动汇总）
      DotaHookBus.RegisterSpeedQuery("axe.speed_query", Args =>
      {
        var Ch = Args.Character;
        if (Ch == null || Ch.Deceased || Args.Attacking || !IsAxe(Ch))
          return;

        if (!TalentService.HasTalent(Ch, TalentT1HungerMoveSpeed))
          return;

        int ActiveCount = DotaModifierService.CountAppliedBy(Ch, BattleHungerModifier);
        if (ActiveCount > 0)
          Args.Speed = ScaleSpeed(Args.Speed, 1.0f + Math.Min(0.64f, ActiveCount * HungerTalentMsPerTarget));
      });

      // 10. 战斗饥渴击杀驱散：中饥渴单位击杀任意目标时立即解除饥渴
      DotaHookBus.RegisterAfterKill("axe.battle_hunger_dispel", Args =>
      {
        var Killer = Args.Attacker;
        var Hunger = DotaModifierService.Get(Killer, BattleHungerModifier);
        if (Hunger == null)
          return;

        var Caster = Hunger.Source;
        DotaModifierService.Remove(Args.Engine, Killer, BattleHungerModifier);
        if (Caster != null)
        {
          PathosEngineBridge.WriteInternalLog(
            Args.Engine,
            Caster,
            BattleHungerGlyph,
            DotaLocalization.Format("axe.log.hunger_dispelled", DotaCombatHelper.GetDisplayName(Killer)));
        }
      });
    }

    private void RegisterStatusBadges()
    {
      // 1. 先天：一人之军
      DotaStatusBarBridge.RegisterProvider("axe.one_man_army", AxeClassName, Ch =>
      {
        var State = GetAxeState(Ch);
        if (!State.OneManArmyActive || State.CachedOneManArmyBonusStr <= 0)
          return null;

        return DotaStatusBadge.ForPassive(
          "axe.one_man_army",
          OneManArmyGlyph,
          OneManArmyTalentProperty,
          "axe.status.one_man_army",
          Count: $"+{State.CachedOneManArmyBonusStr}",
          Colour: State.OneManArmyLingerTurns > 0 ? Colour.DarkOrange : Colour.DarkRed);
      });

      // 2. Q 狂战士之吼高甲状态：由 CallArmorModifier 自动生成徽章（剩余回合）

      // 3. E 反击螺旋受击计数
      DotaStatusBarBridge.RegisterProvider("axe.counter_helix", AxeClassName, Ch =>
      {
        int HelixLv = HeroSkillService.GetSkillLevel(Ch, SkillHelixId);
        if (HelixLv <= 0)
          return null;

        var State = GetAxeState(Ch);
        if (State.HelixHitCounter <= 0)
          return null;

        int Need = GetHelixThresholdByLevel(HelixLv);
        return DotaStatusBadge.ForPassive(
          "axe.counter_helix",
          CounterHelixGlyph,
          CounterHelixTalentProperty,
          "axe.status.helix",
          Count: $"{State.HelixHitCounter}/{Need}",
          Colour: State.HelixHitCounter >= Need - 1 ? Colour.DarkOrange : Colour.Black);
      });

      // 4. W 战斗饥渴活跃目标数
      DotaStatusBarBridge.RegisterProvider("axe.battle_hunger", AxeClassName, Ch =>
      {
        int Active = DotaModifierService.CountAppliedBy(Ch, BattleHungerModifier);
        if (Active <= 0)
          return null;

        return new DotaStatusBadge
        {
          Key = "axe.battle_hunger",
          Glyph = BattleHungerGlyph,
          Text = DotaStatusBadge.Title("axe.status.hunger"),
          Count = $"×{Active}",
          Colour = Colour.DarkRed,
          HelpSpell = HungerAbility.Spell
        };
      });

      // 5. R 淘汰之刃永久护甲 / 战吼爆发（战吼也会施加给友军，故不按斧王职业过滤）
      DotaStatusBarBridge.RegisterProvider("axe.culling_blade", Ch =>
      {
        if (Ch == null || Ch.Deceased)
          return null;

        var Buff = DotaModifierService.Get(Ch, CullingBuffModifier);
        bool HasBuff = Buff != null;
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
          Text = DotaStatusBadge.Title("axe.status.culling"),
          Count = CountText,
          Colour = HasBuff ? Colour.Crimson : Colour.Black,
          HelpSpell = CullingAbility.Spell
        };
      });
    }

    private void HandleBerserkersCallCast(DotaHeroContext Ctx, DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster == null || Caster.Deceased || Caster.Square == null)
        return;

      var Call = ComputeCall(Caster, Cast.Tier);
      int DurationTurns = Call.DurationTurns;
      int BonusDef = Call.BonusDefence;
      int Radius = Call.Radius;

      DotaModifierService.Apply(CallArmorModifier, Caster, Caster, DurationTurns, M => M.Defence = BonusDef);

      // 播放 3×3 猩红怒吼冲击波特效
      PathosEngineBridge.StoryExploding(Args.Engine, Delay.HalfSecond, Caster.Square, BerserkersCallExplosion);

      var Hostiles = DotaCombatHelper.FindHostilesInRadius(Caster, Caster.Square, Radius);
      foreach (var Enemy in Hostiles)
      {
        DotaTauntService.ApplyTaunt(
          Ctx,
          Args.Engine,
          Caster,
          Enemy,
          DurationTurns,
          SourceKey: "axe.berserkers_call",
          OnFallbackAttack: (Engine, Taunter, _) => AdvanceCounterHelix(Engine, Taunter));
      }

      RefreshOneManArmy(Args.Engine, Caster);

      PathosEngineBridge.WriteInternalLog(
        Args.Engine,
        Caster,
        BerserkersCallGlyph,
        DotaLocalization.Format("axe.log.call_cast", Hostiles.Count, BonusDef, DurationTurns));
    }

    private void HandleBattleHungerCast(DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster == null || Caster.Deceased || Caster.Square == null)
        return;

      if (!Cast.TryResolveHostileTarget(out var Target, "axe.log.hunger_no_target"))
        return;

      float EffectiveDps = ComputeHungerDps(Caster, Cast.Tier);
      float Slow = GetHungerSlowRatio(HungerAbility.Level(Caster, 4));

      if (Target.Square != null)
        PathosEngineBridge.StoryStriking(Args.Engine, Delay.HalfSecond, Target.Square, BattleHungerStrike);

      var Hunger = DotaModifierService.Apply(BattleHungerModifier, Caster, Target, BattleHungerDurationTurns, M =>
      {
        M.DotDotaDps = EffectiveDps;
        M.MoveSpeedPct = -Slow;
      });

      if (Hunger == null)
      {
        // 目标减益免疫 / 无敌：饥渴未生效
        PathosEngineBridge.WriteInternalLog(Args.Engine, Caster, BattleHungerGlyph, DotaLocalization.Format("axe.log.hunger_immune", DotaCombatHelper.GetDisplayName(Target)));
        return;
      }

      // 施法当回合立即结算 1 次保底纯伤，确保即时反馈
      int FirstDmg = Math.Max(1, (int)MathF.Round((EffectiveDps / DotaMechanicsMapper.TurnsPerSecond) / DotaMechanicsMapper.DamageScale));
      DotaDamage.Strike(Args.Engine, Caster, Target, Args.Direction, FirstDmg, DotaDamageInfo.Pure(BattleHungerSpellName, DotaDamageFlags.Spell | DotaDamageFlags.DoT));

      PathosEngineBridge.WriteInternalLog(
        Args.Engine,
        Caster,
        BattleHungerGlyph,
        DotaLocalization.Format("axe.log.hunger_cast", DotaCombatHelper.GetDisplayName(Target), BattleHungerDurationTurns, $"{Slow * 100.0f:0}"));
    }

    private void HandleCullingBladeCast(DotaAbilityCast Cast)
    {
      var Args = Cast.Args;
      var Caster = Cast.Caster;
      if (Caster == null || Caster.Deceased || Caster.Square == null)
        return;

      if (!Cast.TryResolveHostileTarget(out var Target, "axe.log.culling_no_target"))
        return;

      int SkillLv = CullingAbility.Level(Caster, 3);
      int Tier = Cast.Tier;
      int Threshold = GetCullingEffectiveThreshold(Caster, SkillLv, Tier);

      var TargetSq = Target.Square;
      string TargetName = DotaCombatHelper.GetDisplayName(Target);
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
          DotaDamage.Strike(Args.Engine, Caster, Target, Args.Direction, Math.Max(999, CurrentHp + 50), DotaDamageInfo.Pure(CullingBladeSpellName));
        }

        // 100% 返还法力（对应 DOTA2 斩杀刷新冷却，支持连斩！）
        DotaCombatHelper.RefundSpellCost(Args);

        // 给予斧王及周围 4 格友军战吼增益（+25% 移速 + 护甲）
        int BuffTurns = GetCullingBuffTurns(Caster);
        int BuffDef = GetCullingBuffDefence(SkillLv);

        void ApplyCullingBuff(Character Unit)
          => DotaModifierService.Apply(CullingBuffModifier, Caster, Unit, BuffTurns, M =>
          {
            M.Defence = BuffDef;
            M.MoveSpeedPct = CullingBuffMoveSpeedRatio;
          });

        ApplyCullingBuff(Caster);
        foreach (var Sq in Caster.Square.GetAroundSquares(4))
        {
          var Ally = Sq?.Character;
          if (Ally != null && Ally != Caster && DotaCombatHelper.IsFriendly(Caster, Ally))
            ApplyCullingBuff(Ally);
        }

        // 叠加永久护甲（精英/Boss/唯一怪 +1.0/1.5/2.0 甲，普通小怪 +0.2/0.3/0.4 甲，存档持久化，无上限多段递减）
        bool IsElite = Target.Entity != null && (Target.Entity.Level >= 5 || Target.Entity.IsUnique);
        int DeltaTenths = IsElite ? CullingEliteArmorTenths[SkillLv - 1] : CullingCreepArmorTenths[SkillLv - 1];
        int NewTotalTenths = DotaCharacterStorage.AddCustomInt(Caster, StorageCullingArmorTenthsKey, DeltaTenths);
        int PermDef = DotaMechanicsMapper.MapUncappedArmorToDefence(NewTotalTenths / 10.0f);

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
        DotaDamage.Strike(Args.Engine, Caster, Target, Args.Direction, Rolled, DotaDamageInfo.Pure(CullingBladeSpellName));

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Caster,
          CullingBladeGlyph,
          DotaLocalization.Format("axe.log.culling_damage", TargetName, Rolled, Threshold));
      }
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

      int TargetBonusHp = State.OneManArmyActive ? State.CachedOneManArmyBonusStr * DotaMechanicsMapper.HpPerStrengthPoint : 0;
      SyncOneManArmyBonusLife(Axe, TargetBonusHp);
    }

    private static void SyncOneManArmyBonusLife(Character Axe, int TargetBonusHp)
    {
      if (Axe?.Life == null)
        return;

      int AppliedHp = DotaCharacterStorage.GetCustomInt(Axe, StorageOneManArmyAppliedHpKey);
      int DeltaHp = TargetBonusHp - AppliedHp;
      if (DeltaHp == 0)
        return;

      DotaLife.AdjustMax(Axe, DeltaHp, DotaLifeAdjustMode.KeepRatio);
      DotaCharacterStorage.SetCustomInt(Axe, StorageOneManArmyAppliedHpKey, TargetBonusHp);
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

      Dice DmgDice = GetHelixDamageDice(Axe, HelixLv);

      PathosEngineBridge.StoryExploding(Engine, Delay.FromTurns(18), Axe.Square, CounterHelixExplosion);

      var Hostiles = DotaCombatHelper.FindHostilesInRadius(Axe, Axe.Square, 1);
      if (Hostiles.Count == 0)
        return;

      int TotalHits = 0;
      int LastRolled = 0;

      // 纯粹伤害、非攻击（不会触发暴击，也不会被敌方「受到攻击」类被动计数）
      var HelixInfo = DotaDamageInfo.Pure("counter helix", DotaDamageFlags.None);
      foreach (var Enemy in Hostiles)
      {
        if (Enemy == null || Enemy.Deceased)
          continue;

        int Rolled = DotaCombatHelper.RollDice(DmgDice);
        LastRolled = Rolled;
        TotalHits++;
        var Dir = Axe.Square.AsDirection(Enemy.Square);
        DotaDamage.Strike(Engine, Axe, Enemy, Dir, Rolled, HelixInfo);
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

    /// <summary>先天「一人之军」与被动「反击螺旋」的 Property 帮助面板（实时数值 + 按等级明细）。</summary>
    private static void RegisterPassiveHelpSpecs()
    {
      // E：反击螺旋（被动，技能等级 1~4）
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        PropertyName = "counter helix",
        ResolveCurrentLevel = Ch => IsAxe(Ch) ? HeroSkillService.GetSkillLevel(Ch, SkillHelixId) : 0,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (!IsAxe(Ch))
            return null;

          int Level = HeroSkillService.GetSkillLevel(Ch, SkillHelixId);
          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("ui.help.key_skill_level"), Level > 0
              ? DotaHelpColor.Skill(DotaLocalization.Format("ui.help.val_skill_level", Level, 4))
              : DotaLocalization.T("ui.help.not_learned"))
          };
          if (Level <= 0)
            return Rows;

          var State = GetAxeState(Ch);
          var DmgDice = GetHelixDamageDice(Ch, Level);
          bool HasDmgTalent = TalentService.HasTalent(Ch, TalentT3HelixDamage);
          Rows.Add((DotaLocalization.T("axe.help.e.key_counter"), DotaLocalization.Format(
            "axe.help.e.val_counter",
            DotaHelpColor.Skill($"{State.HelixHitCounter}"),
            DotaHelpColor.Skill($"{GetHelixThresholdByLevel(Level)}"))));
          Rows.Add((DotaLocalization.T("axe.help.e.key_dmg"), DotaLocalization.Format(
            "axe.help.e.val_dmg",
            DotaHelpColor.Physical(DotaSpellHelpBridge.FormatDiceWithRange(DmgDice)),
            DotaHelpColor.Physical($"{DmgDice.Average():0.#}"),
            HasDmgTalent ? DotaLocalization.Format("ui.common.paren_item", DotaHelpColor.Talent(DotaLocalization.T("axe.help.e.t3_tag"))) : string.Empty)));
          return Rows;
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("axe.help.e.mech_1"),
          DotaLocalization.T("axe.help.e.mech_2")
        },
        // 各等级伤害含当前天赋（百科无角色时为基础值）
        BuildLevels = HelpCtx => DotaSpellHelpBridge.Levels(
          4,
          Lv => DotaLocalization.Format("axe.help.e.level_suffix", GetHelixThresholdByLevel(Lv)),
          Lv => DotaLocalization.Format(
            "axe.help.e.level_bullet",
            DotaHelpColor.Physical(DotaSpellHelpBridge.FormatDiceWithRange(GetHelixDamageDice(HelpCtx.Character, Lv)))))
      });

      // 先天：一人之军（无等级）
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        PropertyName = "one man army",
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (!IsAxe(Ch))
            return null;

          var State = GetAxeState(Ch);
          int TotalDef = HelpCtx.Engine != null ? QueryDefenceSafe(HelpCtx.Engine, Ch) : 0;
          int Bonus = State.OneManArmyActive ? State.CachedOneManArmyBonusStr : 0;

          string StatusText = !State.OneManArmyActive
            ? DotaLocalization.T("axe.help.oma.status_off")
            : State.OneManArmyLingerTurns > 0
              ? DotaLocalization.Format("axe.help.oma.status_linger", DotaHelpColor.Control(DotaHelpFormat.Turns(State.OneManArmyLingerTurns)))
              : DotaLocalization.T("axe.help.oma.status_on");

          return new List<(string, string)>
          {
            (DotaLocalization.T("axe.help.oma.key_status"), StatusText),
            (DotaLocalization.T("axe.help.oma.key_def"), DotaLocalization.Format(
              "axe.help.oma.val_def",
              DotaHelpColor.Skill($"{TotalDef}"),
              DotaHelpColor.Skill($"{Math.Max(0, TotalDef - 10)}"))),
            (DotaLocalization.T("axe.help.oma.key_bonus"), Bonus > 0
              ? DotaLocalization.Format(
                  "axe.help.oma.val_bonus",
                  DotaHelpColor.Stat($"+{Bonus}"),
                  DotaHelpColor.Heal($"+{Bonus * DotaMechanicsMapper.HpPerStrengthPoint} HP"))
              : DotaLocalization.T("axe.help.oma.val_none"))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format(
            "axe.help.oma.mech_1",
            DotaHelpColor.Control(DotaHelpFormat.Tiles(OneManArmyRadius)),
            DotaHelpColor.Control(DotaHelpFormat.Turns(OneManArmyLingerTurns))),
          DotaLocalization.Format(
            "axe.help.oma.mech_2",
            DotaHelpColor.Stat("50%"),
            DotaHelpColor.Heal($"{DotaMechanicsMapper.HpPerStrengthPoint} HP"))
        }
      });
    }

    private void RegisterSpellHelpSpecs(DotaHeroContext Ctx)
    {
      // Q：狂战士之吼帮助面板
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = BerserkersCallSpellName,
        ResolveCurrentTier = CallAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Call = ComputeCall(HelpCtx.Character, CallAbility.Tier(HelpCtx.Character));

          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("axe.help.q.key_radius"), DotaHelpColor.Control(DotaLocalization.Format("axe.help.tiles_radius", Call.Radius))),
            (DotaLocalization.T("axe.help.q.key_duration"), DotaHelpColor.Control(DotaHelpFormat.Turns(Call.DurationTurns))),
            (DotaLocalization.T("axe.help.q.key_armor"), DotaHelpColor.Skill(DotaLocalization.Format("axe.help.q.val_armor", Call.BonusDefence, $"{Call.DotaArmor:0}")))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("axe.help.q.mech_1"),
          DotaLocalization.T("axe.help.q.mech_2")
        },
        // 各阶数值按当前技能等级（升星）与天赋实算（当前阶与实时面板一致；百科无角色时按 1 级）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          _ => DotaLocalization.T("ui.help.self_cast"),
          Tier =>
          {
            var Call = ComputeCall(HelpCtx.Character, Tier);
            return DotaLocalization.Format(
              "axe.help.q.tier_bullet_full",
              DotaHelpColor.Control(DotaHelpFormat.Turns(Call.DurationTurns)),
              DotaHelpColor.Skill($"+{Call.BonusDefence}"));
          })
      });

      // W：战斗饥渴帮助面板
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = BattleHungerSpellName,
        ResolveCurrentTier = HungerAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Range = HungerAbility.Range(Ch);
          float EffDps = ComputeHungerDps(Ch, HungerAbility.Tier(Ch));
          int EstTotal = EstimateHungerTotal(EffDps);
          float Slow = GetHungerSlowRatio(HungerAbility.Level(Ch, 4)) * 100.0f;

          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("axe.help.w.key_range"), DotaHelpColor.Control(DotaHelpFormat.Tiles(Range))),
            (DotaLocalization.T("axe.help.w.key_dot"), DotaHelpColor.Physical(DotaLocalization.Format("axe.help.w.val_dot", $"{EffDps:0.#}", EstTotal, BattleHungerDurationTurns))),
            (DotaLocalization.T("axe.help.w.key_slow"), DotaHelpColor.Control($"-{Slow:0}%"))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("axe.help.w.mech_1"),
          DotaLocalization.T("axe.help.w.mech_2")
        },
        // 各阶数值按当前技能等级（升星）与天赋实算（当前阶与实时面板一致；百科无角色时按 1 级）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(HungerAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier =>
          {
            float TierDps = ComputeHungerDps(HelpCtx.Character, Tier);
            return DotaHelpColor.Physical(DotaLocalization.Format(
              "axe.help.w.val_dot", $"{TierDps:0.#}", EstimateHungerTotal(TierDps), BattleHungerDurationTurns));
          })
      });

      // R：淘汰之刃帮助面板
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = CullingBladeSpellName,
        ResolveCurrentTier = CullingAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int SkillLv = CullingAbility.Level(Ch, 3);
          int Tier = CullingAbility.Tier(Ch);
          int Threshold = GetCullingEffectiveThreshold(Ch, SkillLv, Tier);
          int BuffTurns = GetCullingBuffTurns(Ch);
          int BuffDef = GetCullingBuffDefence(SkillLv);
          float PermArmor = DotaCharacterStorage.GetCustomInt(Ch, StorageCullingArmorTenthsKey) / 10.0f;
          int PermDef = DotaMechanicsMapper.MapUncappedArmorToDefence(PermArmor);

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
        // 各阶斩杀线按当前技能等级（升星）与天赋实算（当前阶与实时面板一致；百科无角色时按 1 级）
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(CullingAbility.RangeAt(HelpCtx.Character, Tier)),
          Tier => DotaHelpColor.Physical(DotaLocalization.Format(
            "axe.help.r.val_threshold",
            GetCullingEffectiveThreshold(HelpCtx.Character, CullingAbility.Level(HelpCtx.Character, 3), Tier))))
      });
    }

    private void RegisterAbilities(DotaHeroContext Ctx)
    {
      // Q：狂战士之吼（自身施放，嘲讽周围敌人并获得高甲）
      CallAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = BerserkersCallSpellName,
        Description = "Roar to taunt enemies within 2 tiles (3 with Lv 25 talent) for 4 to 6 turns, silencing their spells and forcing them to march and attack Axe while granting +6 to +8 Defence (+10 with Lv 15 talent).",
        Icon = BerserkersCallGlyph,
        School = Ctx.Schools.abjuration,
        Skill = Ctx.Skills.abjuration,
        Precept = new Precept(Purpose.Buff),
        ManaCost = BerserkersCallManaCost,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillCallId,
        OnCast = Cast => HandleBerserkersCallCast(Ctx, Cast)
      });

      // W：战斗饥渴（指向敌方，4~7 格随附魔系熟练度）
      HungerAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = BattleHungerSpellName,
        DisplayTitle = "Battle Hunger",
        TitleResolver = () => DotaLocalization.T("axe.skill.w.title"),
        Description = "Afflict a targeted enemy within 4 to 7 tiles for 24 turns with Pure damage over time (12/16/20/24 DPS mapped via K_dmg=15) and 16%/19%/22%/25% movement slow; dispelled if the target kills any unit.",
        Icon = BattleHungerGlyph,
        School = Ctx.Schools.enchantment,
        Skill = Ctx.Skills.enchantment,
        Precept = new Precept(Purpose.Blast, Ctx.Elements.fire),
        ManaCost = BattleHungerManaCost,
        Targeting = DotaAbilityTargeting.UnitHostile,
        Range = DotaTable.Of(4, 5, 5, 6, 7),
        SkillSlotId = SkillHungerId,
        OnCast = Cast => HandleBattleHungerCast(Cast)
      });

      // R：淘汰之刃（指向敌方，近战 2 格）
      CullingAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = CullingBladeSpellName,
        DisplayTitle = "Culling Blade",
        TitleResolver = () => DotaLocalization.T("axe.skill.r.title"),
        Description = "Strike a nearby foe (range 2) with Pure force: instantly executes targets at or below 18/25/32 HP (+mastery/talent), refunding 100% mana, granting nearby allies +25% move speed and +7/+8/+8 Defence for 12 turns, and stacking permanent armor; otherwise deals 5d6+1 to 9d6 Pure damage.",
        Icon = CullingBladeGlyph,
        School = Ctx.Schools.evocation,
        Skill = Ctx.Skills.evocation,
        Precept = new Precept(Purpose.Blast, Ctx.Elements.fire),
        ManaCost = CullingBladeManaCost,
        Targeting = DotaAbilityTargeting.UnitHostile,
        Range = DotaTable.Uniform(2),
        SkillSlotId = SkillCullingId,
        OnCast = Cast => HandleCullingBladeCast(Cast)
      });
    }

    private static Class RegisterAxeClass(DotaHeroContext Ctx, Glyph AvatarGlyph, Property OneManArmyTalent)
    {
      return Ctx.Classes.Add(C =>
      {
        Profile.ConfigureClass(
          Ctx,
          C,
          Description: "A relentless DOTA2 crimson berserker who thrives alone with One Man Army, taunts entire rooms with Berserker's Call, carves attackers with Pure Counter Helix spins, and executes the weak with Culling Blade.",
          AvatarGlyph: AvatarGlyph,
          LifeBase: 16,
          LifeDice: 1.d12(),
          RaceManaBase: 1,
          ManaDice: 1.d2());

        C.SetDistribution(
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.dexterity,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.charisma);

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

    /// <summary>反击螺旋纯伤骰（按技能等级 + T3 左天赋 +40），触发结算与帮助面板共用。</summary>
    private static Dice GetHelixDamageDice(Character Axe, int SkillLevel)
      => DotaMechanicsMapper.MapSkillDamageDice(
        GetHelixBaseDotaDamage(SkillLevel) + (TalentService.HasTalent(Axe, TalentT3HelixDamage) ? HelixTalentBonusDotaDamage : 0.0f),
        DieFaces: 6);

    private static float GetCullingBaseDotaThreshold(int SkillLevel)
      => CullingDotaThresholds[Math.Clamp(SkillLevel - 1, 0, 2)];

    // ---- 施法结算与帮助面板共用的数值计算（单一来源，防止两处公式漂移） ----

    private readonly record struct CallStats(int DurationTurns, float DotaArmor, int BonusDefence, int Radius);

    /// <summary>Q 熟练度额外持续回合（Expert +1 / Champion +2）。</summary>
    private static int GetCallTierExtraTurns(int Tier)
      => Tier >= 5 ? 2 : (Tier >= 3 ? 1 : 0);

    /// <summary>Q 狂战士之吼：持续回合、DOTA2 护甲、Pathos 防御加成与嘲讽半径。</summary>
    private CallStats ComputeCall(Character Caster, int Tier)
    {
      int SkillLv = CallAbility.Level(Caster, 4);
      bool HasArmorTalent = TalentService.HasTalent(Caster, TalentT2CallArmor);
      float DotaArmor = GetCallBaseArmor(SkillLv) + (HasArmorTalent ? CallTalentBonusArmor : 0.0f);
      return new CallStats(
        DurationTurns: GetCallBaseDurationTurns(SkillLv) + GetCallTierExtraTurns(Tier),
        DotaArmor: DotaArmor,
        BonusDefence: DotaMechanicsMapper.MapArmorToDefence(DotaArmor, MaxPositiveCap: HasArmorTalent ? 10 : 8),
        Radius: TalentService.HasTalent(Caster, TalentT4CallAoe) ? 3 : 2);
    }

    /// <summary>W 战斗饥渴：(基础 DPS + T2 右天赋) × 升星倍率 × 熟练度倍率（每阶 +8%）。</summary>
    private float ComputeHungerDps(Character Caster, int Tier)
    {
      int SkillLv = HungerAbility.Level(Caster, 4);
      float BaseDps = GetHungerBaseDps(SkillLv) + (TalentService.HasTalent(Caster, TalentT2HungerDps) ? HungerTalentBonusDps : 0.0f);
      float StarAndTierMult = HungerAbility.StarMultiplier(Caster) * (1.0f + (Tier - 1) * 0.08f);
      return BaseDps * StarAndTierMult;
    }

    /// <summary>W 战斗饥渴全程纯伤估算（12 秒 DPS 折算 Pathos 伤害）。</summary>
    private static int EstimateHungerTotal(float EffDps)
      => Math.Max(1, (int)MathF.Round((EffDps * 12.0f) / DotaMechanicsMapper.DamageScale));

    /// <summary>R 淘汰之刃战吼持续回合（T1 右天赋 +6 回合）。</summary>
    private static int GetCullingBuffTurns(Character Caster)
      => CullingBuffBaseTurns + (TalentService.HasTalent(Caster, TalentT1CullingBuffDuration) ? CullingBuffTalentExtraTurns : 0);

    /// <summary>R 淘汰之刃战吼防御加成（按技能等级 1~3）。</summary>
    private static int GetCullingBuffDefence(int SkillLevel)
      => DotaMechanicsMapper.MapArmorToDefence(CullingBuffDotaArmor[Math.Clamp(SkillLevel - 1, 0, 2)]);

    /// <summary>R 淘汰之刃熟练度额外斩杀线（每阶 +2 HP）。</summary>
    private static int GetCullingMasteryBonusHp(int Tier)
      => Math.Max(0, Tier - 1) * 2;

    private static int GetCullingEffectiveThreshold(Character Caster, int SkillLevel, int Tier)
    {
      int BaseHp = DotaMechanicsMapper.MapSkillDamage(GetCullingBaseDotaThreshold(SkillLevel));
      int MasteryBonus = GetCullingMasteryBonusHp(Tier);
      int TalentBonus = TalentService.HasTalent(Caster, TalentT4CullingDamage)
        ? DotaMechanicsMapper.MapSkillDamage(CullingTalentBonusDotaDamage)
        : 0;
      return BaseHp + MasteryBonus + TalentBonus;
    }


    private static int GetCullingPermanentDefenceBonus(Character Axe)
    {
      int Tenths = DotaCharacterStorage.GetCustomInt(Axe, StorageCullingArmorTenthsKey);
      if (Tenths <= 0)
        return 0;

      return DotaMechanicsMapper.MapUncappedArmorToDefence(Tenths / 10.0f);
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

    private static Speed ScaleSpeed(Speed BaseSpeed, float Multiplier)
    {
      return Speed.FromRate(BaseSpeed.GetRate() * Multiplier);
    }
  }
}
