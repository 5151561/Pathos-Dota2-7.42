using System;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// Pathos 六维属性的强类型枚举（替代 "strength" / "dexterity" 等裸字符串）。
  /// </summary>
  public enum DotaStat
  {
    Str,
    Dex,
    Con,
    Int,
    Wis,
    Cha
  }

  /// <summary>
  /// 六维属性读写的强类型封装（转发至 <see cref="PathosEngineBridge"/> 的按名称接口）：
  /// - 当前值：<see cref="Get"/> / <see cref="Str"/> / <see cref="Dex"/> / <see cref="Con"/>
  /// - 上限值（LimitForm，含职业上限、天赋/黄点加成）：<see cref="Limit"/>
  /// - 永久写入（CurrentForm + MaximumForm 同步）：<see cref="SetPermanent"/> / <see cref="RaisePermanentClamped"/>
  /// </summary>
  public static class DotaStats
  {
    /// <summary>六维枚举对应的 Pathos 原生属性名。</summary>
    public static string AttributeName(DotaStat Stat) => Stat switch
    {
      DotaStat.Str => "strength",
      DotaStat.Dex => "dexterity",
      DotaStat.Con => "constitution",
      DotaStat.Int => "intelligence",
      DotaStat.Wis => "wisdom",
      DotaStat.Cha => "charisma",
      _ => throw new ArgumentOutOfRangeException(nameof(Stat), Stat, null)
    };

    /// <summary>角色 CurrentForm 中的当前属性值（未找到返回 0）。</summary>
    public static int Get(Character Character, DotaStat Stat)
      => PathosEngineBridge.GetCurrentAbilityScore(Character, AttributeName(Stat));

    public static int Str(Character Character) => Get(Character, DotaStat.Str);
    public static int Dex(Character Character) => Get(Character, DotaStat.Dex);
    public static int Con(Character Character) => Get(Character, DotaStat.Con);

    /// <summary>角色 LimitForm 中的属性上限（未找到返回 25）。</summary>
    public static int Limit(Character Character, DotaStat Stat)
      => PathosEngineBridge.GetLimitAbilityScore(Character, AttributeName(Stat));

    /// <summary>同步写入 CurrentForm 与 MaximumForm（总量守恒，恢复属性药水不会白嫖回满）。</summary>
    public static bool SetPermanent(Character Character, DotaStat Stat, int NewScore)
      => PathosEngineBridge.SetPermanentAbilityScore(Character, AttributeName(Stat), NewScore);

    /// <summary>
    /// 永久提升属性并夹取到当前上限：写入 Min(Limit, Current + Delta)。
    /// 用于属性类天赋（上限加成须先生效，本方法在其后调用即可吃满新上限）。
    /// </summary>
    public static bool RaisePermanentClamped(Character Character, DotaStat Stat, int Delta)
    {
      if (Character == null || Delta == 0)
        return false;

      int Cur = Get(Character, Stat);
      int Max = Limit(Character, Stat);
      return SetPermanent(Character, Stat, Math.Min(Max, Cur + Delta));
    }
  }
}
