using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Skills;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework.Talents
{
  /// <summary>
  /// DOTA2 独立天赋树面板 UI 桥接层（原生 Pathos 风格）：
  /// - 入口按钮为原生 Pathos.MenuButton，放置在左下角「休息」按钮右侧（通过 StagePanel.EdgeWidth 动态对齐，避免被 LeftDock 裁切）。
  /// - 天赋树面板为左下角停靠的原生 Pathos 风格侧边面板（与角色/法术左侧面板同位互斥），复用 Pathos.Theme 配色与标准字体。
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

    private static FieldInfo TalentPanelDictField;
    private static PropertyInfo TileOptionIsVisibleProp;
    private static PropertyInfo KeystrokeKeyProp;

    private static PropertyInfo ShellManifestProp;
    private static PropertyInfo HelpLibraryPropertiesProp;
    private static FieldInfo HelpLibraryRecordListField;
    private static FieldInfo HelpSectionRecordListField;
    private static FieldInfo HelpSectionCatalogueListField;
    private static FieldInfo HelpCatalogueRecordListField;
    private static PropertyInfo HelpRecordNameProp;

    public static void ApplyUiPatches(Harmony Harmony)
    {
      try
      {
        var PathosClientAsm = PathosClientReflection.FindClientAssembly();
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
        var HelpLibraryType = PathosClientAsm.GetType("Pathos.HelpLibrary");
        var HelpSectionType = PathosClientAsm.GetType("Pathos.HelpSection");
        var HelpCatalogueType = PathosClientAsm.GetType("Pathos.HelpCatalogue");
        var HelpRecordType = PathosClientAsm.GetType("Pathos.HelpRecord");

        if (StagePanelType == null)
          return;

        const BindingFlags AnyInstance = PathosClientReflection.AnyInstance;
        const BindingFlags AnyStatic = PathosClientReflection.AnyStatic;

        StageSurfaceField = StagePanelType.GetField("Surface", AnyInstance);
        StageLeftMenuStackField = StagePanelType.GetField("LeftMenuStack", AnyInstance);
        StagePlayerProp = StagePanelType.GetProperty("Player", AnyInstance);
        StageShellProp = StagePanelType.GetProperty("Shell", AnyInstance);
        StageCharacterPanelProp = StagePanelType.GetProperty("CharacterPanel", AnyInstance);
        StageCastPanelProp = StagePanelType.GetProperty("CastPanel", AnyInstance);
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

        TalentPanelDictField = TalentPanelType?.GetField("PropertyOptionDictionary", AnyInstance);
        TileOptionIsVisibleProp = TileOptionType?.GetProperty("IsVisible", AnyInstance);

        ShellManifestProp = ShellType?.GetProperty("Manifest", AnyInstance);
        HelpLibraryPropertiesProp = HelpLibraryType?.GetProperty("Properties", AnyInstance);
        HelpLibraryRecordListField = HelpLibraryType?.GetField("RecordList", AnyInstance);
        HelpSectionRecordListField = HelpSectionType?.GetField("RecordList", AnyInstance);
        HelpSectionCatalogueListField = HelpSectionType?.GetField("CatalogueList", AnyInstance);
        HelpCatalogueRecordListField = HelpCatalogueType?.GetField("RecordList", AnyInstance);
        HelpRecordNameProp = HelpRecordType?.GetProperty("Name", AnyInstance);

        // 1. StagePanel.InternalRefresh：同步左下角按钮与左侧面板状态
        PatchSafe(Harmony, StagePanelType, "InternalRefresh", Type.EmptyTypes,
          postfix: nameof(StageInternalRefreshPostfix));

        // 2. StagePanel.ArrangeLeft：窗口尺寸变化时同步按钮水平偏移，始终紧贴「休息」按钮右侧
        PatchSafe(Harmony, StagePanelType, "ArrangeLeft", Type.EmptyTypes,
          postfix: nameof(StageArrangeLeftPostfix));

        // 3. StagePanel.RouteKeystroke：支持 F2 一键开关与 ESC 关闭天赋树面板
        // 注意：不要拦截 StagePanel.CheckCancel，因为 ScenePanel.Prepare() 在视角/格位变化时会自动调用 CheckCancel()，会导致面板误关闭
        var RouteKeyMethod = StagePanelType.GetMethod("RouteKeystroke", AnyInstance);
        if (RouteKeyMethod != null)
        {
          var KeystrokeParam = RouteKeyMethod.GetParameters().FirstOrDefault()?.ParameterType;
          if (KeystrokeParam != null)
            KeystrokeKeyProp = KeystrokeParam.GetProperty("Key", AnyInstance);

          Harmony.Patch(RouteKeyMethod, prefix: new HarmonyMethod(typeof(TalentTreeUiBridge), nameof(StageRouteKeystrokePrefix)));
          HookBootstrap.WriteDebug("Patched Pathos.StagePanel.RouteKeystroke");
        }

        // 5. TalentPanel.Refresh(Character)：在原生角色面板中隐藏内部 dota_talent_* 标记与主动技能短期状态
        if (TalentPanelType != null)
        {
          PatchSafe(Harmony, TalentPanelType, "Refresh", new[] { typeof(Character) },
            postfix: nameof(TalentPanelRefreshPostfix));

          if (TileOptionType != null)
          {
            PatchSafe(Harmony, TalentPanelType, "ToggleTalent", new[] { TileOptionType, typeof(Property) },
              prefix: nameof(TalentPanelTogglePrefix));
          }
        }

        // 6. HelpLibrary 构造后置：从百科「天赋/特性」列表中剔除内部天赋树标记与主动技能短期状态（技能仍在「法术」栏目中展示）
        if (HelpLibraryType != null && ShellType != null)
        {
          var HelpCtor = AccessTools.Constructor(HelpLibraryType, new[] { ShellType });
          if (HelpCtor != null)
          {
            Harmony.Patch(HelpCtor, postfix: new HarmonyMethod(typeof(TalentTreeUiBridge), nameof(HelpLibraryCtorPostfix)));
            HookBootstrap.WriteDebug("Patched Pathos.HelpLibrary..ctor");
          }
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
      PathosClientReflection.PatchSafe(
        Harmony,
        TargetType,
        MethodName,
        ParamTypes,
        typeof(TalentTreeUiBridge),
        Prefix: prefix,
        Postfix: postfix,
        Tag: "TalentUI");
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
        {
          Controller.OnNativeLeftPanelsChanged();
          Controller.UpdateLayoutMetrics();
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"StageArrangeLeftPostfix failed: {Ex}");
      }
    }

    private static bool StageRouteKeystrokePrefix(object __instance, object Keystroke)
    {
      if (__instance == null || Keystroke == null || KeystrokeKeyProp == null)
        return true;

      try
      {
        if (KeystrokeKeyProp.GetValue(Keystroke) is Inv.Key Key)
        {
          if (Key == Inv.Key.Escape &&
              StageControllers.TryGetValue(__instance, out var OpenController) &&
              OpenController.IsPanelOpen)
          {
            OpenController.ClosePanel();
            return false;
          }

          if (Key == Inv.Key.F2)
          {
            var Controller = GetOrCreateController(__instance);
            if (Controller != null && Controller.CanOpenForCurrentCharacter())
            {
              Controller.TogglePanel();
              return false;
            }
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
              TalentRegistry.IsHiddenFromTalentPanel(Prop) &&
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

    private static bool TalentPanelTogglePrefix(Property Property)
    {
      if (Property != null && TalentRegistry.IsHiddenFromTalentPanel(Property))
        return false;

      return true;
    }

    private static void HelpLibraryCtorPostfix(object __instance, object Shell)
    {
      if (__instance == null || Shell == null)
        return;

      try
      {
        var Manifest = ShellManifestProp?.GetValue(Shell) as Manifest;
        var PropertiesSection = HelpLibraryPropertiesProp?.GetValue(__instance);
        if (Manifest?.Properties?.List == null || PropertiesSection == null)
          return;

        var HiddenNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var Prop in Manifest.Properties.List)
        {
          if (TalentRegistry.IsHiddenFromTalentPanel(Prop))
          {
            if (!string.IsNullOrEmpty(Prop.Name))
              HiddenNames.Add(Prop.Name);
            var Localized = PathosEngineBridge.GetLocalizedName(Prop);
            if (!string.IsNullOrEmpty(Localized))
              HiddenNames.Add(Localized);
          }
        }

        if (HiddenNames.Count == 0)
          return;

        var RemovedRecords = new System.Collections.Generic.HashSet<object>();

        void PruneRecordList(IList RecordList)
        {
          if (RecordList == null)
            return;

          for (int i = RecordList.Count - 1; i >= 0; i--)
          {
            var Rec = RecordList[i];
            var RecName = Rec != null ? HelpRecordNameProp?.GetValue(Rec) as string : null;
            if (!string.IsNullOrEmpty(RecName) && HiddenNames.Contains(RecName))
            {
              RemovedRecords.Add(Rec);
              RecordList.RemoveAt(i);
            }
          }
        }

        PruneRecordList(HelpSectionRecordListField?.GetValue(PropertiesSection) as IList);

        if (HelpSectionCatalogueListField?.GetValue(PropertiesSection) is IEnumerable Catalogues)
        {
          foreach (var Cat in Catalogues)
            PruneRecordList(HelpCatalogueRecordListField?.GetValue(Cat) as IList);
        }

        if (RemovedRecords.Count > 0 && HelpLibraryRecordListField?.GetValue(__instance) is IList GlobalRecords)
        {
          for (int i = GlobalRecords.Count - 1; i >= 0; i--)
          {
            if (RemovedRecords.Contains(GlobalRecords[i]))
              GlobalRecords.RemoveAt(i);
          }
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"HelpLibraryCtorPostfix failed: {Ex}");
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
      => PathosClientReflection.GetThemeGlyphImage(Glyph);

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
      // 配色：底色/描边沿用 Pathos 原生冷灰主题，仅「可操作 / 已学」使用 DOTA2 金橙色点缀
      private static readonly Inv.Colour TileBgColour = Inv.Colour.FromArgb(255, 38, 40, 45);
      private static readonly Inv.Colour TileBorderColour = Inv.Colour.FromArgb(255, 52, 55, 63);
      private static readonly Inv.Colour TileDimBgColour = Inv.Colour.FromArgb(255, 30, 31, 35);
      private static readonly Inv.Colour IconBgColour = Inv.Colour.FromArgb(255, 42, 44, 50);
      private static readonly Inv.Colour BadgeBgColour = Inv.Colour.FromArgb(255, 23, 24, 27);
      private static readonly Inv.Colour UnlearnedShadeColour = Inv.Colour.FromArgb(160, 12, 13, 15);

      private static readonly Inv.Colour TextPrimaryColour = Inv.Colour.FromArgb(255, 240, 242, 246);
      private static readonly Inv.Colour TextSecondaryColour = Inv.Colour.FromArgb(255, 190, 195, 204);
      private static readonly Inv.Colour TextMutedColour = Inv.Colour.FromArgb(255, 128, 134, 146);
      private static readonly Inv.Colour TextDisabledColour = Inv.Colour.FromArgb(255, 78, 82, 92);

      private static readonly Inv.Colour GoldBgColour = Inv.Colour.FromArgb(255, 46, 42, 32);
      private static readonly Inv.Colour GoldBorderColour = Inv.Colour.FromArgb(255, 201, 165, 90);
      private static readonly Inv.Colour GoldTextColour = Inv.Colour.FromArgb(255, 240, 220, 168);
      private static readonly Inv.Colour GoldBrightColour = Inv.Colour.FromArgb(255, 243, 205, 110);
      private static readonly Inv.Colour DualLevelColour = Inv.Colour.FromArgb(255, 140, 118, 70);

      private static readonly Inv.Colour LearnedBgColour = Inv.Colour.FromArgb(255, 96, 60, 26);
      private static readonly Inv.Colour LearnedBorderColour = Inv.Colour.FromArgb(255, 224, 160, 80);
      private static readonly Inv.Colour LearnedTextColour = Inv.Colour.FromArgb(255, 255, 243, 220);

      private static readonly Inv.Colour PlusBgColour = Inv.Colour.FromArgb(255, 201, 165, 90);
      private static readonly Inv.Colour PlusTextColour = Inv.Colour.FromArgb(255, 42, 31, 8);
      private static readonly Inv.Colour PipFilledColour = Inv.Colour.FromArgb(255, 232, 184, 74);
      private static readonly Inv.Colour PipEmptyColour = Inv.Colour.FromArgb(255, 58, 61, 69);
      private static readonly Inv.Colour UpgradeGreenColour = Inv.Colour.FromArgb(255, 146, 222, 120);

      private const int AbilityIconSize = 54;
      private const int AbilityColumnWidth = 60;

      private readonly object StagePanel;
      private readonly Inv.Surface Surface;

      private Inv.Frame ButtonHostFrame;
      private object MenuButtonObj;
      private bool MenuButtonAttached;
      private Glyph LastButtonGlyph;

      private Inv.Frame SidePanelFrame;
      private Inv.Graphic HeaderPortrait;
      private Inv.Label HeaderTitleLabel;
      private Inv.Label HeaderLevelLabel;
      private Inv.Frame PointsBadgeFrame;
      private Inv.Label PointsBadgeLabel;
      private Inv.Stack ContentStack;
      private string SelectedSlotId;
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
        return Char != null && (HeroSkillService.HasSkillBuild(Char) || TalentService.HasTalentTree(Char));
      }

      public void Refresh()
      {
        EnsureMenuButtonAttached();

        var Character = GetActiveCharacter(StagePanel);
        HeroSkillBuild SkillBuild = null;
        HeroTalentTree Tree = null;
        bool HasBuild = Character != null && HeroSkillRegistry.TryGetBuild(Character, out SkillBuild);
        bool HasTree = Character != null && TalentRegistry.TryGetTree(Character, out Tree);

        if (!HasBuild && !HasTree)
        {
          SetMenuButtonVisible(false);
          if (PanelOpen)
            ClosePanel();
          return;
        }

        // 若玩家点开了原版左侧面板（角色/法术），自动收起成长面板以保持同位互斥
        // 注意：JournalPanel 是顶部自动弹出的消息日志横幅，不属于左侧面板，升级/点天赋写日志会使其自动变为可见，绝不可纳入互斥判定
        if (PanelOpen && AnyNativeLeftPanelVisible())
          ClosePanel();

        int SkillPoints = HasBuild ? HeroSkillService.GetAvailableSkillPoints(Character) : 0;
        int TalentPoints = HasTree ? TalentService.GetAvailableTalentPoints(Character) : 0;
        int TotalPoints = SkillPoints + TalentPoints;

        SetMenuButtonVisible(true);
        UpdateLayoutMetrics();

        Glyph ButtonGlyph = SkillBuild?.IconGlyph ?? Tree?.IconGlyph;
        if (ButtonGlyph != null && ButtonGlyph != LastButtonGlyph)
        {
          var Img = GetGlyphImage(ButtonGlyph);
          if (Img != null)
          {
            MenuButtonImageProp?.SetValue(MenuButtonObj, Img);
            LastButtonGlyph = ButtonGlyph;
          }
        }

        string HintText;
        if (SkillPoints > 0 && TalentPoints > 0)
          HintText = DotaLocalization.Format("ui.panel.hint.both", SkillPoints, TalentPoints);
        else if (SkillPoints > 0)
          HintText = DotaLocalization.Format("ui.panel.hint.skills", SkillPoints);
        else if (TalentPoints > 0)
          HintText = DotaLocalization.Format("ui.panel.hint.talents", TalentPoints);
        else
          HintText = HasBuild && HasTree
            ? DotaLocalization.T("ui.panel.hint.none_both")
            : HasBuild
              ? DotaLocalization.T("ui.panel.hint.none_skills")
              : DotaLocalization.T("ui.panel.hint.none_talents");

        MenuButtonHintProp?.SetValue(MenuButtonObj, HintText);
        MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, PanelOpen);
        MenuButtonIsHighlightedProp?.SetValue(MenuButtonObj, TotalPoints > 0 && !PanelOpen);

        if (PanelOpen)
          RebuildPanelContent(Character, SkillBuild, Tree);
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
          int PanelW = Math.Max(EdgeW, 440);
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
        HeroSkillBuild SkillBuild = null;
        HeroTalentTree Tree = null;
        bool HasBuild = Character != null && HeroSkillRegistry.TryGetBuild(Character, out SkillBuild);
        bool HasTree = Character != null && TalentRegistry.TryGetTree(Character, out Tree);
        if (!HasBuild && !HasTree)
          return;

        // 每次打开重新挑选默认详情：优先显示可升级的技能
        if (!PanelOpen)
          SelectedSlotId = null;

        // 关闭同侧原版面板（仅左侧角色/法术面板；不隐藏顶部 JournalPanel 日志横幅）
        HideNativeSidePanel(StageCharacterPanelProp?.GetValue(StagePanel));
        HideNativeSidePanel(StageCastPanelProp?.GetValue(StagePanel));

        if (!PanelOpen)
        {
          EnsureSidePanelBuilt();
          PanelOpen = true;
          StageAddModalityPanelMethod?.Invoke(StagePanel, new object[] { SidePanelFrame });
        }

        // 防止此前弹窗（ModalityPanel.ShowQuery）对已收起的 SidePanelFrame 遗留 Window.PreventInput 锁死状态
        Surface.Window.AllowInput(SidePanelFrame);

        UpdateLayoutMetrics();
        MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, true);
        MenuButtonIsHighlightedProp?.SetValue(MenuButtonObj, false);
        RebuildPanelContent(Character, SkillBuild, Tree);
      }

      public void ClosePanel()
      {
        if (!PanelOpen)
          return;

        PanelOpen = false;
        if (SidePanelFrame != null)
        {
          Surface.Window.AllowInput(SidePanelFrame);
          StageRemoveModalityPanelMethod?.Invoke(StagePanel, new object[] { SidePanelFrame });
        }

        MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, false);
      }

      public void OnNativeLeftPanelsChanged()
      {
        if (PanelOpen && AnyNativeLeftPanelVisible())
          ClosePanel();
      }

      private bool AnyNativeLeftPanelVisible()
      {
        return IsNativeSidePanelVisible(StageCharacterPanelProp?.GetValue(StagePanel)) ||
               IsNativeSidePanelVisible(StageCastPanelProp?.GetValue(StagePanel));
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
        // 底部留足 112px 避开左下角圆形菜单栏，顶部留足 68px 避开菜单按钮
        SidePanelFrame.Margin.Set(4, 68, 0, 112);
        SidePanelFrame.Size.SetWidth(Math.Max(GetStageEdgeWidth(StagePanel), 440));
        SidePanelFrame.Size.SetMaximumHeight(700);

        var RootOverlay = Surface.NewOverlay();
        SidePanelFrame.Content = RootOverlay;

        // 底层透明按钮拦截面板内非交互区域的鼠标点击，避免穿透到下层地图 ScenePanel
        var ClickBlocker = Surface.NewFlatButton();
        ClickBlocker.Alignment.Stretch();
        ClickBlocker.Background.Colour = Inv.Colour.Transparent;
        ClickBlocker.SingleTapEvent += () => { };
        RootOverlay.AddPanel(ClickBlocker);

        var RootDock = Surface.NewVerticalDock();
        RootDock.Padding.Set(10, 10);
        RootOverlay.AddPanel(RootDock);

        // 顶部：英雄头像 + 名称/等级 + 可用点数徽章
        var HeaderDock = Surface.NewHorizontalDock();
        HeaderDock.Margin.Set(0, 0, 0, 8);
        RootDock.AddHeader(HeaderDock);

        var PortraitFrame = Surface.NewFrame();
        PortraitFrame.Size.Set(36, 36);
        PortraitFrame.Background.Colour = IconBgColour;
        PortraitFrame.Border.Set(1);
        PortraitFrame.Border.Colour = GetThemeStrokeColour();
        PortraitFrame.Margin.Set(0, 0, 8, 0);
        PortraitFrame.Alignment.CenterLeft();
        HeaderPortrait = Surface.NewGraphic();
        HeaderPortrait.Size.Set(32, 32);
        HeaderPortrait.Alignment.Center();
        PortraitFrame.Content = HeaderPortrait;
        HeaderDock.AddHeader(PortraitFrame);

        var NameStack = Surface.NewVerticalStack();
        NameStack.Alignment.CenterLeft();
        HeaderTitleLabel = CreateLabel(TextPrimaryColour, FontSize: 18);
        NameStack.AddPanel(HeaderTitleLabel);
        HeaderLevelLabel = CreateLabel(TextMutedColour, FontSize: 13);
        NameStack.AddPanel(HeaderLevelLabel);
        HeaderDock.AddClient(NameStack);

        PointsBadgeFrame = Surface.NewFrame();
        PointsBadgeFrame.Alignment.CenterRight();
        PointsBadgeFrame.Border.Set(1);
        PointsBadgeFrame.Border.Colour = GoldBorderColour;
        PointsBadgeFrame.Background.Colour = GoldBgColour;
        PointsBadgeFrame.Padding.Set(8, 3);
        PointsBadgeLabel = CreateLabel(GoldTextColour, FontSize: 13);
        PointsBadgeFrame.Content = PointsBadgeLabel;
        HeaderDock.AddFooter(PointsBadgeFrame);

        // 可滚动内容区（天赋树 → 技能栏 → 技能详情）
        var Scroll = Surface.NewVerticalScroll();
        RootDock.AddClient(Scroll);

        ContentStack = Surface.NewVerticalStack();
        Scroll.Content = ContentStack;
      }

      private void RebuildPanelContent(Character Character, HeroSkillBuild SkillBuild, HeroTalentTree Tree)
      {
        if (ContentStack == null)
          return;

        bool HasBuild = SkillBuild != null;
        bool HasTree = Tree != null;

        int SkillPoints = HasBuild ? HeroSkillService.GetAvailableSkillPoints(Character) : 0;
        int TalentPoints = HasTree ? TalentService.GetAvailableTalentPoints(Character) : 0;

        HeaderTitleLabel.Text = SkillBuild?.HeroDisplayName ?? Tree?.HeroDisplayName ?? DotaLocalization.T("ui.panel.hero_fallback");
        HeaderLevelLabel.Text = DotaLocalization.Format("ui.panel.hero_level", Character.Level);

        Glyph PortraitGlyph = SkillBuild?.IconGlyph ?? Tree?.IconGlyph;
        HeaderPortrait.Image = PortraitGlyph != null ? GetGlyphImage(PortraitGlyph) : null;

        if (SkillPoints > 0 || TalentPoints > 0)
        {
          PointsBadgeLabel.Text = DotaLocalization.Format("ui.panel.badge.both", SkillPoints, TalentPoints);
          PointsBadgeFrame.Visibility.Show();
        }
        else
        {
          PointsBadgeFrame.Visibility.Collapse();
        }

        ContentStack.RemovePanels();

        if (HasTree)
        {
          ContentStack.AddPanel(BuildSectionCaption(DotaLocalization.T("ui.panel.section.talents")));

          // DOTA2 天赋树：高阶（25 级）在上、低阶（10 级）在下
          for (int i = Tree.Tiers.Count - 1; i >= 0; i--)
            ContentStack.AddPanel(BuildTalentRow(Character, Tree.Tiers[i], TalentPoints > 0));
        }

        if (HasBuild)
        {
          if (HasTree)
            ContentStack.AddPanel(BuildDivider());

          ContentStack.AddPanel(BuildSectionCaption(DotaLocalization.T("ui.panel.section.skills")));

          var Selected = ResolveSelectedSlot(Character, SkillBuild);
          ContentStack.AddPanel(BuildAbilityBar(Character, SkillBuild, Selected));
          if (Selected != null)
            ContentStack.AddPanel(BuildSkillDetail(Character, Selected));
        }
      }

      private DotaSkillSlot ResolveSelectedSlot(Character Character, HeroSkillBuild SkillBuild)
      {
        if (SkillBuild.Slots.Count == 0)
          return null;

        var Current = SkillBuild.Slots.FirstOrDefault(S => S.Id == SelectedSlotId);
        if (Current != null)
          return Current;

        // 首次打开：优先选中可升级的技能
        var Upgradable = SkillBuild.Slots.FirstOrDefault(S =>
          HeroSkillService.GetUpgradeState(Character, S, out _) == DotaSkillUpgradeState.Available);
        var Pick = Upgradable ?? SkillBuild.Slots[0];
        SelectedSlotId = Pick.Id;
        return Pick;
      }

      private void RebuildForActiveCharacter()
      {
        var Character = GetActiveCharacter(StagePanel);
        if (Character == null)
          return;

        HeroSkillRegistry.TryGetBuild(Character, out var SkillBuild);
        TalentRegistry.TryGetTree(Character, out var Tree);
        RebuildPanelContent(Character, SkillBuild, Tree);
      }

      private Inv.Panel BuildSectionCaption(string Text)
      {
        var Label = CreateLabel(TextMutedColour, FontSize: 13);
        Label.Text = Text;
        Label.Justify.Center();
        Label.Margin.Set(0, 0, 0, 6);
        return Label;
      }

      private Inv.Panel BuildDivider()
      {
        var Line = Surface.NewFrame();
        Line.Size.SetHeight(1);
        Line.Background.Colour = GetThemeStrokeColour();
        Line.Margin.Set(0, 8, 0, 10);
        return Line;
      }

      // ───────────────────────── 天赋树 ─────────────────────────

      private Inv.Panel BuildTalentRow(Character Character, TalentTier Tier, bool HasTalentPoints)
      {
        var LeftState = TalentService.GetNodeState(Character, Tier, TalentBranch.Left);
        var RightState = TalentService.GetNodeState(Character, Tier, TalentBranch.Right);
        bool TierReached = Character.Level >= Tier.UnlockLevel;

        var Table = Surface.NewTable();
        Table.Margin.Set(0, 0, 0, 5);
        var ColLeft = Table.AddStarColumn(1);
        var ColBadge = Table.AddFixedColumn(52);
        var ColRight = Table.AddStarColumn(1);
        var Row = Table.AddAutoRow();

        Table.GetCell(ColLeft, Row).Content = BuildTalentTile(Tier, TalentBranch.Left, LeftState, HasTalentPoints);
        Table.GetCell(ColBadge, Row).Content = BuildTalentLevelBadge(Tier, TierReached);
        Table.GetCell(ColRight, Row).Content = BuildTalentTile(Tier, TalentBranch.Right, RightState, HasTalentPoints);
        return Table;
      }

      private Inv.Panel BuildTalentLevelBadge(TalentTier Tier, bool Reached)
      {
        var Badge = Surface.NewFrame();
        Badge.Size.Set(40, 40);
        Badge.Alignment.Center();
        Badge.Background.Colour = BadgeBgColour;
        Badge.Border.Set(2);
        Badge.Border.Colour = Reached ? GoldBorderColour : GetThemeStrokeColour();

        var Stack = Surface.NewVerticalStack();
        Stack.Alignment.Center();

        var LevelLabel = CreateLabel(Reached ? GoldBrightColour : TextMutedColour, FontSize: 15);
        LevelLabel.Text = Tier.UnlockLevel.ToString();
        LevelLabel.Justify.Center();
        Stack.AddPanel(LevelLabel);

        // 双修补点等级（27~30）以小字标注在等级数字下方
        if (Tier.DualUnlockLevel > Tier.UnlockLevel)
        {
          var DualLabel = CreateLabel(DualLevelColour, FontSize: 10);
          DualLabel.Text = Tier.DualUnlockLevel.ToString();
          DualLabel.Justify.Center();
          Stack.AddPanel(DualLabel);
        }

        Badge.Content = Stack;
        return Badge;
      }

      private Inv.Panel BuildTalentTile(TalentTier Tier, TalentBranch Branch, TalentNodeState State, bool HasTalentPoints)
      {
        var Node = Tier.GetNode(Branch);
        bool Clickable = State == TalentNodeState.Available && HasTalentPoints;

        Inv.Colour Bg, Border, TextCol;
        if (State == TalentNodeState.Learned)
        {
          Bg = LearnedBgColour;
          Border = LearnedBorderColour;
          TextCol = LearnedTextColour;
        }
        else if (Clickable)
        {
          Bg = GoldBgColour;
          Border = GoldBorderColour;
          TextCol = GoldTextColour;
        }
        else if (State == TalentNodeState.OppositeBlocked)
        {
          Bg = TileDimBgColour;
          Border = TileDimBgColour;
          TextCol = TextDisabledColour;
        }
        else
        {
          Bg = TileBgColour;
          Border = TileBorderColour;
          TextCol = TextMutedColour;
        }

        var Label = CreateLabel(TextCol, FontSize: 14);
        Label.Text = State == TalentNodeState.Learned ? "✓ " + Node.Title : Node.Title;
        Label.Justify.Center();
        Label.LineWrapping = true;
        Label.Alignment.Center();

        // 全部用按钮承载，保证悬停时都能看到天赋完整说明；仅可学习状态响应点击
        var Btn = Surface.NewFlatButton();
        Btn.Alignment.Stretch();
        Btn.Size.SetMinimumHeight(42);
        Btn.Background.Colour = Bg;
        Btn.Border.Set(1);
        Btn.Border.Colour = Border;
        Btn.Padding.Set(6, 4);
        if (!string.IsNullOrWhiteSpace(Node.Description))
          Btn.Hint = Node.Description;
        Btn.Content = Label;
        if (Clickable)
          Btn.SingleTapEvent += () => HandleSelectTalent(Tier, Branch);
        else
          Btn.SingleTapEvent += () => { };
        return Btn;
      }

      // ───────────────────────── 技能栏 ─────────────────────────

      private Inv.Panel BuildAbilityBar(Character Character, HeroSkillBuild SkillBuild, DotaSkillSlot Selected)
      {
        var Bar = Surface.NewHorizontalStack();
        Bar.Alignment.TopCenter();
        Bar.Margin.Set(0, 0, 0, 8);

        foreach (var Slot in SkillBuild.Slots)
          Bar.AddPanel(BuildAbilityColumn(Character, Slot, Slot == Selected));

        return Bar;
      }

      private Inv.Panel BuildAbilityColumn(Character Character, DotaSkillSlot Slot, bool IsSelected)
      {
        int CurrentLv = HeroSkillService.GetSkillLevel(Character, Slot);
        var State = HeroSkillService.GetUpgradeState(Character, Slot, out _);

        var Column = Surface.NewVerticalStack();
        Column.Size.SetWidth(AbilityColumnWidth);
        Column.Margin.Set(3, 0, 3, 0);

        // 1. DOTA2 风格金色「+」：可升级时出现在图标上方，点击直接加点
        if (State == DotaSkillUpgradeState.Available)
        {
          var PlusBtn = Surface.NewFlatButton();
          PlusBtn.Size.Set(AbilityIconSize, 18);
          PlusBtn.Alignment.Center();
          PlusBtn.Background.Colour = PlusBgColour;
          PlusBtn.Border.Set(1);
          PlusBtn.Border.Colour = GoldBrightColour;
          PlusBtn.Hint = DotaLocalization.Format("ui.skill.plus_hint", Slot.Title);
          var PlusLabel = CreateLabel(PlusTextColour, FontSize: 14);
          PlusLabel.Text = "+";
          PlusLabel.Justify.Center();
          PlusLabel.Alignment.Center();
          PlusBtn.Content = PlusLabel;
          PlusBtn.SingleTapEvent += () =>
          {
            SelectedSlotId = Slot.Id;
            HandleUpgradeSkill(Slot);
          };
          Column.AddPanel(PlusBtn);
        }
        else
        {
          var Spacer = Surface.NewFrame();
          Spacer.Size.Set(AbilityIconSize, 18);
          Column.AddPanel(Spacer);
        }

        // 2. 技能图标（点击查看详情）+ 左上角快捷键 + 未学习暗化遮罩
        var IconOverlay = Surface.NewOverlay();

        var GlyphImg = Slot.IconGlyph != null ? GetGlyphImage(Slot.IconGlyph) : null;
        if (GlyphImg != null)
        {
          var Graphic = Surface.NewGraphic();
          Graphic.Image = GlyphImg;
          Graphic.Size.Set(AbilityIconSize - 8, AbilityIconSize - 8);
          Graphic.Alignment.Center();
          IconOverlay.AddPanel(Graphic);
        }

        if (CurrentLv <= 0)
        {
          var Shade = Surface.NewFrame();
          Shade.Alignment.Stretch();
          Shade.Background.Colour = UnlearnedShadeColour;
          IconOverlay.AddPanel(Shade);
        }

        var HotkeyLabel = CreateLabel(CurrentLv > 0 ? TextPrimaryColour : TextMutedColour, FontSize: 11);
        HotkeyLabel.Text = Slot.Hotkey;
        HotkeyLabel.Alignment.TopLeft();
        HotkeyLabel.Margin.Set(3, 1, 0, 0);
        IconOverlay.AddPanel(HotkeyLabel);

        var IconBtn = Surface.NewFlatButton();
        IconBtn.Size.Set(AbilityIconSize, AbilityIconSize);
        IconBtn.Alignment.Center();
        IconBtn.Background.Colour = IconBgColour;
        IconBtn.Border.Set(2);
        IconBtn.Border.Colour = IsSelected
          ? GoldBrightColour
          : State == DotaSkillUpgradeState.Available ? GoldBorderColour : TileBorderColour;
        IconBtn.Hint = Slot.Title;
        IconBtn.Content = IconOverlay;
        IconBtn.SingleTapEvent += () =>
        {
          if (SelectedSlotId != Slot.Id)
          {
            SelectedSlotId = Slot.Id;
            RebuildForActiveCharacter();
          }
        };
        Column.AddPanel(IconBtn);

        // 3. 等级格（金色 = 已学等级）
        Column.AddPanel(BuildLevelPips(CurrentLv, Slot.MaxLevel));
        return Column;
      }

      private Inv.Panel BuildLevelPips(int CurrentLv, int MaxLevel)
      {
        const int Gap = 2;
        int PipW = Math.Clamp((AbilityIconSize - (MaxLevel - 1) * Gap) / Math.Max(1, MaxLevel), 4, 11);

        var Pips = Surface.NewHorizontalStack();
        Pips.Alignment.TopCenter();
        Pips.Margin.Set(0, 4, 0, 0);
        for (int i = 0; i < MaxLevel; i++)
        {
          var Pip = Surface.NewFrame();
          Pip.Size.Set(PipW, 4);
          Pip.Margin.Set(i == 0 ? 0 : Gap, 0, 0, 0);
          Pip.Background.Colour = i < CurrentLv ? PipFilledColour : PipEmptyColour;
          Pips.AddPanel(Pip);
        }
        return Pips;
      }

      // ───────────────────────── 技能详情 + 升级预览 ─────────────────────────

      private Inv.Panel BuildSkillDetail(Character Character, DotaSkillSlot Slot)
      {
        int CurrentLv = HeroSkillService.GetSkillLevel(Character, Slot);
        var State = HeroSkillService.GetUpgradeState(Character, Slot, out int RequiredHeroLv);
        bool IsMaxed = CurrentLv >= Slot.MaxLevel;

        var Frame = Surface.NewFrame();
        Frame.Background.Colour = TileBgColour;
        Frame.Border.Set(1);
        Frame.Border.Colour = TileBorderColour;
        Frame.Padding.Set(10, 8);

        var Stack = Surface.NewVerticalStack();
        Frame.Content = Stack;

        // 标题行：名称 + 等级/星级 | 升级按钮或状态
        var TitleDock = Surface.NewHorizontalDock();
        var TitleStack = Surface.NewVerticalStack();
        TitleStack.Alignment.CenterLeft();

        var NameLabel = CreateLabel(TextPrimaryColour, FontSize: 16);
        NameLabel.Text = Slot.Title;
        NameLabel.LineWrapping = true;
        TitleStack.AddPanel(NameLabel);

        var LevelLabel = CreateLabel(CurrentLv > 0 ? GoldBrightColour : TextMutedColour, FontSize: 13);
        LevelLabel.Text = DescribeSlotLevel(Character, Slot, CurrentLv);
        TitleStack.AddPanel(LevelLabel);
        TitleDock.AddClient(TitleStack);

        var ActionHost = Surface.NewFrame();
        ActionHost.Alignment.CenterRight();
        ActionHost.Content = BuildSkillActionControl(Slot, CurrentLv, State, RequiredHeroLv);
        TitleDock.AddFooter(ActionHost);
        Stack.AddPanel(TitleDock);

        // 描述
        string DescText = Slot.DescribeLevel?.Invoke(CurrentLv);
        if (!string.IsNullOrWhiteSpace(DescText))
        {
          var DescLabel = CreateLabel(TextSecondaryColour, FontSize: 13);
          DescLabel.Text = DescText;
          DescLabel.LineWrapping = true;
          DescLabel.Margin.Set(0, 6, 0, 0);
          Stack.AddPanel(DescLabel);
        }

        // 升级效果预览：当前值 → 下一级值
        var Preview = BuildUpgradePreview(Slot, CurrentLv, IsMaxed);
        if (Preview != null)
          Stack.AddPanel(Preview);

        return Frame;
      }

      private static string DescribeSlotLevel(Character Character, DotaSkillSlot Slot, int CurrentLv)
      {
        if (CurrentLv <= 0)
          return DotaLocalization.Format("ui.skill.unlearned", Slot.MaxLevel);

        return Slot.Kind switch
        {
          DotaSkillSlotKind.Active => DotaLocalization.Format(
            "ui.skill.level_stars", CurrentLv, Slot.MaxLevel, HeroSkillService.GetActiveStarsBadge(Character, Slot)),
          DotaSkillSlotKind.Passive => DotaLocalization.Format("ui.skill.level_passive", CurrentLv, Slot.MaxLevel),
          _ => DotaLocalization.Format("ui.skill.level_plain", CurrentLv, Slot.MaxLevel)
        };
      }

      private Inv.Panel BuildUpgradePreview(DotaSkillSlot Slot, int CurrentLv, bool IsMaxed)
      {
        if (Slot.Stats == null || Slot.Stats.Count == 0)
          return null;

        int NextLv = IsMaxed ? CurrentLv : CurrentLv + 1;

        var Rows = new System.Collections.Generic.List<(string Label, string Cur, string Next)>();
        foreach (var Stat in Slot.Stats)
        {
          string Cur = CurrentLv > 0 ? Stat.Value(CurrentLv) : null;
          string Next = IsMaxed ? null : Stat.Value(NextLv);
          if (Cur == null && Next == null)
            continue;
          Rows.Add((Stat.Label(), Cur, Next));
        }

        if (Rows.Count == 0)
          return null;

        var Stack = Surface.NewVerticalStack();
        Stack.Margin.Set(0, 8, 0, 0);

        var Caption = CreateLabel(TextMutedColour, FontSize: 12);
        Caption.Text = IsMaxed
          ? DotaLocalization.Format("ui.skill.preview.maxed", CurrentLv)
          : DotaLocalization.Format("ui.skill.preview.caption", CurrentLv, NextLv);
        Caption.Margin.Set(0, 0, 0, 4);
        Stack.AddPanel(Caption);

        var Table = Surface.NewTable();
        var ColLabel = Table.AddStarColumn(1);
        var ColValue = Table.AddAutoColumn();

        foreach (var (Label, Cur, Next) in Rows)
        {
          var Row = Table.AddAutoRow();

          var NameLabel = CreateLabel(TextSecondaryColour, FontSize: 13);
          NameLabel.Text = Label;
          NameLabel.Alignment.CenterLeft();
          NameLabel.Margin.Set(0, 2, 8, 2);
          Table.GetCell(ColLabel, Row).Content = NameLabel;

          Table.GetCell(ColValue, Row).Content = BuildPreviewValue(Cur, Next, IsMaxed);
        }

        Stack.AddPanel(Table);
        return Stack;
      }

      private Inv.Panel BuildPreviewValue(string Cur, string Next, bool IsMaxed)
      {
        var Line = Surface.NewHorizontalStack();
        Line.Alignment.CenterRight();

        var CurLabel = CreateLabel(Cur != null ? TextPrimaryColour : TextMutedColour, FontSize: 13);
        CurLabel.Text = Cur ?? "—";
        Line.AddPanel(CurLabel);

        if (!IsMaxed)
        {
          bool Changed = !string.Equals(Cur, Next, StringComparison.Ordinal);

          var Arrow = CreateLabel(Changed ? GoldBrightColour : TextMutedColour, FontSize: 13);
          Arrow.Text = "  →  ";
          Line.AddPanel(Arrow);

          var NextLabel = CreateLabel(Changed ? UpgradeGreenColour : TextMutedColour, FontSize: 13);
          NextLabel.Text = Next ?? "—";
          Line.AddPanel(NextLabel);
        }

        return Line;
      }

      private Inv.Panel BuildSkillActionControl(
        DotaSkillSlot Slot,
        int CurrentLv,
        DotaSkillUpgradeState State,
        int RequiredHeroLv)
      {
        if (State == DotaSkillUpgradeState.Available)
        {
          string BtnText = CurrentLv == 0 && Slot.Kind != DotaSkillSlotKind.AttributeBonus
            ? DotaLocalization.T("ui.skill.btn.learn")
            : (Slot.Kind == DotaSkillSlotKind.Active
              ? DotaLocalization.T("ui.skill.btn.star_up")
              : DotaLocalization.T("ui.skill.btn.upgrade"));

          var Btn = Surface.NewFlatButton();
          Btn.Background.Colour = GoldBgColour;
          Btn.Border.Set(1);
          Btn.Border.Colour = GoldBorderColour;
          Btn.Padding.Set(12, 6);

          var Label = CreateLabel(GoldTextColour, FontSize: 14);
          Label.Text = BtnText;
          Label.Justify.Center();
          Btn.Content = Label;
          Btn.SingleTapEvent += () => HandleUpgradeSkill(Slot);
          return Btn;
        }

        string StatusText = State switch
        {
          DotaSkillUpgradeState.Maxed => DotaLocalization.T("ui.skill.status.maxed"),
          DotaSkillUpgradeState.LevelLocked => DotaLocalization.Format("ui.skill.status.req_lv", RequiredHeroLv),
          _ => DotaLocalization.T("ui.skill.status.no_points")
        };

        var StatusLabel = CreateLabel(State == DotaSkillUpgradeState.Maxed ? GoldBrightColour : TextMutedColour, FontSize: 13);
        StatusLabel.Text = StatusText;
        StatusLabel.Margin.Set(8, 0, 0, 0);
        return StatusLabel;
      }

      private void HandleUpgradeSkill(DotaSkillSlot Slot)
      {
        var Character = GetActiveCharacter(StagePanel);
        var Engine = GetEngine(StagePanel);
        if (Character == null || Engine == null || Slot == null)
          return;

        if (HeroSkillService.TryUpgradeSkill(Engine, Character, Slot))
        {
          Refresh();
          StageRefreshPanelsMethod?.Invoke(StagePanel, null);
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
          Refresh();
          StageRefreshPanelsMethod?.Invoke(StagePanel, null);
        }
      }
    }
  }
}
