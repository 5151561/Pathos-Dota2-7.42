using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// DOTA2 通用「嘲讽（Taunt）」状态与行为重定向基建服务：
  /// - 支持任意施法者/指定嘲讽者（如斧王狂战士之吼、军团决斗、寒冬飞龙寒冬诅咒等）。
  /// - 自动处理唤醒沉睡与移除恐惧、全链路禁魔拦截（Silenced）、行动强制转向并近战普攻嘲讽者。
  /// - 嘲讽剩余回合随嘲讽者自身回合统一递减（防止高速/多动怪提前解控），嘲讽者死亡时自动解除。
  /// </summary>
  public static class DotaTauntService
  {
    private sealed class TauntState
    {
      public WeakReference<Character> Taunter;
      public int RemainingTurns;
      public string SourceKey;
      public Action<object, Character, Character> OnFallbackAttack;
    }

    private sealed class TaunterState
    {
      public readonly HashSet<WeakReference<Character>> ActiveTauntedTargets = new();
    }

    private static readonly ConditionalWeakTable<Character, TauntState> TauntedUnits = new();
    private static readonly ConditionalWeakTable<Character, TaunterState> TaunterStates = new();

    private static int InitializedFlag;

    [ThreadStatic]
    private static bool IsInsideTauntRedirect;

    /// <summary>
    /// 幂等注册嘲讽所需的全局禁魔、行动重定向与回合推进 Hook。
    /// 在首次调用 <see cref="ApplyTaunt"/> 时也会自动触发，无需担心漏注册。
    /// </summary>
    public static void EnsureInitialized()
    {
      if (Interlocked.Exchange(ref InitializedFlag, 1) != 0)
        return;

      DotaHookBus.RegisterSilenceQuery("dota.taunt_silence", Ch => IsTaunted(Ch));
      DotaHookBus.RegisterBeforeExecuteCommand("dota.taunt_redirect", HandleTauntedUnitCommand);
      DotaHookBus.RegisterAfterExecuteCommand("dota.taunt_tick", HandleAfterExecuteCommand);
    }

    /// <summary>
    /// 对 <paramref name="Victim"/> 施加来自 <paramref name="Taunter"/> 的嘲讽控制。
    /// </summary>
    /// <param name="Ctx">英雄上下文（用于清除沉睡/恐惧等原生状态，可为 null）。</param>
    /// <param name="Engine">当前 PathosEngine 实例。</param>
    /// <param name="Taunter">被攻击的嘲讽目标（通常为施法者，也可为第三方单位如寒冬诅咒目标）。</param>
    /// <param name="Victim">受嘲讽控制的受害者。</param>
    /// <param name="DurationTurns">持续回合数（随嘲讽者行动回合递减）。</param>
    /// <param name="SourceKey">可选来源标识（如 "axe.berserkers_call"），供调用方区分不同技能来源。</param>
    /// <param name="ClearSleepAndFear">是否自动唤醒沉睡并移除恐惧状态。</param>
    /// <param name="ShowCaption">是否在受害者头顶显示飘字提示。</param>
    /// <param name="CaptionText">自定义飘字文本；为空时使用默认本地化文案。</param>
    /// <param name="CaptionColour">自定义飘字颜色；为空时默认使用猩红（Colour.Crimson）。</param>
    /// <param name="OnFallbackAttack">当近战强制攻击回退为动画占位时触发的可选回调（Engine, Taunter, Victim）。</param>
    public static bool ApplyTaunt(
      DotaHeroContext Ctx,
      object Engine,
      Character Taunter,
      Character Victim,
      int DurationTurns,
      string SourceKey = null,
      bool ClearSleepAndFear = true,
      bool ShowCaption = true,
      string CaptionText = null,
      Colour CaptionColour = null,
      Action<object, Character, Character> OnFallbackAttack = null)
    {
      if (Taunter == null || Taunter.Deceased || Victim == null || Victim.Deceased || DurationTurns <= 0)
        return false;

      EnsureInitialized();

      if (ClearSleepAndFear && Ctx != null && Engine != null)
      {
        if (PathosEngineBridge.HasTransient(Victim, Ctx.Properties.sleeping))
          PathosEngineBridge.RemoveTransient(Engine, Taunter, Victim, Ctx.Properties.sleeping);
        if (PathosEngineBridge.HasTransient(Victim, Ctx.Properties.fear))
          PathosEngineBridge.RemoveTransient(Engine, Taunter, Victim, Ctx.Properties.fear);
      }

      var State = TauntedUnits.GetOrCreateValue(Victim);
      State.Taunter = new WeakReference<Character>(Taunter);
      State.RemainingTurns = DurationTurns;
      State.SourceKey = SourceKey;
      State.OnFallbackAttack = OnFallbackAttack;

      TrackTauntedTarget(Taunter, Victim);

      if (ShowCaption && Engine != null && Victim.Square != null)
      {
        string Text = !string.IsNullOrEmpty(CaptionText)
          ? CaptionText
          : DotaLocalization.T("dota.caption.taunted");
        PathosEngineBridge.StoryCaption(Engine, Victim.Square, CaptionColour ?? Colour.Crimson, Text, Delayed: false);
      }

      return true;
    }

    /// <summary>
    /// 判断指定单位当前是否处于有效嘲讽状态（嘲讽者存活且剩余回合 &gt; 0）。
    /// </summary>
    public static bool IsTaunted(Character Unit)
      => TryGetTaunter(Unit, out _, out _);

    /// <summary>
    /// 判断指定单位当前是否处于有效嘲讽状态，并返回其嘲讽者。
    /// </summary>
    public static bool IsTaunted(Character Unit, out Character Taunter)
      => TryGetTaunter(Unit, out Taunter, out _);

    /// <summary>
    /// 查询指定单位当前的嘲讽者与来源 Key；若嘲讽者已死亡则自动清理残留状态。
    /// </summary>
    public static bool TryGetTaunter(Character Unit, out Character Taunter, out string SourceKey)
    {
      Taunter = null;
      SourceKey = null;

      if (Unit == null || Unit.Deceased || !TauntedUnits.TryGetValue(Unit, out var State) || State.RemainingTurns <= 0)
        return false;

      if (State.Taunter == null || !State.Taunter.TryGetTarget(out var T) || T == null || T.Deceased)
      {
        TauntedUnits.Remove(Unit);
        return false;
      }

      Taunter = T;
      SourceKey = State.SourceKey;
      return true;
    }

    /// <summary>
    /// 获取单位当前剩余嘲讽回合数（未受嘲讽或嘲讽者已死亡时返回 0）。
    /// </summary>
    public static int GetRemainingTurns(Character Unit)
    {
      if (!TryGetTaunter(Unit, out _, out _) || !TauntedUnits.TryGetValue(Unit, out var State))
        return 0;

      return Math.Max(0, State.RemainingTurns);
    }

    /// <summary>
    /// 主动解除指定单位的嘲讽状态。
    /// </summary>
    public static void ClearTaunt(Character Unit)
    {
      if (Unit != null)
        TauntedUnits.Remove(Unit);
    }

    /// <summary>
    /// 主动解除由指定嘲讽者施加的全部仍在生效的嘲讽状态。
    /// </summary>
    public static void ClearTauntsByTaunter(Character Taunter)
    {
      if (Taunter == null || !TaunterStates.TryGetValue(Taunter, out var State))
        return;

      foreach (var Wr in State.ActiveTauntedTargets)
      {
        if (!Wr.TryGetTarget(out var Victim) || Victim == null)
          continue;

        if (TauntedUnits.TryGetValue(Victim, out var TState) &&
            TState.Taunter != null &&
            TState.Taunter.TryGetTarget(out var Owner) &&
            Owner == Taunter)
        {
          TauntedUnits.Remove(Victim);
        }
      }

      State.ActiveTauntedTargets.Clear();
    }

    /// <summary>
    /// 统计指定嘲讽者当前仍在嘲讽中的存活目标数量。
    /// </summary>
    public static int CountActiveTauntedTargets(Character Taunter)
    {
      if (Taunter == null || !TaunterStates.TryGetValue(Taunter, out var State) || State.ActiveTauntedTargets.Count == 0)
        return 0;

      int Count = 0;
      State.ActiveTauntedTargets.RemoveWhere(Wr =>
      {
        if (!Wr.TryGetTarget(out var Victim) || Victim == null || Victim.Deceased)
          return true;

        if (!TauntedUnits.TryGetValue(Victim, out var TState) ||
            TState.RemainingTurns <= 0 ||
            TState.Taunter == null ||
            !TState.Taunter.TryGetTarget(out var Owner) ||
            Owner != Taunter)
          return true;

        Count++;
        return false;
      });

      return Count;
    }

    private static void HandleTauntedUnitCommand(CommandEventArgs Args)
    {
      if (IsInsideTauntRedirect || Args.Character == null || Args.Character.Deceased)
        return;

      var Unit = Args.Character;
      if (!TryGetTaunter(Unit, out var Taunter, out _))
        return;

      if (!TauntedUnits.TryGetValue(Unit, out var TState) || TState.RemainingTurns <= 0)
        return;

      if (Unit.Square == null || Taunter.Square == null || Unit.Square.Map != Taunter.Square.Map)
        return;

      int Dist = Unit.Square.AsRange(Taunter.Square);
      var DirToTaunter = Unit.Square.AsDirection(Taunter.Square);

      // 若该单位的原始指令已经是朝嘲讽者挥砍，直接放行
      if (Dist <= 1 && PathosEngineBridge.IsAttackCommand(Args.Command))
      {
        var CmdDir = PathosEngineBridge.GetAttackCommandDirection(Args.Command);
        if (CmdDir == DirToTaunter)
          return;
      }

      // 否则取消原指令（逃跑/施法/打友军），强制转向嘲讽者
      Args.Cancel = true;

      IsInsideTauntRedirect = true;
      try
      {
        if (Dist <= 1)
        {
          if (!PathosEngineBridge.ForceDirectorAttack(Args.Director, Unit, DirToTaunter))
          {
            var AtkDelay = PathosEngineBridge.GetAttackDelay(Args.Engine, Unit);
            PathosEngineBridge.SetBusy(Args.Engine, Unit, AtkDelay);
            PathosEngineBridge.StoryAttacking(Args.Engine, Delay.FromTurns(10), Unit, DirToTaunter);
            TState.OnFallbackAttack?.Invoke(Args.Engine, Taunter, Unit);
          }
        }
        else
        {
          var NextSq = DotaCombatHelper.FindStepToward(Unit.Square, Taunter.Square);
          if (NextSq != null)
            PathosEngineBridge.DashCharacter(Args.Engine, Unit, NextSq);

          var StepDelay = PathosEngineBridge.GetAttackDelay(Args.Engine, Unit);
          PathosEngineBridge.SetBusy(Args.Engine, Unit, StepDelay);
        }
      }
      finally
      {
        IsInsideTauntRedirect = false;
      }
    }

    private static void HandleAfterExecuteCommand(CommandEventArgs Args)
    {
      var Actor = Args.Character;
      if (Actor == null || Actor.Deceased)
        return;

      TickTauntedTargets(Actor);
    }

    private static void TrackTauntedTarget(Character Taunter, Character Victim)
    {
      if (Taunter == null || Victim == null)
        return;

      var State = TaunterStates.GetOrCreateValue(Taunter);
      State.ActiveTauntedTargets.RemoveWhere(Wr =>
        !Wr.TryGetTarget(out var Existing) || Existing == null || Existing.Deceased || Existing == Victim);
      State.ActiveTauntedTargets.Add(new WeakReference<Character>(Victim));
    }

    private static void TickTauntedTargets(Character Taunter)
    {
      if (Taunter == null || !TaunterStates.TryGetValue(Taunter, out var State) || State.ActiveTauntedTargets.Count == 0)
        return;

      State.ActiveTauntedTargets.RemoveWhere(Wr =>
      {
        if (!Wr.TryGetTarget(out var Victim) || Victim == null || Victim.Deceased)
          return true;

        if (!TauntedUnits.TryGetValue(Victim, out var TState) ||
            TState.Taunter == null ||
            !TState.Taunter.TryGetTarget(out var Owner) ||
            Owner != Taunter)
          return true;

        TState.RemainingTurns--;
        if (TState.RemainingTurns <= 0)
        {
          TauntedUnits.Remove(Victim);
          return true;
        }

        return false;
      });
    }
  }
}
