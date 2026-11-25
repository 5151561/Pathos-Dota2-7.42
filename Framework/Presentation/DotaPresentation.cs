using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework.Presentation
{
  /// <summary>
  /// 技能演出时间轴（真实时间节拍 + 世界冻结）。
  ///
  /// 背景：Pathos 有两条时钟——
  /// <list type="bullet">
  /// <item><c>Story.Clock</c>：动画时钟，客户端每帧 +1（60 帧 ≈ 1 秒，与「游戏是否在等玩家输入」无关）；</item>
  /// <item><c>Adventure.Clock</c>：游戏逻辑时钟，只在 <c>Engine.Process()</c> 中推进，正常速度一回合仅 5 帧（≈0.08 秒）。</item>
  /// </list>
  /// 因此「多回合自动连斩」之类的技能会在不到 1 秒内全部结算完，特效互相叠成一片。
  ///
  /// 本服务把技能拆成挂在 <c>Story.Clock</c> 上的若干步骤（<see cref="DotaSequence.At"/>），每帧由
  /// <c>Story.NextTurn</c> 后置补丁按真实时间触发；<see cref="DotaSequence.FreezeWorld"/> 的序列播放期间
  /// 跳过 <c>Engine.Process()</c>（怪物不行动、回合不流逝、玩家无法输入），动画照常播放，
  /// 可选地每步放行若干逻辑帧，让演出既有 DOTA2 的时长观感、又保持原有的回合消耗。
  /// </summary>
  public static class DotaPresentation
  {
    /// <summary>Story 时钟每秒帧数（Pathos 默认动画速度）。</summary>
    public const int TicksPerSecond = 60;

    /// <summary>任何序列的最长存活帧数（安全阀，防止异常导致世界永久冻结）。</summary>
    private const int MaxSequenceTicks = TicksPerSecond * 12;

    private static readonly List<DotaSequence> ActiveSequences = new();
    private static readonly List<DotaLoop> ActiveLoops = new();

    private static readonly Type StoryType = typeof(Expansion).Assembly.GetType("Pathos.Story");
    private static readonly Type EngineType = typeof(Expansion).Assembly.GetType("Pathos.Engine");
    private static readonly PropertyInfo StoryClockProp = StoryType?.GetProperty("Clock", PathosClientReflection.AnyInstance);
    private static readonly FieldInfo StoryEngineField = StoryType?.GetField("Engine", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo EngineStoryProp = EngineType?.GetProperty("Story", PathosClientReflection.AnyInstance);

    private static bool PendingRefresh;

    /// <summary>秒 → Story 帧数（至少 1 帧）。</summary>
    public static int Ticks(float Seconds) => Math.Max(1, (int)MathF.Round(Seconds * TicksPerSecond));

    /// <summary>秒 → Story 动画 <see cref="Delay"/>（用于 Story* 特效的持续时长）。</summary>
    public static Delay Seconds(float Seconds) => Delay.FromTurns(Ticks(Seconds));

    /// <summary>当前 Story 动画时钟（帧）；取不到时返回 -1。</summary>
    public static long GetStoryClock(object Engine)
    {
      var Story = Engine != null ? EngineStoryProp?.GetValue(Engine) : null;
      return Story != null && StoryClockProp?.GetValue(Story) is Clock C ? C.GetTurns() : -1;
    }

    /// <summary>
    /// 新建一条演出序列（调用 <see cref="DotaSequence.Play"/> 才会开始）。
    /// 同一 Owner + Key 的旧序列会被立即结束（触发其 OnFinish），保证幂等。
    /// </summary>
    public static DotaSequence Sequence(object Engine, Character Owner, string Key)
      => new DotaSequence(Engine, Owner, Key);

    /// <summary>
    /// 卡肉/顿帧：冻结世界 <paramref name="Seconds"/> 秒（动画继续播放），用于重击命中的力量感。
    /// </summary>
    public static void HitStop(object Engine, Character Owner, float Seconds)
    {
      if (Engine == null || Seconds <= 0.0f)
        return;
      Sequence(Engine, Owner, "hitstop").FreezeWorld().At(Seconds, _ => { }).Play();
    }

    /// <summary>
    /// 注册一个按真实时间循环的纯视觉驱动（如剑刃风暴持续旋转），不冻结世界。
    /// 每 <paramref name="IntervalSeconds"/> 秒执行一次 <paramref name="Tick"/>，<paramref name="While"/> 返回 false 时自动移除。
    /// 同一 Owner + Key 重复注册会替换旧循环。
    /// </summary>
    public static void Loop(object Engine, Character Owner, string Key, float IntervalSeconds, Func<Character, bool> While, Action<object, Character> Tick)
    {
      if (Engine == null || Owner == null || Tick == null)
        return;

      long Now = GetStoryClock(Engine);
      if (Now < 0)
        return;

      lock (ActiveLoops)
      {
        ActiveLoops.RemoveAll(L => L.Owner == Owner && L.Key == Key);
        ActiveLoops.Add(new DotaLoop
        {
          Engine = Engine,
          Owner = Owner,
          Key = Key,
          IntervalTicks = Ticks(IntervalSeconds),
          NextTick = Now,
          While = While,
          Tick = Tick
        });
      }
    }

    /// <summary>该角色是否有指定 Key 的序列正在播放。</summary>
    public static bool IsPlaying(Character Owner, string Key)
    {
      lock (ActiveSequences)
        return ActiveSequences.Exists(S => S.Owner == Owner && S.Key == Key && !S.Finished);
    }

    /// <summary>当前是否有冻结世界的序列在播放。</summary>
    public static bool IsWorldFrozen(object Engine)
    {
      lock (ActiveSequences)
        return ActiveSequences.Exists(S => S.Engine == Engine && S.Freezes && !S.Finished);
    }

    internal static void Start(DotaSequence Sequence)
    {
      long Now = GetStoryClock(Sequence.Engine);
      if (Now < 0)
      {
        // 无 Story（如 PathosMaker 打包期或测试环境）：立即按顺序同步执行，保证逻辑不丢失
        Sequence.RunAllImmediately();
        return;
      }

      DotaSequence Replaced;
      lock (ActiveSequences)
      {
        Replaced = ActiveSequences.Find(S => S.Owner == Sequence.Owner && S.Key == Sequence.Key && !S.Finished);
        Sequence.StartTick = Now;
        ActiveSequences.Add(Sequence);
      }

      Replaced?.Finish(Completed: false);

      // 第 0 帧的步骤立即执行（施法当帧即有反馈）
      Sequence.Advance(Now);
    }

    internal static void ApplyPatches(Harmony Harmony)
    {
      try
      {
        if (StoryType == null || EngineType == null)
        {
          HookBootstrap.WriteError("DotaPresentation: Pathos.Story / Pathos.Engine not found.");
          return;
        }

        PathosClientReflection.PatchSafe(Harmony, StoryType, "NextTurn", Type.EmptyTypes, typeof(DotaPresentation),
          Postfix: nameof(StoryNextTurnPostfix), Tag: "DotaPresentation");
        PathosClientReflection.PatchSafe(Harmony, EngineType, "Process", Type.EmptyTypes, typeof(DotaPresentation),
          Prefix: nameof(EngineProcessPrefix), Tag: "DotaPresentation");
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaPresentation.ApplyPatches failed: {Ex}");
      }
    }

    // Story.NextTurn 每个客户端帧都会调用（即使正在等待玩家输入），在此按真实时间推进全部序列与循环
    private static void StoryNextTurnPostfix(object __instance)
    {
      if (__instance == null)
        return;

      try
      {
        var Engine = StoryEngineField?.GetValue(__instance);
        if (StoryClockProp?.GetValue(__instance) is not Clock C)
          return;
        long Now = C.GetTurns();

        DotaSequence[] Sequences;
        lock (ActiveSequences)
        {
          // 读档/新开局后旧引擎的序列直接作废（不回调，避免操作已失效的角色）
          ActiveSequences.RemoveAll(S => S.Finished || S.Engine != Engine);
          Sequences = ActiveSequences.Count > 0 ? ActiveSequences.ToArray() : null;
        }

        if (Sequences != null)
        {
          foreach (var S in Sequences)
          {
            if (Now - S.StartTick > MaxSequenceTicks)
            {
              HookBootstrap.WriteError($"DotaPresentation: sequence '{S.Key}' exceeded {MaxSequenceTicks} ticks; force finishing.");
              S.Finish(Completed: false);
              continue;
            }

            if (S.Advance(Now))
              PendingRefresh = true;
          }
        }

        TickLoops(Engine, Now);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaPresentation tick failed: {Ex}");
      }
    }

    private static void TickLoops(object Engine, long Now)
    {
      DotaLoop[] Loops;
      lock (ActiveLoops)
      {
        ActiveLoops.RemoveAll(L => L.Engine != Engine || L.Owner == null);
        if (ActiveLoops.Count == 0)
          return;
        Loops = ActiveLoops.ToArray();
      }

      foreach (var L in Loops)
      {
        if (Now < L.NextTick)
          continue;

        bool Alive;
        try
        {
          Alive = !L.Owner.Deceased && L.Owner.Square != null && (L.While == null || L.While(L.Owner));
          if (Alive)
            L.Tick(Engine, L.Owner);
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"DotaPresentation loop '{L.Key}' failed: {Ex}");
          Alive = false;
        }

        if (Alive)
        {
          L.NextTick = Math.Max(L.NextTick + L.IntervalTicks, Now + 1);
        }
        else
        {
          lock (ActiveLoops)
            ActiveLoops.Remove(L);
        }
      }
    }

    // 冻结世界：有冻结序列在播放时跳过 Engine.Process()（回合不流逝），仅放行序列预算内的逻辑帧
    private static bool EngineProcessPrefix(object __instance, ref bool __result)
    {
      DotaSequence Blocking = null;
      lock (ActiveSequences)
      {
        foreach (var S in ActiveSequences)
        {
          if (S.Engine == __instance && S.Freezes && !S.Finished)
          {
            Blocking = S;
            break;
          }
        }
      }

      if (Blocking == null)
        return true;

      if (Blocking.WorldTickBudget > 0)
      {
        Blocking.WorldTickBudget--;
        return true;
      }

      __result = PendingRefresh;
      PendingRefresh = false;
      return false;
    }

    private sealed class DotaLoop
    {
      public object Engine;
      public Character Owner;
      public string Key;
      public int IntervalTicks;
      public long NextTick;
      public Func<Character, bool> While;
      public Action<object, Character> Tick;
    }
  }

  /// <summary>
  /// 演出序列中单个步骤执行时的上下文。
  /// </summary>
  public sealed class DotaSequenceContext
  {
    internal DotaSequenceContext(DotaSequence Sequence) => this.Sequence = Sequence;

    internal readonly DotaSequence Sequence;

    public object Engine => Sequence.Engine;
    public Character Owner => Sequence.Owner;

    /// <summary>当前步骤是第几个已执行步骤（从 0 开始）。</summary>
    public int StepIndex { get; internal set; }

    /// <summary>提前结束序列：尚未执行的常规步骤全部取消，已通过 <see cref="After"/> 排入的收尾步骤仍会播完，之后 OnFinish 以 Completed = false 回调。</summary>
    public void Stop() => Sequence.StopRequested = true;

    /// <summary>在当前步骤之后 <paramref name="Seconds"/> 秒插入一个追加步骤（如突进落地后再挥砍）。</summary>
    public void After(float Seconds, Action<DotaSequenceContext> Step) => Sequence.InsertAfterNow(Seconds, Step);
  }

  /// <summary>
  /// 一条演出序列：若干挂在真实时间上的步骤 + 可选的世界冻结与结束回调。
  /// </summary>
  public sealed class DotaSequence
  {
    private sealed class Step
    {
      public long OffsetTicks;
      public int Order;
      public Action<DotaSequenceContext> Action;
      public bool FollowUp;
    }

    private readonly List<Step> Steps = new();
    private int NextOrder;
    private Action<DotaSequenceContext, bool> FinishAction;
    private long CurrentTick;
    private int ExecutedSteps;

    internal DotaSequence(object Engine, Character Owner, string Key)
    {
      this.Engine = Engine;
      this.Owner = Owner;
      this.Key = Key ?? string.Empty;
      Context = new DotaSequenceContext(this);
    }

    internal readonly object Engine;
    internal readonly Character Owner;
    internal readonly string Key;
    internal readonly DotaSequenceContext Context;
    internal long StartTick;
    internal bool Freezes;
    internal int WorldTicksPerStep;
    internal int WorldTickBudget;
    internal bool StopRequested;
    internal bool Finished;

    /// <summary>
    /// 播放期间冻结世界（怪物不行动、回合不流逝、玩家不能输入），动画照常播放。
    /// <paramref name="WorldTicksPerStep"/> &gt; 0 时，每执行一个步骤放行该数量的逻辑帧，
    /// 用于保留技能原有的回合消耗（如每斩消耗 1 回合）。
    /// </summary>
    public DotaSequence FreezeWorld(int WorldTicksPerStep = 0)
    {
      Freezes = true;
      this.WorldTicksPerStep = Math.Max(0, WorldTicksPerStep);
      return this;
    }

    /// <summary>在序列开始后 <paramref name="Seconds"/> 秒执行一个步骤。</summary>
    public DotaSequence At(float Seconds, Action<DotaSequenceContext> Action)
    {
      if (Action != null)
        Steps.Add(new Step { OffsetTicks = Seconds <= 0.0f ? 0 : DotaPresentation.Ticks(Seconds), Order = NextOrder++, Action = Action });
      return this;
    }

    /// <summary>序列结束（全部步骤执行完、被 Stop、被替换或超时）时回调；Completed 表示是否正常走完。</summary>
    public DotaSequence OnFinish(Action<DotaSequenceContext, bool> Action)
    {
      FinishAction = Action;
      return this;
    }

    /// <summary>开始播放。</summary>
    public void Play()
    {
      Steps.Sort(CompareSteps);
      DotaPresentation.Start(this);
    }

    internal void InsertAfterNow(float Seconds, Action<DotaSequenceContext> Action)
    {
      if (Action == null || Finished)
        return;
      long Offset = (CurrentTick - StartTick) + (Seconds <= 0.0f ? 0 : DotaPresentation.Ticks(Seconds));
      Steps.Add(new Step { OffsetTicks = Offset, Order = NextOrder++, Action = Action, FollowUp = true });
      Steps.Sort(CompareSteps);
    }

    /// <summary>执行所有到期步骤；返回本帧是否执行了步骤（需要刷新场景）。</summary>
    internal bool Advance(long Now)
    {
      if (Finished)
        return false;

      CurrentTick = Now;
      bool Ran = false;
      while (!Finished && Steps.Count > 0)
      {
        if (StopRequested)
        {
          // 已请求停止：丢弃尚未执行的常规步骤，只保留 After 排入的收尾步骤
          Steps.RemoveAll(S => !S.FollowUp);
          if (Steps.Count == 0)
            break;
        }

        if (StartTick + Steps[0].OffsetTicks > Now)
          break;

        var Next = Steps[0];
        Steps.RemoveAt(0);
        Ran = true;
        try
        {
          if (!Next.FollowUp)
            Context.StepIndex = ExecutedSteps++;
          Next.Action(Context);
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"DotaPresentation step of '{Key}' failed: {Ex}");
          StopRequested = true;
          Steps.RemoveAll(S => !S.FollowUp);
        }

        if (Freezes && !Next.FollowUp)
          WorldTickBudget += WorldTicksPerStep;
      }

      if (!Finished && Steps.Count == 0)
        Finish(Completed: !StopRequested);

      return Ran;
    }

    internal void RunAllImmediately()
    {
      StartTick = 0;
      Advance(long.MaxValue / 2);
    }

    internal void Finish(bool Completed)
    {
      if (Finished)
        return;
      Finished = true;
      Steps.Clear();
      try
      {
        FinishAction?.Invoke(Context, Completed);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaPresentation finish of '{Key}' failed: {Ex}");
      }
    }

    private static int CompareSteps(Step A, Step B)
    {
      int C = A.OffsetTicks.CompareTo(B.OffsetTicks);
      return C != 0 ? C : A.Order.CompareTo(B.Order);
    }
  }
}
