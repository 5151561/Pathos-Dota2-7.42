using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;

namespace Pathos.DotaHeroes.Framework.UI
{
  /// <summary>
  /// 左上角状态栏（StagePanel.LeftColumn）动态状态徽章描述。
  /// 供各 DOTA2 英雄的叠层/充能类短期状态（如主宰「剑心犹在」）复用原生 Pathos StatusIcon 渲染。
  /// </summary>
  public sealed class DotaStatusBadge
  {
    public string Key { get; init; }
    public Glyph Glyph { get; init; }
    public string Text { get; init; }
    public string Count { get; init; }
    public Colour Colour { get; init; } = Colour.Black;
    public Property HelpProperty { get; init; }
    public Spell HelpSpell { get; init; }
    public string HelpPassiveId { get; init; }

    /// <summary>徽章标题：本地化 Key 的大写文本。</summary>
    public static string Title(string Key) => DotaLocalization.T(Key).ToUpper();

    /// <summary>
    /// 先天 / 被动徽章（零 Property）：标题取 <paramref name="TitleKey"/>，点击弹出 <see cref="DotaSpellHelpBridge"/> 注册的被动帮助面板。
    /// </summary>
    public static DotaStatusBadge ForPassive(string Key, Glyph Glyph, string PassiveId, string TitleKey, string Count, Colour Colour)
      => new()
      {
        Key = Key,
        Glyph = Glyph,
        Text = Title(TitleKey),
        Count = Count,
        Colour = Colour,
        HelpPassiveId = PassiveId
      };
  }

  /// <summary>
  /// Pathos 左上角状态栏（StagePanel.ArrangeStatus）桥接：
  /// 允许 DOTA2 英雄将带层数的短期状态以原生 StatusIcon 样式显示在左上角状态栏，并支持点击查看属性说明。
  /// </summary>
  public static class DotaStatusBarBridge
  {
    private static readonly ConcurrentDictionary<string, Func<Character, DotaStatusBadge>> Providers =
      new(StringComparer.Ordinal);

    private static readonly ConditionalWeakTable<object, Dictionary<string, object>> StageIconCaches = new();
    private static readonly ConditionalWeakTable<object, Property> IconHelpProperties = new();
    private static readonly ConditionalWeakTable<object, Spell> IconHelpSpells = new();
    private static readonly ConditionalWeakTable<object, string> IconHelpPassiveIds = new();

    private static PropertyInfo StagePlayerProp;
    private static PropertyInfo StageShellProp;
    private static PropertyInfo StageHelpQueryProp;
    private static FieldInfo StageSurfaceField;
    private static FieldInfo StageLeftColumnField;

    private static FieldInfo SideColumnTopIconsField;

    private static ConstructorInfo StatusIconCtor;
    private static PropertyInfo StatusIconImageProp;
    private static PropertyInfo StatusIconTextProp;
    private static PropertyInfo StatusIconCountProp;
    private static PropertyInfo StatusIconColourProp;
    private static PropertyInfo StatusIconIsEnabledProp;
    private static EventInfo StatusIconSingleTapEvent;
    private static EventInfo StatusIconContextTapEvent;

    private static MethodInfo HelpQueryShowPropertyMethod;
    private static MethodInfo HelpQueryShowSpellMethod;

    /// <summary>
    /// 按唯一 Key 幂等注册一个左上角状态栏图标提供器（返回 null 表示当前不显示）。
    /// </summary>
    public static void RegisterProvider(string Key, Func<Character, DotaStatusBadge> Provider)
    {
      if (string.IsNullOrWhiteSpace(Key) || Provider == null)
        return;

      Providers[Key] = Provider;
    }

    /// <summary>
    /// 注册仅对指定职业、且存活角色生效的状态栏提供器（统一替代各英雄 <c>if (!IsX(Ch) || Ch.Deceased) return null;</c> 样板）。
    /// </summary>
    public static void RegisterProvider(string Key, string ClassName, Func<Character, DotaStatusBadge> Provider)
    {
      if (Provider == null)
        return;

      RegisterProvider(Key, Ch =>
        Ch != null && !Ch.Deceased && DotaCombatHelper.IsClass(Ch, ClassName) ? Provider(Ch) : null);
    }

