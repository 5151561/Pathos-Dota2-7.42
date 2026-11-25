using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Inv;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Presentation;
using Pathos.DotaHeroes.Framework.Skills;
using Pathos.DotaHeroes.Framework.Talents;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Heroes
{
  /// <summary>
  /// DOTA2 7.42 英雄：帕吉（Pudge / 屠夫），0 Codex.Properties 占用。
  /// - 先天「腐肉堆积」：帕吉击杀或 3 格内死亡的英雄级单位（唯一 / 守卫 / 等级 ≥ 帕吉的怪物）永久叠层，每层 +2 DOTA 力量（按 1:5 后期标尺折算 STR/CON 与生命）
  /// - Q 肉钩：方向施法 7 格，勾住路径上第一个单位拖到身边；普通怪物直接秒杀，英雄级单位吃纯粹伤害，友军只拖不伤
  /// - W 腐烂：0 蓝开关；每回合对周围 3×3 敌人与自己造成魔法伤害并减速，自伤最多扣到 1 HP，肉盾按 DOTA2 逐跳格挡自伤
  /// - E 肉盾：自身增益，每次受到伤害格挡 DOTA2 8/14/20/26 ÷ 15（小数按概率进位）
  /// - R 肢解：相邻敌人持续引导 6 回合，目标眩晕，每回合魔法伤害（基础 + 力量倍数）并等量回血
  /// </summary>
  public sealed class PudgeHero : IDotaHeroDefinition
  {
    private const string PudgeClassName = "pudge";
    private const string MeatHookSpellName = "meat hook";
    private const string RotSpellName = "pudge rot"; // 官方已有 "rot" 条目，必须改名
    private const string MeatShieldSpellName = "meat shield";
    private const string DismemberSpellName = "dismember";

    // 技能加点槽位 ID（写入存档，禁止改名）
    private const string SkillHookId = "pudge.q.meat_hook";
    private const string SkillRotId = "pudge.w.rot";
    private const string SkillShieldId = "pudge.e.meat_shield";
    private const string SkillDismemberId = "pudge.r.dismember";

    // 天赋 ID（写入存档，禁止改名）
    private const string TalentT1RotSlow = "pudge.t1.rot_slow";
    private const string TalentT1Armor = "pudge.t1.armor";
    private const string TalentT2HookDamage = "pudge.t2.hook_dmg";
    private const string TalentT2SpellLifesteal = "pudge.t2.spell_lifesteal";
    private const string TalentT3HookCooldown = "pudge.t3.hook_cd";
    private const string TalentT3DismemberDuration = "pudge.t3.dismember_dur";
    private const string TalentT4FleshHeap = "pudge.t4.flesh_heap";
    private const string TalentT4DismemberDamage = "pudge.t4.dismember_dmg";

    // 存档持久化键
    private const string StorageFleshHeapStacksKey = "pudge.flesh_heap_stacks";
    private const string StorageFleshHeapAppliedHpKey = "pudge.flesh_heap_applied_hp";

    private const string HookSequenceKey = "pudge.meat_hook";
    private const string DismemberSequenceKey = "pudge.dismember";
    private const string RotLoopKey = "pudge.rot_cloud";

    // DOTA2 7.42：STR 25 (+3.0) / AGI 13 (+1.4) / INT 16 (+1.8)
    private static readonly DotaHeroProfile Profile = new(
      PudgeClassName,
      BaseStr: 25, StrGain: 3.0f,
      BaseAgi: 13, AgiGain: 1.4f,
      BaseInt: 16, IntGain: 1.8f);

    private const float DotaUnitsPerTile = 175.0f;

    // 蓝耗（K_mana = 20）
    private static readonly int MeatHookManaCost = DotaMechanicsMapper.MapSkillManaCost(120);   // 6
    private static readonly int MeatShieldManaCost = DotaMechanicsMapper.MapSkillManaCost(70);  // 65/70/75/80 中位 → 4
    private static readonly int DismemberManaCost = DotaMechanicsMapper.MapSkillManaCost(130);  // 100/130/170 中位 → 6

    // 先天：腐肉堆积（7.42：+2 力量/层，450 码，T4 左 ×1.5）
    private const int FleshHeapRadius = 3;                    // 450 ÷ 175 ≈ 2.6 → 3 格
    private const float FleshHeapDotaStrPerStack = 2.0f;
    private const float FleshHeapLateStatScale = 0.2f;        // 累积成长按 1:5 后期标尺（同斧王 +15 力量天赋 → +3）
    private const float FleshHeapTalentMultiplier = 1.5f;

    // Q：肉钩（7.42：150/220/290/360 纯伤，1300 射程，1600 钩速，前摇 0.3s，T2 左 +150，T3 左 -4s / 12s 满级冷却）
    private static readonly float[] HookDotaDamage = { 150.0f, 220.0f, 290.0f, 360.0f };
    private const float HookTalentBonusDotaDamage = 150.0f;
    private const int HookRangeTiles = 7;                     // 1300 ÷ 175 ≈ 7.4
    private const float HookCastPointSeconds = 0.3f;
    private const float HookSecondsPerTile = DotaUnitsPerTile / 1600.0f;
    private static readonly float HookTalentCdr = DotaMechanicsMapper.ComputeCooldownReductionRatio(4.0f, 12.0f); // 33%

    // W：腐烂（7.42：30/60/90/120 DPS，每 0.2s 一跳，半径 250，减速 14/20/26/32%，T1 左 +10%）
    private static readonly float[] RotDotaDps = { 30.0f, 60.0f, 90.0f, 120.0f };
    private static readonly float[] RotSlowRatios = { 0.14f, 0.20f, 0.26f, 0.32f };
    private const float RotTalentBonusSlow = 0.10f;
    private const float RotTickSeconds = 0.2f;
    private const int RotRadius = 1;
    private const int RotSlowTurns = 2;

    // E：肉盾（7.42：格挡 8/14/20/26，持续 4/5/6/7s，T4 左 ×1.5）
    private static readonly float[] ShieldDotaBlock = { 8.0f, 14.0f, 20.0f, 26.0f };
    private static readonly float[] ShieldDotaDurations = { 4.0f, 5.0f, 6.0f, 7.0f };

    // R：肢解（7.42：80/100/120 DPS + 力量 × 0.3/0.5/0.7，引导 2.75s，T3 右 +0.75s，T4 右 ×1.5）
    private static readonly float[] DismemberDotaDps = { 80.0f, 100.0f, 120.0f };
    private static readonly float[] DismemberStrMultipliers = { 0.3f, 0.5f, 0.7f };
    private const float DismemberDotaChannelSeconds = 2.75f;
    private const float DismemberTalentBonusSeconds = 0.75f;
    private const float DismemberTalentMultiplier = 1.5f;

    // 天赋：+5 护甲 / +8% 技能吸血
    private const float TalentDotaArmor = 5.0f;
    private const float TalentSpellLifesteal = 0.08f;

    private sealed class PudgeRuntimeState
    {
      public float LifestealAccumulator;
    }

    private static readonly ConditionalWeakTable<Character, PudgeRuntimeState> PudgeStates = new();
    private static readonly DotaCharacterFlagSet RotActive = new();
    private static readonly DotaCharacterFlagSet Dismembering = new();

    private DotaHeroContext Ctx;

    private DotaAbility HookAbility;
    private DotaAbility RotAbility;
    private DotaAbility ShieldAbility;
    private DotaAbility DismemberAbility;

    private DotaModifierSpec RotSlowModifier;
    private DotaModifierSpec MeatShieldModifier;

    private Glyph PudgeGlyph;
    private Glyph FleshHeapGlyph;
    private Glyph MeatHookGlyph;
    private Glyph RotGlyph;
    private Glyph MeatShieldGlyph;
    private Glyph DismemberGlyph;

    private Beam HookChainBeam;
    private Strike HookHeadStrike;
    private Strike HookBloodStrike;
    private Explosion RotCloudExplosion;
    private Strike MeatShieldStrike;
    private Strike DismemberStrike;

    public string Id => "pudge";

    public void Register(DotaHeroContext Ctx)
    {
      this.Ctx = Ctx;

      // 1. 贴图（"pudge rot" 避开官方 Glyph "rot"）
      PudgeGlyph = Ctx.RegisterGlyph("pudge");
      FleshHeapGlyph = Ctx.RegisterGlyph("flesh heap");
      MeatHookGlyph = Ctx.RegisterGlyph("meat hook");
      RotGlyph = Ctx.RegisterGlyph("pudge rot");
      MeatShieldGlyph = Ctx.RegisterGlyph("meat shield");
      DismemberGlyph = Ctx.RegisterGlyph("dismember");

      // 1.5 自定义像素 VFX（tools/VfxGen 生成，配色对照官方演示视频）
      HookChainBeam = Ctx.RegisterBeamVfx("meat hook chain", "meat hook fx beam h", "meat hook fx beam v", "meat hook fx beam f", "meat hook fx beam b");
      HookHeadStrike = Ctx.RegisterStrikeVfx("meat hook head", "meat hook fx hook");
      HookBloodStrike = Ctx.RegisterStrikeVfx("meat hook blood", "meat hook fx strike");
      RotCloudExplosion = Ctx.RegisterExplosion3x3Vfx("pudge rot cloud", "pudge rot fx");
      MeatShieldStrike = Ctx.RegisterStrikeVfx("meat shield wrap", "meat shield fx strike");
      DismemberStrike = Ctx.RegisterStrikeVfx("dismember bite", "dismember fx strike");

      DotaVfx.Style(HookChainBeam, new DotaVfxStyle { FadeIn = 0.05f, FadeOutStart = 0.85f });
      DotaVfx.Style(HookHeadStrike, new DotaVfxStyle { FadeIn = 0.0f, FadeOutStart = 0.9f });
      DotaVfx.Style(HookBloodStrike, new DotaVfxStyle { FadeIn = 0.04f, FadeOutStart = 0.45f, ScaleFrom = 0.7f, ScaleTo = 1.1f, RandomAngle = true });
      // 毒雾贴地：画在地板之上、物品与角色之下，不遮挡帕吉与敌人
      DotaVfx.Style(RotCloudExplosion, new DotaVfxStyle { Ground = true, FadeIn = 0.25f, FadeOutStart = 0.55f, PeakOpacity = 0.5f });
      DotaVfx.Style(MeatShieldStrike, new DotaVfxStyle { FadeIn = 0.1f, FadeOutStart = 0.55f, ScaleFrom = 1.3f, ScaleTo = 1.0f });
      DotaVfx.Style(DismemberStrike, new DotaVfxStyle { FadeIn = 0.03f, FadeOutStart = 0.4f, ScaleFrom = 0.75f, ScaleTo = 1.1f, RandomAngle = true });

      // 2. 主动技能与 Modifier
      RegisterAbilities(Ctx);
      RegisterModifiers();

      // 3. 职业与预设英雄（官方兽人：天生毒抗、吃尸体无同类惩罚）
      var PudgeClass = RegisterPudgeClass(Ctx, PudgeGlyph);
      Ctx.RegisterPresetHero("Pudge", Ctx.Races.orc, PudgeClass, Ctx.Genders.male, PudgeGlyph);

      // 4. 技能加点与天赋树
      RegisterSkillBuild(Ctx);
      RegisterTalentTree(Ctx);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      Profile.RegisterStats();

      DotaHookBus.RegisterAfterCreateActor("pudge.init_flesh_heap", Args =>
      {
        if (Args?.Character == null || !IsPudge(Args.Character))
          return;

        if (Args.IsReroll)
        {
          DotaCharacterStorage.SetCustomInt(Args.Character, StorageFleshHeapStacksKey, 0);
          DotaCharacterStorage.SetCustomInt(Args.Character, StorageFleshHeapAppliedHpKey, 0);
        }
        SyncFleshHeapLife(Args.Character);
      });

      RegisterStatusBadges();
      RegisterSpellHelpSpecs();
      RegisterPassiveHelpSpecs();

      // 每回合：腐烂结算（伤害、减速、自伤）+ 腐肉堆积生命同步
      DotaHookBus.RegisterAfterExecuteCommand("pudge.turn_ticks", Args =>
      {
        var Actor = Args.Character;
        if (Actor == null || Actor.Deceased || !IsPudge(Actor))
          return;

        if (RotActive.Contains(Actor))
          TickRot(Args.Engine, Actor);
      });

      // 先天：腐肉堆积力量加成（STR + CON 同步到面板、命中、伤害与负重）
      DotaHookBus.RegisterActiveFormQuery("pudge.flesh_heap_str", Args =>
      {
        if (Args.Character == null || !IsPudge(Args.Character))
          return;

        int Bonus = GetFleshHeapBonusStr(Args.Character);
        if (Bonus <= 0)
          return;

        Args.BonusStr += Bonus;
        Args.BonusCon += Bonus;
      });

      // 先天：腐肉堆积叠层（帕吉击杀，或 3 格内死亡的英雄级敌人）
      DotaHookBus.RegisterAfterKill("pudge.flesh_heap_kill", Args => HandleFleshHeapKill(Args));

      // T1 右：+5 护甲
      DotaHookBus.RegisterDefenceQuery("pudge.talent_armor", Args =>
      {
        if (Args.Character == null || Args.Character.Deceased || !IsPudge(Args.Character))
          return;
        if (TalentService.HasTalent(Args.Character, TalentT1Armor))
          Args.Defence += DotaMechanicsMapper.MapArmorToDefence(TalentDotaArmor);
      });

      // T2 右：+8% 技能吸血（帕吉技能造成的伤害，按小数累加回血）
      DotaHookBus.RegisterAfterDamage("pudge.spell_lifesteal", Args =>
      {
        var Pudge = Args.Attacker;
        if (Pudge == null || Pudge.Deceased || Args.Defender == null || Args.Defender == Pudge || Args.Info == null)
          return;
        if (Args.Info.Origin != DotaDamageOrigin.Scripted || !Args.Info.Has(DotaDamageFlags.Spell) || !IsPudge(Pudge))
          return;
        if (!TalentService.HasTalent(Pudge, TalentT2SpellLifesteal))
          return;

        var State = GetState(Pudge);
        State.LifestealAccumulator += Args.DamagePoints * TalentSpellLifesteal;
        int Heal = (int)MathF.Floor(State.LifestealAccumulator);
        if (Heal <= 0)
          return;

        State.LifestealAccumulator -= Heal;
        if (Pudge.Life != null && Pudge.Life.Points < Pudge.Life.Total)
          PathosEngineBridge.HealCharacter(Args.Engine, Pudge, Pudge, Direction.Self, Heal);
      });
    }

    // ---------------------------------------------------------------- 注册

    private void RegisterAbilities(DotaHeroContext Ctx)
    {
      HookAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = MeatHookSpellName,
        DisplayTitle = "Meat Hook",
        TitleResolver = () => DotaLocalization.T("pudge.skill.q.title"),
        Description = "Hurl a bloody hook up to 7 tiles in a direction. It latches onto the first unit in its path and drags it beside Pudge: ordinary monsters are slain outright, unique and guardian foes take 2d6+3 / 4d6+1 / 5d6+2 / 6d6+3 Pure damage (+10 with Lv 15 talent), and allies are pulled without harm.",
        Icon = MeatHookGlyph,
        School = Ctx.Schools.evocation,
        Skill = Ctx.Skills.evocation,
        Precept = new Precept(Purpose.Blast, Ctx.Elements.physical),
        ManaCost = MeatHookManaCost,
        Targeting = DotaAbilityTargeting.PointAny,
        Range = DotaTable.Uniform(HookRangeTiles),
        SkillSlotId = SkillHookId,
        OnCast = HandleMeatHookCast
      });

      RotAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = RotSpellName,
        Description = "Toggle a cloud of toxic gas around Pudge (free). Each turn it deals 1/2/3/4 magical damage to adjacent foes and to Pudge himself, and slows foes by 14%/20%/26%/32%. Rot can never reduce Pudge below 1 HP, and Meat Shield blocks its self-damage per DOTA2 tick.",
        Icon = RotGlyph,
        School = Ctx.Schools.necromancy,
        Skill = Ctx.Skills.necromancy,
        Precept = new Precept(Purpose.Buff),
        ManaCost = 0,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillRotId,
        OnCast = HandleRotCast
      });

      ShieldAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = MeatShieldSpellName,
        Description = "Wrap Pudge in a layer of flesh for 8/10/12/14 turns. Every instance of damage of any type is reduced by 0.53/0.93/1.33/1.73 on average (the fraction rounds up by chance; x1.5 with Lv 25 talent). Cannot be dispelled.",
        Icon = MeatShieldGlyph,
        School = Ctx.Schools.abjuration,
        Skill = Ctx.Skills.abjuration,
        Precept = new Precept(Purpose.Buff),
        ManaCost = MeatShieldManaCost,
        Targeting = DotaAbilityTargeting.Self,
        SkillSlotId = SkillShieldId,
        OnCast = HandleMeatShieldCast
      });

      DismemberAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
      {
        SpellName = DismemberSpellName,
        DisplayTitle = "Dismember",
        TitleResolver = () => DotaLocalization.T("pudge.skill.r.title"),
        Description = "Channel on an adjacent foe for 6 turns (7 with Lv 20 talent), stunning it. Each turn deals magical damage of (80/100/120 + 0.3/0.5/0.7 x Pudge's DOTA2-equivalent Strength) x 0.5 / 15 and heals Pudge by the same amount (x1.5 with Lv 25 talent). The channel breaks if Pudge is disabled or the target leaves.",
        Icon = DismemberGlyph,
        School = Ctx.Schools.necromancy,
        Skill = Ctx.Skills.necromancy,
        Precept = new Precept(Purpose.Blast, Ctx.Elements.physical),
        ManaCost = DismemberManaCost,
        Targeting = DotaAbilityTargeting.UnitHostile,
        Range = DotaTable.Uniform(1),
        SkillSlotId = SkillDismemberId,
        OnCast = HandleDismemberCast
      });
    }

    private void RegisterModifiers()
    {
      RotSlowModifier = DotaModifierService.Define(new DotaModifierSpec
      {
        Key = "pudge.rot_slow",
        IsDebuff = true,
        IsDispellable = true,
        Stacking = DotaModifierStacking.Replace
      });

      MeatShieldModifier = DotaModifierService.Define(new DotaModifierSpec
      {
        Key = "pudge.meat_shield",
        IsDebuff = false,
        IsDispellable = false,
        Stacking = DotaModifierStacking.Replace,
        BadgeGlyph = MeatShieldGlyph,
        BadgeTitle = () => DotaLocalization.T("pudge.status.shield"),
        BadgeColour = Colour.DarkRed,
        HelpSpell = ShieldAbility.Spell
      });
    }

    private static Class RegisterPudgeClass(DotaHeroContext Ctx, Glyph AvatarGlyph)
    {
      return Ctx.Classes.Add(C =>
      {
        Profile.ConfigureClass(
          Ctx,
          C,
          Description: "A DOTA2 butcher who drags foes to his cleaver with Meat Hook, chokes everything nearby with Rot, shrugs off blows behind a Meat Shield, devours his prey with Dismember, and grows ever stronger with every hero-tier kill through Flesh Heap.",
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

        C.AddFeat(4, Ctx.Properties.free_action);
        C.AddFeat(8, Ctx.Properties.clarity);
        C.AddFeat(12, Ctx.Properties.life_regeneration);

        C.Startup.SetSkill(Ctx.Qualifications.proficient,
          Ctx.Skills.literacy,
          Ctx.Skills.axe,
          Ctx.Skills.light_armour,
          Ctx.Skills.medium_armour,
          Ctx.Skills.evocation,
          Ctx.Skills.necromancy,
          Ctx.Skills.abjuration);

        C.Startup.Loot.AddKit(Chance.Always, Modifier.Plus1, Ctx.Items.axe);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.leather_armour);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_healing);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.tripe_ration);
      });
    }

    private void RegisterSkillBuild(DotaHeroContext Ctx)
    {
      var Build = HeroSkillBuild.CreateStandard(
        ClassName: PudgeClassName,
        HeroDisplayName: "Pudge",
        HeroId: "pudge",
        IconGlyph: PudgeGlyph,
        SlotQ: DotaSkillSlot.CreateActive(
          Id: SkillHookId,
          Hotkey: "Q",
          Title: "Meat Hook",
          IconGlyph: MeatHookGlyph,
          Spell: HookAbility.Spell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("pudge.skill.q.desc_0")
            : DotaLocalization.Format("pudge.skill.q.desc_lv", GetHookDice(null, Lv), MaxSuffix(Lv, 4, "pudge.skill.q.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("pudge.skill.q.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.pure_damage", Lv => Lv > 0 ? $"{GetHookDice(null, Lv)}" : null),
            DotaSkillStat.Of("ui.stat.range", Lv => Lv > 0 ? DotaHelpFormat.Tiles(HookRangeTiles) : null)
          }),
        SlotW: DotaSkillSlot.CreateActive(
          Id: SkillRotId,
          Hotkey: "W",
          Title: "Rot",
          IconGlyph: RotGlyph,
          Spell: RotAbility.Spell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("pudge.skill.w.desc_0")
            : DotaLocalization.Format("pudge.skill.w.desc_lv", GetRotDamagePerTurn(Lv), $"{RotSlowRatios[Clamp(Lv, 4) - 1] * 100:0}", MaxSuffix(Lv, 4, "pudge.skill.w.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("pudge.skill.w.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.damage_per_turn", Lv => Lv > 0 ? $"{GetRotDamagePerTurn(Lv)}" : null),
            DotaSkillStat.Of("ui.stat.slow", Lv => Lv > 0 ? $"{RotSlowRatios[Clamp(Lv, 4) - 1] * 100:0}%" : null)
          }),
        SlotE: DotaSkillSlot.CreateActive(
          Id: SkillShieldId,
          Hotkey: "E",
          Title: "Meat Shield",
          IconGlyph: MeatShieldGlyph,
          Spell: ShieldAbility.Spell,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("pudge.skill.e.desc_0")
            : DotaLocalization.Format("pudge.skill.e.desc_lv", $"{GetShieldBlock(null, Lv):0.##}", GetShieldTurns(Lv), MaxSuffix(Lv, 4, "pudge.skill.e.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("pudge.skill.e.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.block", Lv => Lv > 0 ? $"{GetShieldBlock(null, Lv):0.##}" : null),
            DotaSkillStat.Of("ui.stat.duration", Lv => Lv > 0 ? DotaHelpFormat.Turns(GetShieldTurns(Lv)) : null)
          }),
        SlotR: DotaSkillSlot.CreateActive(
          Id: SkillDismemberId,
          Hotkey: "R",
          Title: "Dismember",
          IconGlyph: DismemberGlyph,
          Spell: DismemberAbility.Spell,
          IsUltimate: true,
          DescribeLevel: Lv => Lv <= 0
            ? DotaLocalization.T("pudge.skill.r.desc_0")
            : DotaLocalization.Format("pudge.skill.r.desc_lv", $"{DismemberDotaDps[Clamp(Lv, 3) - 1]:0}", $"{DismemberStrMultipliers[Clamp(Lv, 3) - 1]:0.0}", MaxSuffix(Lv, 3, "pudge.skill.r.desc_next_suffix")),
          TitleResolver: () => DotaLocalization.T("pudge.skill.r.title"),
          Stats: new[]
          {
            DotaSkillStat.Of("ui.stat.dota_dps", Lv => Lv > 0 ? $"{DismemberDotaDps[Clamp(Lv, 3) - 1]:0}" : null),
            DotaSkillStat.Of("ui.stat.str_multiplier", Lv => Lv > 0 ? $"×{DismemberStrMultipliers[Clamp(Lv, 3) - 1]:0.0}" : null)
          }),
        HeroDisplayNameResolver: () => DotaLocalization.T("pudge.hero_display"),
        PrimaryAttribute: DotaPrimaryAttribute.Strength);

      HeroSkillRegistry.Register(Ctx, Build);
    }

    private void RegisterTalentTree(DotaHeroContext Ctx)
    {
      int ReducedHookMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(MeatHookManaCost, HookTalentCdr);

      var Tree = HeroTalentTree.CreateStandard(
        ClassName: PudgeClassName,
        HeroDisplayNameResolver: () => DotaLocalization.T("pudge.hero_display"),
        IconGlyph: PudgeGlyph,
        T1Left: new TalentNode(
          Id: TalentT1RotSlow,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t1l.title"),
          DescriptionResolver: () => DotaLocalization.T("pudge.talent.t1l.desc")),
        T1Right: new TalentNode(
          Id: TalentT1Armor,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t1r.title"),
          DescriptionResolver: () => DotaLocalization.Format("pudge.talent.t1r.desc", DotaMechanicsMapper.MapArmorToDefence(TalentDotaArmor))),
        T2Left: new TalentNode(
          Id: TalentT2HookDamage,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t2l.title"),
          DescriptionResolver: () => DotaLocalization.Format("pudge.talent.t2l.desc", DotaMechanicsMapper.MapSkillDamage(HookTalentBonusDotaDamage))),
        T2Right: new TalentNode(
          Id: TalentT2SpellLifesteal,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t2r.title"),
          DescriptionResolver: () => DotaLocalization.T("pudge.talent.t2r.desc")),
        T3Left: new TalentNode(
          Id: TalentT3HookCooldown,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t3l.title"),
          DescriptionResolver: () => DotaLocalization.Format("pudge.talent.t3l.desc", MeatHookManaCost, ReducedHookMana),
          ManaReductions: new[] { new SpellManaReductionRule(MeatHookSpellName, HookTalentCdr) }),
        T3Right: new TalentNode(
          Id: TalentT3DismemberDuration,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t3r.title"),
          DescriptionResolver: () => DotaLocalization.Format("pudge.talent.t3r.desc", GetDismemberTicks(false), GetDismemberTicks(true))),
        T4Left: new TalentNode(
          Id: TalentT4FleshHeap,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t4l.title"),
          DescriptionResolver: () => DotaLocalization.T("pudge.talent.t4l.desc"),
          OnLearn: (_, Ch) => SyncFleshHeapLife(Ch)),
        T4Right: new TalentNode(
          Id: TalentT4DismemberDamage,
          TitleResolver: () => DotaLocalization.T("pudge.talent.t4r.title"),
          DescriptionResolver: () => DotaLocalization.T("pudge.talent.t4r.desc")));

      TalentRegistry.Register(Ctx, Tree);
    }

    private void RegisterStatusBadges()
    {
      // 先天：腐肉堆积（层数 / 力量加成）
      DotaStatusBarBridge.RegisterProvider("pudge.flesh_heap", PudgeClassName, Ch =>
      {
        int Stacks = DotaCharacterStorage.GetCustomInt(Ch, StorageFleshHeapStacksKey);
        if (Stacks <= 0)
          return null;

        return DotaStatusBadge.ForPassive(
          "pudge.flesh_heap",
          FleshHeapGlyph,
          "pudge.flesh_heap",
          "pudge.status.flesh_heap",
          Count: $"×{Stacks}",
          Colour: Colour.DarkRed);
      });

      // W：腐烂开启中
      DotaStatusBarBridge.RegisterProvider("pudge.rot", PudgeClassName, Ch =>
      {
        if (!RotActive.Contains(Ch))
          return null;

        return new DotaStatusBadge
        {
          Key = "pudge.rot",
          Glyph = RotGlyph,
          Text = DotaStatusBadge.Title("pudge.status.rot"),
          Count = DotaLocalization.T("pudge.status.rot_on"),
          Colour = Colour.DarkGreen,
          HelpSpell = RotAbility.Spell
        };
      });
    }

    // ---------------------------------------------------------------- Q 肉钩

    private void HandleMeatHookCast(DotaAbilityCast Cast)
    {
      var Engine = Cast.Engine;
      var Caster = Cast.Caster;
      var Origin = Caster?.Square;
      if (Origin == null || Caster.Deceased)
        return;

      var Path = BuildHookPath(Origin, Cast.ConsumePendingTarget()?.TargetSquare, Cast.Direction, Cast.Range);
      if (Path.Count == 0)
      {
        Cast.RefundAndLog("pudge.log.hook_blocked");
        return;
      }

      // 沿路径寻找第一个单位；遇到墙体 / 虚空时钩子停下
      int HitIndex = -1;
      int ReachIndex = Path.Count - 1;
      for (int I = 0; I < Path.Count; I++)
      {
        var Sq = Path[I];
        if (Sq.Character != null && Sq.Character != Caster)
        {
          HitIndex = I;
          ReachIndex = I;
          break;
        }
        if (Sq.IsVoid() || Sq.IsBlocked(Vision: false))
        {
          ReachIndex = I - 1;
          break;
        }
      }

      var Victim = HitIndex >= 0 ? Path[HitIndex].Character : null;
      int Steps = ReachIndex + 1; // 钩头飞过的格数
      var Dir = Origin.AsDirection(Steps > 0 ? Path[ReachIndex] : Path[0]);
      float AngleDeg = DotaVfx.AngleOf(Origin, Path[Path.Count - 1]);
      float Out = Steps * HookSecondsPerTile;
      float HitTime = HookCastPointSeconds + Out;
      float ReturnEnd = HitTime + Out;

      // 甩钩动作（前摇 0.3 秒）
      PathosEngineBridge.StoryAttacking(Engine, DotaPresentation.Seconds(HookCastPointSeconds), Caster, Dir);

      var Sequence = DotaPresentation.Sequence(Engine, Caster, HookSequenceKey).FreezeWorld();

      // 出钩：钩头逐格前进，身后铺铁链；每节铁链保留到钩子收回经过它为止
      for (int I = 0; I < Steps; I++)
      {
        var Sq = Path[I];
        int Index = I;
        Sequence.At(HookCastPointSeconds + I * HookSecondsPerTile, SeqCtx =>
        {
          float ChainLife = Math.Max(0.1f, ReturnEnd - (HookCastPointSeconds + Index * HookSecondsPerTile) - Index * HookSecondsPerTile);
          DotaVfx.Beam(SeqCtx.Engine, Sq, Dir, HookChainBeam, ChainLife);
          DotaVfx.Strike(SeqCtx.Engine, Sq, HookHeadStrike, HookSecondsPerTile * 1.2f, AngleDeg);
        });
      }

      if (Victim == null)
      {
        // 落空：钩头原路收回
        for (int I = Steps - 1; I >= 0; I--)
        {
          var Sq = Path[I];
          int Back = Steps - 1 - I;
          Sequence.At(HitTime + Back * HookSecondsPerTile, SeqCtx => DotaVfx.Strike(SeqCtx.Engine, Sq, HookHeadStrike, HookSecondsPerTile * 1.2f, AngleDeg));
        }
        Sequence.Play();
        Cast.Log(DotaLocalization.T("pudge.log.hook_miss"));
        return;
      }

      Sequence.At(HitTime, SeqCtx => ResolveHookHit(SeqCtx.Engine, Caster, Victim, Path, HitIndex, Out));
      Sequence.Play();
    }

    /// <summary>
    /// 肉钩路径：优先朝玩家点击格方向延伸到最大射程；无点击时沿施法方向。返回不含起点的格子序列。
    /// </summary>
    private static List<Square> BuildHookPath(Square Origin, Square Aim, Direction Direction, int Range)
    {
      var Result = new List<Square>();
      if (Aim != null && Aim.Map == Origin.Map && Aim != Origin)
      {
        int Dx = Aim.X - Origin.X, Dy = Aim.Y - Origin.Y;
        float K = Range / (float)Math.Max(Math.Abs(Dx), Math.Abs(Dy));
        int Fx = Origin.X + (int)MathF.Round(Dx * K), Fy = Origin.Y + (int)MathF.Round(Dy * K);
        var Map = Origin.Map;
        // 终点可能落在地图外：沿直线向回收缩到地图内
        while ((Fx != Origin.X || Fy != Origin.Y) && !Map.IsValid(Fx, Fy))
        {
          Fx -= Math.Sign(Fx - Origin.X);
          Fy -= Math.Sign(Fy - Origin.Y);
        }
        if (Fx == Origin.X && Fy == Origin.Y)
          return Result;

        var Line = DotaVfx.LinePath(Origin, Map[Fx, Fy], Range + 2);
        for (int I = 1; I < Line.Count && Result.Count < Range; I++)
          Result.Add(Line[I]);
        return Result;
      }

      if (Direction == Direction.Self)
        return Result;

      var Cursor = Origin;
      for (int I = 0; I < Range; I++)
      {
        var Next = Cursor.Adjacent(Direction, 1);
        if (Next == null || Next == Cursor)
          break;
        Result.Add(Next);
        Cursor = Next;
      }
      return Result;
    }

    private void ResolveHookHit(object Engine, Character Caster, Character Victim, List<Square> Path, int HitIndex, float ReturnSeconds)
    {
      if (Caster.Deceased || Caster.Square == null || Victim == null || Victim.Deceased || Victim.Square == null)
        return;

      var HitSq = Victim.Square;
      string VictimName = DotaCombatHelper.GetDisplayName(Victim);
      bool Hostile = DotaCombatHelper.IsHostile(Caster, Victim);
      bool Friendly = !Hostile && DotaCombatHelper.IsFriendly(Caster, Victim);

      if (!Hostile && !Friendly)
      {
        // 中立单位（商人、和平生物等）：钩子弹开，不拖拽不伤害
        PathosEngineBridge.StoryCaption(Engine, HitSq, Colour.Gray, DotaLocalization.T("pudge.caption.clank"));
        PathosEngineBridge.WriteInternalLog(Engine, Caster, MeatHookGlyph, DotaLocalization.Format("pudge.log.hook_neutral", VictimName));
        return;
      }

      if (Hostile)
      {
        DotaVfx.Strike(Engine, HitSq, HookBloodStrike, 0.5f);

        if (DotaHookBus.QueryInvulnerable(Victim))
        {
          PathosEngineBridge.WriteInternalLog(Engine, Caster, MeatHookGlyph, DotaLocalization.Format("pudge.log.hook_immune", VictimName));
        }
        else if (!IsHeroTier(Victim))
        {
          // DOTA2：非远古单位直接秒杀
          bool Killed = PathosEngineBridge.KillCharacter(Engine, Caster, Victim, MeatHookSpellName);
          if (!Killed && !Victim.Deceased)
            DotaDamage.Strike(Engine, Caster, Victim, Caster.Square.AsDirection(HitSq), Math.Max(999, (Victim.Life?.Points ?? 0) + 50), DotaDamageInfo.Pure(MeatHookSpellName));

          PathosEngineBridge.StoryCaption(Engine, HitSq, Colour.Crimson, DotaLocalization.T("pudge.caption.fresh_meat"));
          PathosEngineBridge.WriteInternalLog(Engine, Caster, MeatHookGlyph, DotaLocalization.Format("pudge.log.hook_kill", VictimName));
          return;
        }
        else
        {
          int Rolled = DotaCombatHelper.RollDice(GetHookDice(Caster, HookAbility.Level(Caster, 4)));
          DotaDamage.Strike(Engine, Caster, Victim, Caster.Square.AsDirection(HitSq), Rolled, DotaDamageInfo.Pure(MeatHookSpellName));
          PathosEngineBridge.WriteInternalLog(Engine, Caster, MeatHookGlyph, DotaLocalization.Format("pudge.log.hook_damage", VictimName, Rolled));
        }
      }

      if (Victim.Deceased || Victim.Square == null)
        return;

      // 拖到帕吉身边：沿路径从近到远找第一个可站立空地
      Square Land = null;
      for (int I = 0; I < HitIndex; I++)
      {
        if (DotaCombatHelper.IsOpenSquare(Path[I]))
        {
          Land = Path[I];
          break;
        }
      }
      Land ??= DotaCombatHelper.FindOpenAdjacentSquare(Victim, Caster.Square);
      if (Land == null || Land == Victim.Square)
        return;

      var From = Victim.Square;
      PathosEngineBridge.DashCharacter(Engine, Victim, Land);
      PathosEngineBridge.StoryFlinging(Engine, DotaPresentation.Seconds(ReturnSeconds), From, Land, Victim);

      if (Friendly)
        PathosEngineBridge.WriteInternalLog(Engine, Caster, MeatHookGlyph, DotaLocalization.Format("pudge.log.hook_ally", VictimName));
    }

    // ---------------------------------------------------------------- W 腐烂

    private void HandleRotCast(DotaAbilityCast Cast)
    {
      var Caster = Cast.Caster;
      if (Caster == null || Caster.Deceased)
        return;

      if (RotActive.Contains(Caster))
      {
        RotActive.Remove(Caster);
        Cast.Log(DotaLocalization.T("pudge.log.rot_off"));
        return;
      }

      RotActive.Add(Caster);
      Cast.Log(DotaLocalization.T("pudge.log.rot_on"));

      // 持续毒雾：按真实时间循环播放（不冻结世界），腐烂关闭或帕吉死亡即停止
      DotaPresentation.Loop(Cast.Engine, Caster, RotLoopKey, 0.5f,
        Ch => Ch != null && !Ch.Deceased && RotActive.Contains(Ch),
        (Engine, Ch) => DotaVfx.Explode(Engine, Ch.Square, RotCloudExplosion, 0.75f));
    }

    private void TickRot(object Engine, Character Pudge)
    {
      if (Pudge.Square == null)
        return;

      int Level = RotAbility.Level(Pudge, 4);
      if (Level <= 0)
      {
        RotActive.Remove(Pudge);
        return;
      }

      int Damage = GetRotDamagePerTurn(Level);
      float Slow = GetRotSlow(Pudge, Level);
      var Info = DotaDamageInfo.Magical(RotSpellName, DotaDamageFlags.Spell | DotaDamageFlags.DoT);

      foreach (var Enemy in DotaCombatHelper.FindHostilesInRadius(Pudge, Pudge.Square, RotRadius))
      {
        if (Enemy.Deceased || Enemy.Square == null)
          continue;

        DotaModifierService.Apply(RotSlowModifier, Pudge, Enemy, RotSlowTurns, M => M.MoveSpeedPct = -Slow);
        DotaDamage.Strike(Engine, Pudge, Enemy, Pudge.Square.AsDirection(Enemy.Square), Damage, Info);
      }

      // 自伤：按 DOTA2 每 0.2 秒一跳结算肉盾格挡，再折算回 Pathos（小数按概率进位），最多扣到 1 HP
      int SelfDamage = RollStochastic(ComputeRotSelfDamage(Pudge, Level));
      int Cap = (Pudge.Life?.Points ?? 1) - 1;
      SelfDamage = Math.Min(SelfDamage, Cap);
      if (SelfDamage > 0 && !Pudge.Deceased)
        DotaDamage.Strike(Engine, Pudge, Pudge, Direction.Self, SelfDamage, DotaDamageInfo.Magical(RotSpellName, DotaDamageFlags.Spell | DotaDamageFlags.DoT | DotaDamageFlags.NoBlock));
    }

    /// <summary>
    /// 腐烂每回合自伤期望（Pathos 点）：每回合 0.5 秒 = 2.5 跳，每跳 = DPS × 0.2 − 肉盾格挡（DOTA2 原值），不低于 0。
    /// </summary>
    private float ComputeRotSelfDamage(Character Pudge, int Level)
    {
      float TickDota = RotDotaDps[Clamp(Level, 4) - 1] * RotTickSeconds;
      float BlockDota = DotaModifierService.Get(Pudge, MeatShieldModifier)?.DamageBlock * DotaMechanicsMapper.DamageScale ?? 0.0f;
      float TicksPerTurn = 1.0f / DotaMechanicsMapper.TurnsPerSecond / RotTickSeconds;
      return TicksPerTurn * MathF.Max(0.0f, TickDota - BlockDota) / DotaMechanicsMapper.DamageScale;
    }

    // ---------------------------------------------------------------- E 肉盾

    private void HandleMeatShieldCast(DotaAbilityCast Cast)
    {
      var Caster = Cast.Caster;
      if (Caster == null || Caster.Deceased)
        return;

      int Level = ShieldAbility.Level(Caster, 4);
      float Block = GetShieldBlock(Caster, Level);
      int Turns = GetShieldTurns(Level);

      DotaModifierService.Apply(MeatShieldModifier, Caster, Caster, Turns, M => M.DamageBlock = Block);
      DotaVfx.StrikeOn(Cast.Engine, Caster, MeatShieldStrike, 0.6f);
      Cast.Log(DotaLocalization.Format("pudge.log.shield_cast", $"{Block:0.##}", Turns));
    }

    // ---------------------------------------------------------------- R 肢解

    private void HandleDismemberCast(DotaAbilityCast Cast)
    {
      var Engine = Cast.Engine;
      var Caster = Cast.Caster;
      if (Caster?.Square == null || Caster.Deceased)
        return;

      if (!Cast.TryResolveHostileTarget(out var Target, "pudge.log.dismember_no_target"))
        return;

      if (!Caster.Square.IsAdjacent(Target.Square, 1))
      {
        Cast.RefundAndLog("pudge.log.dismember_no_target");
        return;
      }

      bool HasDurationTalent = TalentService.HasTalent(Caster, TalentT3DismemberDuration);
      int Ticks = GetDismemberTicks(HasDurationTalent);
      float ChannelSeconds = DismemberDotaChannelSeconds + (HasDurationTalent ? DismemberTalentBonusSeconds : 0.0f);
      float Interval = ChannelSeconds / Ticks;
      int WorldTicksPerStep = Math.Max(1, PathosEngineBridge.GetMoveDelay(Engine, Caster).GetTurns());

      Dismembering.Add(Caster);
      PathosEngineBridge.ApplyTransient(Engine, Caster, Target, Ctx.Properties.stunned, Dice.Fixed(Ticks + 1));
      PathosEngineBridge.SetBusy(Engine, Caster, Delay.FromTurns(WorldTicksPerStep * (Ticks + 1)));
      DotaPresentation.HitStop(Engine, Caster, 0.12f);

      int DoneTicks = 0;
      int TotalDamage = 0;
      float Accumulator = 0.0f;
      string TargetName = DotaCombatHelper.GetDisplayName(Target);

      var Sequence = DotaPresentation.Sequence(Engine, Caster, DismemberSequenceKey)
        .FreezeWorld(WorldTicksPerStep)
        .OnFinish((SeqCtx, _) =>
        {
          Dismembering.Remove(Caster);
          if (!Target.Deceased && DoneTicks < Ticks)
            PathosEngineBridge.RemoveTransient(SeqCtx.Engine, Caster, Target, Ctx.Properties.stunned);
          if (!Caster.Deceased)
            PathosEngineBridge.SetBusy(SeqCtx.Engine, Caster, PathosEngineBridge.GetMoveDelay(SeqCtx.Engine, Caster));

          bool Broken = DoneTicks < Ticks && !Target.Deceased;
          PathosEngineBridge.WriteInternalLog(SeqCtx.Engine, Caster, DismemberGlyph,
            DotaLocalization.Format(Broken ? "pudge.log.dismember_broken" : "pudge.log.dismember_done", TargetName, DoneTicks, TotalDamage));
        });

      for (int I = 0; I < Ticks; I++)
      {
        Sequence.At(I * Interval, SeqCtx =>
        {
          var E = SeqCtx.Engine;
          if (Caster.Deceased || Caster.Square == null || Target.Deceased || Target.Square == null
            || !Caster.Square.IsAdjacent(Target.Square, 1) || IsDisabled(Caster))
          {
            SeqCtx.Stop();
            return;
          }

          Accumulator += ComputeDismemberPerTurn(Caster);
          int Damage = (int)MathF.Floor(Accumulator);
          Accumulator -= Damage;
          DoneTicks++;

          var Dir = Caster.Square.AsDirection(Target.Square);
          PathosEngineBridge.StoryAttacking(E, DotaPresentation.Seconds(Interval * 0.6f), Caster, Dir);
          DotaVfx.StrikeOn(E, Target, DismemberStrike, Interval * 0.9f);

          if (Damage > 0)
          {
            TotalDamage += Damage;
            DotaDamage.Strike(E, Caster, Target, Dir, Damage, DotaDamageInfo.Magical(DismemberSpellName, DotaDamageFlags.Spell));
            if (Caster.Life != null && Caster.Life.Points < Caster.Life.Total)
              PathosEngineBridge.HealCharacter(E, Caster, Caster, Direction.Self, Damage);
          }

          if (Target.Deceased)
            SeqCtx.Stop();
        });
      }

      Sequence.Play();
    }

    private bool IsDisabled(Character Character)
      => PathosEngineBridge.HasTransient(Character, Ctx.Properties.stunned)
      || PathosEngineBridge.HasTransient(Character, Ctx.Properties.paralysis)
      || PathosEngineBridge.HasTransient(Character, Ctx.Properties.sleeping);

    /// <summary>肢解每回合伤害 / 治疗期望（Pathos 点）：(DPS + 力量倍数 × 等效 DOTA 力量) × 0.5 / 15，T4 右 ×1.5。</summary>
    private float ComputeDismemberPerTurn(Character Caster)
    {
      int Level = DismemberAbility.Level(Caster, 3);
      float DotaDps = DismemberDotaDps[Level - 1] + DismemberStrMultipliers[Level - 1] * GetEquivalentDotaStrength(Caster);
      float PerTurn = DotaDps / DotaMechanicsMapper.TurnsPerSecond / DotaMechanicsMapper.DamageScale;
      return TalentService.HasTalent(Caster, TalentT4DismemberDamage) ? PerTurn * DismemberTalentMultiplier : PerTurn;
    }

    /// <summary>Pathos STR 按初始属性映射反推的等效 DOTA2 力量：20 + (STR − 12) / 0.45。</summary>
    private static float GetEquivalentDotaStrength(Character Character)
    {
      int Str = Character != null ? DotaStats.Str(Character) : DotaMechanicsMapper.MapAttribute(25);
      return MathF.Max(0.0f, 20.0f + (Str - 12) / 0.45f);
    }

    private static int GetDismemberTicks(bool HasDurationTalent)
      => DotaMechanicsMapper.MapDurationTurns(DismemberDotaChannelSeconds + (HasDurationTalent ? DismemberTalentBonusSeconds : 0.0f));

    // ---------------------------------------------------------------- 先天 腐肉堆积

    private void HandleFleshHeapKill(KillEventArgs Args)
    {
      var Victim = Args.Defender;
      if (Victim == null)
        return;

      Character Pudge = null;
      if (Args.Attacker != null && Args.Attacker != Victim && IsPudge(Args.Attacker) && !Args.Attacker.Deceased)
      {
        Pudge = Args.Attacker;
      }
      else if (Victim.Square != null)
      {
        foreach (var Sq in Victim.Square.GetAroundSquares(FleshHeapRadius))
        {
          var Cand = Sq?.Character;
          if (Cand != null && Cand != Victim && !Cand.Deceased && IsPudge(Cand))
          {
            Pudge = Cand;
            break;
          }
        }
      }

      if (Pudge == null || Victim.Neutral || PathosEngineBridge.IsAllied(Pudge, Victim))
        return;
      if (!IsHeroTier(Victim) && (Victim.Entity == null || Victim.Entity.Level < Pudge.Level))
        return;

      int Stacks = DotaCharacterStorage.AddCustomInt(Pudge, StorageFleshHeapStacksKey, 1);
      SyncFleshHeapLife(Pudge);

      if (Pudge.Square != null)
        PathosEngineBridge.StoryCaption(Args.Engine, Pudge.Square, Colour.DarkRed, DotaLocalization.T("pudge.caption.flesh_heap"));
      PathosEngineBridge.WriteInternalLog(Args.Engine, Pudge, FleshHeapGlyph,
        DotaLocalization.Format("pudge.log.flesh_heap", DotaCombatHelper.GetDisplayName(Victim), Stacks, GetFleshHeapBonusStr(Pudge)));
    }

    private static float GetFleshHeapDotaStr(Character Pudge)
    {
      int Stacks = DotaCharacterStorage.GetCustomInt(Pudge, StorageFleshHeapStacksKey);
      float PerStack = FleshHeapDotaStrPerStack * (TalentService.HasTalent(Pudge, TalentT4FleshHeap) ? FleshHeapTalentMultiplier : 1.0f);
      return Stacks * PerStack;
    }

    private static int GetFleshHeapBonusStr(Character Pudge)
      => Pudge == null ? 0 : (int)MathF.Floor(GetFleshHeapDotaStr(Pudge) * FleshHeapLateStatScale + 0.0001f);

    /// <summary>把腐肉堆积的力量加成同步到最大生命（每点 +6 HP，已同步量存档防止读档重复叠加）。</summary>
    private static void SyncFleshHeapLife(Character Pudge)
    {
      if (Pudge?.Life == null)
        return;

      int Target = GetFleshHeapBonusStr(Pudge) * DotaMechanicsMapper.HpPerStrengthPoint;
      int Applied = DotaCharacterStorage.GetCustomInt(Pudge, StorageFleshHeapAppliedHpKey);
      if (Target == Applied)
        return;

      DotaLife.AdjustMax(Pudge, Target - Applied, DotaLifeAdjustMode.KeepRatio);
      DotaCharacterStorage.SetCustomInt(Pudge, StorageFleshHeapAppliedHpKey, Target);
    }

    // ---------------------------------------------------------------- 帮助面板

    private void RegisterSpellHelpSpecs()
    {
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = MeatHookSpellName,
        ResolveCurrentTier = HookAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          var D = GetHookDice(Ch, HookAbility.Level(Ch, 4));
          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("pudge.help.q.key_range"), DotaHelpColor.Control(DotaHelpFormat.Tiles(HookAbility.Range(Ch)))),
            (DotaLocalization.T("pudge.help.q.key_dmg"), DotaHelpColor.Physical(DotaLocalization.Format("pudge.help.q.val_dmg", DotaSpellHelpBridge.FormatDiceWithRange(D), $"{D.Average():0.#}")))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("pudge.help.q.mech_1"),
          DotaLocalization.T("pudge.help.q.mech_2")
        },
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(HookAbility.RangeAt(HelpCtx.Character, Tier)),
          _ => DotaHelpColor.Physical(DotaSpellHelpBridge.FormatDiceWithRange(GetHookDice(HelpCtx.Character, HookAbility.Level(HelpCtx.Character, 4)))))
      });

      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = RotSpellName,
        ResolveCurrentTier = RotAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Level = RotAbility.Level(Ch, 4);
          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("pudge.help.w.key_state"), Ch != null && RotActive.Contains(Ch)
              ? DotaHelpColor.Skill(DotaLocalization.T("pudge.status.rot_on"))
              : DotaLocalization.T("pudge.help.w.val_off")),
            (DotaLocalization.T("pudge.help.w.key_dmg"), DotaHelpColor.Magic(DotaLocalization.Format("pudge.help.w.val_dmg", GetRotDamagePerTurn(Level)))),
            (DotaLocalization.T("pudge.help.w.key_self"), DotaHelpColor.Magic(DotaLocalization.Format("pudge.help.w.val_self", $"{(Ch != null ? ComputeRotSelfDamage(Ch, Level) : GetRotDamagePerTurn(Level)):0.##}"))),
            (DotaLocalization.T("pudge.help.w.key_slow"), DotaHelpColor.Control($"-{GetRotSlow(Ch, Level) * 100:0}%"))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("pudge.help.w.mech_1"),
          DotaLocalization.T("pudge.help.w.mech_2")
        },
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          _ => DotaLocalization.T("ui.help.self_cast"),
          _ => DotaHelpColor.Magic(DotaLocalization.Format("pudge.help.w.val_dmg", GetRotDamagePerTurn(RotAbility.Level(HelpCtx.Character, 4)))))
      });

      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = MeatShieldSpellName,
        ResolveCurrentTier = ShieldAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Level = ShieldAbility.Level(Ch, 4);
          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("pudge.help.e.key_block"), DotaHelpColor.Skill(DotaLocalization.Format("pudge.help.e.val_block", $"{GetShieldBlock(Ch, Level):0.##}"))),
            (DotaLocalization.T("pudge.help.e.key_duration"), DotaHelpColor.Control(DotaHelpFormat.Turns(GetShieldTurns(Level))))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("pudge.help.e.mech_1"),
          DotaLocalization.T("pudge.help.e.mech_2")
        },
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          _ => DotaLocalization.T("ui.help.self_cast"),
          _ => DotaHelpColor.Skill(DotaLocalization.Format("pudge.help.e.val_block", $"{GetShieldBlock(HelpCtx.Character, ShieldAbility.Level(HelpCtx.Character, 4)):0.##}")))
      });

      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = DismemberSpellName,
        ResolveCurrentTier = DismemberAbility.Tier,
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          bool Talent = Ch != null && TalentService.HasTalent(Ch, TalentT3DismemberDuration);
          int Ticks = GetDismemberTicks(Talent);
          float PerTurn = ComputeDismemberPerTurn(Ch);
          return new (string Key, string Value)[]
          {
            (DotaLocalization.T("pudge.help.r.key_channel"), DotaHelpColor.Control(DotaHelpFormat.Turns(Ticks))),
            (DotaLocalization.T("pudge.help.r.key_dmg"), DotaHelpColor.Magic(DotaLocalization.Format("pudge.help.r.val_dmg", $"{PerTurn:0.#}", $"{PerTurn * Ticks:0}"))),
            (DotaLocalization.T("pudge.help.r.key_str"), DotaHelpColor.Stat($"{GetEquivalentDotaStrength(Ch):0}"))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.T("pudge.help.r.mech_1"),
          DotaLocalization.T("pudge.help.r.mech_2")
        },
        BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(
          Tier => DotaHelpFormat.CastRangeSuffix(DismemberAbility.RangeAt(HelpCtx.Character, Tier)),
          _ => DotaHelpColor.Magic(DotaLocalization.Format("pudge.help.r.val_tier", $"{ComputeDismemberPerTurn(HelpCtx.Character):0.#}")))
      });
    }

    private void RegisterPassiveHelpSpecs()
    {
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        Id = "pudge.flesh_heap",
        IconGlyph = FleshHeapGlyph,
        TitleResolver = () => DotaLocalization.T("pudge.status.flesh_heap"),
        DescriptionResolver = () => DotaLocalization.T("pudge.help.fh.desc"),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (!IsPudge(Ch))
            return null;

          int Stacks = DotaCharacterStorage.GetCustomInt(Ch, StorageFleshHeapStacksKey);
          int Bonus = GetFleshHeapBonusStr(Ch);
          return new List<(string, string)>
          {
            (DotaLocalization.T("pudge.help.fh.key_stacks"), DotaHelpColor.Passive($"{Stacks}")),
            (DotaLocalization.T("pudge.help.fh.key_bonus"), DotaLocalization.Format(
              "pudge.help.fh.val_bonus",
              DotaHelpColor.Stat($"{GetFleshHeapDotaStr(Ch):0.#}"),
              DotaHelpColor.Stat($"+{Bonus}"),
              DotaHelpColor.Heal($"+{Bonus * DotaMechanicsMapper.HpPerStrengthPoint} HP")))
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          DotaLocalization.Format("pudge.help.fh.mech_1", DotaHelpColor.Control(DotaHelpFormat.Tiles(FleshHeapRadius))),
          DotaLocalization.T("pudge.help.fh.mech_2")
        }
      });
    }

    // ---------------------------------------------------------------- 数值（施法与帮助面板共用）

    private static int Clamp(int Level, int Max) => Math.Clamp(Level, 1, Max);

    private static string MaxSuffix(int Level, int Max, string NextKey)
      => DotaLocalization.T(Level >= Max ? "pudge.skill.desc_max_suffix" : NextKey);

    private static Dice GetHookDice(Character Caster, int Level)
    {
      float Dota = HookDotaDamage[Clamp(Level, 4) - 1]
        + (Caster != null && TalentService.HasTalent(Caster, TalentT2HookDamage) ? HookTalentBonusDotaDamage : 0.0f);
      return DotaMechanicsMapper.MapSkillDamageDice(Dota, DieFaces: 6);
    }

    private static int GetRotDamagePerTurn(int Level)
      => DotaMechanicsMapper.MapDotDamagePerTurn(RotDotaDps[Clamp(Level, 4) - 1]);

    private static float GetRotSlow(Character Caster, int Level)
      => RotSlowRatios[Clamp(Level, 4) - 1] + (Caster != null && TalentService.HasTalent(Caster, TalentT1RotSlow) ? RotTalentBonusSlow : 0.0f);

    private static float GetShieldBlock(Character Caster, int Level)
    {
      bool Talent = Caster != null && TalentService.HasTalent(Caster, TalentT4FleshHeap);
      return ShieldDotaBlock[Clamp(Level, 4) - 1] * (Talent ? FleshHeapTalentMultiplier : 1.0f) / DotaMechanicsMapper.DamageScale;
    }

    private static int GetShieldTurns(int Level)
      => DotaMechanicsMapper.MapDurationTurns(ShieldDotaDurations[Clamp(Level, 4) - 1]);

    /// <summary>小数按概率进位（期望不变）。</summary>
    private static int RollStochastic(float Value) => DotaModifierService.RollBlock(Value);

    /// <summary>DOTA2「英雄 / 远古」在 Pathos 中的对应：唯一怪物、守卫，以及非怪物角色。</summary>
    private static bool IsHeroTier(Character Character)
      => Character.Entity == null || Character.Entity.IsUnique || Character.Entity.IsGuardian;

    private static PudgeRuntimeState GetState(Character Character)
      => PudgeStates.GetValue(Character, _ => new PudgeRuntimeState());

    private static bool IsPudge(Character Character)
      => DotaCombatHelper.IsClass(Character, PudgeClassName);
  }
}
