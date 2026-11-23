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

      // 2. 注册水人四大专属技能
      var WaveformSpell = RegisterWaveformSpell(Ctx, WaveformGlyph);
      var AdaptiveStrikeSpell = RegisterAdaptiveStrikeSpell(Ctx, AdaptiveStrikeGlyph);
      var MorphAgilitySpell = RegisterMorphAgilitySpell(Ctx, MorphAgilityGlyph);
      var MorphStrengthSpell = RegisterMorphStrengthSpell(Ctx, MorphStrengthGlyph);

      // 3. 注册专属种族：变体水灵（waterkin）
      var WaterkinRace = RegisterWaterkinRace(Ctx, MorphlingGlyph);

      // 4. 注册专属职业：变体精灵（morphling）
      var MorphlingClass = RegisterMorphlingClass(
        Ctx,
        MorphlingGlyph,
        WaveformSpell,
        AdaptiveStrikeSpell,
        MorphAgilitySpell,
        MorphStrengthSpell);

      // 5. 注册预设英雄：Morphling（可在选人界面一键选中）
      Ctx.Heroes.Add(H =>
      {
        H.Name = "Morphling";
        H.Race = WaterkinRace;
        H.Class = MorphlingClass;
        H.Gender = Ctx.Genders.male;
        H.CustomGlyph = MorphlingGlyph;
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
        S.Level = 2;
        S.Mana = 12;

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
        S.Level = 2;
        S.Mana = 10;

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
        S.Mana = 5;

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
        S.Mana = 5;

        void Configure(Qualification Qual, Dice HealDice)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().SetTargetSelf(true);
          Adept.Apply.TradeoffAbility(Ctx.Attributes.strength, Ctx.Attributes.dexterity);
          Adept.Apply.HealEntity(HealDice, Modifier.Zero);
        }

        Configure(Ctx.Qualifications.proficient, 1.d8());
        Configure(Ctx.Qualifications.specialist, 2.d6());
        Configure(Ctx.Qualifications.expert, 2.d8());
        Configure(Ctx.Qualifications.master, 3.d8());
        Configure(Ctx.Qualifications.champion, 4.d8());
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
        // Lv 1: 变形控制（配合大招变形术，可精准选择目标形态）
        C.AddFeat(1, Ctx.Properties.polymorph_control);
        // Lv 4: 水系抗性
        C.AddFeat(4, Ctx.Elements.water);
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
          Ctx.Skills.light_armour,
          Ctx.Skills.light_blade,
          Ctx.Skills.unarmed_combat);

        // 开局掌握全套水人技能（Q 波浪形态 / W 变体攻击 / E1 转敏 / E2 转力 / R 变形术）
        C.Startup.AddGrimoire(Dice.One, WaveformSpell);
        C.Startup.AddGrimoire(Dice.One, AdaptiveStrikeSpell);
        C.Startup.AddGrimoire(Dice.One, MorphAgilitySpell);
        C.Startup.AddGrimoire(Dice.One, MorphStrengthSpell);
        C.Startup.AddGrimoire(Dice.One, Ctx.Spells.polymorph);

        // 开局补给与基础装备
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.robe);
        C.Startup.Loot.AddKit(Chance.Always, Modifier.Plus1, Ctx.Items.short_sword);
        C.Startup.Loot.AddKit(Chance.Always, Dice.Two, Ctx.Items.potion_of_water);
        C.Startup.Loot.AddKit(Chance.Always, Ctx.Items.potion_of_gain_energy);
      });
    }
  }
}