    internal static void ApplyPatches(Harmony Harmony)
    {
      try
      {
        var ClientAsm = PathosClientReflection.FindClientAssembly();
        if (ClientAsm == null)
        {
          HookBootstrap.WriteDebug("Pathos.dll not loaded; skipping DotaStatusBarBridge patches.");
          return;
        }

        const BindingFlags AnyInstance = PathosClientReflection.AnyInstance;

        var StagePanelType = ClientAsm.GetType("Pathos.StagePanel");
        var SideColumnType = ClientAsm.GetType("Pathos.SideColumn");
        var StatusIconType = ClientAsm.GetType("Pathos.StatusIcon");
        var HelpQueryType = ClientAsm.GetType("Pathos.HelpQuery");
        var ShellType = ClientAsm.GetType("Pathos.Shell");

        if (StagePanelType == null || SideColumnType == null || StatusIconType == null)
        {
          HookBootstrap.WriteError("DotaStatusBarBridge: required Pathos client types not found.");
          return;
        }

        StagePlayerProp = StagePanelType.GetProperty("Player", AnyInstance);
        StageShellProp = StagePanelType.GetProperty("Shell", AnyInstance);
        StageHelpQueryProp = StagePanelType.GetProperty("HelpQuery", AnyInstance);
        StageSurfaceField = StagePanelType.GetField("Surface", AnyInstance);
        StageLeftColumnField = StagePanelType.GetField("LeftColumn", AnyInstance);

        SideColumnTopIconsField = SideColumnType.GetField("TopIcons", AnyInstance);

        StatusIconCtor = StatusIconType.GetConstructor(AnyInstance, null, new[] { ShellType, typeof(Surface), typeof(bool) }, null);
        StatusIconImageProp = StatusIconType.GetProperty("Image", AnyInstance);
        StatusIconTextProp = StatusIconType.GetProperty("Text", AnyInstance);
        StatusIconCountProp = StatusIconType.GetProperty("Count", AnyInstance);
        StatusIconColourProp = StatusIconType.GetProperty("Colour", AnyInstance);
        StatusIconIsEnabledProp = StatusIconType.GetProperty("IsEnabled", AnyInstance);
        StatusIconSingleTapEvent = StatusIconType.GetEvent("SingleTapEvent", AnyInstance);
        StatusIconContextTapEvent = StatusIconType.GetEvent("ContextTapEvent", AnyInstance);

        HelpQueryShowPropertyMethod = HelpQueryType?.GetMethod("ShowProperty", AnyInstance, null, new[] { typeof(Property) }, null);
        HelpQueryShowSpellMethod = HelpQueryType?.GetMethod("ShowSpell", AnyInstance, null, new[] { typeof(Character), typeof(Spell) }, null);

        PathosClientReflection.PatchSafe(
          Harmony,
          StagePanelType,
          "ArrangeStatus",
          Type.EmptyTypes,
          typeof(DotaStatusBarBridge),
          Postfix: nameof(StageArrangeStatusPostfix),
          Tag: "StatusBar");
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaStatusBarBridge.ApplyPatches failed: {Ex}");
      }
    }

