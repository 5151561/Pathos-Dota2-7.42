using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework.UI
{
  /// <summary>
  /// Pathos 桌面客户端（Pathos.dll）公共反射与 Harmony 安全补丁辅助层，
  /// 供 TalentTreeUiBridge、DotaRangeTargetingBridge、DotaStatusBarBridge 共享，
  /// 避免在各客户端桥接类中重复查找程序集、挂载补丁和解析 Theme 图标。
  /// </summary>
  internal static class PathosClientReflection
  {
    internal const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static MethodInfo CachedThemeGetImageMethod;

    /// <summary>
    /// 查找当前已加载的 Pathos 客户端程序集（Pathos.dll）；在 PathosMaker 打包期返回 null。
    /// </summary>
    public static Assembly FindClientAssembly()
    {
      return AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(A => string.Equals(A.GetName().Name, "Pathos", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 安全挂载一个 Harmony 补丁，找不到目标方法时记录错误日志而非抛出异常。
    /// </summary>
    public static bool PatchSafe(
      Harmony Harmony,
      Type TargetType,
      string MethodName,
      Type[] ParamTypes,
      Type PatchOwnerType,
      string Prefix = null,
      string Postfix = null,
      string Tag = null)
    {
      if (Harmony == null || TargetType == null || PatchOwnerType == null)
        return false;

      var Method = AccessTools.Method(TargetType, MethodName, ParamTypes);
      if (Method == null)
      {
        string Label = string.IsNullOrEmpty(Tag) ? "Client hook" : Tag;
        HookBootstrap.WriteError($"{Label} target not found: {TargetType.FullName}.{MethodName}");
        return false;
      }

      var PrefixMethod = Prefix != null ? new HarmonyMethod(PatchOwnerType, Prefix) : null;
      var PostfixMethod = Postfix != null ? new HarmonyMethod(PatchOwnerType, Postfix) : null;
      Harmony.Patch(Method, prefix: PrefixMethod, postfix: PostfixMethod);

      string Suffix = string.IsNullOrEmpty(Tag) ? string.Empty : $" ({Tag})";
      HookBootstrap.WriteDebug($"Patched {TargetType.FullName}.{MethodName}{Suffix}");
      return true;
    }

    /// <summary>
    /// 通过 Pathos.Theme.GetImage(Glyph) 获取客户端渲染用的 Inv.Image。
    /// </summary>
    public static Inv.Image GetThemeGlyphImage(Glyph Glyph)
    {
      if (Glyph == null)
        return null;

      if (CachedThemeGetImageMethod == null)
      {
        var ClientAsm = FindClientAssembly();
        var ThemeType = ClientAsm?.GetType("Pathos.Theme");
        CachedThemeGetImageMethod = ThemeType?.GetMethod("GetImage", AnyStatic, null, new[] { typeof(Glyph) }, null);
      }

      if (CachedThemeGetImageMethod == null)
        return null;

      try
      {
        return CachedThemeGetImageMethod.Invoke(null, new object[] { Glyph }) as Inv.Image;
      }
      catch
      {
        return null;
      }
    }
  }
}
