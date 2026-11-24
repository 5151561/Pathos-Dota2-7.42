using System;

namespace Pathos.DotaHeroes.Framework.Hooks
{
  /// <summary>
  /// DOTA2 英雄统一 Hook 事件总线。
  /// 所有订阅按字符串 Key 幂等注册：即使 Pathos 多次重建 Campaign / Expansion，
  /// 相同 Key 只会覆盖旧处理器而不会重复触发。
  /// 处理器按 (Priority, 首次注册顺序) 确定性执行（见 <see cref="DotaHookPriority"/>）；
  /// Before* 可取消事件在某个处理器设置 Cancel 后立即停止分发。
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

    private static readonly DotaHookHandlerList<Action<SpellCastEventArgs>> BeforeCastSpellHandlers = new();
    private static readonly DotaHookHandlerList<Action<SpellCastEventArgs>> AfterCastSpellHandlers = new();
    private static readonly DotaHookHandlerList<Action<DamageEventArgs>> BeforeDamageHandlers = new();
    private static readonly DotaHookHandlerList<Action<DamageEventArgs>> AfterDamageHandlers = new();
    private static readonly DotaHookHandlerList<Action<KillEventArgs>> AfterKillHandlers = new();
    private static readonly DotaHookHandlerList<Action<ResurrectEventArgs>> AfterResurrectHandlers = new();
    private static readonly DotaHookHandlerList<Action<SpeedQueryEventArgs>> SpeedQueryHandlers = new();
    private static readonly DotaHookHandlerList<Action<AttackRangeQueryEventArgs>> AttackRangeQueryHandlers = new();
    private static readonly DotaHookHandlerList<Action<CastRangeEventArgs>> CastRangeHandlers = new();
    private static readonly DotaHookHandlerList<Action<TransientEventArgs>> BeforeApplyTransientHandlers = new();
    private static readonly DotaHookHandlerList<Action<CommandEventArgs>> BeforeExecuteCommandHandlers = new();
    private static readonly DotaHookHandlerList<Action<CommandEventArgs>> AfterExecuteCommandHandlers = new();
    private static readonly DotaHookHandlerList<Action<AttackEventArgs>> BeforeAttackHandlers = new();
    private static readonly DotaHookHandlerList<Action<AttackEventArgs>> AfterAttackHandlers = new();
    private static readonly DotaHookHandlerList<Action<ActorCreateEventArgs>> AfterCreateActorHandlers = new();
    private static readonly DotaHookHandlerList<Action<DefenceQueryEventArgs>> DefenceQueryHandlers = new();
    private static readonly DotaHookHandlerList<Action<SpellCostQueryEventArgs>> SpellCostQueryHandlers = new();
    private static readonly DotaHookHandlerList<Action<LevelUpEventArgs>> AfterLevelUpHandlers = new();
    private static readonly DotaHookHandlerList<Action<LimitFormQueryEventArgs>> LimitFormQueryHandlers = new();
    private static readonly DotaHookHandlerList<Action<ActiveFormQueryEventArgs>> ActiveFormQueryHandlers = new();
    private static readonly DotaHookHandlerList<Func<Character, bool>> InvulnerableQueries = new();
    private static readonly DotaHookHandlerList<Func<Character, bool>> SilenceQueries = new();

