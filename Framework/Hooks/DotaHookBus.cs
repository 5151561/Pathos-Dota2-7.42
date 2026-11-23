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
    private static readonly ConcurrentDictionary<string, Action<SpellCastEventArgs>> BeforeCastSpellHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<SpellCastEventArgs>> AfterCastSpellHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<DamageEventArgs>> BeforeDamageHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<DamageEventArgs>> AfterDamageHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<KillEventArgs>> AfterKillHandlers = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Action<ResurrectEventArgs>> AfterResurrectHandlers = new(StringComparer.Ordinal);

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
