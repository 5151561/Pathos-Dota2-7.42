using System;
using System.Collections.Generic;

namespace Pathos.DotaHeroes.Framework.Hooks
{
  /// <summary>
  /// Hook 处理器执行优先级（数值越小越先执行，同优先级按首次注册顺序执行）。
  /// 建议约定（以 BeforeDamage 为例）：
  /// Early = 免疫/取消判定 → Normal = 固定值加减 → Amplify = 乘算增伤/暴击 → Reduction = 减伤/魔抗 → Late = 只读观察（日志/吸血）。
  /// </summary>
  public static class DotaHookPriority
  {
    public const int Early = -200;
    public const int Normal = 0;
    public const int Amplify = 100;
    public const int Reduction = 200;
    public const int Late = 300;
  }

  /// <summary>
  /// 有序、幂等的 Hook 处理器列表：
  /// - 按 (Priority, 首次注册序号) 稳定排序，保证每次分发顺序确定；
  /// - 相同 Key 重复注册时原位覆盖处理器与优先级，不会重复触发；
  /// - 写时复制快照，分发期间无需加锁。
  /// </summary>
  internal sealed class DotaHookHandlerList<THandler> where THandler : Delegate
  {
    internal readonly struct Entry
    {
      public Entry(string Key, int Priority, long Sequence, THandler Handler)
      {
        this.Key = Key;
        this.Priority = Priority;
        this.Sequence = Sequence;
        this.Handler = Handler;
      }

      public string Key { get; }
      public int Priority { get; }
      public long Sequence { get; }
      public THandler Handler { get; }
    }

    private static long NextSequence;

    private readonly object SyncRoot = new();
    private Entry[] Snapshot = Array.Empty<Entry>();

    public bool IsEmpty => Snapshot.Length == 0;

    public Entry[] GetSnapshot() => Snapshot;

    public void Register(string Key, THandler Handler, int Priority)
    {
      if (string.IsNullOrWhiteSpace(Key))
        throw new ArgumentException("Hook handler key cannot be null or empty.", nameof(Key));
      if (Handler == null)
        throw new ArgumentNullException(nameof(Handler));

      lock (SyncRoot)
      {
        var List = new List<Entry>(Snapshot);
        int Existing = List.FindIndex(E => string.Equals(E.Key, Key, StringComparison.Ordinal));
        if (Existing >= 0)
          List[Existing] = new Entry(Key, Priority, List[Existing].Sequence, Handler);
        else
          List.Add(new Entry(Key, Priority, System.Threading.Interlocked.Increment(ref NextSequence), Handler));

        List.Sort((A, B) => A.Priority != B.Priority ? A.Priority.CompareTo(B.Priority) : A.Sequence.CompareTo(B.Sequence));
        Snapshot = List.ToArray();
      }
    }
  }
}
