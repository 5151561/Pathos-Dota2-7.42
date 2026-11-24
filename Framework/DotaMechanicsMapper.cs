using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// Pathos 英雄固定初始六维属性。
  /// </summary>
  public readonly struct PathosStartingStats
  {
    public PathosStartingStats(int Str, int Dex, int Con, int Int, int Wis, int Cha)
    {
      this.Str = Str;
      this.Dex = Dex;
      this.Con = Con;
      this.Int = Int;
      this.Wis = Wis;
      this.Cha = Cha;
    }

    public int Str { get; }
    public int Dex { get; }
    public int Con { get; }
    public int Int { get; }
    public int Wis { get; }
    public int Cha { get; }
  }

  /// <summary>
  /// Pathos 英雄职业专属六维属性上限（由 DOTA2 7.42 30 级自然属性推导，通过 Hook 覆盖种族默认 LimitForm）。
  /// </summary>
  public readonly struct PathosStatLimits
  {
    public PathosStatLimits(int Str, int Dex, int Con, int Int, int Wis, int Cha)
    {
      this.Str = Str;
      this.Dex = Dex;
      this.Con = Con;
      this.Int = Int;
      this.Wis = Wis;
      this.Cha = Cha;
    }

    public int Str { get; }
    public int Dex { get; }
    public int Con { get; }
    public int Int { get; }
    public int Wis { get; }
    public int Cha { get; }
  }

  /// <summary>
  /// DOTA2 → Pathos 核心数值与机制统一换算工具：
  /// 1. 初始六维属性：线性锚定公式（12 + (x - 20) * 0.45，裁剪至 [6, 18]），开局/重摇自动锁定，告别 4d6 摇骰子。
  /// 2. 蓝量池与技能蓝耗：统一标尺 K_mana = 20（20 点 DOTA2 魔法 = 1 点 Pathos 法力），0 蓝耗技能忠实保留 0 蓝耗。
  /// 3. 技能伤害：统一标尺 K_dmg = 15（15 点 DOTA2 伤害 = 1 点 Pathos 伤害），持续伤害（DPS）按 1 秒 = 2 回合（每回合 0.5 秒）折算。
  /// 4. 护甲（被动加甲 / 减甲）：统一映射为 Pathos Defence（分段换算，防挨打吃解离/撤销/附带特效），敏捷护甲直接复用原生 DEX 机制。
  /// </summary>
  public static class DotaMechanicsMapper
  {
    public const float ManaScale = 20.0f;
    public const float DamageScale = 15.0f;   // 15 点 DOTA2 技能伤害 = 1 点 Pathos 技能伤害
    public const float TurnsPerSecond = 2.0f; // 1 秒 DOTA2 时间 = 2 回合 Pathos
    public const int DotaBaseMana = 75;
    public const int DotaManaPerInt = 12;

    private static readonly ConcurrentDictionary<string, PathosStartingStats> FixedClassStats =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, PathosStatLimits> FixedClassLimits =
      new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConditionalWeakTable<Form, ConcurrentDictionary<string, Form>> CachedLimitForms = new();

    /// <summary>
    /// 线性锚定公式：将 DOTA2 英雄初始单项属性映射为 Pathos 初始属性（6 ~ 18）。
    /// </summary>
    public static int MapAttribute(int DotaBaseValue)
    {
      return Math.Clamp((int)MathF.Round(12.0f + (DotaBaseValue - 20) * 0.45f), 6, 18);
    }

    /// <summary>
    /// 将 DOTA2 英雄 1 级三维（力/敏/智）映射为 Pathos 固定初始六维。
    /// DOTA2 力量 -> STR &amp; CON；敏捷 -> DEX；智力 -> INT &amp; WIS；CHA 默认 10。
    /// </summary>
    public static PathosStartingStats MapStartingStats(int BaseStr, int BaseAgi, int BaseInt, int Charisma = 10)
    {
      int StrCon = MapAttribute(BaseStr);
      int Dex = MapAttribute(BaseAgi);
      int IntWis = MapAttribute(BaseInt);
      int Cha = Math.Clamp(Charisma, 6, 18);
      return new PathosStartingStats(Str: StrCon, Dex: Dex, Con: StrCon, Int: IntWis, Wis: IntWis, Cha: Cha);
    }

    /// <summary>
    /// 按 DOTA2 英雄 30 级自然属性（Base + 29 × Gain）推导 Pathos 单项属性上限（20 ~ 34）：
    /// - 锚定公式：Clamp(Round(25 + (Lv30 - 80) * 0.12), 20, 34)
    /// - 保底不低于 StartingScore + 3，确保 7 级三系轮转黄点（单项最多 +3）100% 有效不撞墙，同时防止突破 Pathos d20 平衡。
    /// </summary>
    public static int MapAttributeLimit(int DotaBaseValue, float DotaGainPerLevel, int StartingScore)
    {
      float Lv30 = DotaBaseValue + 29.0f * Math.Max(0.0f, DotaGainPerLevel);
      int Mapped = Math.Clamp((int)MathF.Round(25.0f + (Lv30 - 80.0f) * 0.12f), 20, 34);
      int MinForYellowBonus = Math.Min(34, Math.Max(20, StartingScore + 3));
      return Math.Max(Mapped, MinForYellowBonus);
    }

    /// <summary>
    /// 将 DOTA2 英雄 7.42 三维初始值与每级成长映射为 Pathos 职业专属六维属性上限。
    /// </summary>
    public static PathosStatLimits MapStatLimits(
      int BaseStr,
      float StrGain,
      int BaseAgi,
      float AgiGain,
      int BaseInt,
      float IntGain,
      int CharismaLimit = 20,
      int StartingCha = 10)
    {
      var Starting = MapStartingStats(BaseStr, BaseAgi, BaseInt, StartingCha);
      int StrConLimit = MapAttributeLimit(BaseStr, StrGain, Starting.Str);
      int DexLimit = MapAttributeLimit(BaseAgi, AgiGain, Starting.Dex);
      int IntWisLimit = MapAttributeLimit(BaseInt, IntGain, Starting.Int);
      int ChaLimit = Math.Clamp(Math.Max(CharismaLimit, Starting.Cha + 1), 20, 34);
      return new PathosStatLimits(
        Str: StrConLimit,
        Dex: DexLimit,
        Con: StrConLimit,
        Int: IntWisLimit,
        Wis: IntWisLimit,
        Cha: ChaLimit);
    }

    /// <summary>
    /// Pathos 原生属性修正值公式：Floor((Score - 10) / 2)。
    /// </summary>
    public static int GetAbilityModifier(int Score)
    {
      return (int)Math.Floor((Score - 10) / 2.0);
    }

    /// <summary>
    /// 计算 DOTA2 英雄 1 级在 Pathos 中的目标初始法力值：Round((75 + 12 * BaseInt) / 20)。
    /// </summary>
    public static int MapStartingMana(int DotaBaseInt)
    {
      return Math.Max(1, (int)MathF.Round((DotaBaseMana + DotaManaPerInt * DotaBaseInt) / ManaScale));
    }

    /// <summary>
    /// 反推职业 ManaAdvancement.HardBase，使 1 级实际初始蓝量（ClassBase + RaceBase + WIS修正）精准等于目标值。
    /// </summary>
    public static int ComputeClassManaBase(int DotaBaseInt, int RaceManaBase, int WisScore)
    {
      int Target = MapStartingMana(DotaBaseInt);
      int WisMod = GetAbilityModifier(WisScore);
      return Math.Max(1, Target - RaceManaBase - WisMod);
    }

    /// <summary>
    /// 将 DOTA2 主动技能蓝耗映射为 Pathos Spell.Mana（0 蓝耗忠实返回 0）。
    /// </summary>
    public static int MapSkillManaCost(int DotaManaCost)
    {
      if (DotaManaCost <= 0)
        return 0;

      return Math.Max(1, (int)MathF.Round(DotaManaCost / ManaScale));
    }

    /// <summary>
    /// 将 DOTA2 持续时间（秒）映射为 Pathos 回合数（1 秒 = 2 回合）。
    /// </summary>
    public static int MapDurationTurns(float DotaSeconds, int MinTurns = 1)
    {
      if (DotaSeconds <= 0.0f)
        return 0;

      return Math.Max(MinTurns, (int)MathF.Round(DotaSeconds * TurnsPerSecond));
    }

    /// <summary>
    /// 将 DOTA2 持续每秒耗蓝（Toggle）按「1 秒 = 2 回合（每回合 0.5 秒）」折算为每回合 Pathos 蓝耗。
    /// </summary>
    public static int MapToggleManaPerTurn(float DotaManaPerSecond)
    {
      if (DotaManaPerSecond <= 0.0f)
        return 0;

      return Math.Max(1, (int)MathF.Round((DotaManaPerSecond / TurnsPerSecond) / ManaScale));
    }

    /// <summary>
    /// 将 Pathos 技能蓝耗对齐至原生法术等级（1 ~ 7，对应 5 / 10 / 15 ... 35 阶梯）。
    /// </summary>
    public static int MapSpellLevel(int PathosManaCost)
    {
      return Math.Clamp((int)MathF.Round(PathosManaCost / 5.0f), 1, 7);
    }

    /// <summary>
    /// 将 DOTA2 技能单次伤害映射为 Pathos 伤害期望（统一标尺 K_dmg = 15）。
    /// </summary>
    public static int MapSkillDamage(float DotaDamage, int MinDamage = 1)
    {
      if (DotaDamage <= 0.0f)
        return 0;

      return Math.Max(MinDamage, (int)MathF.Round(DotaDamage / DamageScale, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// 将 DOTA2 持续伤害（每秒 DPS）按「1 秒 = 2 回合（每回合 0.5 秒）」与 K_dmg = 15 折算为 Pathos 每回合伤害期望。
    /// </summary>
    public static int MapDotDamagePerTurn(float DotaDamagePerSecond, int MinDamage = 1)
    {
      if (DotaDamagePerSecond <= 0.0f)
        return 0;

      return MapSkillDamage(DotaDamagePerSecond / TurnsPerSecond, MinDamage);
    }

    /// <summary>
    /// 将 DOTA2 技能伤害映射为期望贴合「DotaDamage / K_dmg」的 Pathos 伤害骰（NdF + Bonus，Bonus >= 0）。
    /// </summary>
    public static Dice MapSkillDamageDice(float DotaDamage, int DieFaces = 6, int MinDamage = 1)
    {
      if (DotaDamage <= 0.0f)
        return Dice.Zero;

      int Faces = Math.Max(2, DieFaces);
      float TargetAvg = Math.Max(MinDamage, DotaDamage / DamageScale);
      float AvgPerDie = (Faces + 1) * 0.5f;

      int Number = Math.Max(1, (int)MathF.Floor(TargetAvg / AvgPerDie));
      int Bonus = Math.Max(0, (int)MathF.Round(TargetAvg - Number * AvgPerDie, MidpointRounding.AwayFromZero));
      return new Dice(Number, Faces, Bonus);
    }

    /// <summary>
    /// 将 DOTA2 持续伤害（每秒 DPS）按每回合 0.5 秒折算并生成对应 Pathos 伤害骰。
    /// </summary>
    public static Dice MapDotDamageDicePerTurn(float DotaDamagePerSecond, int DieFaces = 4, int MinDamage = 1)
    {
      if (DotaDamagePerSecond <= 0.0f)
        return Dice.Zero;

      return MapSkillDamageDice(DotaDamagePerSecond / TurnsPerSecond, DieFaces, MinDamage);
    }

    /// <summary>
    /// 根据 DOTA2 技能减冷却秒数与基准冷却（通常取满级冷却）推导减冷却百分比（0.0 ~ 1.0）。
    /// </summary>
    public static float ComputeCooldownReductionRatio(float ReducedSeconds, float ReferenceCooldownSeconds)
    {
      if (ReducedSeconds <= 0.0f || ReferenceCooldownSeconds <= 0.0f)
        return 0.0f;

      return Math.Clamp(ReducedSeconds / ReferenceCooldownSeconds, 0.0f, 1.0f);
    }

    /// <summary>
    /// DOTA2 减冷却（CDR）→ Pathos 减蓝耗统一换算：
    /// - 0 蓝耗技能保持 0 蓝耗。
    /// - 正蓝耗技能按 (1 - CDR%) 缩减并四舍五入，保底为 1。
    /// - 小蓝耗保底生效：只要 CDR% > 0 且原蓝耗 > 1，至少减少 1 点蓝耗，确保天赋收益肉眼可见。
    /// - 多来源叠加默认封顶减耗 60%，防止蓝耗趋零破坏平衡。
    /// </summary>
    public static int MapCooldownReductionToManaCost(int BaseManaCost, float CdrRatio, float MaxReductionCap = 0.60f)
    {
      if (BaseManaCost <= 0)
        return 0;

      if (CdrRatio <= 0.0001f)
        return BaseManaCost;

      float EffectiveRatio = Math.Clamp(CdrRatio, 0.0f, Math.Clamp(MaxReductionCap, 0.0f, 0.95f));
      int Reduced = Math.Max(1, (int)MathF.Round(BaseManaCost * (1.0f - EffectiveRatio)));

      if (BaseManaCost > 1 && Reduced >= BaseManaCost)
        Reduced = BaseManaCost - 1;

      return Reduced;
    }

    /// <summary>
    /// 将 DOTA2 护甲加成/削减映射为 Pathos Defence 修正值（分段递减，防止击穿 d20 命中阈值）：
    /// - 12 甲以内：2.5 点 DOTA2 护甲 = 1 点 Defence
    /// - 超出 12 甲：4.0 点 DOTA2 护甲 = 1 点 Defence
    /// - 默认正负封顶 [+8, -8]
    /// </summary>
    public static int MapArmorToDefence(float DotaArmorBonus, int MaxPositiveCap = 8, int MaxNegativeCap = -8)
    {
      if (MathF.Abs(DotaArmorBonus) < 0.01f)
        return 0;

      float Abs = MathF.Abs(DotaArmorBonus);
      float Scaled = Abs <= 12.0f
        ? Abs / 2.5f
        : (12.0f / 2.5f) + (Abs - 12.0f) / 4.0f;

      int Delta = (int)MathF.Round(Scaled);
      if (DotaArmorBonus < 0.0f)
        Delta = -Delta;

      return Math.Clamp(Delta, MaxNegativeCap, MaxPositiveCap);
    }

    /// <summary>
    /// 为指定 DOTA2 职业注册固定初始六维属性。
    /// 在 Engine.CreateActor 与 Engine.RerollActor 后自动写入 CurrentForm/MaximumForm 并同步重算 1 级初始 HP 与 Mana。
    /// </summary>
    public static void RegisterFixedStartingStats(string ClassName, PathosStartingStats Stats)
    {
      if (string.IsNullOrWhiteSpace(ClassName))
        throw new ArgumentException("ClassName cannot be null or empty.", nameof(ClassName));

      FixedClassStats[ClassName] = Stats;
      DotaHookBus.RegisterAfterCreateActor("dota.fixed_starting_stats", HandleAfterCreateActor);
    }

    /// <summary>
    /// 为指定 DOTA2 职业注册专属六维属性上限（LimitForm）。
    /// 通过 Character.GetLimitForm Postfix 在非变形状态下接管上限，使种族只负责风味与生态、英雄职业负责 DOTA2 30 级成长空间。
    /// </summary>
    public static void RegisterHeroStatLimits(string ClassName, PathosStatLimits Limits)
    {
      if (string.IsNullOrWhiteSpace(ClassName))
        throw new ArgumentException("ClassName cannot be null or empty.", nameof(ClassName));

      FixedClassLimits[ClassName] = Limits;
      DotaHookBus.RegisterLimitFormQuery("dota.hero_stat_limits", HandleLimitFormQuery);
    }

    private static void HandleLimitFormQuery(LimitFormQueryEventArgs Args)
    {
      var Character = Args?.Character;
      var BaseLimitForm = Args?.LimitForm;
      if (Character == null || BaseLimitForm == null)
        return;

      // 变形期间（如变形术、水人 Morph）保留目标生物自身的属性上限
      if (PathosEngineBridge.IsPolymorphed(Character))
        return;

      var ClassName = Character.Class?.Name;
      if (string.IsNullOrEmpty(ClassName) || !FixedClassLimits.TryGetValue(ClassName, out var Limits))
        return;

      var PerTemplate = CachedLimitForms.GetOrCreateValue(BaseLimitForm);
      Args.LimitForm = PerTemplate.GetOrAdd(
        ClassName,
        _ => PathosEngineBridge.CloneFormWithScores(
          BaseLimitForm,
          Limits.Str,
          Limits.Dex,
          Limits.Con,
          Limits.Int,
          Limits.Wis,
          Limits.Cha));
    }

    private static void HandleAfterCreateActor(ActorCreateEventArgs Args)
    {
      var Character = Args?.Character;
      var ClassName = Character?.Class?.Name;
      if (string.IsNullOrEmpty(ClassName))
        return;

      if (!FixedClassStats.TryGetValue(ClassName, out var Stats))
        return;

      ApplyStatClamped(Character, "strength", Stats.Str);
      ApplyStatClamped(Character, "dexterity", Stats.Dex);
      ApplyStatClamped(Character, "constitution", Stats.Con);
      ApplyStatClamped(Character, "intelligence", Stats.Int);
      ApplyStatClamped(Character, "wisdom", Stats.Wis);
      ApplyStatClamped(Character, "charisma", Stats.Cha);

      // 按 Pathos 原生公式重算 1 级初始 Life 与 Mana，消除摇骰子残留的旧 CON/WIS 修正
      int FinalCon = PathosEngineBridge.GetCurrentAbilityScore(Character, "constitution");
      int FinalWis = PathosEngineBridge.GetCurrentAbilityScore(Character, "wisdom");

      int ClassLifeBase = Character.Class?.LifeAdvancement?.HardBase ?? 10;
      int RaceLifeBase = Character.Entity?.LifeAdvancement?.HardBase ?? 0;
      int InitialLife = Math.Max(1, ClassLifeBase + RaceLifeBase + GetAbilityModifier(FinalCon));

      int ClassManaBase = Character.Class?.ManaAdvancement?.HardBase ?? 1;
      int RaceManaBase = Character.Entity?.ManaAdvancement?.HardBase ?? 0;
      int InitialMana = Math.Max(0, ClassManaBase + RaceManaBase + GetAbilityModifier(FinalWis));

      if (Character.Life != null)
        PathosEngineBridge.SetMeter(Character.Life, InitialLife, InitialLife);

      if (Character.Mana != null)
        PathosEngineBridge.SetMeter(Character.Mana, InitialMana, InitialMana);
    }

    private static void ApplyStatClamped(Character Character, string AttributeName, int TargetScore)
    {
      int Limit = PathosEngineBridge.GetLimitAbilityScore(Character, AttributeName);
      int Clamped = Math.Clamp(TargetScore, 3, Math.Max(3, Limit));
      PathosEngineBridge.SetPermanentAbilityScore(Character, AttributeName, Clamped);
    }
  }
}
