using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework.Targeting
{
  public enum DotaTargetMode
  {
    UnitHostile,
    PointOpen,
    PointAny
  }

  public sealed class DotaRangeSpellSpec
  {
    public string SpellName { get; }
    public string DisplayTitle { get; }
    public DotaTargetMode Mode { get; }
    public Func<Character, int> ResolveRange { get; }
    public bool AllowSelf { get; }

    public DotaRangeSpellSpec(
      string SpellName,
      string DisplayTitle,
      DotaTargetMode Mode,
      Func<Character, int> ResolveRange,
      bool AllowSelf = false)
    {
      this.SpellName = SpellName;
      this.DisplayTitle = DisplayTitle;
      this.Mode = Mode;
      this.ResolveRange = ResolveRange;
      this.AllowSelf = AllowSelf;
    }
  }

  public sealed class PendingSpellTarget
  {
    public Square TargetSquare { get; }
    public Character TargetCharacter { get; }
    public DateTime CreatedAtUtc { get; }

    public PendingSpellTarget(Square TargetSquare, Character TargetCharacter)
    {
      this.TargetSquare = TargetSquare;
      this.TargetCharacter = TargetCharacter;
      this.CreatedAtUtc = DateTime.UtcNow;
    }
  }

  /// <summary>
  /// Pathos 原生选格扩距桥接：
  /// 复用 Pathos.Theme 原生 DirectQuery 选框配色（DirectBackgroundBrush / HighlightPen / HostileBrush / HostileTargetPen），
  /// 仅将原版 DirectQuery 局限于周围 1 格的 8 方向方块选框扩展至技能的实际最大射程，
  /// 允许玩家直接点击射程内的任意格子或敌方目标施放技能。
  /// </summary>
  public static class DotaRangeTargetingBridge
  {
    private sealed class ActiveTargetingSession
    {
      public object Stage;
      public Character Caster;
      public DotaRangeSpellSpec Spec;
      public int MaxRange;
      public Action<Direction> SelectAction;
    }

    private static readonly Dictionary<string, DotaRangeSpellSpec> RegisteredSpells = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConditionalWeakTable<object, ActiveTargetingSession> StageSessions = new();
    private static readonly ConditionalWeakTable<Character, PendingSpellTarget> PendingTargets = new();

    [ThreadStatic] private static object CurrentCastStage;
    [ThreadStatic] private static Spell CurrentCastSpell;

    // 反射缓存（Pathos.dll 客户端类型）
    private static PropertyInfo StagePlayerProp;
    private static PropertyInfo StageShellProp;
    private static PropertyInfo ShellEngineProp;
    private static PropertyInfo SceneStageProp;
    private static PropertyInfo SceneDrawXProp;
    private static PropertyInfo SceneDrawYProp;
    private static PropertyInfo SceneSquareSizeProp;
    private static PropertyInfo SceneViewRegionProp;
    private static PropertyInfo SceneViewMapProp;
    private static PropertyInfo GestureSquareProp;

    private static PropertyInfo ThemeDirectBackgroundBrushProp;
    private static PropertyInfo ThemeHighlightPenProp;
    private static PropertyInfo ThemeHostileBrushProp;
    private static PropertyInfo ThemeHostileTargetPenProp;

    public static void RegisterSpell(DotaRangeSpellSpec Spec)
    {
      if (Spec == null || string.IsNullOrEmpty(Spec.SpellName))
        return;

      RegisteredSpells[Spec.SpellName] = Spec;
    }

    public static PendingSpellTarget ConsumePendingTarget(Character Caster)
    {
      if (Caster == null)
        return null;

      if (!PendingTargets.TryGetValue(Caster, out var Target))
        return null;

      PendingTargets.Remove(Caster);
      if ((DateTime.UtcNow - Target.CreatedAtUtc).TotalSeconds > 10.0)
        return null;

      return Target;
    }

    internal static void ApplyPatches(Harmony Harmony)
    {
      try
      {
        var ClientAsm = AppDomain.CurrentDomain.GetAssemblies()
          .FirstOrDefault(A => string.Equals(A.GetName().Name, "Pathos", StringComparison.OrdinalIgnoreCase));

        if (ClientAsm == null)
        {
          HookBootstrap.WriteDebug("Pathos.dll not loaded; skipping DotaRangeTargetingBridge patches.");
          return;
        }

        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var StagePanelType = ClientAsm.GetType("Pathos.StagePanel");
        var ScenePanelType = ClientAsm.GetType("Pathos.ScenePanel");
        var DirectQueryType = ClientAsm.GetType("Pathos.DirectQuery");
        var GestureType = ClientAsm.GetType("Pathos.Gesture");
        var ShellType = ClientAsm.GetType("Pathos.Shell");
        var ThemeType = ClientAsm.GetType("Pathos.Theme");

        if (StagePanelType == null || ScenePanelType == null || DirectQueryType == null || GestureType == null)
        {
          HookBootstrap.WriteError("DotaRangeTargetingBridge: required Pathos client types not found.");
          return;
        }

        StagePlayerProp = StagePanelType.GetProperty("Player", AnyInstance);
        StageShellProp = StagePanelType.GetProperty("Shell", AnyInstance);
        ShellEngineProp = ShellType?.GetProperty("Engine", AnyInstance);
        SceneStageProp = ScenePanelType.GetProperty("Stage", AnyInstance);
        SceneDrawXProp = ScenePanelType.GetProperty("DrawX", AnyInstance);
        SceneDrawYProp = ScenePanelType.GetProperty("DrawY", AnyInstance);
        SceneSquareSizeProp = ScenePanelType.GetProperty("SquareSize", AnyInstance);
        SceneViewRegionProp = ScenePanelType.GetProperty("ViewRegion", AnyInstance);
        SceneViewMapProp = ScenePanelType.GetProperty("ViewMap", AnyInstance);
        GestureSquareProp = GestureType.GetProperty("Square", AnyInstance);

        ThemeDirectBackgroundBrushProp = ThemeType?.GetProperty("DirectBackgroundBrush", AnyStatic);
        ThemeHighlightPenProp = ThemeType?.GetProperty("HighlightPen", AnyStatic);
        ThemeHostileBrushProp = ThemeType?.GetProperty("HostileBrush", AnyStatic);
        ThemeHostileTargetPenProp = ThemeType?.GetProperty("HostileTargetPen", AnyStatic);

        // 1. StagePanel.CastSpell(Spell)：记录当前发起施法的法术与舞台
        PatchSafe(Harmony, StagePanelType, "CastSpell", new[] { typeof(Spell) },
          prefix: nameof(StageCastSpellPrefix),
          postfix: nameof(StageCastSpellPostfix));

        // 2. DirectQuery.Ask(...)：将 1 格方向框替换为同风格的全射程方格选框
        PatchSafe(Harmony, DirectQueryType, "Ask",
          new[] { typeof(string), typeof(Dice), typeof(bool), typeof(Action<Direction>) },
          prefix: nameof(DirectQueryAskPrefix));

        // 3. StagePanel.SelectSquare(Gesture, bool)：捕获玩家在射程方格内的点击
        PatchSafe(Harmony, StagePanelType, "SelectSquare", new[] { GestureType, typeof(bool) },
          prefix: nameof(StageSelectSquarePrefix));

        // 4. StagePanel.CheckCancel()：ESC 优先取消选格
        PatchSafe(Harmony, StagePanelType, "CheckCancel", Type.EmptyTypes,
          prefix: nameof(StageCheckCancelPrefix));

        // 5. ScenePanel.Draw(DrawContract)：使用 Pathos 原生配色绘制射程内方块选框
        PatchSafe(Harmony, ScenePanelType, "Draw", new[] { typeof(DrawContract) },
          postfix: nameof(SceneDrawPostfix));
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaRangeTargetingBridge.ApplyPatches failed: {Ex}");
      }
    }

    private static void PatchSafe(
      Harmony Harmony,
      Type TargetType,
      string MethodName,
      Type[] ParamTypes,
      string prefix = null,
      string postfix = null)
    {
      var Method = AccessTools.Method(TargetType, MethodName, ParamTypes);
      if (Method == null)
      {
        HookBootstrap.WriteError($"DotaRangeTargetingBridge target not found: {TargetType?.FullName}.{MethodName}");
        return;
      }

      var PrefixMethod = prefix != null ? new HarmonyMethod(typeof(DotaRangeTargetingBridge), prefix) : null;
      var PostfixMethod = postfix != null ? new HarmonyMethod(typeof(DotaRangeTargetingBridge), postfix) : null;
      Harmony.Patch(Method, prefix: PrefixMethod, postfix: PostfixMethod);
      HookBootstrap.WriteDebug($"Patched {TargetType.FullName}.{MethodName} (Targeting)");
    }

    private static void StageCastSpellPrefix(object __instance, Spell Spell)
    {
      if (__instance != null)
        StageSessions.Remove(__instance);

      CurrentCastStage = __instance;
      CurrentCastSpell = Spell;
    }

    private static void StageCastSpellPostfix()
    {
      CurrentCastStage = null;
      CurrentCastSpell = null;
    }

    private static bool DirectQueryAskPrefix(Action<Direction> SelectAction)
    {
      if (CurrentCastStage == null || CurrentCastSpell == null || SelectAction == null)
        return true;

      if (!RegisteredSpells.TryGetValue(CurrentCastSpell.Name, out var Spec))
        return true;

      try
      {
        var Player = StagePlayerProp?.GetValue(CurrentCastStage) as Player;
        var Caster = Player?.Prime?.Character;
        if (Caster?.Square == null || Caster.Deceased)
          return true;

        int MaxRange = Math.Max(1, Spec.ResolveRange?.Invoke(Caster) ?? 4);
        var Session = new ActiveTargetingSession
        {
          Stage = CurrentCastStage,
          Caster = Caster,
          Spec = Spec,
          MaxRange = MaxRange,
          SelectAction = SelectAction
        };

        StageSessions.AddOrUpdate(CurrentCastStage, Session);

        // 使用 Pathos 原生飘字提示当前技能射程
        var Shell = StageShellProp?.GetValue(CurrentCastStage);
        var Engine = Shell != null ? ShellEngineProp?.GetValue(Shell) : null;
        if (Engine != null)
          PathosEngineBridge.StoryCaption(Engine, Caster.Square, Colour.White, $"{Spec.DisplayTitle} ({MaxRange})", false);

        return false;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DirectQueryAskPrefix failed: {Ex}");
        return true;
      }
    }

    private static bool StageSelectSquarePrefix(object __instance, object Gesture)
    {
      if (__instance == null || !StageSessions.TryGetValue(__instance, out var Session))
        return true;

      try
      {
        var ClickedSquare = Gesture != null ? GestureSquareProp?.GetValue(Gesture) as Square : null;
        var Caster = Session.Caster;

        if (ClickedSquare == null || Caster?.Square == null || Caster.Deceased || ClickedSquare.Map != Caster.Square.Map)
        {
          StageSessions.Remove(__instance);
          return false;
        }

        int Dist = Caster.Square.AsRange(ClickedSquare);
        if (Dist > Session.MaxRange || (Dist == 0 && !Session.Spec.AllowSelf))
        {
          StageSessions.Remove(__instance);
          return false;
        }

        PendingTargets.AddOrUpdate(Caster, new PendingSpellTarget(ClickedSquare, ClickedSquare.Character));
        var Dir = Dist == 0
          ? Caster.Square.AsDirection(Caster.Square)
          : Caster.Square.AsDirection(ClickedSquare);

        StageSessions.Remove(__instance);
        Session.SelectAction?.Invoke(Dir);
        return false;
      }
      catch (Exception Ex)
      {
        StageSessions.Remove(__instance);
        HookBootstrap.WriteError($"StageSelectSquarePrefix failed: {Ex}");
        return false;
      }
    }

    private static bool StageCheckCancelPrefix(object __instance, ref bool __result)
    {
      if (__instance != null && StageSessions.TryGetValue(__instance, out _))
      {
        StageSessions.Remove(__instance);
        __result = true;
        return false;
      }

      return true;
    }

    private static void SceneDrawPostfix(object __instance, DrawContract DC)
    {
      if (__instance == null || DC == null)
        return;

      try
      {
        var Stage = SceneStageProp?.GetValue(__instance);
        if (Stage == null || !StageSessions.TryGetValue(Stage, out var Session))
          return;

        var Caster = Session.Caster;
        var ViewMap = SceneViewMapProp?.GetValue(__instance) as Map;
        if (Caster?.Square == null || Caster.Deceased || ViewMap == null || Caster.Square.Map != ViewMap)
        {
          StageSessions.Remove(Stage);
          return;
        }

        int SquareSize = (int)(SceneSquareSizeProp?.GetValue(__instance) ?? 0);
        if (SquareSize <= 0)
          return;

        int DrawX = (int)(SceneDrawXProp?.GetValue(__instance) ?? 0);
        int DrawY = (int)(SceneDrawYProp?.GetValue(__instance) ?? 0);
        var ViewRegion = (Region)SceneViewRegionProp.GetValue(__instance);

        // 复用 Pathos.Theme 原生 DirectQuery 选框配色
        var TileFill = ThemeDirectBackgroundBrushProp?.GetValue(null) as Colour ?? Colour.FromArgb(48, 255, 255, 255);
        var TileBorder = ThemeHighlightPenProp?.GetValue(null) as Colour ?? Colour.FromArgb(140, 255, 255, 255);
        var HostileFill = ThemeHostileBrushProp?.GetValue(null) as Colour ?? Colour.FromArgb(72, 220, 40, 40);
        var HostileBorder = ThemeHostileTargetPenProp?.GetValue(null) as Colour ?? Colour.Red;

        int Range = Session.MaxRange;
        var Origin = Caster.Square;

        for (int Dy = -Range; Dy <= Range; Dy++)
        {
          for (int Dx = -Range; Dx <= Range; Dx++)
          {
            if (Dx == 0 && Dy == 0 && !Session.Spec.AllowSelf)
              continue;

            int Tx = Origin.X + Dx;
            int Ty = Origin.Y + Dy;
            if (!ViewMap.IsValid(Tx, Ty))
              continue;

            var Sq = ViewMap[Tx, Ty];
            if (Sq == null || Sq.IsVoid())
              continue;

            if (Sq.IsBlocked(Vision: false) && Sq.Character == null)
              continue;

            int Left = DrawX + (Tx - ViewRegion.Left) * SquareSize;
            int Top = DrawY + (Ty - ViewRegion.Top) * SquareSize;
            var TileRect = new Rect(Left + 1, Top + 1, SquareSize - 2, SquareSize - 2);

            if (Session.Spec.Mode == DotaTargetMode.UnitHostile && IsHostile(Caster, Sq.Character))
              DC.DrawRectangle(HostileFill, HostileBorder, 2, TileRect);
            else
              DC.DrawRectangle(TileFill, TileBorder, 1, TileRect);
          }
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaRangeTargetingBridge.SceneDrawPostfix failed: {Ex}");
      }
    }

    private static bool IsHostile(Character Source, Character Target)
    {
      if (Source == null || Target == null || Target == Source || Target.Deceased)
        return false;
      if (Target.Life != null && Target.Life.Points <= 0)
        return false;
      if (Target.Neutral || PathosEngineBridge.IsAllied(Source, Target))
        return false;
      return true;
    }
  }
}
