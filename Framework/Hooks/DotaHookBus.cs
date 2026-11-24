using System;
using System.Collections.Concurrent;

namespace Pathos.DotaHeroes.Framework.Hooks
{
  /// <summary>
  /// DOTA2 英雄统一 Hook 事件总线。
  /// 所有订阅按字符串 Key 幂等注册：即使 Pathos 多次重建 Campaign / Expansion，
  /// 相同 Key 只会覆盖旧处理器而不会重复触发。
  /// </summary>
  public static class DotaHookBus
  {
    [ThreadStatic]
    private static SpellCastEventArgs CurrentSpellCast;

    [ThreadStatic]
    private static AttackEventArgs CurrentAttack;

    /// <summary>
    /// 当前线程正在结算的施法上下文（仅在 Director.CastSpell 执行期间非空），
    /// 便于 BeforeDamage / BeforeApplyTransient 精准识别触发来源法术。
    /// </summary>
    public static SpellCastEventArgs ActiveSpellCast
    {
      get => CurrentSpellCast;
      internal set => CurrentSpellCast = value;
    }

    /// <summary>
    /// 当前线程正在结算的普通攻击上下文（仅在 Director.Attack 执行期间非空），
    /// 便于 BeforeDamage 精准区分普通攻击与法术伤害。
    /// </summary>
    public static AttackEventArgs ActiveAttack
    {
      get => CurrentAttack;
      internal set => CurrentAttack = value;
    }

    private static readonly ConcurrentDictionary<string, Action<SpellCastEventArgs>> BeforeCastSpellHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<SpellCastEventArgs>> AfterCastSpellHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<DamageEventArgs>> BeforeDamageHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<DamageEventArgs>> AfterDamageHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<KillEventArgs>> AfterKillHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<ResurrectEventArgs>> AfterResurrectHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<SpeedQueryEventArgs>> SpeedQueryHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<AttackRangeQueryEventArgs>> AttackRangeQueryHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<CastRangeEventArgs>> CastRangeHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<TransientEventArgs>> BeforeApplyTransientHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<CommandEventArgs>> BeforeExecuteCommandHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<CommandEventArgs>> AfterExecuteCommandHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<AttackEventArgs>> BeforeAttackHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<AttackEventArgs>> AfterAttackHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<ActorCreateEventArgs>> AfterCreateActorHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<DefenceQueryEventArgs>> DefenceQueryHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<SpellCostQueryEventArgs>> SpellCostQueryHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<LevelUpEventArgs>> AfterLevelUpHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<LimitFormQueryEventArgs>> LimitFormQueryHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Func<Character, bool>> InvulnerableQueries = new(StringComparer.Ordinal);

    public static void RegisterBeforeCastSpell(string Key, Action<SpellCastEventArgs> Handler)
      => Register(BeforeCastSpellHandlers, Key, Handler);

    public static void RegisterAfterCastSpell(string Key, Action<SpellCastEventArgs> Handler)
      => Register(AfterCastSpellHandlers, Key, Handler);

    public static void RegisterBeforeDamage(string Key, Action<DamageEventArgs> Handler)
      => Register(BeforeDamageHandlers, Key, Handler);

    public static void RegisterAfterDamage(string Key, Action<DamageEventArgs> Handler)
      => Register(AfterDamageHandlers, Key, Handler);

    public static void RegisterAfterKill(string Key, Action<KillEventArgs> Handler)
      => Register(AfterKillHandlers, Key, Handler);

    public static void RegisterAfterResurrect(string Key, Action<ResurrectEventArgs> Handler)
      => Register(AfterResurrectHandlers, Key, Handler);

    public static void RegisterSpeedQuery(string Key, Action<SpeedQueryEventArgs> Handler)
      => Register(SpeedQueryHandlers, Key, Handler);

    public static void RegisterAttackRangeQuery(string Key, Action<AttackRangeQueryEventArgs> Handler)
      => Register(AttackRangeQueryHandlers, Key, Handler);

    public static void RegisterCastRange(string Key, Action<CastRangeEventArgs> Handler)
      => Register(CastRangeHandlers, Key, Handler);

    public static void RegisterBeforeApplyTransient(string Key, Action<TransientEventArgs> Handler)
      => Register(BeforeApplyTransientHandlers, Key, Handler);

    public static void RegisterBeforeExecuteCommand(string Key, Action<CommandEventArgs> Handler)
      => Register(BeforeExecuteCommandHandlers, Key, Handler);

    public static void RegisterAfterExecuteCommand(string Key, Action<CommandEventArgs> Handler)
      => Register(AfterExecuteCommandHandlers, Key, Handler);

    public static void RegisterBeforeAttack(string Key, Action<AttackEventArgs> Handler)
      => Register(BeforeAttackHandlers, Key, Handler);

    public static void RegisterAfterAttack(string Key, Action<AttackEventArgs> Handler)
      => Register(AfterAttackHandlers, Key, Handler);

    public static void RegisterAfterCreateActor(string Key, Action<ActorCreateEventArgs> Handler)
      => Register(AfterCreateActorHandlers, Key, Handler);

    public static void RegisterDefenceQuery(string Key, Action<DefenceQueryEventArgs> Handler)
      => Register(DefenceQueryHandlers, Key, Handler);

    public static void RegisterSpellCostQuery(string Key, Action<SpellCostQueryEventArgs> Handler)
      => Register(SpellCostQueryHandlers, Key, Handler);

    public static void RegisterAfterLevelUp(string Key, Action<LevelUpEventArgs> Handler)
      => Register(AfterLevelUpHandlers, Key, Handler);

