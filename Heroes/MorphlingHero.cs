using System;
using Pathos.DotaHeroes.Framework;

namespace Pathos.DotaHeroes.Heroes
{
  /// <summary>
  /// DOTA2 英雄：变体精灵（Morphling / 水人）
  /// 包含：
  /// - 专属种族：变体水灵（waterkin）
  /// - 专属职业：变体精灵（morphling）
  /// - 专属技能：波浪形态（Q）、变体攻击（W）、属性转换·敏捷（E1）、属性转换·力量（E2）+ 原生变形术与变形控制（R）
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

      // 2. 注册水人专属无限远程普攻：潮汐水弹（tidal orb）
      var TidalOrbItem = RegisterTidalOrbItem(Ctx, TidalOrbGlyph);

      // 3. 注册水人四大专属技能
      var WaveformSpell = RegisterWaveformSpell(Ctx, WaveformGlyph);
      var AdaptiveStrikeSpell = RegisterAdaptiveStrikeSpell(Ctx, AdaptiveStrikeGlyph);
      var MorphAgilitySpell = RegisterMorphAgilitySpell(Ctx, MorphAgilityGlyph);
      var MorphStrengthSpell = RegisterMorphStrengthSpell(Ctx, MorphStrengthGlyph);

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
        MorphStrengthSpell);

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

    private static Item RegisterTidalOrbItem(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Items.Add(I =>
      {
        I.Name = "tidal orb";
        I.Description = "A condensed sphere of living tidewater hurled at range. Reforms endlessly in a Morphling's grasp without consuming ammunition.";
        I.Glyph = Icon;
        I.Stock = Ctx.Stocks.weapon;
        I.Type = ItemType.ThrownWeapon;
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
          Ctx.Skills.disc,
          Ctx.Sonics.water_splash,
          Ctx.Elements.water,
          DamageType.Bludgeon,
          1.d6(),
          _ => { });
        W.FixedRange = 5;
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
        S.Description = "Dissolve into a surging wave, blinking rapidly across the battlefield and briefly phasing through obstacles.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Teleport);
        S.Level = 1;
        S.Mana = 6;

        void Configure(Qualification Qual, int Range, int PhaseTurns)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.BlinkEntity();
          Adept.Apply.ApplyTransient(Ctx.Properties.phasing, Dice.Fixed(PhaseTurns));
        }

        Configure(Ctx.Qualifications.proficient, Range: 5, PhaseTurns: 2);
        Configure(Ctx.Qualifications.specialist, Range: 6, PhaseTurns: 2);
        Configure(Ctx.Qualifications.expert, Range: 7, PhaseTurns: 3);
        Configure(Ctx.Qualifications.master, Range: 9, PhaseTurns: 3);
        Configure(Ctx.Qualifications.champion, Range: 11, PhaseTurns: 4);
      });
    }

    private static Spell RegisterAdaptiveStrikeSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "adaptive strike";
        S.Description = "Blast a target with a high-pressure surge of water, dealing water damage, knocking them back, and potentially stunning them.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.water);
        S.Level = 1;
        S.Mana = 5;

        void Configure(Qualification Qual, int Range, Dice Damage, Chance StunChance, Dice StunDuration)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Explosion(Ctx.Explosions.watery, Dice.Fixed(Range));
          Adept.Apply.HarmEntity(Ctx.Elements.water, Damage);
          Adept.Apply.Knockback();
          Adept.Apply.WhenChance(StunChance,
            Then => Then.ApplyTransient(Ctx.Properties.stunned, StunDuration),
            Else => { });
        }

        Configure(Ctx.Qualifications.proficient, 5, 2.d6(), Chance.OneIn4, 1.d2());
        Configure(Ctx.Qualifications.specialist, 6, 3.d6(), Chance.OneIn3, 1.d2());
        Configure(Ctx.Qualifications.expert, 7, 4.d6(), Chance.OneIn3, 1.d3());
        Configure(Ctx.Qualifications.master, 8, 5.d6(), Chance.OneIn2, 1.d3());
        Configure(Ctx.Qualifications.champion, 9, 6.d6(), Chance.TwoIn3, 1.d4());
      });
    }

    private static Spell RegisterMorphAgilitySpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph agility";
        S.Description = "Shift your fluid form toward agility, trading Strength for Dexterity and gaining a brief burst of quickness.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Buff);
        S.Level = 1;
        S.Mana = 2;

        void Configure(Qualification Qual, Dice QuickDuration)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.TradeoffAbility(Ctx.Attributes.dexterity, Ctx.Attributes.strength);
          Adept.Apply.ApplyTransient(Ctx.Properties.quickness, QuickDuration);
        }

        Configure(Ctx.Qualifications.proficient, 1.d3());
        Configure(Ctx.Qualifications.specialist, 1.d4());
        Configure(Ctx.Qualifications.expert, 2.d3());
        Configure(Ctx.Qualifications.master, 2.d4());
        Configure(Ctx.Qualifications.champion, 3.d4());
      });
    }

    private static Spell RegisterMorphStrengthSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "morph strength";
        S.Description = "Shift your fluid form toward strength, trading Dexterity for Strength while restoring vitality.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.transmutation;
        S.Precept = new Precept(Purpose.Healing);
        S.Level = 1;
        S.Mana = 2;

        void Configure(Qualification Qual, Dice HealDice)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.TradeoffAbility(Ctx.Attributes.strength, Ctx.Attributes.dexterity);
          Adept.Apply.HealEntity(HealDice, Modifier.Zero);
        }

        Configure(Ctx.Qualifications.proficient, 1.d3());
        Configure(Ctx.Qualifications.specialist, 1.d4());
        Configure(Ctx.Qualifications.expert, 1.d6());
        Configure(Ctx.Qualifications.master, 2.d4());
        Configure(Ctx.Qualifications.champion, 2.d6());
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

        // 初始六维与上限（赋予较大的力量/敏捷转换空间）
        Ctx.SetFormScores(E.DefaultForm, Str: 9, Dex: 12, Con: 9, Int: 11, Wis: 10, Cha: 9);
        Ctx.SetFormScores(E.LimitForm, Str: 24, Dex: 24, Con: 18, Int: 20, Wis: 18, Cha: 16);

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
      Spell AdaptiveStrikeSpell,
      Spell MorphAgilitySpell,
      Spell MorphStrengthSpell)
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
        // Lv 6: 解锁大招——变形 + 变形控制（与官方变形怪同款机制，6 级起可自由精准变身）
        C.AddFeat(6, Ctx.Properties.polymorph, Ctx.Properties.polymorph_control);
        // Lv 8: 水雾幻影（闪避位移）
        C.AddFeat(8, Ctx.Properties.displacement);
        // Lv 12: 澄澈心境
        C.AddFeat(12, Ctx.Properties.clarity);

        // 开局熟练技能
        C.Startup.SetSkill(Ctx.Qualifications.proficient,
          Ctx.Skills.literacy,
          Ctx.Skills.evocation,
          Ctx.Skills.transmutation,
          Ctx.Skills.swimming,
          Ctx.Skills.disc,
          Ctx.Skills.light_armour,
          Ctx.Skills.unarmed_combat);

        // 开局掌握水人四大基础技能（Q 波浪形态 / W 变体攻击 / E1 转敏 / E2 转力；R 变形大招于 6 级自动解锁）
        C.Startup.AddGrimoire(Dice.One, WaveformSpell);
        C.Startup.AddGrimoire(Dice.One, AdaptiveStrikeSpell);
        C.Startup.AddGrimoire(Dice.One, MorphAgilitySpell);
        C.Startup.AddGrimoire(Dice.One, MorphStrengthSpell);

        // 开局补给与基础装备（无限远程潮汐水弹 + 长袍 + 2瓶水 + 1瓶魔力药水）
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.robe);
        C.Startup.Loot.AddKit(Chance.Always, TidalOrbItem);
        C.Startup.Loot.AddKit(Chance.Always, Dice.Two, Ctx.Items.potion_of_water);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
      });
    }
  }
}
