using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Pathos.DotaHeroes.Framework.Localization
{
  /// <summary>
  /// 扩展包 C# 自绘面板、技能参数、天赋树与战斗/升级日志的运行时多语言本地化层。
  /// 直接读取 Pathos 引擎当前的 <c>Pathos.Translation.Map.Language.Code</c>，
  /// 按需加载内嵌在 DLL 中的 <c>Localization.ui.&lt;lang&gt;.json</c> 词表，支持游戏内实时切换语言。
  /// </summary>
  public static class DotaLocalization
  {
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Assembly EngineAsm = typeof(Expansion).Assembly;
    private static readonly Type TranslationType = EngineAsm.GetType("Pathos.Translation");
    private static readonly FieldInfo TranslationMapField = TranslationType?.GetField("Map", AnyStatic);
    private static PropertyInfo MapLanguageProp;
    private static PropertyInfo LanguageCodeProp;

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache =
      new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取 Pathos 当前激活的规范化语言代码（英文统一映射为 "en"，其余与 Assets/*.Dictionary 一致）。
    /// </summary>
    public static string GetCurrentLanguageCode()
    {
      try
      {
        var Map = TranslationMapField?.GetValue(null);
        if (Map == null)
          return "en";

        MapLanguageProp ??= Map.GetType().GetProperty("Language", AnyInstance);
        var Language = MapLanguageProp?.GetValue(Map);
        if (Language == null)
          return "en";

        LanguageCodeProp ??= Language.GetType().GetProperty("Code", AnyInstance);
        string Code = LanguageCodeProp?.GetValue(Language) as string;
        return NormalizeLanguageCode(Code);
      }
      catch
      {
        return "en";
      }
    }

    /// <summary>
    /// 将 Pathos 内部语言代码规范化为内嵌 UI 词表的资源后缀。
    /// </summary>
    public static string NormalizeLanguageCode(string RawCode)
    {
      if (string.IsNullOrWhiteSpace(RawCode))
        return "en";

      string Trimmed = RawCode.Trim();
      if (Trimmed.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        return "en";

      return Trimmed;
    }

    /// <summary>
    /// 判断当前语言是否属于中日韩等使用全角括号/紧凑排版习惯的语言。
    /// </summary>
    public static bool IsCjkActive()
    {
      string Code = GetCurrentLanguageCode();
      return string.Equals(Code, "zh-Simplified", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Code, "zh-Traditional", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Code, "ja", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按键名查询当前语言的本地化文本（自动按 zh-Traditional → zh-Simplified → en → key 降级回退）。
    /// </summary>
    public static string T(string Key)
    {
      if (string.IsNullOrEmpty(Key))
        return string.Empty;

      string Lang = GetCurrentLanguageCode();
      if (TryGetFromLanguage(Lang, Key, out string Value))
        return Value;

      if (string.Equals(Lang, "zh-Traditional", StringComparison.OrdinalIgnoreCase) &&
          TryGetFromLanguage("zh-Simplified", Key, out Value))
      {
        return Value;
      }

      if (!string.Equals(Lang, "en", StringComparison.OrdinalIgnoreCase) &&
          TryGetFromLanguage("en", Key, out Value))
      {
        return Value;
      }

      return Key;
    }

    /// <summary>
    /// 按键名查询当前语言模板并安全格式化参数；若格式串异常则回退到英文模板。
    /// </summary>
    public static string Format(string Key, params object[] Args)
    {
      string Template = T(Key);
      if (Args == null || Args.Length == 0)
        return Template;

      try
      {
        return string.Format(CultureInfo.InvariantCulture, Template, Args);
      }
      catch (FormatException)
      {
        if (TryGetFromLanguage("en", Key, out string EnTemplate))
        {
          try
          {
            return string.Format(CultureInfo.InvariantCulture, EnTemplate, Args);
          }
          catch (FormatException)
          {
          }
        }

        return Template;
      }
    }

    private static bool TryGetFromLanguage(string LangCode, string Key, out string Value)
    {
      var Dict = GetOrLoadTable(LangCode);
      if (Dict != null && Dict.TryGetValue(Key, out string Found) && !string.IsNullOrEmpty(Found))
      {
        Value = Found;
        return true;
      }

      Value = null;
      return false;
    }

    private static IReadOnlyDictionary<string, string> GetOrLoadTable(string LangCode)
    {
      if (string.IsNullOrWhiteSpace(LangCode))
        return null;

      return Cache.GetOrAdd(LangCode, LoadEmbeddedTable);
    }

    private static IReadOnlyDictionary<string, string> LoadEmbeddedTable(string LangCode)
    {
      try
      {
        var Asm = typeof(DotaLocalization).Assembly;
        string ResourceName = $"Localization.ui.{LangCode}.json";
        using var Stream = Asm.GetManifestResourceStream(ResourceName);
        if (Stream == null)
          return EmptyTable;

        var Parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(Stream);
        return Parsed != null
          ? new Dictionary<string, string>(Parsed, StringComparer.Ordinal)
          : EmptyTable;
      }
      catch
      {
        return EmptyTable;
      }
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyTable =
      new Dictionary<string, string>(0, StringComparer.Ordinal);
  }
}
