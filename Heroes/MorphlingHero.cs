using System;
using System.Collections.Generic;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Skills;
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
    private const string SkillWaveformId = "morph.q.waveform";
    private const string SkillAdaptiveStrikeId = "morph.w.adaptive_strike";
    private const string SkillAttributeShiftId = "morph.e.attribute_shift";
    private const string SkillMorphId = "morph.r.morph";
    private const int MinAttributeScore = 3;
    private const int MinMaxLifePoints = 10;
    private const int HpPerShiftedPoint = 6;

    // DOTA2 7.42 变体精灵初始三维：STR 16 / AGI 33 / INT 19
    private const int DotaBaseStr = 16;
    private const int DotaBaseAgi = 33;
    private const int DotaBaseInt = 19;
    private static readonly PathosStartingStats StartingStats =
      DotaMechanicsMapper.MapStartingStats(DotaBaseStr, DotaBaseAgi, DotaBaseInt, Charisma: 10);

    private static readonly int WaveformManaCost = DotaMechanicsMapper.MapSkillManaCost(115);      // 6
    private static readonly int AdaptiveStrikeManaCost = DotaMechanicsMapper.MapSkillManaCost(80); // 4
    private static readonly int AttributeShiftManaCost = DotaMechanicsMapper.MapSkillManaCost(0);  // 0（忠实还原 7.42）
    private static readonly int MorphManaCost = DotaMechanicsMapper.MapSkillManaCost(120);         // 原 50 蓝，Pathos Impersonate 强度修正 -> 6
    private static readonly int MorphRevertManaCost = DotaMechanicsMapper.MapSkillManaCost(0);     // 0

    public string Id => "morphling";

    public void Register(DotaHeroContext Ctx)
    {
      // 1. 注册贴图（对应 Resources/Heroes/morphling/<tileset>/*.png）
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

      // 4. 注册水人全套专属技能（Q / W / E1 / E2 / R1 / R2）
      var WaveformSpell = RegisterWaveformSpell(Ctx, WaveformGlyph);
      var AdaptiveStrikeSpell = RegisterAdaptiveStrikeSpell(Ctx, AdaptiveStrikeGlyph);
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
        WaveformSpell,
        AdaptiveStrikeSpell,
        MorphAgilitySpell,
        MorphStrengthSpell,
        MorphSpell,
        MorphRevertSpell);
    }

    private static void RegisterSkillBuild(
      DotaHeroContext Ctx,
      Glyph HeroGlyph,
      Glyph WaveformGlyph,
      Glyph AdaptiveStrikeGlyph,
      Glyph AttributeShiftGlyph,
      Spell WaveformSpell,
      Spell AdaptiveStrikeSpell,
      Spell MorphAgilitySpell,
      Spell MorphStrengthSpell,
      Spell MorphSpell,
      Spell MorphRevertSpell)
    {
      var Build = HeroSkillBuild.CreateStandard(
        ClassName: MorphlingClassName,
        HeroDisplayName: "变体精灵 · 水人",
        HeroId: "morph",
        IconGlyph: HeroGlyph,
        SlotQ: DotaSkillSlot.CreateActive(
          Id: SkillWaveformId,
          Hotkey: "Q",
          Title: "波浪形态",
          IconGlyph: WaveformGlyph,
          Spell: WaveformSpell,
          DescribeLevel: Lv => Lv <= 0
            ? "学习后获得「波浪形态」；升星额外 +15%/级水波伤害与 +1 格射程"
            : $"升星伤害 ×{1.0f + (Lv - 1) * 0.15f:0.00}，射程 +{Lv - 1} 格{(Lv < 4 ? "（下级继续升星强化）" : "")}"),
        SlotW: DotaSkillSlot.CreateActive(
          Id: SkillAdaptiveStrikeId,
          Hotkey: "W",
          Title: "变体打击",
          IconGlyph: AdaptiveStrikeGlyph,
          Spell: AdaptiveStrikeSpell,
          DescribeLevel: Lv => Lv <= 0
            ? "学习后获得「变体打击」；升星额外 +15%/级敏捷爆发伤害与 +1 回合最大眩晕"
            : $"升星爆发伤害 ×{1.0f + (Lv - 1) * 0.15f:0.00}，最大眩晕 +{Lv - 1} 回合{(Lv < 4 ? "（下级继续升星强化）" : "")}"),
        SlotE: DotaSkillSlot.CreateActiveGroup(
          Id: SkillAttributeShiftId,
          Hotkey: "E",
          Title: "属性转换（敏/力）",
          IconGlyph: AttributeShiftGlyph,
          Spells: new[] { MorphAgilitySpell, MorphStrengthSpell },
          DescribeLevel: Lv => Lv <= 0
            ? "一次学习「属性转换·敏捷/力量」（0 蓝耗）；升星额外 +1/级单次转换步进"
            : $"双技能同步升星：单次属性转换步进 +{Lv - 1}{(Lv < 4 ? "（下级步进再 +1）" : "")}"),
        SlotR: DotaSkillSlot.CreateActiveGroup(
          Id: SkillMorphId,
          Hotkey: "R",
          Title: "变形 / 解除变形",
          IconGlyph: HeroGlyph,
          Spells: new[] { MorphSpell, MorphRevertSpell },
          IsUltimate: true,
          DescribeLevel: Lv => Lv <= 0
            ? "终极技能（Lv 6 解锁）：同步学习变形与解除变形；升星 +1 格/级施法射程"
            : $"升星施法射程 +{Lv - 1} 格并降低变形蓝耗{(Lv < 3 ? "（下级射程再 +1 格）" : "")}"));

      HeroSkillRegistry.Register(Ctx, Build);
    }

    public void RegisterHooks(DotaHeroContext Ctx)
    {
      // 0. 锁定变体精灵固定初始六维（STR 10 / DEX 18 / CON 10 / INT 12 / WIS 12 / CHA 10）与 1 级初始蓝量（15）
      DotaMechanicsMapper.RegisterFixedStartingStats(MorphlingClassName, StartingStats);

      // 0.1 注册 Pathos 原生选框扩距（波浪形态 / 变体打击 / 变形）
      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: "waveform",
        DisplayTitle: "波浪形态",
        Mode: DotaTargetMode.PointOpen,
        ResolveRange: Caster => GetWaveformRange(Ctx, Caster),
        AllowSelf: false));

      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: "adaptive strike",
        DisplayTitle: "变体打击",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: Caster => GetAdaptiveStrikeRange(Ctx, Caster),
        AllowSelf: false));

      DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
        SpellName: "morph",
        DisplayTitle: "变形",
        Mode: DotaTargetMode.UnitHostile,
        ResolveRange: Caster => GetMorphRange(Ctx, Caster),
        AllowSelf: false));

      // 0.2 注册变体精灵主动技能的帮助面板数值与 5 阶熟练度详情（含游戏内实时面板预览）
      RegisterSpellHelpSpecs(Ctx);

      // Q/W 水系技能落点校准与 Pathos 原生水爆特效
      DotaHookBus.RegisterAfterCastSpell("morphling.range_and_vfx", Args =>
      {
        var Caster = Args.Character;
        var SpellName = Args.Spell?.Name;
        if (Caster?.Square == null || string.IsNullOrEmpty(SpellName))
          return;

        if (string.Equals(SpellName, "waveform", StringComparison.OrdinalIgnoreCase))
        {
          var Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
          if (Pending?.TargetSquare != null &&
              Pending.TargetSquare.Map == Caster.Square.Map &&
              DotaCombatHelper.IsOpenSquare(Pending.TargetSquare))
          {
            PathosEngineBridge.TeleportCharacter(Args.Engine, Caster, Pending.TargetSquare, MasterMode: false);
          }

          PathosEngineBridge.StoryExploding(Args.Engine, Delay.Zero, Caster.Square, Ctx.Explosions.watery);
        }
        else if (string.Equals(SpellName, "adaptive strike", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(SpellName, "morph", StringComparison.OrdinalIgnoreCase))
        {
          DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
        }
      });

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

        // 2) W 变体打击：按 STR/DEX 比值动态计算眩晕回合数（STR ≥ 1.5×DEX 达满额重晕）
        var ActiveCast = DotaHookBus.ActiveSpellCast;
        if (ActiveCast != null &&
            Args.Property == Ctx.Properties.stunned &&
            Args.Target != ActiveCast.Character &&
            string.Equals(ActiveCast.Spell?.Name, "adaptive strike", StringComparison.OrdinalIgnoreCase))
        {
          int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "strength"));
          int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "dexterity"));
          float Ratio = Str / (float)Dex;
          float Scale = Math.Clamp((Ratio - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);

          int MasteryTier = DotaCombatHelper.GetSkillTier(Ctx, ActiveCast.Character, Ctx.Skills.evocation);
          // 对应 DOTA2 7.42 变体打击最短 0.5 秒（1 回合）、最长 1.5 / 2.0 / 2.5 / 3.0 秒（3 / 4 / 5 / 6 回合，Champion 7 回合），叠加升星额外眩晕上限
          int MaxStun = DotaMechanicsMapper.MapDurationTurns(1.0f + MasteryTier * 0.5f) +
                        GetAdaptiveStrikeExtraMaxStun(ActiveCast.Character);
          int FinalStun = Math.Max(1, (int)MathF.Round(1.0f + (MaxStun - 1.0f) * Scale));
          Args.Duration = Dice.Fixed(FinalStun);
        }
      });

      // Q 波浪形态升星伤害增幅 & W 变体打击按 DEX/STR 比值动态追加敏捷爆发伤害（含升星增幅）
      DotaHookBus.RegisterBeforeDamage("morphling.adaptive_strike", Args =>
      {
        var ActiveCast = DotaHookBus.ActiveSpellCast;
        if (ActiveCast == null ||
            Args.Attacker != ActiveCast.Character ||
            Args.Defender == ActiveCast.Character)
          return;

        if (string.Equals(ActiveCast.Spell?.Name, "waveform", StringComparison.OrdinalIgnoreCase))
        {
          float StarMult = GetWaveformStarDamageMultiplier(ActiveCast.Character);
          if (StarMult > 1.001f && Args.DamagePoints > 0)
            Args.DamagePoints = Math.Max(Args.DamagePoints + 1, (int)MathF.Round(Args.DamagePoints * StarMult));
          return;
        }

        if (!string.Equals(ActiveCast.Spell?.Name, "adaptive strike", StringComparison.OrdinalIgnoreCase))
          return;

        int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "dexterity"));
        int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(ActiveCast.Character, "strength"));
        float Ratio = Dex / (float)Str;
        float Scale = Math.Clamp((Ratio - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);

        float AdaptiveStarMult = GetAdaptiveStrikeStarDamageMultiplier(ActiveCast.Character);
        int BonusDamage = (int)MathF.Round(Dex * (0.35f + 1.15f * Scale) * AdaptiveStarMult);
        if (BonusDamage > 0)
          Args.DamagePoints += BonusDamage;
      });
    }

    private static void ExecuteAttributeShift(DotaHeroContext Ctx, SpellCastEventArgs Args, bool ToAgility)
    {
      var Character = Args.Character;
      if (Character?.CurrentForm == null || Character.MaximumForm == null)
        return;

      int Step = DotaCombatHelper.GetSkillTier(Ctx, Character, Ctx.Skills.transmutation) +
                 GetAttributeShiftExtraStep(Character);

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

    private static int GetStrRangeBonus(Character Character)
    {
      if (!IsMorphling(Character))
        return 0;
      int Str = PathosEngineBridge.GetCurrentAbilityScore(Character, "strength");
      return Math.Max(0, (Str - 10) / 5);
    }

    private static float GetWaveformStarDamageMultiplier(Character Character)
      => 1.0f + Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillWaveformId) - 1) * 0.15f;

    private static int GetWaveformExtraRange(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillWaveformId) - 1);

    private static float GetAdaptiveStrikeStarDamageMultiplier(Character Character)
      => 1.0f + Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillAdaptiveStrikeId) - 1) * 0.15f;

    private static int GetAdaptiveStrikeExtraMaxStun(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillAdaptiveStrikeId) - 1);

    private static int GetAttributeShiftExtraStep(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillAttributeShiftId) - 1);

    private static int GetMorphExtraRange(Character Character)
      => Math.Max(0, HeroSkillService.GetSkillLevel(Character, SkillMorphId) - 1);

    private static int GetWaveformRange(DotaHeroContext Ctx, Character Caster)
    {
      var Qual = PathosEngineBridge.GetQualification(Caster, Ctx.Skills.transmutation);
      int BaseRange = Qual == Ctx.Qualifications.champion ? 11
        : Qual == Ctx.Qualifications.master ? 9
        : Qual == Ctx.Qualifications.expert ? 7
        : Qual == Ctx.Qualifications.specialist ? 6
        : 5;
      return BaseRange + GetStrRangeBonus(Caster) + GetWaveformExtraRange(Caster);
    }

    private static int GetAdaptiveStrikeRange(DotaHeroContext Ctx, Character Caster)
    {
      var Qual = PathosEngineBridge.GetQualification(Caster, Ctx.Skills.evocation);
      int BaseRange = Qual == Ctx.Qualifications.champion ? 9
        : Qual == Ctx.Qualifications.master ? 8
        : Qual == Ctx.Qualifications.expert ? 7
        : Qual == Ctx.Qualifications.specialist ? 6
        : 5;
      return BaseRange + GetStrRangeBonus(Caster);
    }

    private static int GetMorphRange(DotaHeroContext Ctx, Character Caster)
    {
      var Qual = PathosEngineBridge.GetQualification(Caster, Ctx.Skills.transmutation);
      int BaseRange = Qual == Ctx.Qualifications.champion ? 10
        : Qual == Ctx.Qualifications.master ? 8
        : Qual == Ctx.Qualifications.expert ? 7
        : Qual == Ctx.Qualifications.specialist ? 6
        : 5;
      return BaseRange + GetStrRangeBonus(Caster) + GetMorphExtraRange(Caster);
    }

    private static bool IsMorphling(Character Character)
      => DotaCombatHelper.IsClass(Character, MorphlingClassName);

    private static readonly float[] WaveformDotaDamages = { 75.0f, 150.0f, 225.0f, 300.0f, 360.0f };
    private static readonly int[] WaveformBaseRanges = { 5, 6, 7, 9, 11 };
    private static readonly int[] WaveformRadii = { 2, 2, 2, 3, 3 };
    private static readonly int[] WaveformEvasionTurns = { 3, 4, 4, 5, 6 };

    private static readonly float[] AdaptiveStrikeDotaDamages = { 50.0f, 70.0f, 90.0f, 110.0f, 130.0f };
    private static readonly int[] AdaptiveStrikeBaseRanges = { 5, 6, 7, 8, 9 };
    private static readonly int[] MorphBaseRanges = { 5, 6, 7, 8, 10 };

    private static void RegisterSpellHelpSpecs(DotaHeroContext Ctx)
    {
      // 1. Q：波浪形态
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "waveform",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation);
          int Idx = Math.Clamp(Tier - 1, 0, 4);
          int BaseRange = WaveformBaseRanges[Idx];
          int StrBonus = GetStrRangeBonus(Ch);
          int StarRange = GetWaveformExtraRange(Ch);
          float StarMult = GetWaveformStarDamageMultiplier(Ch);
          Dice WaveDice = DotaMechanicsMapper.MapSkillDamageDice(WaveformDotaDamages[Idx]);
          int Radius = WaveformRadii[Idx];
          int Evasion = WaveformEvasionTurns[Idx];
          int TotalRange = BaseRange + StrBonus + StarRange;
          string RangeDetail = (StrBonus > 0 || StarRange > 0)
            ? $"{DotaHelpColor.Control($"{TotalRange} 格")}（基础 {BaseRange}{(StrBonus > 0 ? $" + {DotaHelpColor.Passive($"潮涨潮落 +{StrBonus}")}" : "")}{(StarRange > 0 ? $" + {DotaHelpColor.Skill($"升星 +{StarRange}")}" : "")}）"
            : DotaHelpColor.Control($"{BaseRange} 格");
          string StarDmgNote = StarMult > 1.001f ? $"，{DotaHelpColor.Skill($"升星 ×{StarMult:0.00}")}" : string.Empty;

          return new List<(string, string)>
          {
            ("实际突进射程", RangeDetail),
            ("落地水波伤害", $"{DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(WaveDice))} 水系伤害（均伤 {DotaHelpColor.Magic($"{WaveDice.Average() * StarMult:0.#}")}{StarDmgNote}，半径 {DotaHelpColor.Control($"{Radius} 格")}）"),
            ("位移与生存", $"瞬移至目标格，获得穿墙与闪避位移 {DotaHelpColor.Control($"{Evasion} 回合")}")
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          $"瞬移至射程内目标空地，落地对周围造成水系伤害并获得短暂穿墙与闪避位移（射程受{DotaHelpColor.Passive("潮涨潮落")}力量加成）"
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int Idx = Tier - 1;
            Dice WaveDice = DotaMechanicsMapper.MapSkillDamageDice(WaveformDotaDamages[Idx]);
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = $"施咒 范围 {WaveformBaseRanges[Idx]}",
              Bullets = new[]
              {
                $"水波伤害 {DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(WaveDice))}（半径 {DotaHelpColor.Control($"{WaveformRadii[Idx]} 格")}），穿墙与闪避 {DotaHelpColor.Control($"{WaveformEvasionTurns[Idx]} 回合")}"
              }
            });
          }
          return List;
        }
      });

      // 2. W：变体打击
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "adaptive strike",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.evocation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.evocation);
          int Idx = Math.Clamp(Tier - 1, 0, 4);
          int BaseRange = AdaptiveStrikeBaseRanges[Idx];
          int StrBonus = GetStrRangeBonus(Ch);
          Dice BaseDice = DotaMechanicsMapper.MapSkillDamageDice(AdaptiveStrikeDotaDamages[Idx]);

          int Dex = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity"));
          int Str = Math.Max(1, PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength"));

          float DexScale = Math.Clamp((Dex / (float)Str - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
          float AdaptiveStarMult = GetAdaptiveStrikeStarDamageMultiplier(Ch);
          int BonusDmg = (int)MathF.Round(Dex * (0.35f + 1.15f * DexScale) * AdaptiveStarMult);

          float StrScale = Math.Clamp((Str / (float)Dex - 0.67f) / (1.50f - 0.67f), 0.0f, 1.0f);
          int MaxStun = DotaMechanicsMapper.MapDurationTurns(1.0f + Tier * 0.5f) + GetAdaptiveStrikeExtraMaxStun(Ch);
          int FinalStun = Math.Max(1, (int)MathF.Round(1.0f + (MaxStun - 1.0f) * StrScale));

          int TotalMin = DotaSpellHelpBridge.GetDiceMin(BaseDice) + BonusDmg;
          int TotalMax = DotaSpellHelpBridge.GetDiceMax(BaseDice) + BonusDmg;
          float TotalAvg = BaseDice.Average() + BonusDmg;

          return new List<(string, string)>
          {
            ("实际射程", StrBonus > 0
              ? $"{DotaHelpColor.Control($"{BaseRange + StrBonus} 格")}（基础 {BaseRange} + {DotaHelpColor.Passive($"潮涨潮落 +{StrBonus}")}）"
              : DotaHelpColor.Control($"{BaseRange} 格")),
            ("预计总伤害", $"{DotaHelpColor.Magic($"{TotalMin}~{TotalMax}")} 水系伤害（均伤 {DotaHelpColor.Magic($"{TotalAvg:0.#}")}；基础 {DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceNotation(BaseDice))} + {DotaHelpColor.Stat($"敏捷追加 +{BonusDmg}")}）"),
            ("击退与眩晕", $"击退目标并眩晕 {DotaHelpColor.Control($"{FinalStun} 回合")}（当前阶上限 {DotaHelpColor.Control($"{MaxStun} 回合")}）"),
            ("当前力敏比", $"{DotaHelpColor.Stat($"敏捷 {Dex}")} / {DotaHelpColor.Stat($"力量 {Str}")}（敏≥1.5×力达满额伤害；力≥1.5×敏达满额眩晕）")
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          $"总伤害 = {DotaHelpColor.Magic("基础水伤")} + {DotaHelpColor.Stat("0.35× ~ 1.50× 敏捷")}（随敏/力比动态缩放），附带击退与{DotaHelpColor.Control("力量比眩晕")}"
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            int Idx = Tier - 1;
            Dice BaseDice = DotaMechanicsMapper.MapSkillDamageDice(AdaptiveStrikeDotaDamages[Idx]);
            int MaxStun = DotaMechanicsMapper.MapDurationTurns(1.0f + Tier * 0.5f);
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = $"施咒 范围 {AdaptiveStrikeBaseRanges[Idx]}",
              Bullets = new[]
              {
                $"基础水伤 {DotaHelpColor.Magic(DotaSpellHelpBridge.FormatDiceWithRange(BaseDice))}，眩晕 {DotaHelpColor.Control($"1~{MaxStun} 回合")}"
              }
            });
          }
          return List;
        }
      });

      // 3. E1：属性转换·敏捷
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "morph agility",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Step = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation) + GetAttributeShiftExtraStep(Ch);
          int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength");
          int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Ch, "constitution");
          int CurDex = PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity");
          int MaxDex = PathosEngineBridge.GetLimitAbilityScore(Ch, "dexterity");

          return new List<(string, string)>
          {
            ("当前单次转换", $"{DotaHelpColor.Stat($"力量/体质 -{Step}")} → {DotaHelpColor.Stat($"敏捷 +{Step}")}（{DotaHelpColor.Mana("0 蓝耗")}）"),
            ("血量联动", $"最大生命 {DotaHelpColor.Physical($"-{Step * HpPerShiftedPoint} HP")}（当前生命不扣减，仅超过新上限时截断）"),
            ("当前属性", $"{DotaHelpColor.Stat($"力量 {CurStr}")} / {DotaHelpColor.Stat($"敏捷 {CurDex}")}（上限 {MaxDex}）/ {DotaHelpColor.Stat($"体质 {CurCon}")}（最低保留 {MinAttributeScore}）")
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          $"总量守恒（力量/体质最低保留 {MinAttributeScore}），最大生命最低保留 {MinMaxLifePoints} HP，当前生命不主动扣减"
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = "自身施法 0 蓝耗",
              Bullets = new[]
              {
                $"单次转换 {DotaHelpColor.Stat($"{Tier} 点")}（最大生命 {DotaHelpColor.Physical($"-{Tier * HpPerShiftedPoint} HP")}）"
              }
            });
          }
          return List;
        }
      });

      // 4. E2：属性转换·力量
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "morph strength",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Step = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation) + GetAttributeShiftExtraStep(Ch);
          int CurStr = PathosEngineBridge.GetCurrentAbilityScore(Ch, "strength");
          int CurCon = PathosEngineBridge.GetCurrentAbilityScore(Ch, "constitution");
          int CurDex = PathosEngineBridge.GetCurrentAbilityScore(Ch, "dexterity");
          int MaxStr = PathosEngineBridge.GetLimitAbilityScore(Ch, "strength");

          return new List<(string, string)>
          {
            ("当前单次转换", $"{DotaHelpColor.Stat($"敏捷 -{Step}")} → {DotaHelpColor.Stat($"力量/体质 +{Step}")}（{DotaHelpColor.Mana("0 蓝耗")}）"),
            ("血量联动", $"最大生命与当前生命同步 {DotaHelpColor.Heal($"+{Step * HpPerShiftedPoint} HP")}"),
            ("当前属性", $"{DotaHelpColor.Stat($"力量 {CurStr}")}（上限 {MaxStr}）/ {DotaHelpColor.Stat($"敏捷 {CurDex}")}（最低保留 {MinAttributeScore}）/ {DotaHelpColor.Stat($"体质 {CurCon}")}")
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          $"总量守恒（敏捷最低保留 {MinAttributeScore}），转力同时提升最大生命与当前生命"
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = "自身施法 0 蓝耗",
              Bullets = new[]
              {
                $"单次转换 {DotaHelpColor.Stat($"{Tier} 点")}（最大与当前生命同步 {DotaHelpColor.Heal($"+{Tier * HpPerShiftedPoint} HP")}）"
              }
            });
          }
          return List;
        }
      });

      // 5. R1：变形
      DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
      {
        SpellName = "morph",
        ResolveCurrentTier = Ch => DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          int Tier = DotaCombatHelper.GetSkillTier(Ctx, Ch, Ctx.Skills.transmutation);
          int BaseRange = MorphBaseRanges[Math.Clamp(Tier - 1, 0, 4)];
          int StrBonus = GetStrRangeBonus(Ch);
          int StarRange = GetMorphExtraRange(Ch);
          int TotalRange = BaseRange + StrBonus + StarRange;

          return new List<(string, string)>
          {
            ("实际施法射程", (StrBonus > 0 || StarRange > 0)
              ? $"{DotaHelpColor.Control($"{TotalRange} 格")}（基础 {BaseRange}{(StrBonus > 0 ? $" + {DotaHelpColor.Passive($"潮涨潮落 +{StrBonus}")}" : "")}{(StarRange > 0 ? $" + {DotaHelpColor.Skill($"升星 +{StarRange}")}" : "")}）"
              : DotaHelpColor.Control($"{BaseRange} 格")),
            ("变形机制", $"化身目标敌方生物，复制其外形、属性、技能与法术，且全程保留先天{DotaHelpColor.Passive("「潮涨潮落」")}")
          };
        },
        BuildMechanicsNotes = _ => new[]
        {
          $"复制目标外形、属性、技能与法术，全程保留先天{DotaHelpColor.Passive("「潮涨潮落」")}，可随时「解除变形」恢复本体"
        },
        BuildTiers = _ =>
        {
          var List = new List<DotaSpellHelpTier>(5);
          for (int Tier = 1; Tier <= 5; Tier++)
          {
            List.Add(new DotaSpellHelpTier
            {
              Tier = Tier,
              HeaderSuffix = $"施咒 范围 {MorphBaseRanges[Tier - 1]}",
              Bullets = new[]
              {
                $"基础施法范围 {DotaHelpColor.Control($"{MorphBaseRanges[Tier - 1]} 格")}（受{DotaHelpColor.Passive("潮涨潮落")}力量加成）"
              }
            });
          }
          return List;
        }
      });
    }

    private static Property RegisterEbbAndFlowTalent(DotaHeroContext Ctx, Glyph Icon)
      => Ctx.RegisterPassiveProperty(
        Name: "ebb and flow",
        Description: "Innate (Ebb & Flow): Agility grants bonus movement speed and ranged attack range; Strength grants bonus spell cast range and slow resistance. Persists while morphed.",
        Icon: Icon);

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
        S.Level = DotaMechanicsMapper.MapSpellLevel(WaveformManaCost);
        S.Mana = WaveformManaCost;

        void Configure(Qualification Qual, int Range, Dice WaveDamage, int WaveRadius, int EvasionTurns)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Plain(Dice.Fixed(Range));
          Adept.Apply.BlinkEntity();
          Adept.Apply.Shout(Dice.Fixed(WaveRadius), Wave => Wave.HarmEntity(Ctx.Elements.water, WaveDamage));
          Adept.Apply.ApplyTransient(Ctx.Properties.phasing, Dice.Fixed(EvasionTurns));
          Adept.Apply.ApplyTransient(Ctx.Properties.displacement, Dice.Fixed(EvasionTurns));
        }

        // DOTA2 7.42 波浪形态基础伤害：75 / 150 / 225 / 300（Champion 360）经 K_dmg = 15 自动生成伤害骰
        Configure(Ctx.Qualifications.proficient, Range: 5, WaveDamage: DotaMechanicsMapper.MapSkillDamageDice(75), WaveRadius: 2, EvasionTurns: 3);
        Configure(Ctx.Qualifications.specialist, Range: 6, WaveDamage: DotaMechanicsMapper.MapSkillDamageDice(150), WaveRadius: 2, EvasionTurns: 4);
        Configure(Ctx.Qualifications.expert, Range: 7, WaveDamage: DotaMechanicsMapper.MapSkillDamageDice(225), WaveRadius: 2, EvasionTurns: 4);
        Configure(Ctx.Qualifications.master, Range: 9, WaveDamage: DotaMechanicsMapper.MapSkillDamageDice(300), WaveRadius: 3, EvasionTurns: 5);
        Configure(Ctx.Qualifications.champion, Range: 11, WaveDamage: DotaMechanicsMapper.MapSkillDamageDice(360), WaveRadius: 3, EvasionTurns: 6);
      });
    }

    private static Spell RegisterAdaptiveStrikeSpell(DotaHeroContext Ctx, Glyph Icon)
    {
      return Ctx.Spells.Add(S =>
      {
        S.Name = "adaptive strike";
        S.Description = "Launch a surge of water dealing base damage, knocking back, and stunning the target. Bonus damage scales with Agility-to-Strength ratio while stun duration scales with Strength-to-Agility ratio.";
        S.Glyph = Icon;
        S.School = Ctx.Schools.evocation;
        S.Precept = new Precept(Purpose.Blast, Ctx.Elements.water);
        S.Level = DotaMechanicsMapper.MapSpellLevel(AdaptiveStrikeManaCost);
        S.Mana = AdaptiveStrikeManaCost;

        void Configure(Qualification Qual, int Range, Dice BaseDamage)
        {
          var Adept = S.SetAdept(Qual);
          Adept.SetCast().Explosion(Ctx.Explosions.watery, Dice.Fixed(Range));
          // 基础伤害 + 击退 + 眩晕占位；额外敏捷爆发伤害与力量眩晕时长由 Hook 按实时力敏比动态结算
          Adept.Apply.HarmEntity(Ctx.Elements.water, BaseDamage);
          Adept.Apply.Knockback();
          Adept.Apply.ApplyTransient(Ctx.Properties.stunned, Dice.One);
        }

        // DOTA2 7.42 变体打击基础伤害：50 / 70 / 90 / 110（Champion 130）经 K_dmg = 15 自动生成伤害骰
        Configure(Ctx.Qualifications.proficient, 5, DotaMechanicsMapper.MapSkillDamageDice(50));
        Configure(Ctx.Qualifications.specialist, 6, DotaMechanicsMapper.MapSkillDamageDice(70));
        Configure(Ctx.Qualifications.expert, 7, DotaMechanicsMapper.MapSkillDamageDice(90));
        Configure(Ctx.Qualifications.master, 8, DotaMechanicsMapper.MapSkillDamageDice(110));
        Configure(Ctx.Qualifications.champion, 9, DotaMechanicsMapper.MapSkillDamageDice(130));
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
        S.Level = DotaMechanicsMapper.MapSpellLevel(AttributeShiftManaCost);
        S.Mana = AttributeShiftManaCost;

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
        S.Level = DotaMechanicsMapper.MapSpellLevel(AttributeShiftManaCost);
        S.Mana = AttributeShiftManaCost;

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
        S.Level = DotaMechanicsMapper.MapSpellLevel(MorphManaCost);
        S.Mana = MorphManaCost;

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
        S.Level = DotaMechanicsMapper.MapSpellLevel(MorphRevertManaCost);
        S.Mana = MorphRevertManaCost;

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

        Ctx.SetFormScores(
          E.DefaultForm,
          Str: StartingStats.Str,
          Dex: StartingStats.Dex,
          Con: StartingStats.Con,
          Int: StartingStats.Int,
          Wis: StartingStats.Wis,
          Cha: StartingStats.Cha);
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
      Spell AdaptiveStrikeSpell,
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
        int ClassManaBase = DotaMechanicsMapper.ComputeClassManaBase(DotaBaseInt, RaceManaBase: 2, StartingStats.Wis);
        C.ManaAdvancement.Set(ClassManaBase, new Dice(1, 3, 0));

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
