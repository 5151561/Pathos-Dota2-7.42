using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.UI
{
  /// <summary>
  /// Pathos 客户端技能冷却 UI 桥接层：
  /// 1. StagePanel.CastSpell Prefix：冷却中直接拦截施法（不弹出选格框、不扣蓝、不耗回合），飘字并记录战斗日志。
  /// 2. SpellPanel.Refresh Postfix：法术面板中冷却中技能尾标显示剩余回合（如 <c>CD 24T</c>）并暗灰提示（仍可右键看帮助）。
  /// 3. QuickIcon.Refresh Postfix：底部快捷栏绑定的法术冷却中时在角标显示剩余回合（如 <c>24T</c>）并降低图标透明度。
  /// </summary>
  internal static class DotaCooldownUiBridge
  {
    private static PropertyInfo StagePlayerProp;
    private static PropertyInfo StageShellProp;
    private static PropertyInfo ShellEngineProp;

    private static FieldInfo SpellPanelOptionDictField;
    private static FieldInfo TileOptionTrailerLabelField;
    private static PropertyInfo TileOptionColourProp;

    private static FieldInfo QuickIconStageField;
    private static FieldInfo QuickIconSelectField;
    private static PropertyInfo QuickIconBaseProp;
    private static PropertyInfo QuickSelectSpellProp;
    private static PropertyInfo StatusIconCountProp;
    private static PropertyInfo StatusIconColourProp;
    private static PropertyInfo StatusIconOpacityProp;

    internal static void ApplyPatches(Harmony Harmony)
    {
      try
      {
        var ClientAsm = PathosClientReflection.FindClientAssembly();
        if (ClientAsm == null)
        {
          HookBootstrap.WriteDebug("Pathos.dll not loaded; skipping DotaCooldownUiBridge patches.");
          return;
        }

        const BindingFlags AnyInstance = PathosClientReflection.AnyInstance;

        var StagePanelType = ClientAsm.GetType("Pathos.StagePanel");
        var ShellType = ClientAsm.GetType("Pathos.Shell");
        var SpellPanelType = ClientAsm.GetType("Pathos.SpellPanel");
        var TileOptionType = ClientAsm.GetType("Pathos.TileOption");
        var QuickIconType = ClientAsm.GetType("Pathos.QuickIcon");
        var QuickSelectType = ClientAsm.GetType("Pathos.QuickSelect") ?? typeof(Expansion).Assembly.GetType("Pathos.QuickSelect");
        var StatusIconType = ClientAsm.GetType("Pathos.StatusIcon");

        if (StagePanelType != null)
        {
          StagePlayerProp = StagePanelType.GetProperty("Player", AnyInstance);
          StageShellProp = StagePanelType.GetProperty("Shell", AnyInstance);
          ShellEngineProp = ShellType?.GetProperty("Engine", AnyInstance);

          PathosClientReflection.PatchSafe(
            Harmony,
            StagePanelType,
            "CastSpell",
            new[] { typeof(Spell) },
            typeof(DotaCooldownUiBridge),
            Prefix: nameof(StageCastSpellPrefix),
            Tag: "CooldownCastBlock");
        }

        if (SpellPanelType != null && TileOptionType != null)
        {
          SpellPanelOptionDictField = SpellPanelType.GetField("SpellOptionDictionary", AnyInstance);
          TileOptionTrailerLabelField = TileOptionType.GetField("TrailerLabel", AnyInstance);
          TileOptionColourProp = TileOptionType.GetProperty("Colour", AnyInstance);

          PathosClientReflection.PatchSafe(
            Harmony,
            SpellPanelType,
            "Refresh",
            new[] { typeof(Character) },
            typeof(DotaCooldownUiBridge),
            Postfix: nameof(SpellPanelRefreshPostfix),
            Tag: "CooldownSpellPanel");
        }

        if (QuickIconType != null && QuickSelectType != null && StatusIconType != null)
        {
          QuickIconStageField = QuickIconType.GetField("Stage", AnyInstance);
          QuickIconSelectField = QuickIconType.GetField("QuickSelect", AnyInstance);
          QuickIconBaseProp = QuickIconType.GetProperty("Base", AnyInstance | BindingFlags.FlattenHierarchy);
          QuickSelectSpellProp = QuickSelectType.GetProperty("Spell", AnyInstance);
          StatusIconCountProp = StatusIconType.GetProperty("Count", AnyInstance);
          StatusIconColourProp = StatusIconType.GetProperty("Colour", AnyInstance);
          StatusIconOpacityProp = StatusIconType.GetProperty("IconOpacity", AnyInstance);

          PathosClientReflection.PatchSafe(
            Harmony,
            QuickIconType,
            "Refresh",
            Type.EmptyTypes,
            typeof(DotaCooldownUiBridge),
            Postfix: nameof(QuickIconRefreshPostfix),
            Tag: "CooldownQuickIcon");
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaCooldownUiBridge.ApplyPatches failed: {Ex}");
      }
    }

    private static bool StageCastSpellPrefix(object __instance, Spell Spell)
    {
      if (__instance == null || Spell == null)
        return true;

      try
      {
        var Player = StagePlayerProp?.GetValue(__instance) as Player;
        var Caster = Player?.Prime?.Character;
        if (Caster == null)
          return true;

        int Remaining = DotaCooldownService.GetRemaining(Caster, Spell);
        if (Remaining <= 0)
          return true;

        var Shell = StageShellProp?.GetValue(__instance);
        var Engine = Shell != null ? ShellEngineProp?.GetValue(Shell) : null;
        if (Engine != null)
          DotaCooldownService.NotifyOnCooldown(Engine, Caster, Spell, Remaining);

        return false;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaCooldownUiBridge.StageCastSpellPrefix failed: {Ex}");
        return true;
      }
    }

    private static void SpellPanelRefreshPostfix(object __instance, Character Character)
    {
      if (__instance == null || Character == null || SpellPanelOptionDictField == null)
        return;

      try
      {
        if (SpellPanelOptionDictField.GetValue(__instance) is not IDictionary Dict)
          return;

        foreach (DictionaryEntry Entry in Dict)
        {
          if (Entry.Key is not Spell Spell || Entry.Value == null)
            continue;

          int Remaining = DotaCooldownService.GetRemaining(Character, Spell);
          if (Remaining <= 0)
            continue;

          if (TileOptionTrailerLabelField?.GetValue(Entry.Value) is Inv.Label TrailerLabel)
            TrailerLabel.Text = DotaLocalization.Format("ui.cooldown.short", Remaining);

          TileOptionColourProp?.SetValue(Entry.Value, Colour.DimGray);
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaCooldownUiBridge.SpellPanelRefreshPostfix failed: {Ex}");
      }
    }

    private static void QuickIconRefreshPostfix(object __instance)
    {
      if (__instance == null || QuickIconSelectField == null || QuickIconStageField == null)
        return;

      try
      {
        var Select = QuickIconSelectField.GetValue(__instance);
        var Spell = Select != null ? QuickSelectSpellProp?.GetValue(Select) as Spell : null;
        if (Spell == null)
          return;

        var Stage = QuickIconStageField.GetValue(__instance);
        var Player = Stage != null ? StagePlayerProp?.GetValue(Stage) as Player : null;
        var Character = Player?.Prime?.Character;
        if (Character == null)
          return;

        var StatusIcon = QuickIconBaseProp?.GetValue(__instance);
        if (StatusIcon == null)
          return;

        int Remaining = DotaCooldownService.GetRemaining(Character, Spell);
        if (Remaining > 0)
        {
          StatusIconCountProp?.SetValue(StatusIcon, $"{Remaining}T");
          StatusIconColourProp?.SetValue(StatusIcon, Colour.DimGray);
          StatusIconOpacityProp?.SetValue(StatusIcon, 0.45f);
        }
        else
        {
          StatusIconOpacityProp?.SetValue(StatusIcon, 1.0f);
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaCooldownUiBridge.QuickIconRefreshPostfix failed: {Ex}");
      }
    }
  }
}
