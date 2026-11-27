using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Aghanim;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Skills;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework.Talents
{
  /// <summary>
  /// DOTA2 独立天赋树面板 UI 桥接层（原生 Pathos 风格）：
  /// - 入口按钮为原生 Pathos.MenuButton，放置在左下角「休息」按钮右侧（通过 StagePanel.EdgeWidth 动态对齐，避免被 LeftDock 裁切）。
  /// - 天赋树面板为左下角停靠的侧边面板（与角色/法术左侧面板同位互斥），统一 DOTA2 HUD 配色 + Pathos 标准字体；
  ///   技能详情与升级预览全部放在悬停卡片中（DOTA2 风格 Inv.Tooltip），面板本体只保留天赋树与技能栏，高度固定不跳动。
  /// - 支持 F2 快捷开关、ESC 关闭；原生 TalentPanel.Refresh 与百科中自动隐藏主动技能短期状态 Property（DotaPropertyVisibility）。
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

        TalentPanelDictField = TalentPanelType?.GetField("PropertyOptionDictionary", AnyInstance);
        TileOptionIsVisibleProp = TileOptionType?.GetProperty("IsVisible", AnyInstance);

        ShellManifestProp = ShellType?.GetProperty("Manifest", AnyInstance);
        HelpLibraryPropertiesProp = HelpLibraryType?.GetProperty("Properties", AnyInstance);
        HelpLibraryRecordListField = HelpLibraryType?.GetField("RecordList", AnyInstance);
        HelpSectionRecordListField = HelpSectionType?.GetField("RecordList", AnyInstance);
        HelpSectionCatalogueListField = HelpSectionType?.GetField("CatalogueList", AnyInstance);
        HelpCatalogueRecordListField = HelpCatalogueType?.GetField("RecordList", AnyInstance);
        HelpRecordNameProp = HelpRecordType?.GetProperty("Name", AnyInstance);

        // 1. StagePanel.RefreshPanels：同步左下角按钮与左侧面板状态
        //    注意不能挂 InternalRefresh：引擎只在 Game.Refresh()（升级等少数事件）里调它，
        //    进游戏/每回合的刷新走 Compose(RequiresRefresh) → RefreshPanels，挂错会导致开局不显示按钮。
        PatchSafe(Harmony, StagePanelType, "RefreshPanels", Type.EmptyTypes,
          postfix: nameof(StageRefreshPanelsPostfix));

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

        // 5. TalentPanel.Refresh(Character)：在原生角色面板中隐藏主动技能短期状态
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

    private static void StageRefreshPanelsPostfix(object __instance)
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
        HookBootstrap.WriteError($"StageRefreshPanelsPostfix failed: {Ex}");
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
      // 配色：统一采用 DOTA2 HUD 风格（近黑蓝灰底 + 钢灰描边 + 金色强调），不再混用 Pathos 原生冷灰
      private static readonly Inv.Colour PanelBgColour = Inv.Colour.FromArgb(248, 15, 18, 22);
      private static readonly Inv.Colour PanelBorderColour = Inv.Colour.FromArgb(255, 54, 60, 68);
      private static readonly Inv.Colour PanelAccentColour = Inv.Colour.FromArgb(255, 150, 118, 58);
      private static readonly Inv.Colour SectionLineColour = Inv.Colour.FromArgb(255, 44, 50, 58);

      private static readonly Inv.Colour TileBgColour = Inv.Colour.FromArgb(255, 26, 30, 36);
      private static readonly Inv.Colour TileHoverBgColour = Inv.Colour.FromArgb(255, 36, 41, 49);
      private static readonly Inv.Colour TileBorderColour = Inv.Colour.FromArgb(255, 46, 52, 60);
      private static readonly Inv.Colour TileHoverBorderColour = Inv.Colour.FromArgb(255, 120, 128, 140);
      private static readonly Inv.Colour TileDimBgColour = Inv.Colour.FromArgb(255, 19, 22, 26);
      private static readonly Inv.Colour HeaderStripColour = Inv.Colour.FromArgb(255, 30, 35, 42);
      private static readonly Inv.Colour IconBgColour = Inv.Colour.FromArgb(255, 8, 9, 11);
      private static readonly Inv.Colour IconBorderColour = Inv.Colour.FromArgb(255, 0, 0, 0);
      private static readonly Inv.Colour BadgeBgColour = Inv.Colour.FromArgb(255, 12, 14, 17);
      private static readonly Inv.Colour UnlearnedShadeColour = Inv.Colour.FromArgb(170, 8, 9, 11);

      private static readonly Inv.Colour TextPrimaryColour = Inv.Colour.FromArgb(255, 238, 240, 243);
      private static readonly Inv.Colour TextSecondaryColour = Inv.Colour.FromArgb(255, 176, 184, 194);
      private static readonly Inv.Colour TextMutedColour = Inv.Colour.FromArgb(255, 112, 120, 132);
      private static readonly Inv.Colour TextDisabledColour = Inv.Colour.FromArgb(255, 70, 76, 86);

      private static readonly Inv.Colour GoldBgColour = Inv.Colour.FromArgb(255, 44, 37, 22);
      private static readonly Inv.Colour GoldHoverBgColour = Inv.Colour.FromArgb(255, 64, 53, 28);
      private static readonly Inv.Colour GoldBorderColour = Inv.Colour.FromArgb(255, 190, 150, 72);
      private static readonly Inv.Colour GoldTextColour = Inv.Colour.FromArgb(255, 240, 216, 158);
      private static readonly Inv.Colour GoldBrightColour = Inv.Colour.FromArgb(255, 255, 210, 100);
      private static readonly Inv.Colour DualLevelColour = Inv.Colour.FromArgb(255, 140, 118, 70);

      private static readonly Inv.Colour LearnedBgColour = Inv.Colour.FromArgb(255, 104, 70, 26);
      private static readonly Inv.Colour LearnedBorderColour = Inv.Colour.FromArgb(255, 232, 178, 82);
      private static readonly Inv.Colour LearnedTextColour = Inv.Colour.FromArgb(255, 255, 240, 205);

      private static readonly Inv.Colour PlusBgColour = Inv.Colour.FromArgb(255, 196, 150, 56);
      private static readonly Inv.Colour PlusHoverBgColour = Inv.Colour.FromArgb(255, 236, 190, 86);
      private static readonly Inv.Colour PlusTextColour = Inv.Colour.FromArgb(255, 36, 26, 6);
      private static readonly Inv.Colour PipFilledColour = Inv.Colour.FromArgb(255, 236, 186, 70);
      private static readonly Inv.Colour PipEmptyColour = Inv.Colour.FromArgb(255, 40, 45, 52);
      private static readonly Inv.Colour UpgradeGreenColour = Inv.Colour.FromArgb(255, 146, 222, 120);
      private static readonly Inv.Colour WarnColour = Inv.Colour.FromArgb(255, 214, 120, 96);

      private static readonly Inv.Colour TooltipBgColour = Inv.Colour.FromArgb(252, 20, 23, 28);
      private static readonly Inv.Colour TooltipBorderColour = Inv.Colour.FromArgb(255, 70, 78, 90);
      private static readonly Inv.Colour AghanimActiveBgColour = Inv.Colour.FromArgb(255, 22, 42, 72);
      private static readonly Inv.Colour AghanimActiveHoverBgColour = Inv.Colour.FromArgb(255, 30, 56, 94);
      private static readonly Inv.Colour AghanimActiveBorderColour = Inv.Colour.FromArgb(255, 96, 178, 255);
      private static readonly Inv.Colour AghanimTextColour = Inv.Colour.FromArgb(255, 165, 218, 255);

      private const int AbilityIconSize = 54;
      private const int AbilityColumnWidth = 64;
      private const int TooltipWidth = 320;

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
        if (MenuButtonObj == null)
          return;

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
        EnsureMenuButtonAttached();
        var Character = GetActiveCharacter(StagePanel);
        HeroSkillBuild SkillBuild = null;
        HeroTalentTree Tree = null;
        bool HasBuild = Character != null && HeroSkillRegistry.TryGetBuild(Character, out SkillBuild);
        bool HasTree = Character != null && TalentRegistry.TryGetTree(Character, out Tree);
        if (!HasBuild && !HasTree)
          return;

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
        if (MenuButtonObj != null) MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, true);
        if (MenuButtonObj != null) MenuButtonIsHighlightedProp?.SetValue(MenuButtonObj, false);
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

        if (MenuButtonObj != null) MenuButtonIsCheckedProp?.SetValue(MenuButtonObj, false);
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
        SidePanelFrame.Background.Colour = PanelBgColour;
        SidePanelFrame.Border.Set(1);
        SidePanelFrame.Border.Colour = PanelBorderColour;
        SidePanelFrame.Corner.Set(3);
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

        // 顶部：英雄头像 + 名称/等级 + 可用点数徽章，下方一条金色细线分隔（DOTA2 HUD 标题样式）
        var HeaderHost = Surface.NewVerticalStack();
        HeaderHost.Margin.Set(0, 0, 0, 10);
        RootDock.AddHeader(HeaderHost);

        var HeaderDock = Surface.NewHorizontalDock();
        HeaderDock.Margin.Set(0, 0, 0, 8);
        HeaderHost.AddPanel(HeaderDock);

        var HeaderRule = Surface.NewFrame();
        HeaderRule.Size.SetHeight(1);
        HeaderRule.Background.Colour = PanelAccentColour;
        HeaderHost.AddPanel(HeaderRule);

        var PortraitFrame = Surface.NewFrame();
        PortraitFrame.Size.Set(40, 40);
        PortraitFrame.Background.Colour = IconBgColour;
        PortraitFrame.Border.Set(1);
        PortraitFrame.Border.Colour = GoldBorderColour;
        PortraitFrame.Margin.Set(0, 0, 10, 0);
        PortraitFrame.Alignment.CenterLeft();
        HeaderPortrait = Surface.NewGraphic();
        HeaderPortrait.Size.Set(36, 36);
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
        PointsBadgeFrame.Padding.Set(10, 3);
        PointsBadgeFrame.Corner.Set(10);
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

          ContentStack.AddPanel(BuildAbilityBar(Character, SkillBuild));
        }

        if (AghanimRegistry.TryGetSpec(Character, out var AghSpec) &&
            (AghSpec.Scepter != null || AghSpec.Shard != null))
        {
          ContentStack.AddPanel(BuildDivider());
          ContentStack.AddPanel(BuildSectionCaption(DotaLocalization.T("ui.panel.section.aghanim")));
          ContentStack.AddPanel(BuildAghanimBar(Character, AghSpec));
        }
      }

      // DOTA2 风格分节标题：── 天赋树 ──
      private Inv.Panel BuildSectionCaption(string Text)
      {
        var Table = Surface.NewTable();
        Table.Margin.Set(0, 0, 0, 8);
        var ColL = Table.AddStarColumn(1);
        var ColText = Table.AddAutoColumn();
        var ColR = Table.AddStarColumn(1);
        var Row = Table.AddAutoRow();

        Table.GetCell(ColL, Row).Content = BuildSectionLine();
        var Label = CreateLabel(GoldTextColour, FontSize: 13);
        Label.Text = Text;
        Label.Justify.Center();
        Label.Margin.Set(10, 0, 10, 0);
        Table.GetCell(ColText, Row).Content = Label;
        Table.GetCell(ColR, Row).Content = BuildSectionLine();
        return Table;
      }

      private Inv.Panel BuildSectionLine()
      {
        var Line = Surface.NewFrame();
        Line.Size.SetHeight(1);
        Line.Alignment.StretchCenter();
        Line.Background.Colour = SectionLineColour;
        return Line;
      }

      private Inv.Panel BuildDivider()
      {
        var Spacer = Surface.NewFrame();
        Spacer.Size.SetHeight(10);
        return Spacer;
      }

      /// <summary>为按钮挂上 DOTA2 式悬停反馈：鼠标移入时切换底色/描边，移出时恢复。</summary>
      private static void AttachHover(Inv.Button Btn, Inv.Colour Bg, Inv.Colour Border, Inv.Colour HoverBg, Inv.Colour HoverBorder)
      {
        Btn.Background.Colour = Bg;
        Btn.Border.Colour = Border;
        Btn.OverEvent += () =>
        {
          Btn.Background.Colour = HoverBg;
          Btn.Border.Colour = HoverBorder;
        };
        Btn.AwayEvent += () =>
        {
          Btn.Background.Colour = Bg;
          Btn.Border.Colour = Border;
        };
      }

      // ───────────────────────── 天赋树 ─────────────────────────

      private Inv.Panel BuildTalentRow(Character Character, TalentTier Tier, bool HasTalentPoints)
      {
        var LeftState = TalentService.GetNodeState(Character, Tier, TalentBranch.Left);
        var RightState = TalentService.GetNodeState(Character, Tier, TalentBranch.Right);
        bool TierReached = Character.Level >= Tier.UnlockLevel;

        var Table = Surface.NewTable();
        Table.Margin.Set(0, 0, 0, 6);
        var ColLeft = Table.AddStarColumn(1);
        var ColBadge = Table.AddFixedColumn(56);
        var ColRight = Table.AddStarColumn(1);
        var Row = Table.AddAutoRow();

        Table.GetCell(ColLeft, Row).Content = BuildTalentTile(Character, Tier, TalentBranch.Left, LeftState, HasTalentPoints);
        Table.GetCell(ColBadge, Row).Content = BuildTalentLevelBadge(Tier, TierReached);
        Table.GetCell(ColRight, Row).Content = BuildTalentTile(Character, Tier, TalentBranch.Right, RightState, HasTalentPoints);
        return Table;
      }

      private Inv.Panel BuildTalentLevelBadge(TalentTier Tier, bool Reached)
      {
        // DOTA2 天赋树中轴的圆形等级徽章
        var Badge = Surface.NewFrame();
        Badge.Size.Set(42, 42);
        Badge.Alignment.Center();
        Badge.Corner.Set(21);
        Badge.Background.Colour = BadgeBgColour;
        Badge.Border.Set(2);
        Badge.Border.Colour = Reached ? GoldBorderColour : TileBorderColour;

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

      private Inv.Panel BuildTalentTile(Character Character, TalentTier Tier, TalentBranch Branch, TalentNodeState State, bool HasTalentPoints)
      {
        var Node = Tier.GetNode(Branch);
        bool Clickable = State == TalentNodeState.Available && HasTalentPoints;

        Inv.Colour Bg, Border, HoverBg, HoverBorder, TextCol;
        if (State == TalentNodeState.Learned)
        {
          Bg = HoverBg = LearnedBgColour;
          Border = HoverBorder = LearnedBorderColour;
          TextCol = LearnedTextColour;
        }
        else if (Clickable)
        {
          Bg = GoldBgColour;
          Border = GoldBorderColour;
          HoverBg = GoldHoverBgColour;
          HoverBorder = GoldBrightColour;
          TextCol = GoldTextColour;
        }
        else if (State == TalentNodeState.OppositeBlocked)
        {
          Bg = HoverBg = TileDimBgColour;
          Border = TileDimBgColour;
          HoverBorder = TileBorderColour;
          TextCol = TextDisabledColour;
        }
        else
        {
          Bg = TileBgColour;
          HoverBg = TileHoverBgColour;
          Border = TileBorderColour;
          HoverBorder = TileHoverBorderColour;
          TextCol = TextSecondaryColour;
        }

        var Label = CreateLabel(TextCol, FontSize: 14);
        Label.Text = Node.Title;
        Label.Justify.Center();
        Label.LineWrapping = true;
        Label.Alignment.Center();

        // 全部用按钮承载：悬停高亮 + 弹出天赋说明卡片；仅可学习状态响应点击
        var Btn = Surface.NewFlatButton();
        Btn.Alignment.Stretch();
        Btn.Size.SetMinimumHeight(44);
        Btn.Border.Set(1);
        Btn.Corner.Set(2);
        Btn.Padding.Set(6, 4);
        AttachHover(Btn, Bg, Border, HoverBg, HoverBorder);
        Btn.Tooltip.Content = BuildTalentTooltip(Character, Tier, Node, State, HasTalentPoints);
        Btn.Content = Label;
        if (Clickable)
          Btn.SingleTapEvent += () => HandleSelectTalent(Tier, Branch);
        else
          Btn.SingleTapEvent += () => { };
        return Btn;
      }

      private Inv.Panel BuildTalentTooltip(Character Character, TalentTier Tier, TalentNode Node, TalentNodeState State, bool HasTalentPoints)
      {
        var Card = NewTooltipCard(out var Body);

        Body.AddPanel(BuildTooltipTitle(Node.Title, DotaLocalization.Format("ui.talent.tier_title", Tier.UnlockLevel)));

        if (!string.IsNullOrWhiteSpace(Node.Description))
        {
          var Desc = CreateLabel(TextSecondaryColour, FontSize: 13);
          Desc.Text = Node.Description;
          Desc.LineWrapping = true;
          Desc.Margin.Set(10, 8, 10, 0);
          Body.AddPanel(Desc);
        }

        string StatusText;
        Inv.Colour StatusColour;
        switch (State)
        {
          case TalentNodeState.Learned:
            StatusText = DotaLocalization.T("ui.talent.learned_tag");
            StatusColour = GoldBrightColour;
            break;
          case TalentNodeState.Available when HasTalentPoints:
            StatusText = DotaLocalization.T("ui.talent.click_tag");
            StatusColour = UpgradeGreenColour;
            break;
          case TalentNodeState.Available:
            StatusText = DotaLocalization.T("ui.talent.status.no_points");
            StatusColour = TextMutedColour;
            break;
          case TalentNodeState.OppositeBlocked:
            StatusText = Tier.DualUnlockLevel > Tier.UnlockLevel
              ? DotaLocalization.Format("ui.talent.status.blocked_dual", Tier.DualUnlockLevel)
              : DotaLocalization.T("ui.talent.status.blocked");
            StatusColour = TextMutedColour;
            break;
          default:
            StatusText = DotaLocalization.Format("ui.talent.status.locked", Tier.UnlockLevel);
            StatusColour = WarnColour;
            break;
        }

        Body.AddPanel(BuildTooltipFooter(StatusText, StatusColour));
        return Card;
      }

      // ───────────────────────── 技能栏 ─────────────────────────

      private Inv.Panel BuildAbilityBar(Character Character, HeroSkillBuild SkillBuild)
      {
        var Bar = Surface.NewHorizontalStack();
        Bar.Alignment.TopCenter();
        Bar.Margin.Set(0, 0, 0, 8);

        foreach (var Slot in SkillBuild.Slots)
          Bar.AddPanel(BuildAbilityColumn(Character, Slot));

        return Bar;
      }

      private Inv.Panel BuildAbilityColumn(Character Character, DotaSkillSlot Slot)
      {
        int CurrentLv = HeroSkillService.GetSkillLevel(Character, Slot);
        var State = HeroSkillService.GetUpgradeState(Character, Slot, out int RequiredHeroLv);

        var Column = Surface.NewVerticalStack();
        Column.Size.SetWidth(AbilityColumnWidth);
        Column.Margin.Set(2, 0, 2, 0);

        // 1. DOTA2 风格金色「+」：可升级时出现在图标上方，点击直接加点（无论是否可升级都占位，保证布局稳定）
        if (State == DotaSkillUpgradeState.Available)
        {
          var PlusBtn = Surface.NewFlatButton();
          PlusBtn.Size.Set(AbilityIconSize, 18);
          PlusBtn.Alignment.Center();
          PlusBtn.Margin.Set(0, 0, 0, 3);
          PlusBtn.Corner.Set(2);
          PlusBtn.Border.Set(1);
          AttachHover(PlusBtn, PlusBgColour, GoldBorderColour, PlusHoverBgColour, GoldBrightColour);
          PlusBtn.Hint = DotaLocalization.Format("ui.skill.plus_hint", Slot.Title);
          var PlusLabel = CreateLabel(PlusTextColour, FontSize: 14);
          PlusLabel.Text = "+";
          PlusLabel.Justify.Center();
          PlusLabel.Alignment.Center();
          PlusBtn.Content = PlusLabel;
          PlusBtn.SingleTapEvent += () => HandleUpgradeSkill(Slot);
          Column.AddPanel(PlusBtn);
        }
        else
        {
          var Spacer = Surface.NewFrame();
          Spacer.Size.Set(AbilityIconSize, 18);
          Spacer.Margin.Set(0, 0, 0, 3);
          Column.AddPanel(Spacer);
        }

        // 2. 技能图标：左上角快捷键 + 未学习暗化遮罩；悬停描边变亮并弹出技能卡片（详情与升级预览都在卡片中）
        var IconOverlay = Surface.NewOverlay();

        var GlyphImg = Slot.IconGlyph != null ? GetGlyphImage(Slot.IconGlyph) : null;
        if (GlyphImg != null)
        {
          var Graphic = Surface.NewGraphic();
          Graphic.Image = GlyphImg;
          Graphic.Size.Set(AbilityIconSize - 6, AbilityIconSize - 6);
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

        var HotkeyFrame = Surface.NewFrame();
        HotkeyFrame.Alignment.TopLeft();
        HotkeyFrame.Background.Colour = Inv.Colour.FromArgb(200, 0, 0, 0);
        HotkeyFrame.Padding.Set(3, 0);
        var HotkeyLabel = CreateLabel(CurrentLv > 0 ? TextPrimaryColour : TextMutedColour, FontSize: 11);
        HotkeyLabel.Text = Slot.Hotkey;
        HotkeyFrame.Content = HotkeyLabel;
        IconOverlay.AddPanel(HotkeyFrame);

        Inv.Colour IconBorder = State == DotaSkillUpgradeState.Available ? GoldBorderColour : IconBorderColour;

        var IconBtn = Surface.NewFlatButton();
        IconBtn.Size.Set(AbilityIconSize, AbilityIconSize);
        IconBtn.Alignment.Center();
        IconBtn.Border.Set(2);
        AttachHover(IconBtn, IconBgColour, IconBorder, IconBgColour, GoldBrightColour);
        IconBtn.Tooltip.Content = BuildSkillTooltip(Character, Slot, CurrentLv, State, RequiredHeroLv);
        IconBtn.Content = IconOverlay;
        IconBtn.SingleTapEvent += () => { };
        Column.AddPanel(IconBtn);

        // 3. 等级格（金色 = 已学等级）
        Column.AddPanel(BuildLevelPips(CurrentLv, Slot.MaxLevel));
        return Column;
      }

      private Inv.Panel BuildLevelPips(int CurrentLv, int MaxLevel)
      {
        const int Gap = 3;
        int PipW = Math.Clamp((AbilityIconSize - (MaxLevel - 1) * Gap) / Math.Max(1, MaxLevel), 4, 10);

        var Pips = Surface.NewHorizontalStack();
        Pips.Alignment.TopCenter();
        Pips.Margin.Set(0, 5, 0, 0);
        for (int i = 0; i < MaxLevel; i++)
        {
          var Pip = Surface.NewFrame();
          Pip.Size.Set(PipW, 5);
          Pip.Margin.Set(i == 0 ? 0 : Gap, 0, 0, 0);
          Pip.Background.Colour = i < CurrentLv ? PipFilledColour : PipEmptyColour;
          Pip.Border.Set(1);
          Pip.Border.Colour = i < CurrentLv ? GoldBorderColour : Inv.Colour.FromArgb(255, 0, 0, 0);
          Pips.AddPanel(Pip);
        }
        return Pips;
      }

      // ───────────────────────── 阿哈利姆神杖与魔晶 ─────────────────────────

      private Inv.Panel BuildAghanimBar(Character Character, HeroAghanimSpec Spec)
      {
        var Table = Surface.NewTable();
        Table.Margin.Set(0, 0, 0, 8);
        var ColL = Table.AddStarColumn(1);
        var ColGap = Table.AddFixedColumn(8);
        var ColR = Table.AddStarColumn(1);
        var Row = Table.AddAutoRow();

        if (Spec.Scepter != null)
          Table.GetCell(ColL, Row).Content = BuildAghanimTile(Character, Spec.Scepter, IsScepter: true);
        if (Spec.Shard != null)
          Table.GetCell(ColR, Row).Content = BuildAghanimTile(Character, Spec.Shard, IsScepter: false);

        return Table;
      }

      private Inv.Panel BuildAghanimTile(Character Character, AghanimUpgradeSpec Upgrade, bool IsScepter)
      {
        bool Active = IsScepter ? AghanimService.HasScepter(Character) : AghanimService.HasShard(Character);
        bool IsBlessing = IsScepter && AghanimService.HasScepterBlessing(Character);
        bool IsEquipped = IsScepter && AghanimService.HasEquippedScepter(Character);

        string HeaderName = IsScepter
          ? DotaLocalization.T("aghanim.help.scepter.title")
          : DotaLocalization.T("aghanim.help.shard.title");

        string StateTag = IsScepter
          ? (IsBlessing && IsEquipped ? DotaLocalization.T("aghanim.badge.blessing")
            : IsBlessing ? DotaLocalization.T("aghanim.badge.blessing")
            : IsEquipped ? DotaLocalization.T("aghanim.badge.equipped")
            : DotaLocalization.T("aghanim.state.inactive"))
          : (Active ? DotaLocalization.T("aghanim.badge.absorbed") : DotaLocalization.T("aghanim.state.inactive"));

        Glyph Icon = IsScepter
          ? (IsBlessing && !IsEquipped ? (AghanimService.BlessingGlyph ?? AghanimService.ScepterGlyph) : AghanimService.ScepterGlyph)
          : AghanimService.ShardGlyph;

        var Bg = Active ? AghanimActiveBgColour : TileDimBgColour;
        var HoverBg = Active ? AghanimActiveHoverBgColour : TileHoverBgColour;
        var Border = Active ? (IsBlessing ? GoldBorderColour : AghanimActiveBorderColour) : TileBorderColour;
        var HoverBorder = Active ? GoldBrightColour : TileHoverBorderColour;

        var Row = Surface.NewHorizontalStack();
        Row.Alignment.CenterLeft();

        var IconOverlay = Surface.NewOverlay();
        IconOverlay.Size.Set(34, 34);
        IconOverlay.Margin.Set(0, 0, 8, 0);
        var Img = Icon != null ? GetGlyphImage(Icon) : null;
        if (Img != null)
        {
          var Graphic = Surface.NewGraphic();
          Graphic.Image = Img;
          Graphic.Size.Set(32, 32);
          Graphic.Alignment.Center();
          IconOverlay.AddPanel(Graphic);
        }
        if (!Active)
        {
          var Shade = Surface.NewFrame();
          Shade.Alignment.Stretch();
          Shade.Background.Colour = UnlearnedShadeColour;
          IconOverlay.AddPanel(Shade);
        }
        Row.AddPanel(IconOverlay);

        var TextStack = Surface.NewVerticalStack();
        TextStack.Alignment.CenterLeft();

        var TitleLbl = CreateLabel(Active ? AghanimTextColour : TextSecondaryColour, FontSize: 13);
        TitleLbl.Text = HeaderName;
        TextStack.AddPanel(TitleLbl);

        var StatusLbl = CreateLabel(Active ? (IsBlessing ? GoldBrightColour : UpgradeGreenColour) : TextMutedColour, FontSize: 11);
        StatusLbl.Text = StateTag;
        TextStack.AddPanel(StatusLbl);

        Row.AddPanel(TextStack);

        var Btn = Surface.NewFlatButton();
        Btn.Alignment.Stretch();
        Btn.Size.SetMinimumHeight(46);
        Btn.Border.Set(1);
        Btn.Corner.Set(2);
        Btn.Padding.Set(8, 5);
        AttachHover(Btn, Bg, Border, HoverBg, HoverBorder);
        Btn.Tooltip.Content = BuildAghanimTooltip(Character, Upgrade, IsScepter, Active, IsBlessing, IsEquipped);
        Btn.Content = Row;
        Btn.SingleTapEvent += () => { };
        return Btn;
      }

      private Inv.Panel BuildAghanimTooltip(
        Character Character,
        AghanimUpgradeSpec Upgrade,
        bool IsScepter,
        bool Active,
        bool IsBlessing,
        bool IsEquipped)
      {
        var Card = NewTooltipCard(out var Body);

        string Subtitle = IsScepter
          ? DotaLocalization.T("aghanim.help.scepter.title")
          : DotaLocalization.T("aghanim.help.shard.title");
        Body.AddPanel(BuildTooltipTitle(Upgrade.Title, Subtitle));

        if (!string.IsNullOrWhiteSpace(Upgrade.Summary))
        {
          var Summary = CreateLabel(AghanimTextColour, FontSize: 13);
          Summary.Text = Upgrade.Summary;
          Summary.LineWrapping = true;
          Summary.Margin.Set(10, 8, 10, 0);
          Body.AddPanel(Summary);
        }

        var Bullets = Upgrade.BuildDetailBullets?.Invoke(Character);
        if (Bullets != null && Bullets.Count > 0)
        {
          var BulletStack = Surface.NewVerticalStack();
          BulletStack.Margin.Set(10, 6, 10, 0);
          foreach (var B in Bullets)
          {
            if (string.IsNullOrWhiteSpace(B))
              continue;
            var Lbl = CreateLabel(TextSecondaryColour, FontSize: 12);
            Lbl.Text = $"· {StripHelpColorTags(B)}";
            Lbl.LineWrapping = true;
            Lbl.Margin.Set(0, 0, 0, 3);
            BulletStack.AddPanel(Lbl);
          }
          Body.AddPanel(BulletStack);
        }

        string FooterText;
        Inv.Colour FooterColour;
        if (IsScepter)
        {
          if (IsBlessing && IsEquipped)
          {
            FooterText = DotaLocalization.T("aghanim.state.both");
            FooterColour = GoldBrightColour;
          }
          else if (IsBlessing)
          {
            FooterText = DotaLocalization.T("aghanim.state.blessing");
            FooterColour = GoldBrightColour;
          }
          else if (IsEquipped)
          {
            FooterText = DotaLocalization.T("aghanim.state.equipped");
            FooterColour = UpgradeGreenColour;
          }
          else
          {
            FooterText = DotaLocalization.T("aghanim.help.scepter.acquire");
            FooterColour = TextMutedColour;
          }
        }
        else
        {
          FooterText = Active
            ? DotaLocalization.T("aghanim.state.absorbed")
            : DotaLocalization.T("aghanim.help.shard.acquire");
          FooterColour = Active ? UpgradeGreenColour : TextMutedColour;
        }

        Body.AddPanel(BuildTooltipFooter(FooterText, FooterColour));
        return Card;
      }

      private static string StripHelpColorTags(string Text)
      {
        if (string.IsNullOrEmpty(Text) || Text.IndexOf('\u27E6') < 0)
          return Text;

        var Sb = new System.Text.StringBuilder(Text.Length);
        int i = 0;
        while (i < Text.Length)
        {
          if (Text[i] == '\u27E6')
          {
            int Colon = Text.IndexOf(':', i + 1);
            int Close = Text.IndexOf('\u27E7', i + 1);
            if (Colon > i && Close > Colon)
            {
              Sb.Append(Text, Colon + 1, Close - Colon - 1);
              i = Close + 1;
              continue;
            }
          }
          Sb.Append(Text[i++]);
        }
        return Sb.ToString();
      }

      // ───────────────────────── 悬停技能卡片（DOTA2 风格） ─────────────────────────

      private Inv.Frame NewTooltipCard(out Inv.Stack Body)
      {
        var Card = Surface.NewFrame();
        Card.Size.SetWidth(TooltipWidth);
        Card.Background.Colour = TooltipBgColour;
        Card.Border.Set(1);
        Card.Border.Colour = TooltipBorderColour;

        Body = Surface.NewVerticalStack();
        Card.Content = Body;
        return Card;
      }

      private Inv.Panel BuildTooltipTitle(string Title, string Subtitle)
      {
        var Strip = Surface.NewFrame();
        Strip.Background.Colour = HeaderStripColour;
        Strip.Padding.Set(10, 6);

        var Stack = Surface.NewVerticalStack();
        var TitleLabel = CreateLabel(TextPrimaryColour, FontSize: 16);
        TitleLabel.Text = Title;
        TitleLabel.LineWrapping = true;
        Stack.AddPanel(TitleLabel);

        if (!string.IsNullOrEmpty(Subtitle))
        {
          var Sub = CreateLabel(GoldTextColour, FontSize: 12);
          Sub.Text = Subtitle;
          Stack.AddPanel(Sub);
        }

        Strip.Content = Stack;
        return Strip;
      }

      private Inv.Panel BuildTooltipFooter(string Text, Inv.Colour Colour)
      {
        var Host = Surface.NewVerticalStack();
        Host.Margin.Set(0, 8, 0, 0);

        var Rule = Surface.NewFrame();
        Rule.Size.SetHeight(1);
        Rule.Background.Colour = SectionLineColour;
        Host.AddPanel(Rule);

        var Label = CreateLabel(Colour, FontSize: 12);
        Label.Text = Text;
        Label.LineWrapping = true;
        Label.Margin.Set(10, 5, 10, 6);
        Host.AddPanel(Label);
        return Host;
      }

      private Inv.Panel BuildSkillTooltip(Character Character, DotaSkillSlot Slot, int CurrentLv, DotaSkillUpgradeState State, int RequiredHeroLv)
      {
        var Card = NewTooltipCard(out var Body);

        string KindText = Slot.Kind switch
        {
          DotaSkillSlotKind.Active => DotaLocalization.T("ui.skill.kind.active"),
          DotaSkillSlotKind.Passive => DotaLocalization.T("ui.skill.kind.passive"),
          _ => DotaLocalization.T("ui.skill.kind.attribute")
        };
        Body.AddPanel(BuildTooltipTitle(
          $"[{Slot.Hotkey}] {Slot.Title}",
          $"{KindText} · {DescribeSlotLevel(Character, Slot, CurrentLv)}"));

        // 第一段：技能本身做什么（与等级无关）；第二段：当前等级的一句话数值。
        // 未学会时 DescribeLevel(0) 只是「学习 X：……」的简介，已有完整描述就不再重复。
        string SkillText = Slot.Description;
        string LevelText = Slot.DescribeLevel?.Invoke(CurrentLv);
        if (CurrentLv <= 0 && !string.IsNullOrWhiteSpace(SkillText))
          LevelText = null;

        if (!string.IsNullOrWhiteSpace(SkillText))
        {
          var Desc = CreateLabel(TextSecondaryColour, FontSize: 13);
          Desc.Text = SkillText;
          Desc.LineWrapping = true;
          Desc.Margin.Set(10, 8, 10, 0);
          Body.AddPanel(Desc);
        }

        if (!string.IsNullOrWhiteSpace(LevelText))
        {
          var Level = CreateLabel(string.IsNullOrWhiteSpace(SkillText) ? TextSecondaryColour : GoldBrightColour, FontSize: 13);
          Level.Text = LevelText;
          Level.LineWrapping = true;
          Level.Margin.Set(10, 6, 10, 0);
          Body.AddPanel(Level);
        }

        // DOTA2 式逐级数值：100 / 150 / 200 / 250，当前等级金色高亮；可升级时下一级绿色高亮（即升级预览）
        bool CanUpgrade = State == DotaSkillUpgradeState.Available;
        if (Slot.Stats != null && Slot.Stats.Count > 0)
        {
          var StatStack = Surface.NewVerticalStack();
          StatStack.Margin.Set(10, 8, 10, 0);
          foreach (var Stat in Slot.Stats)
          {
            var Line = BuildPerLevelStatLine(Stat, CurrentLv, CanUpgrade ? CurrentLv + 1 : 0, Slot.MaxLevel);
            if (Line != null)
              StatStack.AddPanel(Line);
          }
          Body.AddPanel(StatStack);
        }

        // 可升级时图标上方已有金色「+」，不再重复说明；仅在无法升级时说明原因（需等级 / 已满级 / 无点数）
        if (CanUpgrade)
        {
          var BottomPad = Surface.NewFrame();
          BottomPad.Size.SetHeight(8);
          Body.AddPanel(BottomPad);
        }
        else
        {
          Body.AddPanel(BuildTooltipFooter(
            DescribeUpgradeStatus(State, RequiredHeroLv),
            State == DotaSkillUpgradeState.Maxed ? GoldBrightColour : TextMutedColour));
        }
        return Card;
      }

      private Inv.Panel BuildPerLevelStatLine(DotaSkillStat Stat, int CurrentLv, int NextLv, int MaxLevel)
      {
        var Values = new string[MaxLevel];
        bool Any = false;
        for (int Lv = 1; Lv <= MaxLevel; Lv++)
        {
          Values[Lv - 1] = Stat.Value(Lv);
          Any |= Values[Lv - 1] != null;
        }
        if (!Any)
          return null;

        var Stack = Surface.NewVerticalStack();
        Stack.Margin.Set(0, 0, 0, 4);

        var Name = CreateLabel(TextMutedColour, FontSize: 12);
        Name.Text = Stat.Label();
        Stack.AddPanel(Name);

        var Row = Surface.NewHorizontalStack();
        for (int i = 0; i < MaxLevel; i++)
        {
          if (i > 0)
          {
            var Sep = CreateLabel(TextDisabledColour, FontSize: 13);
            Sep.Text = " / ";
            Row.AddPanel(Sep);
          }

          var ValColour = i + 1 == CurrentLv ? GoldBrightColour
            : i + 1 == NextLv ? UpgradeGreenColour
            : TextSecondaryColour;
          var Val = CreateLabel(ValColour, FontSize: 13);
          Val.Text = Values[i] ?? "—";
          Row.AddPanel(Val);
        }
        Stack.AddPanel(Row);
        return Stack;
      }

      private static string DescribeUpgradeStatus(DotaSkillUpgradeState State, int RequiredHeroLv) => State switch
      {
        DotaSkillUpgradeState.Available => DotaLocalization.T("ui.skill.status.available"),
        DotaSkillUpgradeState.Maxed => DotaLocalization.T("ui.skill.status.maxed"),
        DotaSkillUpgradeState.LevelLocked => DotaLocalization.Format("ui.skill.status.req_lv", RequiredHeroLv),
        _ => DotaLocalization.T("ui.skill.status.no_points")
      };

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

      private void HandleUpgradeSkill(DotaSkillSlot Slot)
      {
        var Character = GetActiveCharacter(StagePanel);
        var Engine = GetEngine(StagePanel);
        if (Character == null || Engine == null || Slot == null)
          return;

        if (HeroSkillService.TryUpgradeSkill(Engine, Character, Slot))
        {
          // RefreshPanels 的 postfix 会回调 Refresh()，无需手动重复
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
          // RefreshPanels 的 postfix 会回调 Refresh()，无需手动重复
          StageRefreshPanelsMethod?.Invoke(StagePanel, null);
        }
      }
    }
  }
}
