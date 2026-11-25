using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework.Presentation
{
  /// <summary>
  /// 自定义 VFX 的渲染样式（淡入淡出、缩放、旋转、跨格尺寸、峰值透明度）。
  ///
  /// Pathos 原生 <c>Striking</c> / <c>Beaming</c> / <c>Exploding</c> 渲染全程 100% 不透明、没有任何衰减，
  /// 多段特效叠在一起会「满屏高亮交错」。为 Strike / Beam / Explosion 注册样式后，
  /// <see cref="DotaVfx"/> 的客户端补丁接管其绘制，按动画进度计算透明度包络与变换。
  /// </summary>
  public sealed class DotaVfxStyle
  {
    /// <summary>淡入所占生命周期比例（0 = 直接满亮度）。</summary>
    public float FadeIn { get; init; } = 0.12f;

    /// <summary>从生命周期的哪个比例开始淡出（到 1.0 时完全透明，二次缓出）。</summary>
    public float FadeOutStart { get; init; } = 0.45f;

    /// <summary>峰值不透明度（0~1），叠加较多的特效建议 0.6~0.85。</summary>
    public float PeakOpacity { get; init; } = 1.0f;

    /// <summary>起始缩放（相对占格尺寸）。</summary>
    public float ScaleFrom { get; init; } = 1.0f;

    /// <summary>结束缩放（相对占格尺寸，三次缓出插值）。</summary>
    public float ScaleTo { get; init; } = 1.0f;

    /// <summary>Strike 的占格边长（1 = 单格，3 = 覆盖 3×3）。</summary>
    public int Span { get; init; } = 1;

    /// <summary>旋转速度（度/秒），按 Story 绝对时钟计算，循环重复生成的特效可无缝衔接。</summary>
    public float SpinDegreesPerSecond { get; init; }

    /// <summary>每个特效实例随机固定一个角度（用于斩击类，避免多刀完全重叠）。</summary>
    public bool RandomAngle { get; init; }

    /// <summary>施放时指定方向角后的随机抖动幅度（±度）。</summary>
    public float AngleJitter { get; init; }

    /// <summary>
    /// 地面层（目前仅 Explosion 支持）：改为逐格画在地板之上、物品与角色之下（如帕吉腐烂毒雾），
    /// 而不是像普通特效那样在所有角色之后整体覆盖。地面层忽略缩放，只套用淡入淡出与峰值不透明度；
    /// 不会画在墙体 / 关闭的门等阻挡格上。
    /// </summary>
    public bool Ground { get; init; }

    internal float Opacity(float P)
    {
      float A = 1.0f;
      if (FadeIn > 0.0f && P < FadeIn)
      {
        float T = P / FadeIn;
        A = 1.0f - (1.0f - T) * (1.0f - T);
      }
      else if (P > FadeOutStart && FadeOutStart < 1.0f)
      {
        float T = 1.0f - (P - FadeOutStart) / (1.0f - FadeOutStart);
        A = T * T;
      }
      return Math.Clamp(A * PeakOpacity, 0.0f, 1.0f);
    }

    internal float Scale(float P)
    {
      float E = 1.0f - MathF.Pow(1.0f - Math.Clamp(P, 0.0f, 1.0f), 3.0f);
      return ScaleFrom + (ScaleTo - ScaleFrom) * E;
    }
  }

  /// <summary>
  /// DOTA2 英雄 VFX 统一入口：样式注册 + 带时长（秒）的播放 + 跟随角色 + 客户端渲染接管。
  /// 英雄代码只需 <c>DotaVfx.Style(strike, new DotaVfxStyle { ... })</c> 一次，
  /// 之后用 <see cref="Strike"/> / <see cref="StrikeOn"/> / <see cref="Beam"/> / <see cref="Explode"/> 播放。
  /// </summary>
  public static class DotaVfx
  {
    private sealed class InstanceState
    {
      public Character Anchor;
      public float? Angle;
      public int? Span;
    }

    private static readonly Dictionary<object, DotaVfxStyle> Styles = new(ReferenceEqualityComparer.Instance);
    private static readonly ConditionalWeakTable<object, InstanceState> Instances = new();

    // 地面层：动画阶段（角色之后）只记录要画的格子，下一帧 ScenePanel.DrawLocation 画完该格地板后立即补画（1 帧延迟，肉眼不可见）
    private static Dictionary<Square, List<(Glyph Glyph, float Opacity)>> GroundRecording = new();
    private static Dictionary<Square, List<(Glyph Glyph, float Opacity)>> GroundDrawing = new();
    private static readonly HashSet<Square> GroundDrawnThisFrame = new();

    private static readonly Assembly EngineAsm = typeof(Expansion).Assembly;
    private static readonly Type EngineType = EngineAsm.GetType("Pathos.Engine");
    private static readonly Type StoryType = EngineAsm.GetType("Pathos.Story");
    private static readonly Type AnimationType = EngineAsm.GetType("Pathos.Animation");
    private static readonly Type StrikingType = EngineAsm.GetType("Pathos.StrikingAnimation");
    private static readonly Type BeamingType = EngineAsm.GetType("Pathos.BeamingAnimation");
    private static readonly Type ExplodingType = EngineAsm.GetType("Pathos.ExplodingAnimation");

    private static readonly PropertyInfo EngineStoryProp = EngineType?.GetProperty("Story", PathosClientReflection.AnyInstance);
    private static readonly MethodInfo StoryNonCharacterAnimationsMethod = StoryType?.GetMethod("GetNonCharacterAnimations", PathosClientReflection.AnyInstance);
    private static readonly MethodInfo AnimationProgressMethod = AnimationType?.GetMethod("GetProgress", PathosClientReflection.AnyInstance);
    private static readonly MethodInfo AnimationStartedMethod = AnimationType?.GetMethod("IsStarted", PathosClientReflection.AnyInstance);
    private static readonly MethodInfo AnimationElapsedMethod = AnimationType?.GetMethod("GetElapsed", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo AnimationEpisodeProp = AnimationType?.GetProperty("Episode", PathosClientReflection.AnyInstance);

    private static readonly PropertyInfo StrikingSquareProp = StrikingType?.GetProperty("Square", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo StrikingStrikeProp = StrikingType?.GetProperty("Strike", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo BeamingSquareProp = BeamingType?.GetProperty("Square", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo BeamingDirectionProp = BeamingType?.GetProperty("Direction", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo BeamingBeamProp = BeamingType?.GetProperty("Beam", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo ExplodingSquareProp = ExplodingType?.GetProperty("Square", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo ExplodingExplosionProp = ExplodingType?.GetProperty("Explosion", PathosClientReflection.AnyInstance);
    private static readonly PropertyInfo LocationSquareProp = EngineAsm.GetType("Pathos.Location")?.GetProperty("Square", PathosClientReflection.AnyInstance);

    // 客户端（Pathos.dll）ScenePanel 成员，在 ApplyPatches 中解析
    private static FieldInfo AnimatorScenePanelField;
    private static PropertyInfo PanelDrawXProp;
    private static PropertyInfo PanelDrawYProp;
    private static PropertyInfo PanelViewRegionProp;
    private static PropertyInfo PanelSquareSizeProp;
    private static MethodInfo PanelIsObscuredMethod;
    private static MethodInfo PanelSelectImageMethod;

    // ---------------------------------------------------------------- 样式注册

    public static Strike Style(Strike Strike, DotaVfxStyle Style) { Register(Strike, Style); return Strike; }
    public static Beam Style(Beam Beam, DotaVfxStyle Style) { Register(Beam, Style); return Beam; }
    public static Explosion Style(Explosion Explosion, DotaVfxStyle Style) { Register(Explosion, Style); return Explosion; }

    private static void Register(object Key, DotaVfxStyle Style)
    {
      if (Key == null || Style == null)
        return;
      lock (Styles)
        Styles[Key] = Style;
    }

    private static DotaVfxStyle GetStyle(object Key)
    {
      if (Key == null)
        return null;
      lock (Styles)
        return Styles.TryGetValue(Key, out var S) ? S : null;
    }

    // ---------------------------------------------------------------- 播放

    /// <summary>
    /// 在格子上播放 Strike（时长单位：秒）。<paramref name="AngleDegrees"/> 指定斩击方向；
    /// <paramref name="Span"/> 覆盖样式的占格边长（如按技能半径 R 传 2R+1，让冲击环正好覆盖作用范围）。
    /// </summary>
    public static void Strike(object Engine, Square Square, Strike Strike, float Seconds, float? AngleDegrees = null, int? Span = null)
    {
      if (Engine == null || Square == null || Strike == null)
        return;
      PathosEngineBridge.StoryStriking(Engine, DotaPresentation.Seconds(Seconds), Square, Strike);
      if (AngleDegrees.HasValue || Span.HasValue)
        Tag(Engine, S => { S.Angle = AngleDegrees; S.Span = Span; });
    }

    /// <summary>在角色身上播放 Strike，并在整个生命周期内跟随角色移动（如剑刃风暴旋风、光环）。</summary>
    public static void StrikeOn(object Engine, Character Character, Strike Strike, float Seconds)
    {
      if (Engine == null || Character?.Square == null || Strike == null)
        return;
      PathosEngineBridge.StoryStriking(Engine, DotaPresentation.Seconds(Seconds), Character.Square, Strike);
      Tag(Engine, S => S.Anchor = Character);
    }

    /// <summary>播放单格光束段。</summary>
    public static void Beam(object Engine, Square Square, Direction Direction, Beam Beam, float Seconds)
    {
      if (Engine == null || Square == null || Beam == null)
        return;
      PathosEngineBridge.StoryBeaming(Engine, DotaPresentation.Seconds(Seconds), Square, Direction, Beam);
    }

    /// <summary>沿 From → To 的直线路径铺设一段光束残影（不含起点则传 IncludeStart = false）。</summary>
    public static void BeamTrail(object Engine, Square From, Square To, Beam Beam, float Seconds, bool IncludeStart = true, bool IncludeEnd = true)
    {
      if (Engine == null || From == null || To == null || From.Map != To.Map || Beam == null)
        return;
      var Dir = From.AsDirection(To);
      var Path = LinePath(From, To);
      for (int I = 0; I < Path.Count; I++)
      {
        if ((I == 0 && !IncludeStart) || (I == Path.Count - 1 && !IncludeEnd))
          continue;
        PathosEngineBridge.StoryBeaming(Engine, DotaPresentation.Seconds(Seconds), Path[I], Dir, Beam);
      }
    }

    /// <summary>播放 3×3 爆炸。</summary>
    public static void Explode(object Engine, Square Square, Explosion Explosion, float Seconds)
    {
      if (Engine == null || Square == null || Explosion == null)
        return;
      PathosEngineBridge.StoryExploding(Engine, DotaPresentation.Seconds(Seconds), Square, Explosion);
    }

    /// <summary>Bresenham 直线路径（含首尾格），供突进/波浪等按格推进的演出使用。</summary>
    public static List<Square> LinePath(Square From, Square To, int MaxSteps = 64)
    {
      var Result = new List<Square>();
      if (From == null || To == null || From.Map != To.Map)
        return Result;

      var Map = From.Map;
      int X0 = From.X, Y0 = From.Y, X1 = To.X, Y1 = To.Y;
      int Dx = Math.Abs(X1 - X0), Dy = Math.Abs(Y1 - Y0);
      int Sx = X0 < X1 ? 1 : -1, Sy = Y0 < Y1 ? 1 : -1;
      int Err = Dx - Dy;
      for (int Steps = 0; Steps < MaxSteps; Steps++)
      {
        if (Map.IsValid(X0, Y0))
          Result.Add(Map[X0, Y0]);
        if (X0 == X1 && Y0 == Y1)
          break;
        int E2 = 2 * Err;
        if (E2 > -Dy) { Err -= Dy; X0 += Sx; }
        if (E2 < Dx) { Err += Dx; Y0 += Sy; }
      }
      return Result;
    }

    /// <summary>方向 → 屏幕角度（度，0 = 向右，顺时针），用于让斩击贴图对齐攻击方向。</summary>
    public static float AngleOf(Square From, Square To)
    {
      if (From == null || To == null || From == To)
        return 0.0f;
      return MathF.Atan2(To.Y - From.Y, To.X - From.X) * 180.0f / MathF.PI;
    }

    private static void Tag(object Engine, Action<InstanceState> Configure)
    {
      try
      {
        var Story = EngineStoryProp?.GetValue(Engine);
        if (Story == null || StoryNonCharacterAnimationsMethod?.Invoke(Story, null) is not IEnumerable List)
          return;

        object Last = null;
        foreach (var A in List)
          Last = A;
        if (Last != null)
          Configure(Instances.GetValue(Last, _ => new InstanceState()));
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaVfx.Tag failed: {Ex.Message}");
      }
    }

    // ---------------------------------------------------------------- 客户端渲染接管

    internal static void ApplyPatches(Harmony Harmony)
    {
      try
      {
        var ClientAsm = PathosClientReflection.FindClientAssembly();
        if (ClientAsm == null)
        {
          HookBootstrap.WriteDebug("Pathos.dll not loaded; skipping DotaVfx render patches.");
          return;
        }

        var AnimatorType = ClientAsm.GetType("Pathos.Animator");
        var PanelType = ClientAsm.GetType("Pathos.ScenePanel");
        if (AnimatorType == null || PanelType == null || AnimationType == null)
        {
          HookBootstrap.WriteError("DotaVfx: Animator / ScenePanel / Animation type not found.");
          return;
        }

        AnimatorScenePanelField = AnimatorType.GetField("ScenePanel", PathosClientReflection.AnyInstance);
        PanelDrawXProp = PanelType.GetProperty("DrawX", PathosClientReflection.AnyInstance);
        PanelDrawYProp = PanelType.GetProperty("DrawY", PathosClientReflection.AnyInstance);
        PanelViewRegionProp = PanelType.GetProperty("ViewRegion", PathosClientReflection.AnyInstance);
        PanelSquareSizeProp = PanelType.GetProperty("SquareSize", PathosClientReflection.AnyInstance);
        PanelIsObscuredMethod = PanelType.GetMethod("IsObscured", PathosClientReflection.AnyInstance, null, new[] { typeof(Square) }, null);
        PanelSelectImageMethod = PanelType.GetMethod("SelectImage", PathosClientReflection.AnyInstance, null, new[] { typeof(Glyph) }, null);

        if (AnimatorScenePanelField == null || PanelDrawXProp == null || PanelSelectImageMethod == null || PanelIsObscuredMethod == null)
        {
          HookBootstrap.WriteError("DotaVfx: required ScenePanel members not found.");
          return;
        }

        PathosClientReflection.PatchSafe(Harmony, AnimatorType, "Execute", new[] { typeof(DrawContract), AnimationType }, typeof(DotaVfx),
          Prefix: nameof(AnimatorExecutePrefix), Tag: "DotaVfx");

        // 地面层：每帧开始交换记录缓冲；每格地板画完后补画该格的地面特效（早于同格物品与角色）
        var LocationType = AccessTools.TypeByName("Pathos.Location");
        PathosClientReflection.PatchSafe(Harmony, PanelType, "Draw", new[] { typeof(DrawContract) }, typeof(DotaVfx),
          Prefix: nameof(ScenePanelDrawPrefix), Tag: "DotaVfx.Ground");
        if (LocationType != null)
        {
          PathosClientReflection.PatchSafe(Harmony, PanelType, "DrawLocation", new[] { typeof(DrawContract), LocationType, typeof(Rect) }, typeof(DotaVfx),
            Postfix: nameof(DrawLocationPostfix), Tag: "DotaVfx.Ground");
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaVfx.ApplyPatches failed: {Ex}");
      }
    }

    private static bool AnimatorExecutePrefix(object __instance, DrawContract DC, object Animation)
    {
      if (Animation == null || DC == null)
        return true;

      try
      {
        var T = Animation.GetType();
        if (T == StrikingType)
          return !DrawStriking(__instance, DC, Animation);
        if (T == BeamingType)
          return !DrawBeaming(__instance, DC, Animation);
        if (T == ExplodingType)
          return !DrawExploding(__instance, DC, Animation);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaVfx render failed: {Ex.Message}");
      }
      return true;
    }

    private readonly struct Frame
    {
      public Frame(object Panel)
      {
        this.Panel = Panel;
        DrawX = (int)PanelDrawXProp.GetValue(Panel);
        DrawY = (int)PanelDrawYProp.GetValue(Panel);
        View = (Region)PanelViewRegionProp.GetValue(Panel);
        Size = (int)PanelSquareSizeProp.GetValue(Panel);
      }

      public readonly object Panel;
      public readonly int DrawX;
      public readonly int DrawY;
      public readonly Region View;
      public readonly int Size;

      public float CenterX(Square S) => DrawX + (S.X - View.Left) * Size + Size * 0.5f;
      public float CenterY(Square S) => DrawY + (S.Y - View.Top) * Size + Size * 0.5f;
      public bool Obscured(Square S) => PanelIsObscuredMethod.Invoke(Panel, new object[] { S }) is true;
      public Image Image(Glyph G) => PanelSelectImageMethod.Invoke(Panel, new object[] { G }) as Image;
    }

    private static bool TryBegin(object Animator, object Animation, out Frame Frame, out float Progress, out long Elapsed, out long StartTick)
    {
      Frame = default;
      Progress = 0.0f;
      Elapsed = 0;
      StartTick = 0;
      if (AnimationStartedMethod?.Invoke(Animation, null) is not true)
        return false;
      var Panel = AnimatorScenePanelField.GetValue(Animator);
      if (Panel == null)
        return false;
      Frame = new Frame(Panel);
      Progress = AnimationProgressMethod?.Invoke(Animation, null) is float P ? P : 0.0f;
      Elapsed = AnimationElapsedMethod?.Invoke(Animation, null) is Delay D ? D.GetTurns() : 0;
      StartTick = AnimationEpisodeProp?.GetValue(Animation) is Episode E ? E.Start.GetTurns() : 0;
      return true;
    }

    private static float ResolveAngle(DotaVfxStyle Style, InstanceState State, Square Square, long StartTick, long Elapsed)
    {
      float Angle = 0.0f;
      uint Hash = (uint)(StartTick * 7919 + Square.X * 73856093 + Square.Y * 19349663);
      float Rand01 = (Hash % 1000) / 1000.0f;

      if (State?.Angle != null)
        Angle = State.Angle.Value + (Style.AngleJitter > 0 ? (Rand01 * 2.0f - 1.0f) * Style.AngleJitter : 0.0f);
      else if (Style.RandomAngle)
        Angle = Rand01 * 360.0f;

      if (Style.SpinDegreesPerSecond != 0.0f)
        Angle += Style.SpinDegreesPerSecond * ((StartTick + Elapsed) / (float)DotaPresentation.TicksPerSecond);

      return Angle % 360.0f;
    }

    private static bool DrawStriking(object Animator, DrawContract DC, object Animation)
    {
      var Strike = StrikingStrikeProp?.GetValue(Animation) as Strike;
      var Style = GetStyle(Strike);
      if (Style == null)
        return false;

      if (!TryBegin(Animator, Animation, out var F, out float P, out long Elapsed, out long Start))
        return true;

      var Square = StrikingSquareProp?.GetValue(Animation) as Square;
      Instances.TryGetValue(Animation, out var State);
      var Anchor = State?.Anchor;
      if (Anchor != null && !Anchor.Deceased && Anchor.Square != null && Anchor.Square.Map == Square?.Map)
        Square = Anchor.Square;

      if (Square == null || Strike.Glyph == null || F.Obscured(Square))
        return true;

      float Opacity = Style.Opacity(P);
      if (Opacity <= 0.01f)
        return true;

      int Px = Math.Max(1, (int)MathF.Round(F.Size * Math.Max(1, State?.Span ?? Style.Span) * Style.Scale(P)));
      float Cx = F.CenterX(Square), Cy = F.CenterY(Square);
      var Rect = new Rect((int)MathF.Round(Cx - Px * 0.5f), (int)MathF.Round(Cy - Px * 0.5f), Px, Px);
      float Angle = ResolveAngle(Style, State, Square, Start, Elapsed);
      Rotation? Rot = MathF.Abs(Angle) > 0.01f ? Rotation.Center(Angle) : null;

      DC.DrawImage(F.Image(Strike.Glyph), Rect, Opacity, null, null, Rot);
      return true;
    }

    private static bool DrawBeaming(object Animator, DrawContract DC, object Animation)
    {
      var Beam = BeamingBeamProp?.GetValue(Animation) as Beam;
      var Style = GetStyle(Beam);
      if (Style == null)
        return false;

      if (!TryBegin(Animator, Animation, out var F, out float P, out _, out _))
        return true;

      var Square = BeamingSquareProp?.GetValue(Animation) as Square;
      if (Square == null || F.Obscured(Square))
        return true;

      float Opacity = Style.Opacity(P);
      if (Opacity <= 0.01f)
        return true;

      var Dir = BeamingDirectionProp?.GetValue(Animation) is Direction D ? D : Direction.East;
      var Glyph = Beam.GetGlyph(Dir);
      if (Glyph == null)
        return true;

      int Px = Math.Max(1, (int)MathF.Round(F.Size * Style.Scale(P)));
      float Cx = F.CenterX(Square), Cy = F.CenterY(Square);
      var Rect = new Rect((int)MathF.Round(Cx - Px * 0.5f), (int)MathF.Round(Cy - Px * 0.5f), Px, Px);
      DC.DrawImage(F.Image(Glyph), Rect, Opacity, null, null, null);
      return true;
    }

    private static bool DrawExploding(object Animator, DrawContract DC, object Animation)
    {
      var Explosion = ExplodingExplosionProp?.GetValue(Animation) as Explosion;
      var Style = GetStyle(Explosion);
      if (Style == null)
        return false;

      if (!TryBegin(Animator, Animation, out var F, out float P, out _, out _))
        return true;

      var Center = ExplodingSquareProp?.GetValue(Animation) as Square;
      if (Center == null || F.Obscured(Center))
        return true;

      float Opacity = Style.Opacity(P);
      if (Opacity <= 0.01f)
        return true;

      if (Style.Ground)
      {
        RecordGround(Center, Explosion, Opacity);
        return true;
      }

      float Scale = Style.Scale(P);
      float Cell = F.Size * Scale;
      float Cx = F.CenterX(Center), Cy = F.CenterY(Center);
      int Index = 0;
      for (int Dy = -1; Dy <= 1; Dy++)
      {
        for (int Dx = -1; Dx <= 1; Dx++, Index++)
        {
          var Glyph = Explosion.GetGlyph((ExplosionArea)Index);
          if (Glyph == null)
            continue;
          float X = Cx + (Dx - 0.5f) * Cell;
          float Y = Cy + (Dy - 0.5f) * Cell;
          int Px = (int)MathF.Ceiling(Cell);
          DC.DrawImage(F.Image(Glyph), new Rect((int)MathF.Round(X), (int)MathF.Round(Y), Px, Px), Opacity, null, null, null);
        }
      }
      return true;
    }

    // ---------------------------------------------------------------- 地面层

    private static void RecordGround(Square Center, Explosion Explosion, float Opacity)
    {
      var Map = Center.Map;
      int Index = 0;
      for (int Dy = -1; Dy <= 1; Dy++)
      {
        for (int Dx = -1; Dx <= 1; Dx++, Index++)
        {
          var Glyph = Explosion.GetGlyph((ExplosionArea)Index);
          if (Glyph == null || Map == null || !Map.IsValid(Center.X + Dx, Center.Y + Dy))
            continue;

          var Sq = Map[Center.X + Dx, Center.Y + Dy];
          if (!GroundRecording.TryGetValue(Sq, out var List))
            GroundRecording[Sq] = List = new List<(Glyph, float)>(1);
          List.Add((Glyph, Opacity));
        }
      }
    }

    private static void ScenePanelDrawPrefix()
    {
      // 上一帧动画阶段记录的地面层 → 本帧随地板绘制
      (GroundDrawing, GroundRecording) = (GroundRecording, GroundDrawing);
      GroundRecording.Clear();
      GroundDrawnThisFrame.Clear();
    }

    private static void DrawLocationPostfix(object __instance, DrawContract DC, object Location, Rect LocationRect)
    {
      if (GroundDrawing.Count == 0 || DC == null || Location == null)
        return;

      try
      {
        var Square = LocationSquareProp?.GetValue(Location) as Square;
        if (Square == null || !GroundDrawing.TryGetValue(Square, out var List) || !GroundDrawnThisFrame.Add(Square))
          return;
        if (Square.IsVoid() || Square.IsBlocked(Vision: true))
          return;

        foreach (var (Glyph, Opacity) in List)
        {
          if (PanelSelectImageMethod.Invoke(__instance, new object[] { Glyph }) is Image Img)
            DC.DrawImage(Img, LocationRect, Opacity, null, null, null);
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaVfx ground layer failed: {Ex.Message}");
      }
    }
  }
}