    private static void StageArrangeStatusPostfix(object __instance)
    {
      if (__instance == null || Providers.IsEmpty)
        return;

      try
      {
        var Player = StagePlayerProp?.GetValue(__instance) as Player;
        var Character = Player?.Prime?.Character;
        if (Character == null)
          return;

        var LeftColumn = StageLeftColumnField?.GetValue(__instance);
        if (LeftColumn == null)
          return;

        if (SideColumnTopIconsField?.GetValue(LeftColumn) is not IList TopIcons)
          return;

        var Shell = StageShellProp?.GetValue(__instance);
        var Surface = StageSurfaceField?.GetValue(__instance) as Surface;
        if (Shell == null || Surface == null || StatusIconCtor == null)
          return;

        var Cache = StageIconCaches.GetValue(__instance, _ => new Dictionary<string, object>(StringComparer.Ordinal));

        foreach (var Pair in Providers)
        {
          DotaStatusBadge Badge;
          try
          {
            Badge = Pair.Value(Character);
          }
          catch (Exception Ex)
          {
            HookBootstrap.WriteError($"DotaStatusBarBridge provider '{Pair.Key}' failed: {Ex}");
            continue;
          }

          if (Badge == null)
            continue;

          string BadgeKey = string.IsNullOrEmpty(Badge.Key) ? Pair.Key : Badge.Key;
          if (!Cache.TryGetValue(BadgeKey, out var IconObj))
          {
            IconObj = StatusIconCtor.Invoke(new object[] { Shell, Surface, false });
            if (IconObj == null)
              continue;

            var StageRef = __instance;
            var CapturedIcon = IconObj;
            Action OnTap = () =>
            {
              try
              {
                var HelpQuery = StageHelpQueryProp?.GetValue(StageRef);
                if (HelpQuery == null)
                  return;

                if (IconHelpPassiveIds.TryGetValue(CapturedIcon, out var PassiveId) && !string.IsNullOrEmpty(PassiveId))
                {
                  var CurPlayer = StagePlayerProp?.GetValue(StageRef) as Player;
                  var CurCh = CurPlayer?.Prime?.Character;
                  DotaSpellHelpBridge.ShowPassiveHelp(HelpQuery, CurCh, PassiveId);
                }
                else if (IconHelpProperties.TryGetValue(CapturedIcon, out var Prop) && Prop != null && HelpQueryShowPropertyMethod != null)
                {
                  HelpQueryShowPropertyMethod.Invoke(HelpQuery, new object[] { Prop });
                }
                else if (IconHelpSpells.TryGetValue(CapturedIcon, out var Spell) && Spell != null && HelpQueryShowSpellMethod != null)
                {
                  var CurPlayer = StagePlayerProp?.GetValue(StageRef) as Player;
                  var CurCh = CurPlayer?.Prime?.Character;
                  if (CurCh != null)
                    HelpQueryShowSpellMethod.Invoke(HelpQuery, new object[] { CurCh, Spell });
                }
              }
              catch (Exception Ex)
              {
                HookBootstrap.WriteError($"DotaStatusBarBridge.OnTap failed: {Ex}");
              }
            };

            if (StatusIconSingleTapEvent != null)
              StatusIconSingleTapEvent.AddEventHandler(IconObj, OnTap);
            if (StatusIconContextTapEvent != null)
              StatusIconContextTapEvent.AddEventHandler(IconObj, OnTap);

            StatusIconIsEnabledProp?.SetValue(IconObj, true);
            Cache[BadgeKey] = IconObj;
          }

          if (!string.IsNullOrEmpty(Badge.HelpPassiveId))
          {
            IconHelpPassiveIds.AddOrUpdate(IconObj, Badge.HelpPassiveId);
            IconHelpProperties.Remove(IconObj);
            IconHelpSpells.Remove(IconObj);
          }
          else if (Badge.HelpProperty != null)
          {
            IconHelpProperties.AddOrUpdate(IconObj, Badge.HelpProperty);
            IconHelpPassiveIds.Remove(IconObj);
            IconHelpSpells.Remove(IconObj);
          }
          else if (Badge.HelpSpell != null)
          {
            IconHelpSpells.AddOrUpdate(IconObj, Badge.HelpSpell);
            IconHelpPassiveIds.Remove(IconObj);
            IconHelpProperties.Remove(IconObj);
          }

          if (Badge.Glyph != null)
          {
            var Img = PathosClientReflection.GetThemeGlyphImage(Badge.Glyph);
            if (Img != null)
              StatusIconImageProp?.SetValue(IconObj, Img);
          }

          StatusIconTextProp?.SetValue(IconObj, Badge.Text ?? string.Empty);
          StatusIconCountProp?.SetValue(IconObj, Badge.Count ?? string.Empty);
          StatusIconColourProp?.SetValue(IconObj, Badge.Colour);

          if (!TopIcons.Contains(IconObj))
            TopIcons.Add(IconObj);
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaStatusBarBridge.StageArrangeStatusPostfix failed: {Ex}");
      }
    }
  }
}