    public static void RegisterBeforeCastSpell(string Key, Action<SpellCastEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => BeforeCastSpellHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterCastSpell(string Key, Action<SpellCastEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterCastSpellHandlers.Register(Key, Handler, Priority);

    public static void RegisterBeforeDamage(string Key, Action<DamageEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => BeforeDamageHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterDamage(string Key, Action<DamageEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterDamageHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterKill(string Key, Action<KillEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterKillHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterResurrect(string Key, Action<ResurrectEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterResurrectHandlers.Register(Key, Handler, Priority);

    public static void RegisterSpeedQuery(string Key, Action<SpeedQueryEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => SpeedQueryHandlers.Register(Key, Handler, Priority);

    public static void RegisterAttackRangeQuery(string Key, Action<AttackRangeQueryEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AttackRangeQueryHandlers.Register(Key, Handler, Priority);

    public static void RegisterCastRange(string Key, Action<CastRangeEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => CastRangeHandlers.Register(Key, Handler, Priority);

    public static void RegisterBeforeApplyTransient(string Key, Action<TransientEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => BeforeApplyTransientHandlers.Register(Key, Handler, Priority);

    public static void RegisterBeforeExecuteCommand(string Key, Action<CommandEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => BeforeExecuteCommandHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterExecuteCommand(string Key, Action<CommandEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterExecuteCommandHandlers.Register(Key, Handler, Priority);

    public static void RegisterBeforeAttack(string Key, Action<AttackEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => BeforeAttackHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterAttack(string Key, Action<AttackEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterAttackHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterCreateActor(string Key, Action<ActorCreateEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterCreateActorHandlers.Register(Key, Handler, Priority);

    public static void RegisterDefenceQuery(string Key, Action<DefenceQueryEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => DefenceQueryHandlers.Register(Key, Handler, Priority);

    public static void RegisterSpellCostQuery(string Key, Action<SpellCostQueryEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => SpellCostQueryHandlers.Register(Key, Handler, Priority);

    public static void RegisterAfterLevelUp(string Key, Action<LevelUpEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => AfterLevelUpHandlers.Register(Key, Handler, Priority);

    public static void RegisterLimitFormQuery(string Key, Action<LimitFormQueryEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => LimitFormQueryHandlers.Register(Key, Handler, Priority);

    public static void RegisterActiveFormQuery(string Key, Action<ActiveFormQueryEventArgs> Handler, int Priority = DotaHookPriority.Normal)
      => ActiveFormQueryHandlers.Register(Key, Handler, Priority);

    internal static bool HasActiveFormQueryHandlers => !ActiveFormQueryHandlers.IsEmpty;

    /// <summary>
    /// 注册「无敌/不可选中（Invulnerable）」状态查询器：
    /// 返回 true 时，引擎层自动拦截敌方近战攻击命中、地面陷阱触发、病痛/疫病感染、敌方/环境特效、负面状态与一切伤害。
    /// </summary>
    public static void RegisterInvulnerableQuery(string Key, Func<Character, bool> Predicate)
      => InvulnerableQueries.Register(Key, Predicate, DotaHookPriority.Normal);

    public static bool QueryInvulnerable(Character Character)
    {
      if (Character == null || InvulnerableQueries.IsEmpty)
        return false;

      foreach (var Pair in InvulnerableQueries.GetSnapshot())
      {
        try
        {
          if (Pair.Handler(Character))
            return true;
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Handler '{Pair.Key}' failed in InvulnerableQueries: {Ex}");
        }
      }

      return false;
    }

    /// <summary>
    /// 注册「沉默/禁魔（Silenced / Taunted）」状态查询器：
    /// 返回 true 时，引擎层自动拦截 Engine.CanCast、Director.CastSpell 以及怪物吐息/凝视/射线/魔杖等 Engine.CastEffects 技能释放。
    /// </summary>
    public static void RegisterSilenceQuery(string Key, Func<Character, bool> Predicate)
      => SilenceQueries.Register(Key, Predicate, DotaHookPriority.Normal);

    public static bool QuerySilenced(Character Character)
    {
      if (Character == null || SilenceQueries.IsEmpty)
        return false;

      foreach (var Pair in SilenceQueries.GetSnapshot())
      {
        try
        {
          if (Pair.Handler(Character))
            return true;
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Handler '{Pair.Key}' failed in SilenceQueries: {Ex}");
        }
      }

      return false;
    }

    internal static void DispatchBeforeCastSpell(SpellCastEventArgs Args)
      => Dispatch(BeforeCastSpellHandlers, Args, nameof(BeforeCastSpellHandlers), A => A.Cancel);

    internal static void DispatchAfterCastSpell(SpellCastEventArgs Args)
      => Dispatch(AfterCastSpellHandlers, Args, nameof(AfterCastSpellHandlers));

    internal static void DispatchBeforeDamage(DamageEventArgs Args)
      => Dispatch(BeforeDamageHandlers, Args, nameof(BeforeDamageHandlers), A => A.Cancel);

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
      => Dispatch(BeforeApplyTransientHandlers, Args, nameof(BeforeApplyTransientHandlers), A => A.Cancel);

    internal static void DispatchBeforeExecuteCommand(CommandEventArgs Args)
      => Dispatch(BeforeExecuteCommandHandlers, Args, nameof(BeforeExecuteCommandHandlers), A => A.Cancel);

    internal static void DispatchAfterExecuteCommand(CommandEventArgs Args)
      => Dispatch(AfterExecuteCommandHandlers, Args, nameof(AfterExecuteCommandHandlers));

    internal static void DispatchBeforeAttack(AttackEventArgs Args)
      => Dispatch(BeforeAttackHandlers, Args, nameof(BeforeAttackHandlers), A => A.Cancel);

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

    internal static void DispatchActiveFormQuery(ActiveFormQueryEventArgs Args)
      => Dispatch(ActiveFormQueryHandlers, Args, nameof(ActiveFormQueryHandlers));

    private static void Dispatch<TArgs>(
      DotaHookHandlerList<Action<TArgs>> Handlers,
      TArgs Args,
      string EventName,
      Func<TArgs, bool> IsCancelled = null)
    {
      if (Handlers.IsEmpty)
        return;

      foreach (var Entry in Handlers.GetSnapshot())
      {
        try
        {
          Entry.Handler(Args);
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Handler '{Entry.Key}' failed in {EventName}: {Ex}");
        }

        // 可取消事件：一旦某处理器取消，后续处理器不再执行（避免对已免疫的伤害继续暴击/吸血等副作用）
        if (IsCancelled != null && IsCancelled(Args))
          return;
      }
    }
  }
}
