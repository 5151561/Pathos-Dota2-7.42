using System;
using System.Collections.Generic;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 1 起始、越界自动夹取的数值表：统一替代散落各处的 <c>Arr[Math.Clamp(Lv - 1, 0, N)]</c>。
  /// - 5 阶熟练度表：<c>DotaTable.Of(4, 5, 5, 6, 7)[tier]</c>（tier = 1 Proficient .. 5 Champion）；
  /// - 技能等级表：<c>DotaTable.Of(275f, 375f, 475f)[skillLevel]</c>（≤ 0 取第 1 项，超出取最后一项）。
  /// </summary>
  public sealed class DotaTable<T>
  {
    private readonly T[] Values;

    internal DotaTable(T[] Values)
    {
      if (Values == null || Values.Length == 0)
        throw new ArgumentException("DotaTable requires at least one value.", nameof(Values));
      this.Values = Values;
    }

    /// <summary>按 1 起始序号取值（自动夹取到 [1, Count]）。</summary>
    public T this[int OneBasedIndex] => Values[Math.Clamp(OneBasedIndex - 1, 0, Values.Length - 1)];

    public int Count => Values.Length;

    public IReadOnlyList<T> All => Values;
  }

  public static class DotaTable
  {
    public static DotaTable<T> Of<T>(params T[] Values) => new(Values);

    /// <summary>所有序号取同一值（如淘汰之刃全熟练度 2 格射程）。</summary>
    public static DotaTable<T> Uniform<T>(T Value) => new(new[] { Value });
  }
}
