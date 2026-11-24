using System;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 最大生命值调整时当前生命值的处理方式。
  /// </summary>
  public enum DotaLifeAdjustMode
  {
    /// <summary>
    /// 保持生命百分比（如斧王「一人之军」力量加成增减）：
    /// 新上限 = Max(<see cref="DotaLife.MinMaxLife"/>, 旧上限 + Delta)，当前值按原比例缩放并夹取到 [1, 新上限]（已死亡保持 0）。
    /// </summary>
    KeepRatio,

    /// <summary>
    /// 上限与当前值同步增减（如属性转换·力量、+力量天赋）：
    /// 新上限 = 旧上限 + Delta，新当前值 = Min(新上限, 旧当前值 + Delta)。
    /// </summary>
    RaiseBoth,

    /// <summary>
    /// 仅改上限、当前值不变（如属性转换·敏捷降低最大生命）：
    /// 新上限 = Max(<see cref="DotaLife.MinMaxLife"/>, 旧上限 + Delta)，当前值仅夹取到 [1, 新上限]。
    /// </summary>
    ClampCurrent
  }

  /// <summary>
  /// 生命值上限联动工具：统一「属性 / 先天加成 → 最大生命」的三种既有写法，避免各英雄手写 SetMeter。
  /// 力量 → 生命的换算系数统一使用 <see cref="DotaMechanicsMapper.HpPerStrengthPoint"/>。
  /// </summary>
  public static class DotaLife
  {
    /// <summary>降低最大生命时的保底上限（<see cref="DotaLifeAdjustMode.KeepRatio"/> / <see cref="DotaLifeAdjustMode.ClampCurrent"/>）。</summary>
    public const int MinMaxLife = 10;

    /// <summary>
    /// 按 <paramref name="Mode"/> 调整角色最大生命（Delta 可正可负；Delta 为 0 或无 Life 时不做任何事）。
    /// </summary>
    public static void AdjustMax(Character Character, int Delta, DotaLifeAdjustMode Mode)
    {
      var Life = Character?.Life;
      if (Life == null || Delta == 0)
        return;

      int OldTotal = Life.Total;
      int OldPoints = Life.Points;
      int NewTotal;
      int NewPoints;

      switch (Mode)
      {
        case DotaLifeAdjustMode.KeepRatio:
        {
          NewTotal = Math.Max(MinMaxLife, OldTotal + Delta);
          float Ratio = OldTotal > 0 ? Math.Clamp((float)OldPoints / OldTotal, 0.0f, 1.0f) : 1.0f;
          NewPoints = OldPoints <= 0 ? 0 : Math.Clamp((int)MathF.Round(NewTotal * Ratio), 1, NewTotal);
          break;
        }

        case DotaLifeAdjustMode.RaiseBoth:
          NewTotal = OldTotal + Delta;
          NewPoints = Math.Min(NewTotal, OldPoints + Delta);
          break;

        case DotaLifeAdjustMode.ClampCurrent:
          NewTotal = Math.Max(MinMaxLife, OldTotal + Delta);
          NewPoints = Math.Clamp(OldPoints, 1, NewTotal);
          break;

        default:
          throw new ArgumentOutOfRangeException(nameof(Mode), Mode, null);
      }

      PathosEngineBridge.SetMeter(Life, NewPoints, NewTotal);
    }

    /// <summary>
    /// 按力量点数联动最大生命（每点 <see cref="DotaMechanicsMapper.HpPerStrengthPoint"/> HP）。
    /// </summary>
    public static void AdjustMaxByStrength(Character Character, int StrengthDelta, DotaLifeAdjustMode Mode)
      => AdjustMax(Character, StrengthDelta * DotaMechanicsMapper.HpPerStrengthPoint, Mode);
  }
}
