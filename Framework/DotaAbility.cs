using System;
using System.Collections.Concurrent;
using System.Threading;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Skills;
using Pathos.DotaHeroes.Framework.Targeting;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 技能的施放目标方式。
  /// </summary>
  public enum DotaAbilityTargeting
  {
    /// <summary>对自身施放（无射程、无全射程选格）。</summary>
    Self,

    /// <summary>指向敌方单位（全射程选敌 + 点击敌人旁 1 格吸附）。</summary>
    UnitHostile,

    /// <summary>指向可站立空地（如点地召唤）。</summary>
    PointOpen,

    /// <summary>指向任意格（如波浪形态可点墙/敌人后自动寻找落点）。</summary>
    PointAny
  }

  /// <summary>
  /// DOTA2 技能的一次性声明（每个技能一份），由 <see cref="DotaAbility.Register"/> 自动生成：
  /// 1. 5 阶熟练度的声明式 Pathos 法术外壳（射程按 <see cref="Range"/> 逐阶写入，<c>Apply</c> 默认 <c>Nothing()</c>）；
  /// 2. 全射程选格（<see cref="DotaRangeTargetingBridge"/>，射程 = 熟练度基础射程 + <see cref="RangeBonus"/>）；
  /// 3. 按法术名分发的施法回调 <see cref="OnCast"/>（替代各英雄 AfterCastSpell + if/else 比较法术名）。
  /// </summary>
  public sealed class DotaAbilitySpec
  {
    /// <summary>Pathos 法术名（全局唯一，同时作为伤害来源 Key 与帮助面板 Key）。</summary>
    public string SpellName { get; init; }

    /// <summary>全射程选格标题（英文回退）。</summary>
    public string DisplayTitle { get; init; }

    /// <summary>全射程选格标题（本地化）。</summary>
    public Func<string> TitleResolver { get; init; }

    public string Description { get; init; }
    public Glyph Icon { get; init; }

    /// <summary>法术所属学派（Pathos Spell.School）。</summary>
    public School School { get; init; }

    /// <summary>决定熟练度阶数的技能（通常与 <see cref="School"/> 同名，如 <c>Ctx.Skills.evocation</c>）。</summary>
    public Skill Skill { get; init; }

    public Precept Precept { get; init; }

    /// <summary>已经过 <c>DotaMechanicsMapper.MapSkillManaCost</c> 换算的 Pathos 蓝耗。</summary>
    public int ManaCost { get; init; }

    public DotaAbilityTargeting Targeting { get; init; } = DotaAbilityTargeting.Self;

    /// <summary>5 阶熟练度基础射程（<see cref="DotaAbilityTargeting.Self"/> 可省略）。</summary>
    public DotaTable<int> Range { get; init; }

    /// <summary>选格是否允许点自身所在格（如治疗守卫）。</summary>
    public bool AllowSelfTarget { get; init; }

    /// <summary>射程额外加成（升星、天赋、先天属性等），叠加在熟练度基础射程之上。</summary>
    public Func<Character, int> RangeBonus { get; init; }

    /// <summary>对应 <c>HeroSkillBuild</c> 的技能槽 ID（用于 <see cref="DotaAbility.Level"/> 与升星查询）。</summary>
    public string SkillSlotId { get; init; }

    /// <summary>
    /// 按技能等级（1 起始）的 DOTA2 7.42 原版冷却秒数表（经 <c>DotaMechanicsMapper.MapCooldownTurns</c> 按 1 秒 = 1 回合换算）；
    /// 为 null 表示无冷却（如开关技能、0 CD 技能）。
    /// </summary>
    public DotaTable<float> CooldownSeconds { get; init; }

    /// <summary>
    /// 可选动态冷却秒数解析器（参数：施法者、技能等级），用于神杖等会改动基础冷却的场景；为 null 时使用 <see cref="CooldownSeconds"/>。
    /// </summary>
    public Func<Character, int, float> CooldownSecondsResolver { get; init; }

    /// <summary>
    /// 自定义声明式效果（参数：熟练度阶数 1~5、ApplyEditor）；为 null 时写入 <c>Apply.Nothing()</c>，全部效果由 <see cref="OnCast"/> 结算。
    /// </summary>
    public Action<int, ApplyEditor> ConfigureApply { get; init; }

    /// <summary>施法成功后的结算回调（AfterCastSpell，按法术名精确分发）。</summary>
    public Action<DotaAbilityCast> OnCast { get; init; }
  }

  /// <summary>
  /// 已注册的 DOTA2 技能：持有生成的 Pathos <see cref="Spell"/>，并统一提供熟练度阶数、技能等级、升星与射程查询。
  /// </summary>
  public sealed class DotaAbility
  {
    private static readonly ConcurrentDictionary<string, DotaAbility> ByName = new(StringComparer.OrdinalIgnoreCase);
    private static int DispatchInitialized;

    private readonly DotaHeroContext Ctx;

    private DotaAbility(DotaHeroContext Ctx, DotaAbilitySpec Spec, Spell Spell)
    {
      this.Ctx = Ctx;
      this.Spec = Spec;
      this.Spell = Spell;
    }

    public DotaAbilitySpec Spec { get; }
    public Spell Spell { get; }
    public string Name => Spec.SpellName;
    public int ManaCost => Spec.ManaCost;

    /// <summary>
    /// 声明一个技能：生成 5 阶法术外壳、注册全射程选格与施法分发。在英雄 <c>Register</c> 中调用。
    /// </summary>
    public static DotaAbility Register(DotaHeroContext Ctx, DotaAbilitySpec Spec)
    {
      if (Ctx == null)
        throw new ArgumentNullException(nameof(Ctx));
      if (Spec == null || string.IsNullOrWhiteSpace(Spec.SpellName))
        throw new ArgumentException("Ability spec requires a SpellName.", nameof(Spec));
      if (Spec.Targeting != DotaAbilityTargeting.Self && Spec.Range == null)
        throw new ArgumentException($"Ability '{Spec.SpellName}' is targeted but has no Range table.", nameof(Spec));

      var Spell = Ctx.Spells.Add(S =>
      {
        S.Name = Spec.SpellName;
        S.Description = Spec.Description;
        S.Glyph = Spec.Icon;
        S.School = Spec.School;
        S.Precept = Spec.Precept;
        S.Level = DotaMechanicsMapper.MapSpellLevel(Spec.ManaCost);
        S.Mana = Spec.ManaCost;

        var Qualifications = new[]
        {
          Ctx.Qualifications.proficient,
          Ctx.Qualifications.specialist,
          Ctx.Qualifications.expert,
          Ctx.Qualifications.master,
          Ctx.Qualifications.champion
        };

        for (int Tier = 1; Tier <= Qualifications.Length; Tier++)
        {
          var Adept = S.SetAdept(Qualifications[Tier - 1]);
          var Cast = Adept.SetCast();
          if (Spec.Targeting == DotaAbilityTargeting.Self)
          {
            Cast.SetTargetSelf(true);
          }
          else
          {
            // 声明式射线仅用于触发原生扣蓝与全射程选格，真实效果在 OnCast 中精确结算（避免 Plain 射线每格重复 Apply）
            Cast.Plain(Dice.Fixed(Spec.Range[Tier]));
            if (Spec.AllowSelfTarget)
              Cast.SetTargetSelf(true);
          }

          if (Spec.ConfigureApply != null)
            Spec.ConfigureApply(Tier, Adept.Apply);
          else
            Adept.Apply.Nothing();
        }
      });

      var Ability = new DotaAbility(Ctx, Spec, Spell);

      if (Spec.Targeting != DotaAbilityTargeting.Self)
      {
        DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
          SpellName: Spec.SpellName,
          DisplayTitle: Spec.DisplayTitle ?? Spec.SpellName,
          Mode: Spec.Targeting switch
          {
            DotaAbilityTargeting.UnitHostile => DotaTargetMode.UnitHostile,
            DotaAbilityTargeting.PointOpen => DotaTargetMode.PointOpen,
            _ => DotaTargetMode.PointAny
          },
          ResolveRange: Ability.Range,
          AllowSelf: Spec.AllowSelfTarget,
          ResolveDisplayTitle: Spec.TitleResolver));
      }

      ByName[Spec.SpellName] = Ability;
      EnsureDispatch();
      return Ability;
    }

    /// <summary>按法术名查找已注册技能。</summary>
    public static bool TryGet(Spell Spell, out DotaAbility Ability)
    {
      Ability = null;
      return Spell?.Name != null && ByName.TryGetValue(Spell.Name, out Ability);
    }

    public bool Is(Spell Other) => Other != null && string.Equals(Other.Name, Spec.SpellName, StringComparison.OrdinalIgnoreCase);

    /// <summary>施法者在本技能所属技能上的熟练度阶数（1 Proficient .. 5 Champion）。</summary>
    public int Tier(Character Character) => DotaCombatHelper.GetSkillTier(Ctx, Character, Spec.Skill);

    /// <summary>当前 DOTA2 技能等级（技能加点，未学会 / 未绑定技能槽为 0）。</summary>
    public int Level(Character Character)
      => string.IsNullOrEmpty(Spec.SkillSlotId) ? 0 : HeroSkillService.GetSkillLevel(Character, Spec.SkillSlotId);

    /// <summary>技能等级并夹取到 [1, MaxLevel]（施法结算时常用：已能施放即至少 1 级）。</summary>
    public int Level(Character Character, int MaxLevel) => Math.Clamp(Level(Character), 1, Math.Max(1, MaxLevel));

    /// <summary>升星步数（Lv 1 为 0）。</summary>
    public int StarSteps(Character Character) => Math.Max(0, Level(Character) - 1);

    /// <summary>升星基础数值倍率（默认每星 +15%）。</summary>
    public float StarMultiplier(Character Character, float PerStep = HeroSkillService.DefaultStarBonusPerStep)
      => 1.0f + StarSteps(Character) * PerStep;

    /// <summary>指定熟练度阶数的基础射程（不含加成）。</summary>
    public int BaseRange(int Tier) => Spec.Range != null ? Spec.Range[Tier] : 0;

    /// <summary>施法者当前实际射程 = 熟练度基础射程 + <see cref="DotaAbilitySpec.RangeBonus"/>。</summary>
    public int Range(Character Character)
      => RangeAt(Character, Tier(Character));

    /// <summary>
    /// 施法者在指定熟练度阶数下的射程 = 该阶基础射程 + 当前加成（升星 / 属性 / 天赋）。
    /// 帮助面板熟练度表用它展示「按当前星级」的各阶射程，当前阶与 <see cref="Range"/> 一致；Character 为 null（百科）时只含基础射程。
    /// </summary>
    public int RangeAt(Character Character, int Tier)
      => BaseRange(Tier) + (Character != null && Spec.RangeBonus != null ? Spec.RangeBonus(Character) : 0);

    /// <summary>该技能是否配置了冷却机制。</summary>
    public bool HasCooldown => Spec.CooldownSeconds != null || Spec.CooldownSecondsResolver != null;

    /// <summary>指定技能等级的基础冷却回合数（不含天赋缩减）。</summary>
    public int BaseCooldownTurns(int Level)
    {
      if (Spec.CooldownSeconds == null)
        return 0;

      return DotaMechanicsMapper.MapCooldownTurns(Spec.CooldownSeconds[Level]);
    }

    /// <summary>施法者当前（或指定技能等级）的实际冷却回合数（含已学减冷却天赋）。</summary>
    public int CooldownTurns(Character Character, int? OverrideLevel = null)
      => DotaCooldownService.ComputeCooldownTurns(Character, this, OverrideLevel);

    /// <summary>查询施法者该技能当前的剩余冷却回合数（0 表示已就绪）。</summary>
    public int RemainingCooldown(Character Character)
      => DotaCooldownService.GetRemaining(Character, Spec.SpellName);

    /// <summary>启动该技能冷却（默认按当前角色实际冷却回合数）。</summary>
    public void StartCooldown(Character Character, int? OverrideTurns = null)
      => DotaCooldownService.Start(Character, Spec.SpellName, OverrideTurns ?? CooldownTurns(Character));

    /// <summary>立即重置该技能冷却（归零）。</summary>
    public void ResetCooldown(Character Character)
      => DotaCooldownService.Reset(Character, Spec.SpellName);

    private static void EnsureDispatch()
    {
      if (Interlocked.Exchange(ref DispatchInitialized, 1) != 0)
        return;

      DotaCooldownService.EnsureInitialized();

      DotaHookBus.RegisterAfterCastSpell("dota.abilities.cast", Args =>
      {
        if (Args?.Character == null || !TryGet(Args.Spell, out var Ability))
          return;

        var Cast = new DotaAbilityCast(Ability, Args);
        Ability.Spec.OnCast?.Invoke(Cast);

        if (!Cast.CooldownCancelled && Ability.HasCooldown)
          Ability.StartCooldown(Args.Character);
      });
    }
  }

  /// <summary>
  /// 一次技能施放的上下文：封装施法参数、熟练度/等级快照，以及「解析目标 → 失败则按实际扣蓝返还并写日志」的通用流程。
  /// </summary>
  public sealed class DotaAbilityCast
  {
    private bool PendingConsumed;
    private PendingSpellTarget Pending;

    internal DotaAbilityCast(DotaAbility Ability, SpellCastEventArgs Args)
    {
      this.Ability = Ability;
      this.Args = Args;
      this.Tier = Ability.Tier(Args.Character);
    }

    public DotaAbility Ability { get; }
    public SpellCastEventArgs Args { get; }
    public object Engine => Args.Engine;
    public Character Caster => Args.Character;
    public Direction Direction => Args.Direction;

    /// <summary>施法瞬间的熟练度阶数（1~5）。</summary>
    public int Tier { get; }

    /// <summary>施法者当前实际射程。</summary>
    public int Range => Ability.Range(Caster);

    /// <summary>取出（仅一次）全射程选格捕获的玩家点击目标。</summary>
    public PendingSpellTarget ConsumePendingTarget()
    {
      if (!PendingConsumed)
      {
        Pending = DotaRangeTargetingBridge.ConsumePendingTarget(Caster);
        PendingConsumed = true;
      }
      return Pending;
    }

    /// <summary>按「精确点击 → 1 格吸附 → 方向射线 → 射程内最近敌人」解析敌方目标（不返蓝）。</summary>
    public Character ResolveHostileTarget(bool AllowDirectionFallback = true)
      => DotaCombatHelper.ResolveHostileTarget(Caster, Direction, Range, ConsumePendingTarget(), AllowDirectionFallback);

    /// <summary>
    /// 解析敌方目标；失败时按本次实际扣蓝全额返还并写入日志（<paramref name="NoTargetLogKey"/> 为本地化 Key）。
    /// </summary>
    public bool TryResolveHostileTarget(out Character Target, string NoTargetLogKey, bool AllowDirectionFallback = true)
    {
      Target = ResolveHostileTarget(AllowDirectionFallback);
      if (Target != null && Target != Caster && !Target.Deceased && Target.Square != null)
        return true;

      Target = null;
      RefundAndLog(NoTargetLogKey);
      return false;
    }

    /// <summary>解析点地落点（点击格 → 方向相邻格 → 周围空位回退）。</summary>
    public Square ResolveOpenPointTarget()
      => DotaCombatHelper.ResolveOpenPointTarget(Caster, Direction, ConsumePendingTarget());

    internal bool CooldownCancelled { get; private set; }

    /// <summary>取消本次施法进入冷却（并重置当前冷却，用于空放退还或斩杀刷新）。</summary>
    public void SkipCooldown()
    {
      CooldownCancelled = true;
      Ability.ResetCooldown(Caster);
    }

    /// <summary>按本次实际扣蓝全额返还，并取消本次施法的冷却。</summary>
    public void Refund()
    {
      SkipCooldown();
      DotaCombatHelper.RefundSpellCost(Args);
    }

    /// <summary>返还法力并写一条带技能图标的日志。</summary>
    public void RefundAndLog(string LogKey)
    {
      Refund();
      if (!string.IsNullOrEmpty(LogKey))
        Log(DotaLocalization.T(LogKey));
    }

    /// <summary>以技能图标写入战斗日志。</summary>
    public void Log(string Text)
      => PathosEngineBridge.WriteInternalLog(Engine, Caster, Ability.Spec.Icon ?? Args.Spell?.Glyph, Text);
  }
}
