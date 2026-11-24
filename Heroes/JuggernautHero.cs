using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Inv;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;
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

    // 主宰天赋节点 ID（Lv 5 / 10 / 15 / 20 解锁，Lv 22 / 23 / 24 / 25 双修补点）
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

    // DOTA2 7.42 主宰初始三维：STR 20 / AGI 32 / INT 14
    private const int DotaBaseStr = 20;
    private const int DotaBaseAgi = 32;
    private const int DotaBaseInt = 14;
    private static readonly PathosStartingStats StartingStats =
      DotaMechanicsMapper.MapStartingStats(DotaBaseStr, DotaBaseAgi, DotaBaseInt, Charisma: 10);

    private static readonly int BladeFuryManaCost = DotaMechanicsMapper.MapSkillManaCost(110);
    private static readonly int HealingWardManaCost = DotaMechanicsMapper.MapSkillManaCost(140);
    private static readonly int OmnislashManaCost = DotaMechanicsMapper.MapSkillManaCost(250); // 200/250/300 取中值

    // DOTA2 7.42 技能伤害基准（统一经 DotaMechanicsMapper K_dmg = 15 与 1s = 2 回合折算）
    // 剑刃风暴 7.41b/7.42 每秒伤害：85 / 115 / 145 / 175（Champion 延展至 205），每回合合并结算 1 次并受敏捷/剑心犹在攻速频率缩放
    private static readonly float[] BladeFuryDotaDps = { 85.0f, 115.0f, 145.0f, 175.0f, 205.0f };
    private const float BladeFuryTalentBonusDotaDps = 120.0f; // T3 左天赋：原 +120 每秒伤害
    // 无敌斩 7.42 额外攻击力：25 / 30 / 35（5 阶熟练度对应 25 / 30 / 35 / 40 / 45 → Pathos +2 / +2 / +2 / +3 / +3）
    private static readonly float[] OmnislashBonusDotaDamage = { 25.0f, 30.0f, 35.0f, 40.0f, 45.0f };

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

    private static readonly HashSet<Character> ActiveOmnislashCasters = new();
    private static readonly HashSet<Character> ActiveBladeFuryTicks = new();
    private static readonly HashSet<Character> SkipNextBladeFuryTick = new();
    private static readonly HashSet<Character> SkipNextOmnislashTick = new();

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
      BladeFuryExplosion = RegisterBladeFuryExplosion(Ctx);
      OmnislashStrike = RegisterCustomStrike(Ctx, "omnislash slash", "omnislash fx strike");
      BladeDanceCritStrike = RegisterCustomStrike(Ctx, "blade dance crit", "blade dance fx crit");
      HealingWardPulseStrike = RegisterCustomStrike(Ctx, "healing ward pulse", "healing ward fx pulse");
      OmnislashDashBeam = RegisterOmnislashDashBeam(Ctx);

      // 2. 注册先天技能（剑心犹在）、被动技能（剑舞）与剑刃风暴/无敌斩状态属性
      BladeformTalentProperty = RegisterBladeformTalent(Ctx, BladeformGlyph);
      var BladeDanceTalent = RegisterBladeDanceTalent(Ctx, BladeDanceGlyph);
      BladeFuryStatusProperty = RegisterBladeFuryStatus(Ctx, BladeFuryGlyph);
      OmnislashStatusProperty = RegisterOmnislashStatus(Ctx, OmnislashGlyph);

      // 剑刃风暴与无敌斩属于主动技能的短期状态，不应出现在原生「天赋」面板或百科「天赋/特性」栏目中
      TalentRegistry.RegisterTransientOnlyProperty(BladeFuryStatusProperty);
      TalentRegistry.RegisterTransientOnlyProperty(OmnislashStatusProperty);

      // 3. 注册治疗守卫召唤物实体（使用透明背景的木雕翡翠火焰图腾贴图）
      HealingWardEntity = RegisterHealingWardEntity(Ctx, HealingWardTotemGlyph);

      // 4. 注册主宰主动技能（Q / W / R）
      var BladeFurySpell = RegisterBladeFurySpell(Ctx, BladeFuryGlyph, BladeFuryStatusProperty);
      var HealingWardSpell = RegisterHealingWardSpell(Ctx, HealingWardGlyph, HealingWardEntity);
      var OmnislashSpell = RegisterOmnislashSpell(Ctx, OmnislashGlyph);

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
      Ctx.Heroes.Add(H =>
      {
        H.Name = "Juggernaut";
        H.Race = Ctx.Races.human;
        H.Class = JuggernautClass;
        H.Gender = Ctx.Genders.male;
        H.CustomGlyph = JuggernautGlyph;
      });

      // 7. 注册主宰 4 层天赋树（含减冷却 → 减蓝耗声明式映射）
      RegisterTalentTree(Ctx, JuggernautGlyph);
    }

    private static void RegisterTalentTree(DotaHeroContext Ctx, Glyph IconGlyph)
    {
      int ReducedWardMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(HealingWardManaCost, HealingWardTalentCdr);
      int ReducedOmniMana = DotaMechanicsMapper.MapCooldownReductionToManaCost(OmnislashManaCost, OmnislashTalentCdr);

      var Tree = HeroTalentTree.CreateStandard(
        ClassName: JuggernautClassName,
        HeroDisplayName: "主宰 · 尤涅若",
        IconGlyph: IconGlyph,
        T1Left: new TalentNode(
          Id: TalentT1BladeformInterval,
          Title: "剑心犹在叠层间隔减半",
          Description: "4 回合 → 2 回合"),
        T1Right: new TalentNode(
          Id: TalentT1HealingWardMana,
          Title: "-20% 治疗守卫蓝耗",
          Description: $"蓝耗 {HealingWardManaCost} → {ReducedWardMana}",
          ManaReductions: new[] { new SpellManaReductionRule(HealingWardSpellName, HealingWardTalentCdr) }),
        T2Left: new TalentNode(
          Id: TalentT2BladeFurySpeed,
          Title: "+15% 剑刃风暴移速",
          Description: string.Empty),
        T2Right: new TalentNode(
          Id: TalentT2OmnislashMana,
          Title: "-12.5% 无敌斩蓝耗",
          Description: $"蓝耗 {OmnislashManaCost} → {ReducedOmniMana}",
          ManaReductions: new[] { new SpellManaReductionRule(OmnislashSpellName, OmnislashTalentCdr) }),
        T3Left: new TalentNode(
          Id: TalentT3BladeFuryDamage,
          Title: "+4 剑刃风暴基础伤害",
          Description: "受敏捷与剑心犹在攻速频率缩放"),
        T3Right: new TalentNode(
          Id: TalentT3BladeDanceCrit,
          Title: "+15% 剑舞暴击倍率",
          Description: "1.8× → 1.95×"),
        T4Left: new TalentNode(
          Id: TalentT4BladeDanceLifesteal,
          Title: "+40% 剑舞暴击吸血",
          Description: string.Empty),
        T4Right: new TalentNode(
          Id: TalentT4OmnislashSlashes,
          Title: "+3 无敌斩斩击次数",
          Description: "最高 15 斩"));

      TalentRegistry.Register(Ctx, Tree);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定主宰固定初始六维（STR 12 / DEX 17 / CON 12 / INT 9 / WIS 9 / CHA 10）与 1 级初始蓝量（12）
      DotaMechanicsMapper.RegisterFixedStartingStats(JuggernautClassName, StartingStats);

      // 0.1 注册 Pathos 原生选框扩距（无敌斩 4~7 格敌方目标选定；治疗守卫 3~5 格点地召唤）
      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: OmnislashSpellName,
        DisplayTitle: "无敌斩",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: Caster => GetOmnislashCastRange(Ctx, Caster),
        AllowSelf: false));

      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: HealingWardSpellName,
        DisplayTitle: "治疗守卫",
        Mode: DotaTargetMode.PointOpen,
        ResolveRange: Caster => GetHealingWardCastRange(Ctx, Caster),
        AllowSelf: true));

      // 0.2 注册「剑心犹在」左上角状态栏动态叠层显示（叠层 > 0 时展示层数，受击粘滞期以橙色高亮提示）
      DotaStatusBarBridge.RegisterProvider("juggernaut.bladeform", Character =>
      {
        if (!IsJuggernaut(Character) || Character.Deceased)
          return null;

        var State = GetBladeformState(Character);
        if (State.Stacks <= 0)
          return null;

        return new DotaStatusBadge
        {
          Key = "juggernaut.bladeform",
          Glyph = BladeformGlyph,
          Text = (BladeformTalentProperty != null ? PathosEngineBridge.GetLocalizedName(BladeformTalentProperty) : "剑心犹在").ToUpper(),
          Count = $"×{State.Stacks}",
          Colour = State.LingerTurns > 0 ? Colour.DarkOrange : Colour.Black,
          HelpProperty = BladeformTalentProperty
        };
      });

      // 1. 施法后置钩子：Q 剑刃风暴（驱散+立刻触发一次转伤）、W 治疗守卫（追踪/替换）、R 无敌斩（连续瞬移斩击）
      DotaHookBus.RegisterAfterCastSpell("juggernaut.spell_casts", Args =>
      {
        var SpellName = Args.Spell?.Name;
        if (string.IsNullOrEmpty(SpellName))
          return;

        if (string.Equals(SpellName, BladeFurySpellName, StringComparison.OrdinalIgnoreCase))
        {
          HandleBladeFuryCast(Ctx, Args);
        }
        else if (string.Equals(SpellName, HealingWardSpellName, StringComparison.OrdinalIgnoreCase))
        {
          HandleHealingWardCast(Ctx, Args);
        }
        else if (string.Equals(SpellName, OmnislashSpellName, StringComparison.OrdinalIgnoreCase))
        {
          HandleOmnislashCast(Ctx, Args);
        }
      });

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

      // 3. 状态免疫钩子：剑刃风暴与无敌斩持续期间提供减益免疫
      DotaHookBus.RegisterBeforeApplyTransient("juggernaut.blade_fury_debuff_immunity", Args =>
      {
        if (Args.Target == null || Args.Property == null || !IsNegativeDebuff(Ctx, Args.Property))
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

      // 5. 伤害前置钩子：无敌斩绝对无敌、剑刃风暴 80% 魔法/元素减伤与普攻置零、剑心犹在敏捷增伤、剑舞暴击
      DotaHookBus.RegisterBeforeDamage("juggernaut.damage_modifiers", Args =>
      {
        // (a) 无敌斩期间全程免疫一切伤害
        if (Args.Defender != null && IsInOmnislash(Args.Defender))
        {
          Args.Cancel = true;
          return;
        }

        // (b) 剑刃风暴期间受到非普通物理攻击（法术/元素/环境伤害）减免 80%
        if (BladeFuryStatusProperty != null &&
            Args.Defender != null &&
            PathosEngineBridge.HasTransient(Args.Defender, BladeFuryStatusProperty) &&
            Args.DamagePoints > 0 &&
            IsNonBasicPhysicalIncoming(Args))
        {
          int Reduced = (int)MathF.Floor(Args.DamagePoints * 0.20f);
          if (Reduced <= 0)
          {
            Args.Cancel = true;
            return;
          }
          Args.DamagePoints = Reduced;
        }

        var Attacker = Args.Attacker;
        if (Attacker == null || Args.Defender == Attacker || Args.DamagePoints <= 0)
          return;

        bool IsBasicAttack = DotaHookBus.ActiveAttack?.Character == Attacker;
        bool IsOmnislash = ActiveOmnislashCasters.Contains(Attacker);
        bool IsBladeFurySpin = ActiveBladeFuryTicks.Contains(Attacker);

        // (c) 剑刃风暴期间普通物理普攻不造成常规伤害
        if (BladeFuryStatusProperty != null &&
            PathosEngineBridge.HasTransient(Attacker, BladeFuryStatusProperty) &&
            IsBasicAttack &&
            !IsBladeFurySpin &&
            !IsOmnislash)
        {
          Args.Cancel = true;
          return;
        }

        if (!IsJuggernaut(Attacker))
          return;

        // (d) 先天「剑心犹在」：近战普攻与无敌斩按层数追加等效基础敏捷伤害（每层 +2.5% DEX）
        if (IsBasicAttack || IsOmnislash)
        {
          var State = GetBladeformState(Attacker);
          if (State.Stacks > 0)
          {
            int Dex = PathosEngineBridge.GetCurrentAbilityScore(Attacker, "dexterity");
            int BonusDmg = (int)MathF.Round(Dex * 0.025f * State.Stacks);
            if (BonusDmg > 0)
              Args.DamagePoints += BonusDmg;
          }
        }

        // (e) 被动「剑舞」：近战普攻、无敌斩与剑刃风暴均可触发 1.8x（T3 右天赋 1.95x）致命一击，T4 左天赋附带 40% 暴击吸血
        if (IsBasicAttack || IsOmnislash || IsBladeFurySpin)
        {
          int MasteryTier = GetSkillTier(Ctx, Attacker, Ctx.Skills.medium_blade);
          float CritChance = MasteryTier >= 5 ? 0.35f : 0.20f + (MasteryTier - 1) * 0.04f;
          if (Random.Shared.NextSingle() < CritChance)
          {
            float CritMultiplier = TalentService.HasTalent(Attacker, TalentT3BladeDanceCrit) ? 1.95f : 1.80f;
            Args.DamagePoints = Math.Max(Args.DamagePoints + 1, (int)MathF.Round(Args.DamagePoints * CritMultiplier));

            string LifestealNote = string.Empty;
            if (TalentService.HasTalent(Attacker, TalentT4BladeDanceLifesteal) &&
                Attacker.Life != null &&
                Attacker.Life.Points < Attacker.Life.Total)
            {
              int HealAmt = Math.Max(1, (int)MathF.Round(Args.DamagePoints * 0.40f));
              var SelfDir = Attacker.Square != null ? Attacker.Square.AsDirection(Attacker.Square) : default;
              PathosEngineBridge.HealCharacter(Args.Engine, Attacker, Attacker, SelfDir, HealAmt);
              LifestealNote = $"（吸血 +{HealAmt} HP）";
            }

            if (IsOmnislash)
            {
              LastOmnislashStrikeCritted = true;
            }
            else if (Args.Defender?.Square != null)
            {
              PathosEngineBridge.StoryStriking(Args.Engine, Delay.Zero, Args.Defender.Square, BladeDanceCritStrike ?? Ctx.Strikes.flame);
              PathosEngineBridge.StoryCaption(Args.Engine, Args.Defender.Square, Colour.OrangeRed, "暴击!", Delayed: false);
            }

            string SourceTag = IsOmnislash ? "（无敌斩）" : IsBladeFurySpin ? "（剑刃风暴）" : string.Empty;
            string TargetName = Args.Defender?.Entity != null
              ? PathosEngineBridge.GetLocalizedName(Args.Defender.Entity)
              : "目标";
            PathosEngineBridge.WriteInternalLog(
              Args.Engine,
              Attacker,
              BladeDanceGlyph,
              $"剑舞暴击{SourceTag}！对 {TargetName} 造成 {Args.DamagePoints} 点伤害{LifestealNote}！");
          }
        }
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
            "剑心犹在层数因受伤已消散。");
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
              "剑心犹在已叠满（10 层）：移速 +10%，敏捷攻速与伤害 +25%！");
          }
        }
      }
    }

    private void HandleBladeFuryCast(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster == null)
        return;

      // 基础驱散自身常见负面状态
      var Debuffs = new[]
      {
        Ctx.Properties.slowness,
        Ctx.Properties.stunned,
        Ctx.Properties.confusion,
        Ctx.Properties.blindness,
        Ctx.Properties.fear,
        Ctx.Properties.silence,
        Ctx.Properties.paralysis,
        Ctx.Properties.sleeping,
        Ctx.Properties.hallucination,
        Ctx.Properties.fumbling,
        Ctx.Properties.sickness,
        Ctx.Properties.petrifying,
        Ctx.Properties.narcolepsy,
        Ctx.Properties.fainting
      };

      foreach (var Debuff in Debuffs)
      {
        if (Debuff != null && PathosEngineBridge.HasTransient(Caster, Debuff))
          PathosEngineBridge.RemoveTransient(Args.Engine, Caster, Caster, Debuff);
      }

      // 施法当回合立即结算一次旋转伤害，并标记跳过同一指令的 Postfix 重复结算
      SkipNextBladeFuryTick.Add(Caster);
      TriggerBladeFuryPulse(Ctx, Args.Engine, Caster);
    }

    private void TriggerBladeFuryPulse(DotaHeroContext Ctx, object Engine, Character Caster)
    {
      if (Caster?.Square == null || Caster.Deceased)
        return;

      int Tier = GetSkillTier(Ctx, Caster, Ctx.Skills.evocation);
      int Dex = PathosEngineBridge.GetCurrentAbilityScore(Caster, "dexterity");
      int BladeformStacks = IsJuggernaut(Caster) ? GetBladeformState(Caster).Stacks : 0;

      // DOTA2 7.42 剑刃风暴：基础 DPS 85/115/145/175（Champion 205，T3 左天赋 +120 DPS）
      // 合并为每回合 1 次结算（0.5s），并按敏捷与剑心犹在攻速频率缩放（还原 7.36+/7.42 攻速缩短跳伤间隔）
      float BaseDps = BladeFuryDotaDps[Math.Clamp(Tier - 1, 0, BladeFuryDotaDps.Length - 1)];
      if (TalentService.HasTalent(Caster, TalentT3BladeFuryDamage))
        BaseDps += BladeFuryTalentBonusDotaDps;

      float CadenceMultiplier = 1.0f + Math.Max(0, Dex - 10) * 0.05f + BladeformStacks * 0.025f;
      Dice SpinDice = DotaMechanicsMapper.MapDotDamageDicePerTurn(BaseDps * CadenceMultiplier, DieFaces: 4);

      var Targets = new List<Character>();
      foreach (var Sq in Caster.Square.GetAroundSquares(1))
      {
        var Candidate = Sq?.Character;
        if (IsHostile(Caster, Candidate))
          Targets.Add(Candidate);
      }

      // 播放自定义 3×3 剑刃风暴金橙旋风贴图（Pathos 原生 Explosion 管线），精准标出 3×3 生效范围
      PathosEngineBridge.StoryExploding(Engine, Delay.Zero, Caster.Square, BladeFuryExplosion ?? Ctx.Explosions.fiery);

      if (Targets.Count == 0)
        return;

      ActiveBladeFuryTicks.Add(Caster);
      try
      {
        foreach (var Target in Targets)
        {
          if (Target.Deceased || Target.Square == null)
            continue;

          var Dir = Caster.Square.AsDirection(Target.Square);
          PathosEngineBridge.HarmCharacter(
            Engine,
            Caster,
            Target,
            Dir,
            Ctx.Elements.magical,
            SpinDice,
            Modifier.Zero,
            "blade fury");
        }
      }
      finally
      {
        ActiveBladeFuryTicks.Remove(Caster);
      }
    }

    private void HandleHealingWardCast(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster?.Square == null)
        return;

      int Tier = GetSkillTier(Ctx, Caster, Ctx.Skills.clerical);
      // 对应 DOTA2 百科：持续 18 / 20 / 22 / 24 秒（Champion 保持 24 秒并享受魔晶 6.5%/s 回复）
      float DurationSeconds = Tier switch
      {
        1 => 18.0f,
        2 => 20.0f,
        3 => 22.0f,
        _ => 24.0f
      };
      int Duration = DotaMechanicsMapper.MapDurationTurns(DurationSeconds); // 36 / 40 / 44 / 48 回合

      // 对应 DOTA2 每秒 2% / 3% / 4% / 5%（魔晶 6.5%），按 1 秒 = 2 回合折算为每回合回复比例
      float HealPerSec = Tier switch
      {
        1 => 0.02f,
        2 => 0.03f,
        3 => 0.04f,
        4 => 0.05f,
        _ => 0.065f
      };
      float HealPct = HealPerSec / DotaMechanicsMapper.TurnsPerSecond; // 1.0% / 1.5% / 2.0% / 2.5% / 3.25% 每回合

      // 1. 若之前已有存活的治疗守卫，先彻底消散旧守卫（上限同时存在 1 个，避免占用盟友上限或残留旧单位）
      if (OwnerActiveWard.TryGetValue(Caster, out var OldRef) &&
          OldRef.TryGetTarget(out var OldWard) &&
          OldWard != null &&
          !OldWard.Deceased)
      {
        PathosEngineBridge.DespawnSummon(Args.Engine, OldWard, "replaced");
      }

      // 2. 确定召唤落点：优先玩家通过全射程选格指示器指定的格子，否则回退至施法方向相邻空位或自身周围空位
      var PendingTarget = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      Square DesiredSq = null;
      if (PendingTarget?.TargetSquare != null && PendingTarget.TargetSquare.Map == Caster.Square.Map)
      {
        DesiredSq = PendingTarget.TargetSquare;
      }
      else if (Args.Direction != Direction.Self)
      {
        DesiredSq = Caster.Square.Adjacent(Args.Direction, 1) ?? Caster.Square;
      }
      else
      {
        DesiredSq = Caster.Square;
      }

      Square PlacementSq = null;
      if (DesiredSq != null && DesiredSq.Character == null && !DesiredSq.IsBlocked(Vision: false) && !DesiredSq.IsVoid())
      {
        PlacementSq = DesiredSq;
      }
      else if (DesiredSq != null)
      {
        PlacementSq = FindOpenAdjacentSquare(Caster, DesiredSq) ?? FindOpenAdjacentSquare(Caster, Caster.Square) ?? DesiredSq;
      }

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

        if (!IsFriendly(Ward, Ally))
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
        // Pathos 原生 2 格范围脉冲 + 自定义翡翠治疗光环贴图
        PathosEngineBridge.StoryDetecting(Engine, Ward.Square, 2, Colour.LimeGreen);
        foreach (var Sq in HealedSquares)
          PathosEngineBridge.StoryStriking(Engine, Delay.Zero, Sq, HealingWardPulseStrike ?? Ctx.Strikes.boost);
      }

      State.RemainingTurns--;
      if (State.RemainingTurns <= 0)
        PathosEngineBridge.DespawnSummon(Engine, Ward, "expired");
    }

    private void HandleOmnislashCast(DotaHeroContext Ctx, SpellCastEventArgs Args)
    {
      var Caster = Args.Character;
      if (Caster?.Square == null || Caster.Deceased)
        return;

      int Tier = GetSkillTier(Ctx, Caster, Ctx.Skills.transmutation);
      int CastRange = GetOmnislashCastRange(Ctx, Caster);

      // 优先使用 DOTA2 全射程选格指示器锁定的精确敌方目标（支持 360° 最大射程内任意敌人）
      Character InitialTarget = null;
      var PendingTarget = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
      if (PendingTarget != null)
      {
        if (IsHostile(Caster, PendingTarget.TargetCharacter) &&
            PendingTarget.TargetCharacter.Square != null &&
            Caster.Square.AsRange(PendingTarget.TargetCharacter.Square) <= CastRange)
        {
          InitialTarget = PendingTarget.TargetCharacter;
        }
        else if (PendingTarget.TargetSquare != null && PendingTarget.TargetSquare.Map == Caster.Square.Map)
        {
          // 若玩家点击了敌人身旁的格子，智能吸附该格周围 1 格内最近的敌方单位
          int BestDist = int.MaxValue;
          foreach (var Sq in PendingTarget.TargetSquare.GetAroundSquares(1))
          {
            var Cand = Sq?.Character;
            if (!IsHostile(Caster, Cand) || Caster.Square.AsRange(Sq) > CastRange)
              continue;
            int D = PendingTarget.TargetSquare.AsRange(Sq);
            if (D < BestDist)
            {
              BestDist = D;
              InitialTarget = Cand;
            }
          }
        }
      }

      if (InitialTarget == null)
        InitialTarget = FindInitialOmnislashTarget(Caster, Args.Direction, CastRange);

      if (InitialTarget == null)
      {
        // 空大保护：未找到目标时按实际折后蓝耗返还法力值
        if (Caster.Mana != null)
        {
          int ActualMana = TalentService.HasTalent(Caster, TalentT2OmnislashMana)
            ? DotaMechanicsMapper.MapCooldownReductionToManaCost(OmnislashManaCost, OmnislashTalentCdr)
            : OmnislashManaCost;
          int Refunded = Math.Min(Caster.Mana.Total, Caster.Mana.Points + ActualMana);
          PathosEngineBridge.SetMeterPoints(Caster.Mana, Refunded);
        }

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Caster,
          OmnislashGlyph,
          "无敌斩：射程内无敌方目标（已返还法力）。");
        return;
      }

      int Dex = PathosEngineBridge.GetCurrentAbilityScore(Caster, "dexterity");
      int Str = PathosEngineBridge.GetCurrentAbilityScore(Caster, "strength");
      int BaseSlashes = Math.Clamp(4 + Tier + Math.Max(0, (Dex - 14) / 6), 5, 12);
      int TotalSlashes = TalentService.HasTalent(Caster, TalentT4OmnislashSlashes)
        ? BaseSlashes + 3
        : BaseSlashes;

      var WeaponAsset = GetEquippedWeapon(Ctx, Caster);
      var WeaponDice = PathosEngineBridge.GetMeleeDamageDice(Args.Engine, Caster, WeaponAsset);
      int StatBonus = Math.Max(0, (Math.Max(Str, Dex) - 10) / 2);
      int OmniBonusDamage = DotaMechanicsMapper.MapSkillDamage(
        OmnislashBonusDotaDamage[Math.Clamp(Tier - 1, 0, OmnislashBonusDotaDamage.Length - 1)]);

      var Session = new OmnislashSession
      {
        RemainingSlashes = TotalSlashes,
        TotalSlashes = TotalSlashes,
        SlashesDone = 0,
        StatBonus = StatBonus,
        OmniBonusDamage = OmniBonusDamage,
        WeaponDice = WeaponDice
      };

      // 方案 A：当回合先瞬移斩出第 1 刀，剩余斩数自动推进回合逐斩跳跃（全程无敌、无需手动操作）
      ExecuteSingleOmnislashStep(Ctx, Args.Engine, Caster, Session, InitialTarget);

      if (Session.RemainingSlashes > 0 && !Caster.Deceased && PickRandomNearbyHostile(Caster, Radius: 3) != null)
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

      var NextTarget = PickRandomNearbyHostile(Caster, Radius: 3);
      if (NextTarget == null)
      {
        FinishOmnislashSession(Ctx, Engine, Caster, Session);
        return;
      }

      ExecuteSingleOmnislashStep(Ctx, Engine, Caster, Session, NextTarget);

      if (Session.RemainingSlashes > 0 && !Caster.Deceased && PickRandomNearbyHostile(Caster, Radius: 3) != null)
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
      var DestSquare = FindOpenAdjacentSquare(Caster, TargetSq);

      ActiveOmnislashCasters.Add(Caster);
      try
      {
        if (DestSquare != null && DestSquare != Caster.Square)
        {
          PlayOmnislashDashTrail(Engine, FromSq, DestSquare);
          PathosEngineBridge.TeleportCharacter(Engine, Caster, DestSquare, MasterMode: false);
        }
        else if (FromSq != TargetSq)
        {
          PlayOmnislashDashTrail(Engine, FromSq, TargetSq);
        }

        var ToSq = Caster.Square;
        int SlashDamage = RollDice(Session.WeaponDice) + Session.StatBonus + Session.OmniBonusDamage;
        var Dir = ToSq.AsDirection(TargetSq);

        LastOmnislashStrikeCritted = false;
        PathosEngineBridge.StrikeCharacter(
          Engine,
          Caster,
          Target,
          Dir,
          SlashDamage,
          Delay.Zero,
          "omnislash");

        // 播放 Pathos 原生挥砍动作 + 自定义无敌斩金白交叉斩击贴图（暴击时切换炽红新月暴击贴图）
        PathosEngineBridge.StoryAttacking(Engine, Delay.Zero, Caster, Dir);
        PathosEngineBridge.StoryStriking(
          Engine,
          Delay.Zero,
          TargetSq,
          LastOmnislashStrikeCritted
            ? (BladeDanceCritStrike ?? Ctx.Strikes.flame)
            : (OmnislashStrike ?? Ctx.Strikes.sever));

        if (LastOmnislashStrikeCritted)
          PathosEngineBridge.StoryCaption(Engine, TargetSq, Colour.OrangeRed, "暴击!", Delayed: false);

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
          $"无敌斩发动完毕，共斩出 {Session.SlashesDone} 次无敌斩击！");
      }
    }

    private void PlayOmnislashDashTrail(object Engine, Square FromSq, Square ToSq)
    {
      if (Engine == null || FromSq == null || ToSq == null || FromSq.Map != ToSq.Map || OmnislashDashBeam == null)
        return;

      var Map = FromSq.Map;
      var Dir = FromSq.AsDirection(ToSq);
      int X0 = FromSq.X, Y0 = FromSq.Y;
      int X1 = ToSq.X, Y1 = ToSq.Y;
      int Dx = Math.Abs(X1 - X0), Dy = Math.Abs(Y1 - Y0);
      int Sx = X0 < X1 ? 1 : -1, Sy = Y0 < Y1 ? 1 : -1;
      int Err = Dx - Dy;

      int Steps = 0;
      while (Steps++ < 12)
      {
        if (Map.IsValid(X0, Y0))
          PathosEngineBridge.StoryBeaming(Engine, Delay.Zero, Map[X0, Y0], Dir, OmnislashDashBeam);

        if (X0 == X1 && Y0 == Y1)
          break;

        int E2 = 2 * Err;
        if (E2 > -Dy) { Err -= Dy; X0 += Sx; }
        if (E2 < Dx) { Err += Dx; Y0 += Sy; }
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

    private static int GetOmnislashCastRange(DotaHeroContext Ctx, Character Caster)
    {
      int Tier = GetSkillTier(Ctx, Caster, Ctx.Skills.transmutation);
      return Tier switch
      {
        1 => 4,
        2 => 5,
        3 => 5,
        4 => 6,
        _ => 7
      };
    }

    private static int GetHealingWardCastRange(DotaHeroContext Ctx, Character Caster)
    {
      int Tier = GetSkillTier(Ctx, Caster, Ctx.Skills.clerical);
      return Tier switch
      {
        1 => 3,
        2 => 3,
        3 => 4,
        4 => 4,
        _ => 5
      };
    }

    private static Character FindInitialOmnislashTarget(Character Caster, Direction Direction, int Range)
    {
      // 1. 优先沿施法方向直线搜索首个敌方目标
      var Cursor = Caster.Square;
      for (int Step = 1; Step <= Range; Step++)
      {
        var Next = Cursor.Adjacent(Direction, 1);
        if (Next == null || Next == Cursor)
          break;

        if (IsHostile(Caster, Next.Character))
          return Next.Character;

        if (Next.IsBlocked(Vision: false))
          break;

        Cursor = Next;
      }

      // 2. 若直线未命中，则回退搜索射程内最近的敌方目标
      Character Nearest = null;
      int BestDist = int.MaxValue;
      foreach (var Sq in Caster.Square.GetAroundSquares(Range))
      {
        var Candidate = Sq?.Character;
        if (!IsHostile(Caster, Candidate))
          continue;

        int Dist = Caster.Square.AsRange(Sq);
        if (Dist < BestDist)
        {
          BestDist = Dist;
          Nearest = Candidate;
        }
      }

      return Nearest;
    }

    private static Character PickRandomNearbyHostile(Character Caster, int Radius)
    {
      var Candidates = new List<Character>();
      foreach (var Sq in Caster.Square.GetAroundSquares(Radius))
      {
        var Candidate = Sq?.Character;
        if (IsHostile(Caster, Candidate))
          Candidates.Add(Candidate);
      }

      if (Candidates.Count == 0)
        return null;

      return Candidates[Random.Shared.Next(Candidates.Count)];
    }

    private static Square FindOpenAdjacentSquare(Character Caster, Square TargetSquare)
    {
      if (TargetSquare == null)
        return null;

      if (Caster.Square != null && Caster.Square.IsAdjacent(TargetSquare, 1))
        return Caster.Square;

      var OpenSquares = new List<Square>();
      foreach (var Sq in TargetSquare.GetAroundSquares(1))
      {
        if (Sq != null && Sq.Character == null && !Sq.IsBlocked(Vision: false) && !Sq.IsVoid())
          OpenSquares.Add(Sq);
      }

      if (OpenSquares.Count == 0)
        return null;

      return OpenSquares[Random.Shared.Next(OpenSquares.Count)];
    }

    private static Asset GetEquippedWeapon(DotaHeroContext Ctx, Character Character)
    {
      if (Character?.Inventory == null)
        return null;

      foreach (var Asset in Character.Inventory.GetEquippedAssets())
      {
        if (Asset?.Item != null && Asset.Item.Stock == Ctx.Stocks.weapon)
          return Asset;
      }

      return null;
    }

    private static int RollDice(Dice D)
    {
      int Total = D.Bonus;
      if (D.Number > 0 && D.Base > 0)
      {
        for (int i = 0; i < D.Number; i++)
          Total += Random.Shared.Next(1, D.Base + 1);
      }
      return Math.Max(1, Total);
    }

    private static BladeformState GetBladeformState(Character Character)
    {
      return BladeformStates.GetValue(Character, _ => new BladeformState());
    }

    private static bool IsJuggernaut(Character Character)
    {
      return Character?.Class != null &&
             string.Equals(Character.Class.Name, JuggernautClassName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHealingWard(Character Character)
    {
      return Character?.Entity != null &&
             string.Equals(Character.Entity.Name, HealingWardEntityName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHostile(Character Source, Character Target)
    {
      if (Source == null || Target == null || Target == Source || Target.Deceased)
        return false;
      if (Target.Life != null && Target.Life.Points <= 0)
        return false;
      if (Target.Neutral || PathosEngineBridge.IsAllied(Source, Target))
        return false;
      return true;
    }

    private static bool IsFriendly(Character Source, Character Target)
    {
      if (Source == null || Target == null || Target.Deceased)
        return false;
      if (Target.Life != null && Target.Life.Points <= 0)
        return false;
      return PathosEngineBridge.IsAllied(Source, Target);
    }

    private static bool IsNonBasicPhysicalIncoming(DamageEventArgs Args)
    {
      if (DotaHookBus.ActiveSpellCast != null)
        return true;

      // 非普通攻击来源（如元素喷吐、魔法陷阱、范围法术等）视作魔法/元素减伤范围
      if (DotaHookBus.ActiveAttack == null)
        return true;

      return false;
    }

    private static bool IsNegativeDebuff(DotaHeroContext Ctx, Property Property)
    {
      return Property == Ctx.Properties.slowness ||
             Property == Ctx.Properties.stunned ||
             Property == Ctx.Properties.confusion ||
             Property == Ctx.Properties.blindness ||
             Property == Ctx.Properties.fear ||
             Property == Ctx.Properties.silence ||
             Property == Ctx.Properties.paralysis ||
             Property == Ctx.Properties.sleeping ||
             Property == Ctx.Properties.hallucination ||
             Property == Ctx.Properties.fumbling ||
             Property == Ctx.Properties.sickness ||
             Property == Ctx.Properties.petrifying ||
             Property == Ctx.Properties.narcolepsy ||
             Property == Ctx.Properties.fainting;
    }

    private static int GetSkillTier(DotaHeroContext Ctx, Character Character, Skill Skill)
    {
      if (Character == null || Skill == null)
        return 1;

      var Qual = PathosEngineBridge.GetQualification(Character, Skill);
      if (Qual == Ctx.Qualifications.champion) return 5;
      if (Qual == Ctx.Qualifications.master) return 4;
      if (Qual == Ctx.Qualifications.expert) return 3;
      if (Qual == Ctx.Qualifications.specialist) return 2;
      return 1;
    }

    private static Property RegisterBladeformTalent(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Properties.Add(P =>
      {
        P.Name = "bladeform";
        P.Description = "Innate (Bladeform): Gain 1 stack every 4 turns without taking damage (up to 10 stacks). Each stack grants +1% movement speed and +2.5% agility strike power and attack speed; lingers 4 turns after taking damage.";
        P.Glyph = Icon;
      });
    }

    private static Property RegisterBladeDanceTalent(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Properties.Add(P =>
      {
        P.Name = "blade dance";
        P.Description = "Passive (Blade Dance): Melee attacks, Omnislash strikes, and Blade Fury spins have a 20% to 35% chance (by Medium Blade mastery) to critically strike for 1.8x damage.";
        P.Glyph = Icon;
      });
    }

    private static Property RegisterBladeFuryStatus(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Properties.Add(P =>
      {
        P.Name = "blade fury";
        P.Description = "Spinning in a destructive storm of blades: immune to debuffs, 80% magical damage reduction, and dealing magical damage around you each turn.";
        P.Glyph = Icon;
      });
    }

    private static Property RegisterOmnislashStatus(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Properties.Add(P =>
      {
        P.Name = "omnislash";
        P.Description = "Leaping between nearby enemies in a blur of invulnerable strikes: immune to all damage and debuffs while slashing a nearby foe each turn.";
        P.Glyph = Icon;
      });
    }

    private static Explosion RegisterBladeFuryExplosion(DotaHeroContext Ctx)
    {
      var Tl = Ctx.RegisterGlyph("blade fury fx tl");
      var Tc = Ctx.RegisterGlyph("blade fury fx tc");
      var Tr = Ctx.RegisterGlyph("blade fury fx tr");
      var Ml = Ctx.RegisterGlyph("blade fury fx ml");
      var Mc = Ctx.RegisterGlyph("blade fury fx mc");
      var Mr = Ctx.RegisterGlyph("blade fury fx mr");
      var Bl = Ctx.RegisterGlyph("blade fury fx bl");
      var Bc = Ctx.RegisterGlyph("blade fury fx bc");
      var Br = Ctx.RegisterGlyph("blade fury fx br");

      return Ctx.Explosions.Add(E =>
      {
        E.Name = "blade fury spin";
        E.SetGlyph(ExplosionArea.TopLeft, Tl);
        E.SetGlyph(ExplosionArea.TopCenter, Tc);
        E.SetGlyph(ExplosionArea.TopRight, Tr);
        E.SetGlyph(ExplosionArea.MiddleLeft, Ml);
        E.SetGlyph(ExplosionArea.MiddleCenter, Mc);
        E.SetGlyph(ExplosionArea.MiddleRight, Mr);
        E.SetGlyph(ExplosionArea.BottomLeft, Bl);
        E.SetGlyph(ExplosionArea.BottomCenter, Bc);
        E.SetGlyph(ExplosionArea.BottomRight, Br);
      });
    }

    private static Strike RegisterCustomStrike(DotaHeroContext Ctx, string Name, string GlyphName)
    {
      var Glyph = Ctx.RegisterGlyph(GlyphName);
      return Ctx.Strikes.Add(S =>
      {
        S.Name = Name;
        S.Glyph = Glyph;
      });
    }

    private static Beam RegisterOmnislashDashBeam(DotaHeroContext Ctx)
    {
      var H = Ctx.RegisterGlyph("omnislash fx beam h");
      var V = Ctx.RegisterGlyph("omnislash fx beam v");
      var F = Ctx.RegisterGlyph("omnislash fx beam f");
      var B = Ctx.RegisterGlyph("omnislash fx beam b");

      return Ctx.Beams.Add(Bm =>
      {
        Bm.Name = "omnislash dash";
        Bm.HorizontalGlyph = H;
        Bm.VerticalGlyph = V;
        Bm.ForwardSlantGlyph = F;
        Bm.BackwardSlantGlyph = B;
      });
    }

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
        E.Sonic = Ctx.Sonics.chime;

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

    private static Spell RegisterBladeFurySpell(DotaHeroContext Ctx, Glyph Icon, Property BladeFuryStatus)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = BladeFurySpellName;
        S.Description = "Dispel debuffs and spin for 10 to 12 turns: debuff immunity, 80% magical DR, and 1d4 to 2d4+2 base magical damage per turn (85-205 DPS mapped via K_dmg=15, scaled by DEX & Bladeform attack speed) to adjacent 3x3 foes; can crit with Blade Dance.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = DotaMechanicsMapper.MapSpellLevel(BladeFuryManaCost);
        S.Mana = BladeFuryManaCost;

        void Configure(Qualification Qual, int Turns)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.ApplyTransient(BladeFuryStatus, Dice.Fixed(Turns));
        }

        // 对应 DOTA2 剑刃风暴全等级 5 秒（Champion 对应 +1 秒天赋 -> 6 秒）
        Configure(Ctx.Qualifications.proficient, DotaMechanicsMapper.MapDurationTurns(5.0f)); // 10
        Configure(Ctx.Qualifications.specialist, DotaMechanicsMapper.MapDurationTurns(5.0f)); // 10
        Configure(Ctx.Qualifications.expert, DotaMechanicsMapper.MapDurationTurns(5.0f));     // 10
        Configure(Ctx.Qualifications.master, DotaMechanicsMapper.MapDurationTurns(5.0f));     // 10
        Configure(Ctx.Qualifications.champion, DotaMechanicsMapper.MapDurationTurns(6.0f));   // 12
      });
    }

    private static Spell RegisterHealingWardSpell(DotaHeroContext Ctx, Glyph Icon, Entity HealingWardEntity)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = HealingWardSpellName;
        S.Description = "Summon a fragile movable Healing Ward (1 HP, max 1 active) for 36 to 48 turns that heals all allies within 2 tiles for 1% to 3.25% of their maximum HP each turn.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.clerical;
        S.Precept = new Precept(Purpose.Healing);
        S.Level = DotaMechanicsMapper.MapSpellLevel(HealingWardManaCost);
        S.Mana = HealingWardManaCost;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range)).SetTargetSelf(true);
          // 声明式保留射程外壳以触发全射程选格指示器，实际唯一召唤由 HandleHealingWardCast 精确执行（避免 Plain 射线每格重复召唤）
          Adept.Apply.Nothing();
        }

        Configure(Ctx.Qualifications.proficient, 3);
        Configure(Ctx.Qualifications.specialist, 3);
        Configure(Ctx.Qualifications.expert, 4);
        Configure(Ctx.Qualifications.master, 4);
        Configure(Ctx.Qualifications.champion, 5);
      });
    }

    private static Spell RegisterOmnislashSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = OmnislashSpellName;
        S.Description = "Leap to a targeted enemy and unleash 5 to 12 rapid invulnerable weapon slashes (weapon + stat + 2 to 3 bonus damage per slash) across nearby foes, triggering Blade Dance crits and Bladeform bonuses.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Teleport);
        S.Level = DotaMechanicsMapper.MapSpellLevel(OmnislashManaCost);
        S.Mana = OmnislashManaCost;

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
        C.Name = JuggernautClassName;
        C.Description = "A masked DOTA2 blademaster who builds unstoppable momentum through Bladeform, carves through magic with Blade Fury, sustains allies with Healing Ward, and cuts down foes with Blade Dance and Omnislash.";
        C.Backpack = Ctx.Items.Backpack;
        C.LifeAdvancement.Set(14, 1.d10());
        int ClassManaBase = DotaMechanicsMapper.ComputeClassManaBase(DotaBaseInt, RaceManaBase: 1, StartingStats.Wis);
        C.ManaAdvancement.Set(ClassManaBase, 1.d2());

        C.SetDistribution(
          Ctx.Attributes.dexterity,
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.charisma);

        Ctx.AddClassAvatars(C, AvatarGlyph);

        C.AddFeat(1, BladeformTalent);
        C.AddFeat(1, BladeDanceTalent);
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

        C.Startup.AddGrimoire(Dice.One, BladeFurySpell);
        C.Startup.AddGrimoire(Dice.One, HealingWardSpell);
        C.Startup.AddGrimoire(Dice.One, OmnislashSpell);

        C.Startup.Loot.AddKit(Chance.Always, Modifier.Plus1, Ctx.Items.katana);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.leather_armour);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_healing);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.food_ration);
      });
    }
  }
}
