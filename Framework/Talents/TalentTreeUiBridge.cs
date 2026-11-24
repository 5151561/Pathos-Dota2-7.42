using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Skills;
using Pathos.DotaHeroes.Framework.UI;

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
          Controller.UpdateLayoutMetrics();
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
      private static readonly Inv.Colour StarGoldColour = Inv.Colour.FromArgb(255, 246, 206, 98);
      private static readonly Inv.Colour TabActiveBgColour = Inv.Colour.FromArgb(255, 52, 66, 92);
      private static readonly Inv.Colour TabInactiveBgColour = Inv.Colour.FromArgb(255, 34, 36, 42);

      private enum GrowthTab
      {
        Skills,
        Talents
      }

      private readonly object StagePanel;
      private readonly Inv.Surface Surface;

      private Inv.Frame ButtonHostFrame;
      private object MenuButtonObj;
      private bool MenuButtonAttached;
      private Glyph LastButtonGlyph;

      private Inv.Frame SidePanelFrame;
      private Inv.Label HeaderTitleLabel;
      private Inv.Label PointsBadgeLabel;
      private Inv.Frame TabBarHostFrame;
      private Inv.Stack ContentStack;
      private GrowthTab ActiveTab = GrowthTab.Skills;
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

        // 若玩家点开了原版左侧面板（角色/法术/日志），自动收起成长面板以保持同位互斥
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
          HintText = $"技能与天赋 (技能 {SkillPoints} / 天赋 {TalentPoints}) [F2]";
        else if (SkillPoints > 0)
          HintText = $"技能加点 ({SkillPoints} 点可用) [F2]";
        else if (TalentPoints > 0)
          HintText = $"天赋树 ({TalentPoints} 点可用) [F2]";
        else
          HintText = HasBuild && HasTree ? "技能与天赋 [F2]" : HasBuild ? "技能加点 [F2]" : "天赋树 [F2]";

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

        // 智能选择默认页签：优先切至存在未分配点数的页签
        if (HasBuild && !HasTree)
        {
          ActiveTab = GrowthTab.Skills;
        }
        else if (!HasBuild && HasTree)
        {
          ActiveTab = GrowthTab.Talents;
        }
        else
        {
          int SkillPts = HeroSkillService.GetAvailableSkillPoints(Character);
          int TalentPts = TalentService.GetAvailableTalentPoints(Character);
          if (SkillPts > 0 && TalentPts == 0)
            ActiveTab = GrowthTab.Skills;
          else if (TalentPts > 0 && SkillPts == 0)
            ActiveTab = GrowthTab.Talents;
        }

        // 关闭同侧原版面板，使成长面板与角色/法术/日志面板完全一致地切换
        HideNativeSidePanel(StageCharacterPanelProp?.GetValue(StagePanel));
        HideNativeSidePanel(StageCastPanelProp?.GetValue(StagePanel));
        HideNativeSidePanel(StageJournalPanelProp?.GetValue(StagePanel));

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
        // 底部留足 112px 避开左下角圆形菜单栏，顶部留足 68px 避开菜单按钮
        SidePanelFrame.Margin.Set(4, 68, 0, 112);
        SidePanelFrame.Size.SetWidth(Math.Max(GetStageEdgeWidth(StagePanel), 420));
        SidePanelFrame.Size.SetMaximumHeight(660);

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

        // 顶部标题栏（与原版角色面板风格统一）
        var HeaderDock = Surface.NewHorizontalDock();
        HeaderDock.Margin.Set(2, 0, 2, 8);
        RootDock.AddHeader(HeaderDock);

        HeaderTitleLabel = CreateLabel(TextPrimaryColour, FontSize: 20);
        HeaderTitleLabel.Alignment.CenterLeft();
        HeaderDock.AddClient(HeaderTitleLabel);

        PointsBadgeLabel = CreateLabel(AvailableTagColour, FontSize: 15);
        PointsBadgeLabel.Alignment.CenterRight();
        PointsBadgeLabel.Margin.Set(8, 0, 0, 0);
        HeaderDock.AddFooter(PointsBadgeLabel);

        // 双 Tab 切换栏（技能加点 / 天赋树）
        TabBarHostFrame = Surface.NewFrame();
        TabBarHostFrame.Margin.Set(0, 0, 0, 8);
        RootDock.AddHeader(TabBarHostFrame);

        // 可滚动内容区
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

        if (ActiveTab == GrowthTab.Skills && !HasBuild)
          ActiveTab = GrowthTab.Talents;
        else if (ActiveTab == GrowthTab.Talents && !HasTree)
          ActiveTab = GrowthTab.Skills;

        int SkillPoints = HasBuild ? HeroSkillService.GetAvailableSkillPoints(Character) : 0;
        int TalentPoints = HasTree ? TalentService.GetAvailableTalentPoints(Character) : 0;

        string HeroName = SkillBuild?.HeroDisplayName ?? Tree?.HeroDisplayName ?? "英雄";
        string SectionName = ActiveTab == GrowthTab.Skills ? "技能加点" : "天赋树";
        HeaderTitleLabel.Text = $"{HeroName} · {SectionName}";

        if (SkillPoints > 0 && TalentPoints > 0)
        {
          PointsBadgeLabel.Text = $"技能 {SkillPoints} · 天赋 {TalentPoints}";
          PointsBadgeLabel.Font.Colour = AvailableTagColour;
          PointsBadgeLabel.Visibility.Show();
        }
        else if (SkillPoints > 0)
        {
          PointsBadgeLabel.Text = $"技能点 {SkillPoints}";
          PointsBadgeLabel.Font.Colour = AvailableTagColour;
          PointsBadgeLabel.Visibility.Show();
        }
        else if (TalentPoints > 0)
        {
          PointsBadgeLabel.Text = $"天赋点 {TalentPoints}";
          PointsBadgeLabel.Font.Colour = AvailableTagColour;
          PointsBadgeLabel.Visibility.Show();
        }
        else
        {
          PointsBadgeLabel.Visibility.Collapse();
        }

        // 重建双 Tab 栏
        if (HasBuild && HasTree)
        {
          TabBarHostFrame.Visibility.Show();
          TabBarHostFrame.Content = BuildTabBar(SkillPoints, TalentPoints);
        }
        else
        {
          TabBarHostFrame.Content = null;
          TabBarHostFrame.Visibility.Collapse();
        }

        ContentStack.RemovePanels();

        if (ActiveTab == GrowthTab.Skills && HasBuild)
        {
          foreach (var Slot in SkillBuild.Slots)
            ContentStack.AddPanel(BuildSkillSlotCard(Character, Slot));
        }
        else if (HasTree)
        {
          // 从高阶（25级）到低阶（10级）排列
          for (int i = Tree.Tiers.Count - 1; i >= 0; i--)
          {
            var Tier = Tree.Tiers[i];
            ContentStack.AddPanel(BuildTierBlock(Character, Tier));
          }
        }
      }

      private Inv.Panel BuildTabBar(int SkillPoints, int TalentPoints)
      {
        var Table = Surface.NewTable();
        var ColLeft = Table.AddStarColumn(1);
        var ColGap = Table.AddFixedColumn(6);
        var ColRight = Table.AddStarColumn(1);
        var Row = Table.AddAutoRow();

        string SkillTitle = SkillPoints > 0 ? $"技能加点 ({SkillPoints})" : "技能加点";
        string TalentTitle = TalentPoints > 0 ? $"天赋树 ({TalentPoints})" : "天赋树";

        Table.GetCell(ColLeft, Row).Content = BuildTabButton(SkillTitle, GrowthTab.Skills, SkillPoints > 0);
        Table.GetCell(ColRight, Row).Content = BuildTabButton(TalentTitle, GrowthTab.Talents, TalentPoints > 0);
        return Table;
      }

      private Inv.Panel BuildTabButton(string Title, GrowthTab Tab, bool HasPoints)
      {
        bool IsActive = ActiveTab == Tab;

        var Btn = Surface.NewFlatButton();
        Btn.Alignment.Stretch();
        Btn.Background.Colour = IsActive ? TabActiveBgColour : TabInactiveBgColour;
        Btn.Border.Set(1);
        Btn.Border.Colour = IsActive
          ? LearnedBorderColour
          : HasPoints ? AvailableBorderColour : LockedBorderColour;
        Btn.Padding.Set(8, 7);

        var Label = CreateLabel(
          IsActive ? TextPrimaryColour : (HasPoints ? AvailableTagColour : TextSecondaryColour),
          FontSize: 15);
        Label.Text = Title;
        Label.Justify.Center();
        Btn.Content = Label;

        Btn.SingleTapEvent += () =>
        {
          if (ActiveTab != Tab)
          {
            ActiveTab = Tab;
            var Character = GetActiveCharacter(StagePanel);
            if (Character != null)
            {
              HeroSkillRegistry.TryGetBuild(Character, out var SkillBuild);
              TalentRegistry.TryGetTree(Character, out var Tree);
              RebuildPanelContent(Character, SkillBuild, Tree);
            }
          }
        };

        return Btn;
      }

      private Inv.Panel BuildSkillSlotCard(Character Character, DotaSkillSlot Slot)
      {
        int CurrentLv = HeroSkillService.GetSkillLevel(Character, Slot);
        var State = HeroSkillService.GetUpgradeState(Character, Slot, out int RequiredHeroLv);

        var CardFrame = Surface.NewFrame();
        CardFrame.Background.Colour = TileBgColour;
        CardFrame.Border.Set(1);
        CardFrame.Border.Colour = State == DotaSkillUpgradeState.Available
          ? AvailableBorderColour
          : CurrentLv >= Slot.MaxLevel ? LearnedBorderColour : LockedBorderColour;
        CardFrame.Padding.Set(8, 8);
        CardFrame.Margin.Set(0, 0, 0, 8);

        var Layout = Surface.NewTable();
        var ColIcon = Layout.AddFixedColumn(46);
        var ColInfo = Layout.AddStarColumn(1);
        var ColAction = Layout.AddFixedColumn(84);
        var Row = Layout.AddAutoRow();

        // 1. 左侧：技能图标 + 快捷键徽章
        var IconStack = Surface.NewVerticalStack();
        IconStack.Alignment.Center();
        IconStack.Margin.Set(0, 0, 6, 0);

        var GlyphImg = Slot.IconGlyph != null ? GetGlyphImage(Slot.IconGlyph) : null;
        if (GlyphImg != null)
        {
          var Graphic = Surface.NewGraphic();
          Graphic.Image = GlyphImg;
          Graphic.Size.Set(32, 32);
          Graphic.Alignment.Center();
          IconStack.AddPanel(Graphic);
        }

        var KeyLabel = CreateLabel(SectionBlueColour, FontSize: 13);
        KeyLabel.Text = $"[{Slot.Hotkey}]";
        KeyLabel.Justify.Center();
        KeyLabel.Margin.Set(0, 2, 0, 0);
        IconStack.AddPanel(KeyLabel);
        Layout.GetCell(ColIcon, Row).Content = IconStack;

        // 2. 中间：名称、等级/星级、说明
        var InfoStack = Surface.NewVerticalStack();
        InfoStack.Alignment.CenterLeft();
        InfoStack.Margin.Set(2, 0, 8, 0);

        var TitleDock = Surface.NewHorizontalDock();
        var NameLabel = CreateLabel(TextPrimaryColour, FontSize: 16);
        NameLabel.Text = Slot.Title;
        NameLabel.Alignment.CenterLeft();
        TitleDock.AddClient(NameLabel);

        string BadgeText;
        Inv.Colour BadgeColour;
        if (Slot.Kind == DotaSkillSlotKind.Active)
        {
          string Stars = HeroSkillService.GetActiveStarsBadge(Character, Slot);
          BadgeText = CurrentLv > 0
            ? $"Lv {CurrentLv}/{Slot.MaxLevel} · {Stars}"
            : $"未学习 (0/{Slot.MaxLevel})";
          BadgeColour = CurrentLv > 1 ? StarGoldColour : (CurrentLv == 1 ? LearnedTagColour : TextMutedColour);
        }
        else if (Slot.Kind == DotaSkillSlotKind.Passive)
        {
          BadgeText = CurrentLv > 0
            ? $"Lv {CurrentLv}/{Slot.MaxLevel} · 被动"
            : $"未学习 (0/{Slot.MaxLevel})";
          BadgeColour = CurrentLv > 0 ? LearnedTagColour : TextMutedColour;
        }
        else
        {
          BadgeText = $"Lv {CurrentLv}/{Slot.MaxLevel}";
          BadgeColour = CurrentLv > 0 ? StarGoldColour : TextMutedColour;
        }

        var LevelLabel = CreateLabel(BadgeColour, FontSize: 13);
        LevelLabel.Text = BadgeText;
        LevelLabel.Alignment.CenterRight();
        LevelLabel.Margin.Set(6, 0, 0, 0);
        TitleDock.AddFooter(LevelLabel);
        InfoStack.AddPanel(TitleDock);

        string DescText = Slot.DescribeLevel?.Invoke(CurrentLv);
        if (!string.IsNullOrWhiteSpace(DescText))
        {
          var DescLabel = CreateLabel(TextSecondaryColour, FontSize: 13);
          DescLabel.Text = DescText;
          DescLabel.LineWrapping = true;
          DescLabel.Margin.Set(0, 4, 0, 0);
          InfoStack.AddPanel(DescLabel);
        }

        Layout.GetCell(ColInfo, Row).Content = InfoStack;

        // 3. 右侧：升级按钮或状态标签
        Layout.GetCell(ColAction, Row).Content = BuildSkillActionControl(Slot, CurrentLv, State, RequiredHeroLv);
        CardFrame.Content = Layout;
        return CardFrame;
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
            ? "学习"
            : (Slot.Kind == DotaSkillSlotKind.Active ? "+ 升星" : "+ 升级");

          var Btn = Surface.NewFlatButton();
          Btn.Alignment.Center();
          Btn.Size.SetWidth(78);
          Btn.Background.Colour = AvailableBgColour;
          Btn.Border.Set(1);
          Btn.Border.Colour = AvailableBorderColour;
          Btn.Padding.Set(6, 9);

          var Label = CreateLabel(AvailableTagColour, FontSize: 14);
          Label.Text = BtnText;
          Label.Justify.Center();
          Btn.Content = Label;
          Btn.SingleTapEvent += () => HandleUpgradeSkill(Slot);
          return Btn;
        }

        string StatusText;
        Inv.Colour Bg;
        Inv.Colour Border;
        Inv.Colour TextCol;

        switch (State)
        {
          case DotaSkillUpgradeState.Maxed:
            StatusText = "已满级";
            Bg = LearnedBgColour;
            Border = LearnedBorderColour;
            TextCol = LearnedTagColour;
            break;

          case DotaSkillUpgradeState.LevelLocked:
            StatusText = $"需 Lv {RequiredHeroLv}";
            Bg = TileSubBgColour;
            Border = LockedBorderColour;
            TextCol = TextMutedColour;
            break;

          default:
            StatusText = "无点数";
            Bg = TileSubBgColour;
            Border = LockedBorderColour;
            TextCol = TextMutedColour;
            break;
        }

        var Frame = Surface.NewFrame();
        Frame.Alignment.Center();
        Frame.Size.SetWidth(78);
        Frame.Background.Colour = Bg;
        Frame.Border.Set(1);
        Frame.Border.Colour = Border;
        Frame.Padding.Set(6, 9);

        var StatusLabel = CreateLabel(TextCol, FontSize: 13);
        StatusLabel.Text = StatusText;
        StatusLabel.Justify.Center();
        Frame.Content = StatusLabel;
        return Frame;
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
        TierTitle.Text = $"{Tier.UnlockLevel}级天赋（{Tier.DualUnlockLevel}级双修）";
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

        Inv.Colour Bg, Border, TitleCol, TagCol;
        string TagText = null;

        switch (State)
        {
          case TalentNodeState.Learned:
            Bg = LearnedBgColour;
            Border = LearnedBorderColour;
            TitleCol = TextPrimaryColour;
            TagCol = LearnedTagColour;
            TagText = "✓ 已学习";
            break;

          case TalentNodeState.Available:
            Bg = AvailableBgColour;
            Border = AvailableBorderColour;
            TitleCol = TextPrimaryColour;
            TagCol = AvailableTagColour;
            TagText = "点击学习";
            break;

          default:
            Bg = TileSubBgColour;
            Border = LockedBorderColour;
            TitleCol = TextSecondaryColour;
            TagCol = TextMutedColour;
            break;
        }

        var InnerStack = Surface.NewVerticalStack();
        InnerStack.Alignment.Center();

        var TitleLabel = CreateLabel(TitleCol, FontSize: 15);
        TitleLabel.Text = Node.Title;
        TitleLabel.Justify.Center();
        TitleLabel.LineWrapping = true;
        InnerStack.AddPanel(TitleLabel);

        if (!string.IsNullOrEmpty(TagText))
        {
          var TagLabel = CreateLabel(TagCol, FontSize: 13);
          TagLabel.Text = TagText;
          TagLabel.Justify.Center();
          TagLabel.Margin.Set(0, 4, 0, 0);
          InnerStack.AddPanel(TagLabel);
        }

        if (State == TalentNodeState.Available)
        {
          var Btn = Surface.NewFlatButton();
          Btn.Alignment.Stretch();
          Btn.Background.Colour = Bg;
          Btn.Border.Set(1);
          Btn.Border.Colour = Border;
          Btn.Padding.Set(8, 10);
          if (!string.IsNullOrWhiteSpace(Node.Description))
            Btn.Hint = Node.Description;
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
          Frame.Padding.Set(8, 10);
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
          Refresh();
          StageRefreshPanelsMethod?.Invoke(StagePanel, null);
        }
      }
    }
  }
}
