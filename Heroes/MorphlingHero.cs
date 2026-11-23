using System;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Heroes
{
  /// <summary>
  /// DOTA2 英雄：变体精灵（Morphling / 水人）
  /// 包含：
  /// - 专属种族：变体水灵（waterkin，遇水回血回蓝、遇火蒸发、宽裕力/体/敏转换上限）
  /// - 专属职业：变体精灵（morphling）
  /// - 专属技能（还原 DOTA2 现行版本核心机制）：
  ///   - Q：波浪形态（waveform）— 闪烁突进 + 落地水波 AOE 伤害 + 短暂穿墙与闪避位移
  ///   - W1：变体攻击·敏捷（adaptive strike agility）— 高额水系爆发伤害
  ///   - W2：变体攻击·力量（adaptive strike strength）— 必中击退 + 稳定长眩晕控制
  ///   - E1：属性转换·敏捷（morph agility）— 大幅将 STR + CON 转为 DEX（总量守恒 + 迅捷）
  ///   - E2：属性转换·力量（morph strength）— 大幅将 DEX 转为 STR + CON（总量守恒 + 强力回血与生命恢复）
  ///   - R1：变形（morph）— 指向敌人直接化身目标（复制外形、属性、技能与法术）
  ///   - R2：解除变形（morph revert）— 随时切回水人本体
  /// - 预设英雄：Morphling（选人界面一键开局）
  /// </summary>
  public sealed class MorphlingHero : IDotaHeroDefinition
  {
    public string Id => "morphling";

    public void Register(DotaHeroContext Ctx)
    {
      // 1. 注册贴图（对应 Atlases/<tileset>/*.png）
      var MorphlingGlyph = Ctx.RegisterGlyph("morphling");
      var WaveformGlyph = Ctx.RegisterGlyph("waveform");
      var AdaptiveStrikeGlyph = Ctx.RegisterGlyph("adaptive strike");
      var MorphAgilityGlyph = Ctx.RegisterGlyph("morph agility");
      var MorphStrengthGlyph = Ctx.RegisterGlyph("morph strength");
      var TidalOrbGlyph = Ctx.RegisterGlyph("tidal orb");

      // 2. 注册水人「潮涨潮落」五级双向刻度特质（随变化系熟练度解锁 ±1 ~ ±5 档，防溢出且变形期间持续生效）
      // 状态轴（共 11 态，索引 0..10，索引 5 为 0 档均衡态）：
      // [0] flow V (-5) .. [4] flow I (-1) .. [5] null (0) .. [6] ebb I (+1) .. [10] ebb V (+5)
      var EbbAndFlowAxis = new Property[]
      {
        RegisterEbbAndFlowProperty(Ctx, "flow V",
          "Ebb & Flow (Strength V): Immune to slowness, avoids grappling, and permanent doubled life recovery.",
          MorphStrengthGlyph, AvoidGrappling: true, SlowImmune: true, LifeRecoveryChance: Chance.Always, LifeRecoveryDoubling: true),
        RegisterEbbAndFlowProperty(Ctx, "flow IV",
          "Ebb & Flow (Strength IV): Immune to slowness, avoids grappling, and strong permanent life recovery.",
          MorphStrengthGlyph, AvoidGrappling: true, SlowImmune: true, LifeRecoveryChance: Chance.OneIn2),
        RegisterEbbAndFlowProperty(Ctx, "flow III",
          "Ebb & Flow (Strength III): Immune to slowness, avoids grappling, and improved permanent life recovery.",
          MorphStrengthGlyph, AvoidGrappling: true, SlowImmune: true, LifeRecoveryChance: Chance.OneIn3),
        RegisterEbbAndFlowProperty(Ctx, "flow II",
          "Ebb & Flow (Strength II): Avoids grappling and steady permanent life recovery.",
          MorphStrengthGlyph, AvoidGrappling: true, LifeRecoveryChance: Chance.OneIn4),
        RegisterEbbAndFlowProperty(Ctx, "flow I",
          "Ebb & Flow (Strength I): Avoids grappling and mild permanent life recovery.",
          MorphStrengthGlyph, AvoidGrappling: true, LifeRecoveryChance: Chance.OneIn5),
        null, // 0 档：均衡态，不挂多余特质
        RegisterEbbAndFlowProperty(Ctx, "ebb I",
          "Ebb & Flow (Agility I): +10% movement speed and slight dodge chance.",
          MorphAgilityGlyph, SpeedMultiplier: 1.10f, DodgeChance: Chance.OneIn12),
        RegisterEbbAndFlowProperty(Ctx, "ebb II",
          "Ebb & Flow (Agility II): +20% movement speed and improved dodge chance.",
          MorphAgilityGlyph, SpeedMultiplier: 1.20f, DodgeChance: Chance.OneIn10),
        RegisterEbbAndFlowProperty(Ctx, "ebb III",
          "Ebb & Flow (Agility III): +30% movement speed and agile dodge chance.",
          MorphAgilityGlyph, SpeedMultiplier: 1.30f, DodgeChance: Chance.OneIn8),
        RegisterEbbAndFlowProperty(Ctx, "ebb IV",
          "Ebb & Flow (Agility IV): +40% movement speed and high dodge chance.",
          MorphAgilityGlyph, SpeedMultiplier: 1.40f, DodgeChance: Chance.OneIn6),
        RegisterEbbAndFlowProperty(Ctx, "ebb V",
          "Ebb & Flow (Agility V): +50% movement speed (permanent quickness) and peak dodge chance.",
          MorphAgilityGlyph, SpeedMultiplier: 1.50f, DodgeChance: Chance.OneIn5),
      };

      // 3. 注册水人专属无限远程普攻：潮汐水弹（tidal orb）
      var TidalOrbItem = RegisterTidalOrbItem(Ctx, TidalOrbGlyph);

      // 4. 注册水人全套专属技能（Q / W1 / W2 / E1 / E2 / R1 / R2）
      var WaveformSpell = RegisterWaveformSpell(Ctx, WaveformGlyph);
      var AdaptiveStrikeAgilitySpell = RegisterAdaptiveStrikeAgilitySpell(Ctx, AdaptiveStrikeGlyph);
      var AdaptiveStrikeStrengthSpell = RegisterAdaptiveStrikeStrengthSpell(Ctx, AdaptiveStrikeGlyph);
      var MorphAgilitySpell = RegisterMorphAgilitySpell(Ctx, MorphAgilityGlyph, EbbAndFlowAxis);
      var MorphStrengthSpell = RegisterMorphStrengthSpell(Ctx, MorphStrengthGlyph, EbbAndFlowAxis);
      var MorphSpell = RegisterMorphSpell(Ctx, MorphlingGlyph);
      var MorphRevertSpell = RegisterMorphRevertSpell(Ctx, MorphlingGlyph);

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
        AdaptiveStrikeAgilitySpell,
        AdaptiveStrikeStrengthSpell,
        MorphAgilitySpell,
        MorphStrengthSpell,
        MorphSpell,
        MorphRevertSpell);

      // 7. 注册预设英雄：Morphling（可在选人界面一键选中）
      Ctx.Heroes.Add(H =>
      {
        H.Name = "Morphling";
        H.Race = WaterkinRace;
        H.Class = MorphlingClass;
        H.Gender = Ctx.Genders.male;
        H.CustomGlyph = MorphlingGlyph;
      });
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 水人属性转换后同步 MaximumForm，防止复活或恢复属性药水把已转走的属性回满
      DotaHookBus.RegisterAfterCastSpell("morphling.sync_max_form", Args =>
      {
        var SpellName = Args.Spell?.Name;
        if (!string.Equals(SpellName, "morph agility", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(SpellName, "morph strength", StringComparison.OrdinalIgnoreCase))
          return;

        PathosEngineBridge.SyncMaximumFormScores(Args.Character, "strength", "dexterity", "constitution");
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
      // 水药水（potion_of_water）：水元素饮用（drink）直接吸收水分，回复生命与法力（水人的大药+小蓝）
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
        S.Level = 1;
        S.Mana = 6;

        void Configure(Qualification Qual, int Range, Dice WaveDamage, int WaveRadius, int EvasionTurns)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.BlinkEntity();
          // 落地周围释放水波 AOE 伤害（变体水灵自身水抗且遇水回血，不会伤到自己）
          Adept.Apply.Shout(Dice.Fixed(WaveRadius), Wave => Wave.HarmEntity(Ctx.Elements.water, WaveDamage));
          // 短暂穿墙 + 幻影闪避，模拟波浪形态的无敌突进与逃生手感
          Adept.Apply.ApplyTransient(Ctx.Properties.phasing, Dice.Fixed(EvasionTurns));
          Adept.Apply.ApplyTransient(Ctx.Properties.displacement, Dice.Fixed(EvasionTurns));
        }

        Configure(Ctx.Qualifications.proficient, Range: 5, WaveDamage: 2.d4(), WaveRadius: 2, EvasionTurns: 2);
        Configure(Ctx.Qualifications.specialist, Range: 6, WaveDamage: 2.d6(), WaveRadius: 2, EvasionTurns: 2);
        Configure(Ctx.Qualifications.expert, Range: 7, WaveDamage: 3.d6(), WaveRadius: 2, EvasionTurns: 3);
        Configure(Ctx.Qualifications.master, Range: 9, WaveDamage: 4.d6(), WaveRadius: 3, EvasionTurns: 3);
        Configure(Ctx.Qualifications.champion, Range: 11, WaveDamage: 5.d6(), WaveRadius: 3, EvasionTurns: 4);
      });
    }

    private static Spell RegisterAdaptiveStrikeAgilitySpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "adaptive strike agility";
        S.Description = "Unleash a razor-sharp surge of water focused on agility, dealing heavy burst water damage to enemies.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.water);
        S.Level = 1;
        S.Mana = 5;

        void Configure(Qualification Qual, int Range, Dice Damage)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Explosion(Ctx.Explosions.watery, Dice.Fixed(Range));
          Adept.Apply.HarmEntity(Ctx.Elements.water, Damage);
        }

        Configure(Ctx.Qualifications.proficient, 5, 3.d6());
        Configure(Ctx.Qualifications.specialist, 6, 4.d6());
        Configure(Ctx.Qualifications.expert, 7, 5.d6());
        Configure(Ctx.Qualifications.master, 8, 6.d6());
        Configure(Ctx.Qualifications.champion, 9, 8.d6());
      });
    }

    private static Spell RegisterAdaptiveStrikeStrengthSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "adaptive strike strength";
        S.Description = "Crush foes with a heavy torrent of water focused on strength, dealing light damage while reliably knocking them back and stunning them.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.water);
        S.Level = 1;
        S.Mana = 5;

        void Configure(Qualification Qual, int Range, Dice Damage, Dice StunDuration)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Explosion(Ctx.Explosions.watery, Dice.Fixed(Range));
          Adept.Apply.HarmEntity(Ctx.Elements.water, Damage);
          Adept.Apply.Knockback();
          Adept.Apply.ApplyTransient(Ctx.Properties.stunned, StunDuration);
        }

        Configure(Ctx.Qualifications.proficient, 5, 1.d4(), 1.d3());
        Configure(Ctx.Qualifications.specialist, 6, 1.d6(), 1.d4());
        Configure(Ctx.Qualifications.expert, 7, 2.d4(), 2.d3());
        Configure(Ctx.Qualifications.master, 8, 2.d6(), 2.d4());
        Configure(Ctx.Qualifications.champion, 9, 3.d6(), 3.d4());
      });
    }

    private static Property RegisterEbbAndFlowProperty(
      DotaHeroContext Ctx,
      string Name,
      string Description,
      Glyph Icon,
      float SpeedMultiplier = 1.0f,
      Chance? DodgeChance = null,
      bool AvoidGrappling = false,
      bool SlowImmune = false,
      Chance? LifeRecoveryChance = null,
      bool LifeRecoveryDoubling = false)
    {
      return Ctx.Properties.Add(P =>
      {
        P.Name = Name;
        P.Description = Description;
        P.Glyph = Icon;
        if (SpeedMultiplier != 1.0f)
          P.SpeedMultiplier = SpeedMultiplier;
        if (DodgeChance.HasValue)
          P.DodgeChance = DodgeChance.Value;
        if (AvoidGrappling)
          P.AvoidGrappling = true;
        if (SlowImmune)
          P.SetImmunityProperty(Ctx.Properties.slowness);
        if (LifeRecoveryChance.HasValue)
          P.LifeRecoveryChance = LifeRecoveryChance.Value;
        if (LifeRecoveryDoubling)
          P.LifeRecoveryDoubling = true;
      });
    }

    /// <summary>
    /// 在 11 态「潮涨潮落」刻度轴（索引 0..10，中心 5 为均衡态）上推进被动档位，同时始终执行属性转换与附加效果。
    /// - 不再锁死施法次数：到达 潮落 V / 潮涨 V 后被动保持满档，玩家仍可继续自由转换属性，风险与收益全由玩家自行掌控。
    /// </summary>
    private static void BuildShiftChain(
      ApplyEditor Apply,
      Property[] Axis,
      int Direction, // +1 = 转敏, -1 = 转力
      int CheckPos,
      Action<ApplyEditor> ApplyAttributeShift,
      Action<ApplyEditor> ApplyBonus)
    {
      // 依次跳过均衡态（Axis[5] == null），遍历所有 10 个非空刻度特质
      while (CheckPos < Axis.Length && Axis[CheckPos] == null)
        CheckPos++;

      if (CheckPos >= Axis.Length)
      {
        // 所有刻度特质均未命中 -> 当前处于中心均衡态（索引 5）
        const int Center = 5;
        int NextIndex = Center + Direction;
        if (Axis[NextIndex] != null)
          Apply.GainTalent(Axis[NextIndex]);
        ApplyAttributeShift(Apply);
        ApplyBonus(Apply);
        return;
      }

      var CurrentProp = Axis[CheckPos];
      int CurrentIndex = CheckPos;
      int LimitIndex = Direction > 0 ? Axis.Length - 1 : 0;
      bool AtEdge = Direction > 0 ? CurrentIndex >= LimitIndex : CurrentIndex <= LimitIndex;

      Apply.WhenTargetHasProperty(
        CurrentProp,
        Then =>
        {
          if (!AtEdge)
          {
            Then.LoseTalent(CurrentProp);
            int NextIndex = CurrentIndex + Direction;
            if (Axis[NextIndex] != null)
              Then.GainTalent(Axis[NextIndex]);
          }
          // 无论被动是否已满 V 档，均允许继续自由转换属性
          ApplyAttributeShift(Then);
          ApplyBonus(Then);
        },
        Else =>
        {
          BuildShiftChain(Else, Axis, Direction, CheckPos + 1, ApplyAttributeShift, ApplyBonus);
        });
    }

    private static Spell RegisterMorphAgilitySpell(DotaHeroContext Ctx, Glyph Icon, Property[] EbbAndFlowAxis)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph agility";
        S.Description = "Shift toward agility (converting 1 to 5 STR & CON into DEX per cast by mastery), freely repeatable and empowering permanent Ebb speed and dodge.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = 1;
        S.Mana = 2;

        // 熟练度决定每次转换的「量」（1 / 2 / 3 / 4 / 5），可自由多次转换
        void Configure(Qualification Qual, int Amount)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          BuildShiftChain(
            Adept.Apply,
            EbbAndFlowAxis,
            Direction: +1,
            CheckPos: 0,
            A =>
            {
              A.IncreaseAbility(Ctx.Attributes.dexterity, Dice.Fixed(Amount));
              A.DecreaseAbility(Ctx.Attributes.strength, Dice.Fixed(Amount));
              A.DecreaseAbility(Ctx.Attributes.constitution, Dice.Fixed(Amount));
            },
            Bonus => Bonus.Nothing());
        }

        Configure(Ctx.Qualifications.proficient, Amount: 1);
        Configure(Ctx.Qualifications.specialist, Amount: 2);
        Configure(Ctx.Qualifications.expert, Amount: 3);
        Configure(Ctx.Qualifications.master, Amount: 4);
        Configure(Ctx.Qualifications.champion, Amount: 5);
      });
    }

    private static Spell RegisterMorphStrengthSpell(DotaHeroContext Ctx, Glyph Icon, Property[] EbbAndFlowAxis)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph strength";
        S.Description = "Shift toward strength (converting 1 to 5 DEX into STR & CON per cast by mastery), freely repeatable while empowering permanent Flow slow resistance and life recovery and healing immediately.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Healing);
        S.Level = 1;
        S.Mana = 2;

        // 熟练度决定每次转换的「量」（1 / 2 / 3 / 4 / 5）与回血量，可自由多次转换
        void Configure(Qualification Qual, int Amount, Dice HealDice)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          BuildShiftChain(
            Adept.Apply,
            EbbAndFlowAxis,
            Direction: -1,
            CheckPos: 0,
            A =>
            {
              A.IncreaseAbility(Ctx.Attributes.strength, Dice.Fixed(Amount));
              A.IncreaseAbility(Ctx.Attributes.constitution, Dice.Fixed(Amount));
              A.DecreaseAbility(Ctx.Attributes.dexterity, Dice.Fixed(Amount));
            },
            Bonus => Bonus.HealEntity(HealDice, Modifier.Zero));
        }

        Configure(Ctx.Qualifications.proficient, Amount: 1, HealDice: 2.d4());
        Configure(Ctx.Qualifications.specialist, Amount: 2, HealDice: 2.d6());
        Configure(Ctx.Qualifications.expert, Amount: 3, HealDice: 3.d6());
        Configure(Ctx.Qualifications.master, Amount: 4, HealDice: 4.d6());
        Configure(Ctx.Qualifications.champion, Amount: 5, HealDice: 5.d6());
      });
    }

    private static Spell RegisterMorphSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph";
        S.Description = "Assume the form of a targeted creature, copying its body, attributes, skills, and spells.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = 3;
        S.Mana = 10;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range));
          // Pathos 引擎原生指向性模仿变形：直接复制目标形态、血蓝、六维、技能与法术
          Adept.Apply.ImpersonateEntity();
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
        S.Level = 1;
        S.Mana = 2;

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
      // 先创建种族对应的基础实体（BaseEntity）
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

        // 水元素形态但保留手脚与思维，以便正常穿戴武器装备和施法
        E.Figure.Material = Ctx.Materials.water;
        E.Figure.CombatSkill = Ctx.Skills.unarmed_combat;
        E.Figure.Set(Ctx.Anatomies.amorphous);
        E.Figure.Set(Ctx.Anatomies.eyes);
        E.Figure.Set(Ctx.Anatomies.hands);
        E.Figure.Set(Ctx.Anatomies.limbs);
        E.Figure.Set(Ctx.Anatomies.mind);
        E.Figure.Set(Ctx.Anatomies.voice);

        // 初始六维与上限（贴合官方种族 16~24 的常规上限，略微压缩智/感/魅，为力/体/敏转换留出 26/30/26 的合理空间）
        Ctx.SetFormScores(E.DefaultForm, Str: 10, Dex: 14, Con: 10, Int: 11, Wis: 10, Cha: 9);
        Ctx.SetFormScores(E.LimitForm, Str: 26, Dex: 30, Con: 26, Int: 18, Wis: 16, Cha: 14);

        E.LifeAdvancement.Set(0, Dice.Zero);
        E.ManaAdvancement.Set(2, Dice.Fixed(2));

        // 弱点：电系（符合水元素设定与平衡性）
        E.Chemistry.SetWeakness(Ctx.Elements.shock);

        // 天生抗性与特质
        E.Startup.SetResistance(Ctx.Elements.water, Ctx.Elements.cold);
        E.Startup.SetTalent(Ctx.Properties.slippery);
        E.Startup.SetSkill(Ctx.Qualifications.proficient, Ctx.Skills.swimming, Ctx.Skills.unarmed_combat);

        // 徒手攻击：水流拍击
        E.AddAttack(Ctx.AttackTypes.splash, Ctx.Elements.water, 1.d6(), _ => { });

        // 元素生态反应：
        // - 遇水（喷水陷阱 / 水系敌人 / 水系法术）：吸收水分回血 + 回蓝
        E.AddReaction(Chance.Always, Ctx.Elements.water, A =>
        {
          A.HealEntity(1.d6(), Modifier.Zero);
          A.EnergiseEntity(1.d6(), Modifier.Zero);
        });
        // - 遇火：50% 几率蒸发少量法力
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

      // 双向绑定：玩家种族的 BaseEntity.Race 必须回指该 Race
      Ctx.Entities.Edit(WaterkinEntity).Race = WaterkinRace;

      return WaterkinRace;
    }

    private static Class RegisterMorphlingClass(
      DotaHeroContext Ctx,
      Glyph AvatarGlyph,
      Item TidalOrbItem,
      Spell WaveformSpell,
      Spell AdaptiveStrikeAgilitySpell,
      Spell AdaptiveStrikeStrengthSpell,
      Spell MorphAgilitySpell,
      Spell MorphStrengthSpell,
      Spell MorphSpell,
      Spell MorphRevertSpell)
    {
      return Ctx.Classes.Add(C =>
      {
        C.Name = "morphling";
        C.Description = "A fluid DOTA2 hero who commands surging waves, shifts freely between Strength and Agility, and morphs into foes.";
        C.Backpack = Ctx.Items.Backpack;
        C.LifeAdvancement.Set(12, 1.d8());
        C.ManaAdvancement.Set(14, 1.d6());

        // 属性分配优先级：敏捷 > 智力 > 力量 > 体质 > 感知 > 魅力
        C.SetDistribution(
          Ctx.Attributes.dexterity,
          Ctx.Attributes.intelligence,
          Ctx.Attributes.strength,
          Ctx.Attributes.constitution,
          Ctx.Attributes.wisdom,
          Ctx.Attributes.charisma);

        Ctx.AddClassAvatars(C, AvatarGlyph);

        // 等级专长（Feats）
        // Lv 4: 水系抗性
        C.AddFeat(4, Ctx.Elements.water);
        // Lv 6: 变形控制（与大招变形配合，强化变形稳定性）
        C.AddFeat(6, Ctx.Properties.polymorph_control);
        // Lv 8: 水雾幻影（常驻闪避位移）
        C.AddFeat(8, Ctx.Properties.displacement);
        // Lv 12: 澄澈心境
        C.AddFeat(12, Ctx.Properties.clarity);

        // 开局熟练技能
        C.Startup.SetSkill(Ctx.Qualifications.proficient,
          Ctx.Skills.literacy,
          Ctx.Skills.evocation,
          Ctx.Skills.transmutation,
          Ctx.Skills.swimming,
          Ctx.Skills.sling,
          Ctx.Skills.light_armour,
          Ctx.Skills.unarmed_combat);

        // 开局掌握水人全套技能（Q 波浪形态 / W1 变体攻击·敏捷 / W2 变体攻击·力量 / E1 转敏 / E2 转力 / R1 变形 / R2 解除变形）
        C.Startup.AddGrimoire(Dice.One, WaveformSpell);
        C.Startup.AddGrimoire(Dice.One, AdaptiveStrikeAgilitySpell);
        C.Startup.AddGrimoire(Dice.One, AdaptiveStrikeStrengthSpell);
        C.Startup.AddGrimoire(Dice.One, MorphAgilitySpell);
        C.Startup.AddGrimoire(Dice.One, MorphStrengthSpell);
        C.Startup.AddGrimoire(Dice.One, MorphSpell);
        C.Startup.AddGrimoire(Dice.One, MorphRevertSpell);

        // 开局补给与基础装备（主手 +1 弹弓 + 弹药栏无限潮汐水弹 + 防水随身袋 + 长袍 + 2瓶水 + 1瓶魔力药水）
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
