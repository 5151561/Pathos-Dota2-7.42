using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework.Talents
{
  /// <summary>
  /// DOTA2 独立天赋树面板 UI 桥接层（原生 Pathos 风格）：
  /// - 入口按钮为原生 Pathos.MenuButton，放置在左下角「休息」按钮右侧（通过 StagePanel.EdgeWidth 动态对齐，避免被 LeftDock 裁切）。
  /// - 天赋树面板为左下角停靠的原生 Pathos 风格侧边面板（与角色/法术/日志面板同位互斥），复用 Pathos.Theme 配色与标准字体。
  /// - 支持 F2 快捷开关、ESC 关闭；原生 TalentPanel.Refresh 中自动隐藏内部持久化标记（dota_talent_*）。
  /// </summary>
  internal static class TalentTreeUiBridge
  {
    private static readonly ConditionalWeakTable<object, StageTalentController> StageControllers = new();

    // StagePanel / Shell / Theme / MenuButton 反射缓存
    private static FieldInfo StageSurfaceField;
    private static FieldInfo StageLeftMenuStackField;
    private static PropertyInfo StagePlayerProp;
    private static PropertyInfo StageShellProp;
    private static PropertyInfo StageCharacterPanelProp;
    private static PropertyInfo StageCastPanelProp;
    private static PropertyInfo StageJournalPanelProp;
    private static MethodInfo StageEdgeWidthMethod;
    private static MethodInfo StageAddModalityPanelMethod;
    private static MethodInfo StageRemoveModalityPanelMethod;
    private static MethodInfo StageRefreshPanelsMethod;

    private static PropertyInfo ShellEngineProp;
    private static PropertyInfo ShellGameProp;
    private static PropertyInfo GameStandardFontNameProp;

    private static PropertyInfo MenuStackContextProp;
    private static ConstructorInfo MenuButtonCtor;
    private static PropertyInfo MenuButtonImageProp;
    private static PropertyInfo MenuButtonHintProp;
    private static PropertyInfo MenuButtonIsCheckedProp;
    private static PropertyInfo MenuButtonIsHighlightedProp;
    private static PropertyInfo MenuButtonVisibilityProp;
    private static MethodInfo MenuButtonArrangeMethod;
    private static EventInfo MenuButtonSingleTapEvent;

    private static PropertyInfo ThemePanelColourProp;
    private static PropertyInfo ThemeStrokeColourProp;
    private static MethodInfo ThemeGetImageMethod;

    private static FieldInfo TalentPanelDictField;
    private static PropertyInfo TileOptionIsVisibleProp;
    private static PropertyInfo KeystrokeKeyProp;

    public static void ApplyUiPatches(Harmony Harmony)
    {
      try
      {
        var PathosClientAsm = AppDomain.CurrentDomain.GetAssemblies()
          .FirstOrDefault(A => string.Equals(A.GetName().Name, "Pathos", StringComparison.OrdinalIgnoreCase));

        if (PathosClientAsm == null)
        {
          HookBootstrap.WriteDebug("Pathos.dll not loaded; skipping TalentTree UI patches.");
          return;
        }

        var StagePanelType = PathosClientAsm.GetType("Pathos.StagePanel");
        var TalentPanelType = PathosClientAsm.GetType("Pathos.TalentPanel");
        var ShellType = PathosClientAsm.GetType("Pathos.Shell");
        var GameType = PathosClientAsm.GetType("Pathos.Game");
        var MenuStackType = PathosClientAsm.GetType("Pathos.MenuStack");
        var MenuButtonType = PathosClientAsm.GetType("Pathos.MenuButton");
        var ThemeType = PathosClientAsm.GetType("Pathos.Theme");
        var TileOptionType = PathosClientAsm.GetType("Pathos.TileOption");

        if (StagePanelType == null)
          return;

        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        StageSurfaceField = StagePanelType.GetField("Surface", AnyInstance);
        StageLeftMenuStackField = StagePanelType.GetField("LeftMenuStack", AnyInstance);
        StagePlayerProp = StagePanelType.GetProperty("Player", AnyInstance);
        StageShellProp = StagePanelType.GetProperty("Shell", AnyInstance);
        StageCharacterPanelProp = StagePanelType.GetProperty("CharacterPanel", AnyInstance);
        StageCastPanelProp = StagePanelType.GetProperty("CastPanel", AnyInstance);
        StageJournalPanelProp = StagePanelType.GetProperty("JournalPanel", AnyInstance);
        StageEdgeWidthMethod = StagePanelType.GetMethod("EdgeWidth", AnyInstance, null, Type.EmptyTypes, null);
        StageAddModalityPanelMethod = StagePanelType.GetMethod("AddModalityPanel", AnyInstance, null, new[] { typeof(Inv.Panel) }, null);
        StageRemoveModalityPanelMethod = StagePanelType.GetMethod("RemoveModalityPanel", AnyInstance, null, new[] { typeof(Inv.Panel) }, null);
        StageRefreshPanelsMethod = StagePanelType.GetMethod("RefreshPanels", AnyInstance, null, Type.EmptyTypes, null);

        ShellEngineProp = ShellType?.GetProperty("Engine", AnyInstance);
        ShellGameProp = ShellType?.GetProperty("Game", AnyInstance);
        GameStandardFontNameProp = GameType?.GetProperty("StandardFontName", AnyInstance | AnyStatic);

        MenuStackContextProp = MenuStackType?.GetProperty("Context", AnyInstance);
        MenuButtonCtor = MenuButtonType?.GetConstructors(AnyInstance).FirstOrDefault(C => C.GetParameters().Length == 3);
        MenuButtonImageProp = MenuButtonType?.GetProperty("Image", AnyInstance);
        MenuButtonHintProp = MenuButtonType?.GetProperty("Hint", AnyInstance);
        MenuButtonIsCheckedProp = MenuButtonType?.GetProperty("IsChecked", AnyInstance);
        MenuButtonIsHighlightedProp = MenuButtonType?.GetProperty("IsHighlighted", AnyInstance);
        MenuButtonVisibilityProp = MenuButtonType?.GetProperty("Visibility", AnyInstance);
        MenuButtonArrangeMethod = MenuButtonType?.GetMethod("Arrange", AnyInstance, null, new[] { typeof(bool) }, null);
        MenuButtonSingleTapEvent = MenuButtonType?.GetEvent("SingleTapEvent", AnyInstance);

        ThemePanelColourProp = ThemeType?.GetProperty("PanelColour", AnyStatic);
        ThemeStrokeColourProp = ThemeType?.GetProperty("StrokeColour", AnyStatic);
        ThemeGetImageMethod = ThemeType?.GetMethod("GetImage", AnyStatic, null, new[] { typeof(Glyph) }, null);

        TalentPanelDictField = TalentPanelType?.GetField("PropertyOptionDictionary", AnyInstance);
        TileOptionIsVisibleProp = TileOptionType?.GetProperty("IsVisible", AnyInstance);

        // 1. StagePanel.InternalRefresh：同步左下角按钮与左侧面板状态
        PatchSafe(Harmony, StagePanelType, "InternalRefresh", Type.EmptyTypes,
          postfix: nameof(StageInternalRefreshPostfix));

        // 2. StagePanel.ArrangeLeft：窗口尺寸变化时同步按钮水平偏移，始终紧贴「休息」按钮右侧
        PatchSafe(Harmony, StagePanelType, "ArrangeLeft", Type.EmptyTypes,
          postfix: nameof(StageArrangeLeftPostfix));

        // 3. StagePanel.CheckCancel：ESC 优先关闭天赋树面板
        PatchSafe(Harmony, StagePanelType, "CheckCancel", Type.EmptyTypes,
          prefix: nameof(StageCheckCancelPrefix));

        // 4. StagePanel.RouteKeystroke：支持 F2 一键开关天赋树面板
        var RouteKeyMethod = StagePanelType.GetMethod("RouteKeystroke", AnyInstance);
        if (RouteKeyMethod != null)
        {
          var KeystrokeParam = RouteKeyMethod.GetParameters().FirstOrDefault()?.ParameterType;
          if (KeystrokeParam != null)
            KeystrokeKeyProp = KeystrokeParam.GetProperty("Key", AnyInstance);

          Harmony.Patch(RouteKeyMethod, prefix: new HarmonyMethod(typeof(TalentTreeUiBridge), nameof(StageRouteKeystrokePrefix)));
          HookBootstrap.WriteDebug("Patched Pathos.StagePanel.RouteKeystroke");
        }

        // 5. TalentPanel.Refresh(Character)：在原生角色面板中隐藏内部 dota_talent_* 标记
        if (TalentPanelType != null)
        {
          PatchSafe(Harmony, TalentPanelType, "Refresh", new[] { typeof(Character) },
            postfix: nameof(TalentPanelRefreshPostfix));
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"TalentTreeUiBridge.ApplyUiPatches failed: {Ex}");
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
        HookBootstrap.WriteError($"UI hook target not found: {TargetType?.FullName}.{MethodName}");
        return;
      }

      var PrefixMethod = prefix != null ? new HarmonyMethod(typeof(TalentTreeUiBridge), prefix) : null;
      var PostfixMethod = postfix != null ? new HarmonyMethod(typeof(TalentTreeUiBridge), postfix) : null;
      Harmony.Patch(Method, prefix: PrefixMethod, postfix: PostfixMethod);
      HookBootstrap.WriteDebug($"Patched {TargetType.FullName}.{MethodName}");
    }

    private static void StageInternalRefreshPostfix(object __instance)
    {
      if (__instance == null)
        return;

      try
      {
        var Controller = GetOrCreateController(__instance);
        Controller?.Refresh();
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"StageInternalRefreshPostfix failed: {Ex}");
      }
    }

    private static void StageArrangeLeftPostfix(object __instance)
    {
      if (__instance == null)
        return;

      try
      {
        if (StageControllers.TryGetValue(__instance, out var Controller))
          Controller.UpdateLayoutMetrics();
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"StageArrangeLeftPostfix failed: {Ex}");
      }
    }

    private static bool StageCheckCancelPrefix(object __instance, ref bool __result)
    {
      if (__instance != null && StageControllers.TryGetValue(__instance, out var Controller) && Controller.IsPanelOpen)
      {
        Controller.ClosePanel();
        __result = true;
        return false;
      }

      return true;
    }

    private static bool StageRouteKeystrokePrefix(object __instance, object Keystroke)
    {
      if (__instance == null || Keystroke == null || KeystrokeKeyProp == null)
        return true;

      try
      {
        if (KeystrokeKeyProp.GetValue(Keystroke) is Inv.Key Key && Key == Inv.Key.F2)
        {
          var Controller = GetOrCreateController(__instance);
          if (Controller != null && Controller.CanOpenForCurrentCharacter())
          {
            Controller.TogglePanel();
            return false;
          }
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"StageRouteKeystrokePrefix failed: {Ex}");
      }

      return true;
    }

    private static void TalentPanelRefreshPostfix(object __instance)
    {
      if (__instance == null || TalentPanelDictField == null || TileOptionIsVisibleProp == null)
        return;

      try
      {
        if (TalentPanelDictField.GetValue(__instance) is not IDictionary Dict)
          return;

        foreach (DictionaryEntry Entry in Dict)
        {
          if (Entry.Key is Property Prop &&
              TalentRegistry.IsInternalTalentProperty(Prop) &&
              Entry.Value != null)
          {
            TileOptionIsVisibleProp.SetValue(Entry.Value, false);
          }
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"TalentPanelRefreshPostfix failed: {Ex}");
      }
    }

    private static StageTalentController GetOrCreateController(object StagePanel)
    {
      if (StageControllers.TryGetValue(StagePanel, out var Existing))
        return Existing;

      var Surface = StageSurfaceField?.GetValue(StagePanel) as Inv.Surface;
      if (Surface == null)
        return null;

      var Created = new StageTalentController(StagePanel, Surface);
      StageControllers.Add(StagePanel, Created);
      return Created;
    }

    private static Character GetActiveCharacter(object StagePanel)
    {
      var Player = StagePlayerProp?.GetValue(StagePanel) as Player;
      return Player?.Main?.Character;
    }

    private static object GetEngine(object StagePanel)
    {
      var Shell = StageShellProp?.GetValue(StagePanel);
      return Shell != null && ShellEngineProp != null ? ShellEngineProp.GetValue(Shell) : null;
    }

    private static int GetStageEdgeWidth(object StagePanel)
    {
      try
      {
        if (StageEdgeWidthMethod?.Invoke(StagePanel, null) is int W && W > 0)
          return W;
      }
      catch { }

      return 260;
    }

    private static string GetStandardFontName(object StagePanel)
    {
      try
      {
        var Shell = StageShellProp?.GetValue(StagePanel);
        var Game = Shell != null && ShellGameProp != null ? ShellGameProp.GetValue(Shell) : null;
        if (GameStandardFontNameProp != null)
        {
          var Getter = GameStandardFontNameProp.GetGetMethod(true);
          var Target = Getter != null && Getter.IsStatic ? null : Game;
          if (GameStandardFontNameProp.GetValue(Target) is string Name && !string.IsNullOrEmpty(Name))
            return Name;
        }
      }
      catch
      {
      }

      return null;
    }

    private static Inv.Colour GetThemePanelColour()
    {
      try
      {
        if (ThemePanelColourProp?.GetValue(null) is Inv.Colour C)
          return C;
      }
      catch { }

      return Inv.Colour.FromArgb(245, 30, 31, 35);
    }

    private static Inv.Colour GetThemeStrokeColour()
    {
      try
      {
        if (ThemeStrokeColourProp?.GetValue(null) is Inv.Colour C)
          return C;
      }
      catch { }

      return Inv.Colour.FromArgb(255, 62, 66, 74);
    }

    private static Inv.Image GetGlyphImage(Glyph Glyph)
    {
      if (Glyph == null || ThemeGetImageMethod == null)
        return null;

      try
      {
        return ThemeGetImageMethod.Invoke(null, new object[] { Glyph }) as Inv.Image;
      }
      catch
      {
        return null;
      }
    }

    private static bool IsNativeSidePanelVisible(object Panel)
    {
      if (Panel == null)
        return false;

      var Prop = Panel.GetType().GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      return Prop?.GetValue(Panel) is bool Visible && Visible;
    }

    private static void HideNativeSidePanel(object Panel)
    {
      if (Panel == null || !IsNativeSidePanelVisible(Panel))
        return;

      var HideMethod = Panel.GetType().GetMethod("Hide", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
      HideMethod?.Invoke(Panel, null);
    }

    /// <summary>
    /// 绑定到单个 StagePanel 的左下角原生 MenuButton（紧贴休息按钮右侧）与左侧停靠天赋树面板。
    /// </summary>
    private sealed class StageTalentController
    {
      // 原生 Pathos 角色面板配色（高对比度、清晰锐利）
      private static readonly Inv.Colour TileBgColour = Inv.Colour.FromArgb(255, 42, 43, 48);
      private static readonly Inv.Colour TileSubBgColour = Inv.Colour.FromArgb(255, 34, 35, 40);
      private static readonly Inv.Colour SectionBlueColour = Inv.Colour.FromArgb(255, 126, 158, 204);
      private static readonly Inv.Colour TextPrimaryColour = Inv.Colour.FromArgb(255, 240, 242, 246);
      private static readonly Inv.Colour TextSecondaryColour = Inv.Colour.FromArgb(255, 198, 203, 212);
      private static readonly Inv.Colour TextMutedColour = Inv.Colour.FromArgb(255, 158, 165, 178);

      private static readonly Inv.Colour LearnedBgColour = Inv.Colour.FromArgb(255, 44, 54, 74);
      private static readonly Inv.Colour LearnedBorderColour = Inv.Colour.FromArgb(255, 118, 152, 212);
      private static readonly Inv.Colour LearnedTagColour = Inv.Colour.FromArgb(255, 156, 194, 248);

      private static readonly Inv.Colour AvailableBgColour = Inv.Colour.FromArgb(255, 38, 60, 48);
      private static readonly Inv.Colour AvailableBorderColour = Inv.Colour.FromArgb(255, 112, 196, 134);
      private static readonly Inv.Colour AvailableTagColour = Inv.Colour.FromArgb(255, 146, 232, 166);

      private static readonly Inv.Colour LockedBorderColour = Inv.Colour.FromArgb(255, 58, 61, 70);

      private readonly object StagePanel;
      private readonly Inv.Surface Surface;

      private Inv.Frame ButtonHostFrame;
      private object MenuButtonObj;
      private bool MenuButtonAttached;
      private Glyph LastButtonGlyph;

      private Inv.Frame SidePanelFrame;
      private Inv.Label HeaderTitleLabel;
      private Inv.Label PointsBadgeLabel;
      private Inv.Stack TiersStack;
      private bool PanelOpen;

      public bool IsPanelOpen => PanelOpen;

      public StageTalentController(object StagePanel, Inv.Surface Surface)
      {
        this.StagePanel = StagePanel;
        this.Surface = Surface;
      }

      public bool CanOpenForCurrentCharacter()
      {
        var Char = GetActiveCharacter(StagePanel);
        return Char != null && TalentService.HasTalentTree(Char);
      }

      public void Refresh()
      {
        EnsureMenuButtonAttached();

        var Character = GetActiveCharacter(StagePanel);
        if (Character == null || !TalentRegistry.TryGetTree(Character, out var Tree))
        {
          SetMenuButtonVisible(false);
          if (PanelOpen)
            ClosePanel();
          return;
        }

        // 若玩家点开了原版左侧面板（角色/法术/日志），自动收起天赋树面板以保持同位互斥
        if (PanelOpen && AnyNativeLeftPanelVisible())
          ClosePanel();

        int Points = TalentService.GetAvailableTalentPoints(Character);
        SetMenuButtonVisible(true);
        UpdateLayoutMetrics();

        if (Tree.IconGlyph != null && Tree.IconGlyph != LastButtonGlyph)
        {
          var Img = GetGlyphImage(Tree.IconGlyph);
          if (Img != null)
          {
            MenuButtonImageProp?.SetValue(MenuButtonObj, Img);
            LastButtonGlyph = Tree.IconGlyph;
          }
        }

        MenuButtonHintProp?.SetValue(MenuButtonObj, Points > 0 ? $"天赋树 ({Points} 点可用) [F2]" : "天赋树 [F2]");
        MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, PanelOpen);
        MenuButtonIsHighlightedProp?.SetValue(MenuButtonObj, Points > 0 && !PanelOpen);

        if (PanelOpen)
          RebuildPanelContent(Character, Tree);
      }

      public void UpdateLayoutMetrics()
      {
        int EdgeW = GetStageEdgeWidth(StagePanel);

        if (ButtonHostFrame != null)
        {
          // 紧贴 LeftDock（4 个左下角按钮，总宽约 EdgeW）右侧，完全不被裁切
          ButtonHostFrame.Margin.Set(EdgeW + 4, 0, 0, 4);
        }

        if (MenuButtonObj != null)
          MenuButtonArrangeMethod?.Invoke(MenuButtonObj, new object[] { false });

        if (SidePanelFrame != null)
        {
          int PanelW = Math.Max(EdgeW, 420);
          SidePanelFrame.Size.SetWidth(PanelW);
        }
      }

      public void TogglePanel()
      {
        if (PanelOpen)
          ClosePanel();
        else
          OpenPanel();
      }

      public void OpenPanel()
      {
        var Character = GetActiveCharacter(StagePanel);
        if (Character == null || !TalentRegistry.TryGetTree(Character, out var Tree))
          return;

        // 关闭同侧原版面板，使天赋树面板与角色/法术/日志面板完全一致地切换
        HideNativeSidePanel(StageCharacterPanelProp?.GetValue(StagePanel));
        HideNativeSidePanel(StageCastPanelProp?.GetValue(StagePanel));
        HideNativeSidePanel(StageJournalPanelProp?.GetValue(StagePanel));

        if (!PanelOpen)
        {
          EnsureSidePanelBuilt();
          PanelOpen = true;
          StageAddModalityPanelMethod?.Invoke(StagePanel, new object[] { SidePanelFrame });
        }

        UpdateLayoutMetrics();
        MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, true);
        MenuButtonIsHighlightedProp?.SetValue(MenuButtonObj, false);
        RebuildPanelContent(Character, Tree);
      }

      public void ClosePanel()
      {
        if (!PanelOpen)
          return;

        PanelOpen = false;
        if (SidePanelFrame != null)
          StageRemoveModalityPanelMethod?.Invoke(StagePanel, new object[] { SidePanelFrame });

        MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, false);
      }

      private bool AnyNativeLeftPanelVisible()
      {
        return IsNativeSidePanelVisible(StageCharacterPanelProp?.GetValue(StagePanel)) ||
               IsNativeSidePanelVisible(StageCastPanelProp?.GetValue(StagePanel)) ||
               IsNativeSidePanelVisible(StageJournalPanelProp?.GetValue(StagePanel));
      }

      private void EnsureMenuButtonAttached()
      {
        if (MenuButtonAttached)
          return;

        var Shell = StageShellProp?.GetValue(StagePanel);
        var LeftMenuStack = StageLeftMenuStackField?.GetValue(StagePanel);
        var Context = LeftMenuStack != null && MenuStackContextProp != null
          ? MenuStackContextProp.GetValue(LeftMenuStack)
          : null;

        if (Shell == null || Context == null || MenuButtonCtor == null || StageAddModalityPanelMethod == null)
          return;

        MenuButtonObj = MenuButtonCtor.Invoke(new object[] { Shell, Context, null });
        if (MenuButtonObj is not Inv.Panel MenuButtonPanel)
          return;

        MenuButtonArrangeMethod?.Invoke(MenuButtonObj, new object[] { false });

        if (MenuButtonSingleTapEvent != null)
        {
          Action Handler = TogglePanel;
          MenuButtonSingleTapEvent.AddEventHandler(MenuButtonObj, Handler);
        }

        ButtonHostFrame = Surface.NewFrame();
        ButtonHostFrame.Alignment.BottomLeft();
        ButtonHostFrame.Content = MenuButtonPanel;
        ButtonHostFrame.Visibility.Collapse();

        // 直接挂在 StagePanel 的 ModalityPanel 顶层，紧贴左下角「休息」按钮右侧，绝不被 LeftDock 裁切
        StageAddModalityPanelMethod.Invoke(StagePanel, new object[] { ButtonHostFrame });
        UpdateLayoutMetrics();

        MenuButtonAttached = true;
      }

      private void SetMenuButtonVisible(bool Visible)
      {
        ButtonHostFrame?.Visibility.Set(Visible);
        if (MenuButtonObj != null && MenuButtonVisibilityProp?.GetValue(MenuButtonObj) is Inv.Visibility Vis)
          Vis.Set(Visible);
      }

      private void EnsureSidePanelBuilt()
      {
        if (SidePanelFrame != null)
          return;

        SidePanelFrame = Surface.NewFrame();
        SidePanelFrame.Alignment.BottomLeft();
        SidePanelFrame.Background.Colour = GetThemePanelColour();
        SidePanelFrame.Border.Set(1);
        SidePanelFrame.Border.Colour = GetThemeStrokeColour();
        SidePanelFrame.Padding.Set(10, 10);
        // 底部留足 112px 避开左下角圆形菜单栏，顶部留足 68px 避开菜单按钮
        SidePanelFrame.Margin.Set(4, 68, 0, 112);
        SidePanelFrame.Size.SetWidth(Math.Max(GetStageEdgeWidth(StagePanel), 420));
        SidePanelFrame.Size.SetMaximumHeight(660);

        var RootDock = Surface.NewVerticalDock();
        SidePanelFrame.Content = RootDock;

        // 顶部标题栏（与原版角色面板风格统一）
        var HeaderDock = Surface.NewHorizontalDock();
        HeaderDock.Margin.Set(2, 0, 2, 10);
        RootDock.AddHeader(HeaderDock);

        HeaderTitleLabel = CreateLabel(TextPrimaryColour, FontSize: 20);
        HeaderTitleLabel.Alignment.CenterLeft();
        HeaderDock.AddClient(HeaderTitleLabel);

        PointsBadgeLabel = CreateLabel(AvailableTagColour, FontSize: 16);
        PointsBadgeLabel.Alignment.CenterRight();
        PointsBadgeLabel.Margin.Set(8, 0, 0, 0);
        HeaderDock.AddFooter(PointsBadgeLabel);

        // 可滚动的 4 层天赋瓦片区
        var Scroll = Surface.NewVerticalScroll();
        RootDock.AddClient(Scroll);

        TiersStack = Surface.NewVerticalStack();
        Scroll.Content = TiersStack;
      }

      private void RebuildPanelContent(Character Character, HeroTalentTree Tree)
      {
        if (TiersStack == null)
          return;

        int Points = TalentService.GetAvailableTalentPoints(Character);
        HeaderTitleLabel.Text = $"{Tree.HeroDisplayName} · 天赋树";

        if (Points > 0)
        {
          PointsBadgeLabel.Text = $"可用 {Points} 点";
          PointsBadgeLabel.Font.Colour = AvailableTagColour;
          PointsBadgeLabel.Visibility.Show();
        }
        else
        {
          PointsBadgeLabel.Visibility.Collapse();
        }

        TiersStack.RemovePanels();

        // 从高阶（20级）到低阶（5级）排列
        for (int i = Tree.Tiers.Count - 1; i >= 0; i--)
        {
          var Tier = Tree.Tiers[i];
          TiersStack.AddPanel(BuildTierBlock(Character, Tier));
        }
      }

      private Inv.Panel BuildTierBlock(Character Character, TalentTier Tier)
      {
        var LeftState = TalentService.GetNodeState(Character, Tier, TalentBranch.Left);
        var RightState = TalentService.GetNodeState(Character, Tier, TalentBranch.Right);

        var BlockFrame = Surface.NewFrame();
        BlockFrame.Background.Colour = TileBgColour;
        BlockFrame.Padding.Set(8, 7);
        BlockFrame.Margin.Set(0, 0, 0, 8);

        var BlockStack = Surface.NewVerticalStack();
        BlockFrame.Content = BlockStack;

        var TierTitle = CreateLabel(SectionBlueColour, FontSize: 15);
        TierTitle.Text = $"{Tier.UnlockLevel}级天赋";
        TierTitle.Margin.Set(2, 0, 0, 6);
        BlockStack.AddPanel(TierTitle);

        var PairTable = Surface.NewTable();
        var ColLeft = PairTable.AddStarColumn(1);
        var ColGap = PairTable.AddFixedColumn(6);
        var ColRight = PairTable.AddStarColumn(1);
        var Row = PairTable.AddAutoRow();

        PairTable.GetCell(ColLeft, Row).Content = BuildOptionTile(Tier, TalentBranch.Left, LeftState);
        PairTable.GetCell(ColRight, Row).Content = BuildOptionTile(Tier, TalentBranch.Right, RightState);
        BlockStack.AddPanel(PairTable);

        return BlockFrame;
      }

      private Inv.Panel BuildOptionTile(TalentTier Tier, TalentBranch Branch, TalentNodeState State)
      {
        var Node = Tier.GetNode(Branch);

        Inv.Colour Bg, Border, TitleCol, DescCol, TagCol;
        string TagText = null;

        switch (State)
        {
          case TalentNodeState.Learned:
            Bg = LearnedBgColour;
            Border = LearnedBorderColour;
            TitleCol = TextPrimaryColour;
            DescCol = TextSecondaryColour;
            TagCol = LearnedTagColour;
            TagText = "✓ 已学习";
            break;

          case TalentNodeState.Available:
            Bg = AvailableBgColour;
            Border = AvailableBorderColour;
            TitleCol = TextPrimaryColour;
            DescCol = TextPrimaryColour;
            TagCol = AvailableTagColour;
            TagText = "点击学习";
            break;

          default:
            Bg = TileSubBgColour;
            Border = LockedBorderColour;
            TitleCol = TextPrimaryColour;
            DescCol = TextMutedColour;
            TagCol = TextMutedColour;
            break;
        }

        var InnerStack = Surface.NewVerticalStack();

        var TitleLabel = CreateLabel(TitleCol, FontSize: 16);
        TitleLabel.Text = Node.Title;
        TitleLabel.LineWrapping = true;
        TitleLabel.Margin.Set(0, 0, 0, 4);
        InnerStack.AddPanel(TitleLabel);

        var DescLabel = CreateLabel(DescCol, FontSize: 14);
        DescLabel.Text = Node.Description;
        DescLabel.LineWrapping = true;
        InnerStack.AddPanel(DescLabel);

        if (!string.IsNullOrEmpty(TagText))
        {
          var TagLabel = CreateLabel(TagCol, FontSize: 14);
          TagLabel.Text = TagText;
          TagLabel.Margin.Set(0, 5, 0, 0);
          InnerStack.AddPanel(TagLabel);
        }

        if (State == TalentNodeState.Available)
        {
          var Btn = Surface.NewFlatButton();
          Btn.Alignment.Stretch();
          Btn.Background.Colour = Bg;
          Btn.Border.Set(1);
          Btn.Border.Colour = Border;
          Btn.Padding.Set(8, 7);
          Btn.Content = InnerStack;
          Btn.SingleTapEvent += () => HandleSelectTalent(Tier, Branch);
          return Btn;
        }
        else
        {
          var Frame = Surface.NewFrame();
          Frame.Alignment.Stretch();
          Frame.Background.Colour = Bg;
          Frame.Border.Set(1);
          Frame.Border.Colour = Border;
          Frame.Padding.Set(8, 7);
          Frame.Content = InnerStack;
          return Frame;
        }
      }

      private Inv.Label CreateLabel(Inv.Colour Colour, int FontSize)
      {
        var Label = Surface.NewLabel();
        var StdFont = GetStandardFontName(StagePanel);
        if (!string.IsNullOrEmpty(StdFont))
          Label.Font.Name = StdFont;

        // 不使用合成粗体（WPF/Inv 对中文字体加粗会发虚重影），与 Pathos 原版 Regular 字体保持一致
        Label.Font.Regular();
        Label.Font.Size = FontSize;
        Label.Font.Colour = Colour;
        return Label;
      }

      private void HandleSelectTalent(TalentTier Tier, TalentBranch Branch)
      {
        var Character = GetActiveCharacter(StagePanel);
        var Engine = GetEngine(StagePanel);
        if (Character == null || Engine == null)
          return;

        if (TalentService.TryLearnTalent(Engine, Character, Tier, Branch))
        {
          if (TalentRegistry.TryGetTree(Character, out var Tree))
            RebuildPanelContent(Character, Tree);

          Refresh();
          StageRefreshPanelsMethod?.Invoke(StagePanel, null);
        }
      }
    }
  }
}