    public static void RegisterLimitFormQuery(string Key, Action<LimitFormQueryEventArgs> Handler)
      => Register(LimitFormQueryHandlers, Key, Handler);

    /// <summary>
    /// 注册「无敌/不可选中（Invulnerable）」状态查询器：
    /// 返回 true 时，引擎层自动拦截敌方近战攻击命中、地面陷阱触发、病痛/疫病感染、敌方/环境特效、负面状态与一切伤害。
    /// </summary>
    public static void RegisterInvulnerableQuery(string Key, Func<Character, bool> Predicate)
    {
      if (string.IsNullOrWhiteSpace(Key))
        throw new ArgumentException("Hook handler key cannot be null or empty.", nameof(Key));
      if (Predicate == null)
        throw new ArgumentNullException(nameof(Predicate));

      InvulnerableQueries[Key] = Predicate;
    }

    public static bool QueryInvulnerable(Character Character)
    {
      if (Character == null || InvulnerableQueries.IsEmpty)
        return false;

      foreach (var Pair in InvulnerableQueries)
      {
        try
        {
          if (Pair.Value(Character))
            return true;
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Handler '{Pair.Key}' failed in InvulnerableQueries: {Ex}");
        }
      }

      return false;
    }

    internal static void DispatchBeforeCastSpell(SpellCastEventArgs Args)
      => Dispatch(BeforeCastSpellHandlers, Args, nameof(BeforeCastSpellHandlers));

    internal static void DispatchAfterCastSpell(SpellCastEventArgs Args)
      => Dispatch(AfterCastSpellHandlers, Args, nameof(AfterCastSpellHandlers));

    internal static void DispatchBeforeDamage(DamageEventArgs Args)
      => Dispatch(BeforeDamageHandlers, Args, nameof(BeforeDamageHandlers));

    internal static void DispatchAfterDamage(DamageEventArgs Args)
      => Dispatch(AfterDamageHandlers, Args, nameof(AfterDamageHandlers));

    internal static void DispatchAfterKill(KillEventArgs Args)
      => Dispatch(AfterKillHandlers, Args, nameof(AfterKillHandlers));

    internal static void DispatchAfterResurrect(ResurrectEventArgs Args)
      => Dispatch(AfterResurrectHandlers, Args, nameof(AfterResurrectHandlers));

    internal static void DispatchSpeedQuery(SpeedQueryEventArgs Args)
      => Dispatch(SpeedQueryHandlers, Args, nameof(SpeedQueryHandlers));

    internal static void DispatchAttackRangeQuery(AttackRangeQueryEventArgs Args)
      => Dispatch(AttackRangeQueryHandlers, Args, nameof(AttackRangeQueryHandlers));

    internal static void DispatchCastRange(CastRangeEventArgs Args)
      => Dispatch(CastRangeHandlers, Args, nameof(CastRangeHandlers));

    internal static void DispatchBeforeApplyTransient(TransientEventArgs Args)
      => Dispatch(BeforeApplyTransientHandlers, Args, nameof(BeforeApplyTransientHandlers));

    internal static void DispatchBeforeExecuteCommand(CommandEventArgs Args)
      => Dispatch(BeforeExecuteCommandHandlers, Args, nameof(BeforeExecuteCommandHandlers));

    internal static void DispatchAfterExecuteCommand(CommandEventArgs Args)
      => Dispatch(AfterExecuteCommandHandlers, Args, nameof(AfterExecuteCommandHandlers));

    internal static void DispatchBeforeAttack(AttackEventArgs Args)
      => Dispatch(BeforeAttackHandlers, Args, nameof(BeforeAttackHandlers));

    internal static void DispatchAfterAttack(AttackEventArgs Args)
      => Dispatch(AfterAttackHandlers, Args, nameof(AfterAttackHandlers));

    internal static void DispatchAfterCreateActor(ActorCreateEventArgs Args)
      => Dispatch(AfterCreateActorHandlers, Args, nameof(AfterCreateActorHandlers));

    internal static void DispatchDefenceQuery(DefenceQueryEventArgs Args)
      => Dispatch(DefenceQueryHandlers, Args, nameof(DefenceQueryHandlers));

    internal static void DispatchSpellCostQuery(SpellCostQueryEventArgs Args)
      => Dispatch(SpellCostQueryHandlers, Args, nameof(SpellCostQueryHandlers));

    internal static void DispatchAfterLevelUp(LevelUpEventArgs Args)
      => Dispatch(AfterLevelUpHandlers, Args, nameof(AfterLevelUpHandlers));

    internal static void DispatchLimitFormQuery(LimitFormQueryEventArgs Args)
      => Dispatch(LimitFormQueryHandlers, Args, nameof(LimitFormQueryHandlers));

    private static void Register<TArgs>(ConcurrentDictionary<string, Action<TArgs>> Map, string Key, Action<TArgs> Handler)
    {
      if (string.IsNullOrWhiteSpace(Key))
        throw new ArgumentException("Hook handler key cannot be null or empty.", nameof(Key));
      if (Handler == null)
        throw new ArgumentNullException(nameof(Handler));

      Map[Key] = Handler;
    }

    private static void Dispatch<TArgs>(ConcurrentDictionary<string, Action<TArgs>> Map, TArgs Args, string EventName)
    {
      if (Map.IsEmpty)
        return;

      foreach (var Pair in Map)
      {
        try
        {
          Pair.Value(Args);
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Handler '{Pair.Key}' failed in {EventName}: {Ex}");
        }
      }
    }
  }
}
