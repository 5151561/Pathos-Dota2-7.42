using System.Runtime.CompilerServices;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 弱引用的角色标记集合（替代 static HashSet&lt;Character&gt;）。
  /// 底层为 <see cref="ConditionalWeakTable{TKey, TValue}"/>：角色被回收（死亡清理、读档、切换战役）后标记自动消失，
  /// 不会像强引用集合那样残留旧角色或造成内存泄漏（铁律 6）。
  /// </summary>
  public sealed class DotaCharacterFlagSet
  {
    private static readonly object Marker = new();
    private readonly ConditionalWeakTable<Character, object> Table = new();

    public bool Contains(Character Character)
      => Character != null && Table.TryGetValue(Character, out _);

    /// <summary>标记角色；已存在时返回 false。</summary>
    public bool Add(Character Character)
      => Character != null && Table.TryAdd(Character, Marker);

    /// <summary>移除标记；原本存在时返回 true。</summary>
    public bool Remove(Character Character)
      => Character != null && Table.Remove(Character);
  }
}
