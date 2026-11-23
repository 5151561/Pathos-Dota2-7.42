using System;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Heroes
{
  /// <summary>
  /// DOTA2 7.42 英雄：变体精灵（Morphling / 水人）
  /// 基于「声明式外壳 + Harmony Hook 核心逻辑」忠实还原 DOTA2 7.42 百科机制：
  /// - 先天技能「潮涨潮落（Ebb and Flow）」（实时随属性生效，变形期间持续保留）：
  ///   - 敏捷（DEX）：提供额外移动速度与远程攻击距离
  ///   - 力量（STR）：提供额外施法距离与减速抗性
  /// - Q：波浪形态（waveform）— 闪烁突进 + 落地水波 AOE 伤害 + 短暂穿墙与闪避位移
  /// - W1：变体打击·敏捷（adaptive strike agility）— 基础水伤 + 按实时 DEX/STR 比值动态缩放爆发伤害（DEX ≥ 1.5×STR 达满额倍率）
  /// - W2：变体打击·力量（adaptive strike strength）— 击退 + 按实时 STR/DEX 比值动态缩放眩晕时长（STR ≥ 1.5×DEX 达最长眩晕）
  /// - E1/E2：属性转换·敏捷 / 力量（morph agility / morph strength）—
  ///   - Hook 全权接管，同步修改 CurrentForm 与 MaximumForm，严格总量守恒且复活/恢复属性不再白嫖
  ///   - 忠实还原 DOTA2 血量联动：转力同步提升最大生命与当前生命；转敏同步降低最大生命与当前生命（最低保底 1 HP）
  /// - R1/R2：变形（morph）与解除变形（morph revert）— 化身敌人复制外形与技能，先天技能持续保留
  /// </summary>
  public sealed class MorphlingHero : IDotaHeroDefinition
  {
    private const string MorphlingClassName = "morphling";
    private const int MinAttributeScore = 3;
    private const int MinMaxLifePoints = 10;
    private const int HpPerShiftedPoint = 6;

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

      // 2. 注册水人先天技能天赋：潮涨潮落（显示在角色天赋栏，实际动态效果由 Hook 实时驱动）
      var EbbAndFlowTalent = RegisterEbbAndFlowTalent(Ctx, MorphlingGlyph);

      // 3. 注册水人专属无限远程普攻：潮汐水弹（tidal orb）
      var TidalOrbItem = RegisterTidalOrbItem(Ctx, TidalOrbGlyph);

      // 4. 注册水人全套专属技能（Q / W1 / W2 / E1 / E2 / R1 / R2）
      var WaveformSpell = RegisterWaveformSpell(Ctx, WaveformGlyph);
      var AdaptiveStrikeAgilitySpell = RegisterAdaptiveStrikeAgilitySpell(Ctx, AdaptiveStrikeGlyph);
      var AdaptiveStrikeStrengthSpell = RegisterAdaptiveStrikeStrengthSpell(Ctx, AdaptiveStrikeGlyph);
      var MorphAgilitySpell = RegisterMorphAgilitySpell(Ctx, MorphAgilityGlyph);
      var MorphStrengthSpell = RegisterMorphStrengthSpell(Ctx, MorphStrengthGlyph);
      var MorphSpell = RegisterMorphSpell(Ctx, MorphlingGlyph);
      var MorphRevertSpell = RegisterMorphRevertSpell(Ctx, MorphlingGlyph);

      // 5. 注册专属种族：变体水灵（waterkin，含遇水回血回蓝与遇火蒸发反应）
      var WaterkinRace = RegisterWaterkinRace(Ctx, MorphlingGlyph);

      // 6. 注册水系环境专属交互（水药水吸收 + 喷泉汲取）
      RegisterWaterInteractions(Ctx);

      // 7. 注册专属职业：变体精灵（morphling）
      var MorphlingClass = RegisterMorphlingClass(
        Ctx,
        MorphlingGlyph,
        EbbAndFlowTalent,
        TidalOrbItem,
        WaveformSpell,
        AdaptiveStrikeAgilitySpell,
        AdaptiveStrikeStrengthSpell,
        MorphAgilitySpell,
        MorphStrengthSpell,
        MorphSpell,
        MorphRevertSpell);

      // 8. 注册预设英雄：Morphling（可在选人界面一键选中）
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
      // E1/E2：属性转换（总量守恒 + CurrentForm/MaximumForm 同步 + DOTA2 血量联动）
      DotaHookBus.RegisterAfterCastSpell("morphling.attribute_shift", Args =>
      {
        var SpellName = Args.Spell?.Name;
        bool IsAgilityShift = string.Equals(SpellName, "morph agility", StringComparison.OrdinalIgnoreCase);
        bool IsStrengthShift = string.Equals(SpellName, "morph strength", StringComparison.OrdinalIgnoreCase);
        if (!IsAgilityShift && !IsStrengthShift)
          return;

        ExecuteAttributeShift(Ctx, Args, ToAgility: IsAgilityShift);
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

      // 先天技能「潮涨潮落」— 敏捷提升远程攻击距离（变形为远程生物同样保留）
      DotaHookBus.RegisterAttackRangeQuery("morphling.ebb_flow.attack_range", Args =>
      {
        if (!IsMorphling(Args.Character))
          return;

        int Dex = PathosEngineBridge.GetCurrentAbilityScore(Args.Character, "dexterity");
        int BonusRange = Math.Max(0, (Dex - 10) / 5);
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

      // 先天技能「潮涨潮落」— 力量提供减速抗性；以及 W2 变体打击·力量按 STR/DEX 动态缩放眩晕时长
      DotaHookBus.RegisterBeforeApplyTransient("morphling.transient_hooks", Args =>
      {
        // 1) 潮涨潮落：力量 → 减速抗性（高力直接免疫，中力大幅缩短减速时间）
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

        // 2) W2 变体打击·力量：按 STR/DEX 比值动态计算眩晕回合数（STR ≥ 1.5×DEX 达满额重晕）
        var ActiveCast = DotaHookBus.ActiveSpellCast;
        if (ActiveCast != null &&
            Args.Property == Ctx.Properties.stunned &&
            Args.Target != ActiveCast.Character &&
            string.Equals(ActiveCast.Spell?.Name, "adaptive strike strength", StringComparison.OrdinalIgnoreCase))
        {
          int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "strength"));
          int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "dexterity"));
          float Ratio = Str / (float)Dex;
          float Scale = Math.Clamp((Ratio - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);

          int MasteryTier = GetSkillTier(Ctx, ActiveCast.Character, Ctx.Skills.evocation);
          int MaxStun = 2 + MasteryTier; // 3 ~ 7 回合
          int FinalStun = Math.Max(1, (int)MathF.Round(1.0f + (MaxStun - 1.0f) * Scale));
          Args.Duration = Dice.Fixed(FinalStun);
        }
      });

      // W1 变体打击·敏捷：按 DEX/STR 比值动态追加敏捷爆发伤害（DEX ≥ 1.5×STR 达满额倍率）
      DotaHookBus.RegisterBeforeDamage("morphling.adaptive_strike_agility", Args =>
      {
        var ActiveCast = DotaHookBus.ActiveSpellCast;
        if (ActiveCast == null ||
            Args.Attacker != ActiveCast.Character ||
            Args.Defender == ActiveCast.Character ||
            !string.Equals(ActiveCast.Spell?.Name, "adaptive strike agility", StringComparison.OrdinalIgnoreCase))
          return;

        int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "dexterity"));
        int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "strength"));
        float Ratio = Dex / (float)Str;
        float Scale = Math.Clamp((Ratio - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);

        int BonusDamage = (int)MathF.Round(Dex * (0.35f + 1.15f * Scale));
        if (BonusDamage > 0)
          Args.DamagePoints += BonusDamage;
      });
    }

    private static void ExecuteAttributeShift(DotaHeroContext Ctx, SpellCastEventArgs Args, bool ToAgility)
    {
      var Character = Args.Character;
      if (Character?.CurrentForm == null || Character.MaximumForm == null)
        return;

      int Step = GetSkillTier(Ctx, Character, Ctx.Skills.transmutation);

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
            "Attribute Shift (Agility): already at agility cap or minimum strength.");
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

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Character,
          Args.Spell.Glyph,
          $"Attribute Shift (Agility): DEX +{Shift}, STR/CON -{Shift} (Max HP -{HpDelta}).");
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
            "Attribute Shift (Strength): already at strength cap or minimum agility.");
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

        PathosEngineBridge.WriteInternalLog(
          Args.Engine,
          Character,
          Args.Spell.Glyph,
          $"Attribute Shift (Strength): STR/CON +{Shift}, DEX -{Shift} (HP +{HpDelta}).");
      }
    }

    private static bool IsMorphling(Character Character)
    {
      return Character?.Class != null &&
             string.Equals(Character.Class.Name, MorphlingClassName, StringComparison.OrdinalIgnoreCase);
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

    private static Property RegisterEbbAndFlowTalent(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Properties.Add(P =>
      {
        P.Name = "ebb and flow";
        P.Description = "Innate (Ebb & Flow): Agility grants bonus movement speed and ranged attack range; Strength grants bonus spell cast range and slow resistance. Persists while morphed.";
        P.Glyph = Icon;
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
          Adept.Apply.Shout(Dice.Fixed(WaveRadius), Wave => Wave.HarmEntity(Ctx.Elements.water, WaveDamage));
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
        S.Description = "Launch a surge of water dealing base damage plus bonus burst damage scaling dynamically with your Agility-to-Strength ratio (max scaling at 1.5x Agility).";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.water);
        S.Level = 1;
        S.Mana = 5;

        void Configure(Qualification Qual, int Range, Dice BaseDamage)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Explosion(Ctx.Explosions.watery, Dice.Fixed(Range));
          // 基础伤害由声明式提供，额外敏捷倍率爆发伤害由 Hook 按实时 DEX/STR 动态追加
          Adept.Apply.HarmEntity(Ctx.Elements.water, BaseDamage);
        }

        Configure(Ctx.Qualifications.proficient, 5, 1.d6());
        Configure(Ctx.Qualifications.specialist, 6, 1.d8());
        Configure(Ctx.Qualifications.expert, 7, 2.d6());
        Configure(Ctx.Qualifications.master, 8, 2.d8());
        Configure(Ctx.Qualifications.champion, 9, 3.d6());
      });
    }

    private static Spell RegisterAdaptiveStrikeStrengthSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "adaptive strike strength";
        S.Description = "Crush foes with a torrent of water that knocks them back and stuns them for a duration scaling dynamically with your Strength-to-Agility ratio (max stun at 1.5x Strength).";
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
          Adept.Apply.Knockback();
          // 基础眩晕占位，实际眩晕回合数由 Hook 按实时 STR/DEX 比值动态改写
          Adept.Apply.ApplyTransient(Ctx.Properties.stunned, Dice.One);
        }

        Configure(Ctx.Qualifications.proficient, 5, 1.d4());
        Configure(Ctx.Qualifications.specialist, 6, 1.d6());
        Configure(Ctx.Qualifications.expert, 7, 2.d4());
        Configure(Ctx.Qualifications.master, 8, 2.d6());
        Configure(Ctx.Qualifications.champion, 9, 3.d4());
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
        S.Level = 1;
        S.Mana = 2;

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
        S.Level = 1;
        S.Mana = 2;

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
        S.Level = 3;
        S.Mana = 10;

        void Configure(Qualification Qual, int Range)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range));
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

        Ctx.SetFormScores(E.DefaultForm, Str: 10, Dex: 14, Con: 10, Int: 11, Wis: 10, Cha: 9);
        Ctx.SetFormScores(E.LimitForm, Str: 26, Dex: 30, Con: 26, Int: 18, Wis: 16, Cha: 14);

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
      Spell AdaptiveStrikeAgilitySpell,
      Spell AdaptiveStrikeStrengthSpell,
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
        C.ManaAdvancement.Set(14, 1.d6());

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

        C.Startup.AddGrimoire(Dice.One, WaveformSpell);
        C.Startup.AddGrimoire(Dice.One, AdaptiveStrikeAgilitySpell);
        C.Startup.AddGrimoire(Dice.One, AdaptiveStrikeStrengthSpell);
        C.Startup.AddGrimoire(Dice.One, MorphAgilitySpell);
        C.Startup.AddGrimoire(Dice.One, MorphStrengthSpell);
        C.Startup.AddGrimoire(Dice.One, MorphSpell);
        C.Startup.AddGrimoire(Dice.One, MorphRevertSpell);

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
