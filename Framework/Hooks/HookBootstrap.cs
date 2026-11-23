using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using HarmonyLib;

namespace Pathos.DotaHeroes.Framework.Hooks
{
  /// <summary>
  /// Harmony Hook 运行时引导器（支持 Windows / macOS / Linux）：
  /// - 从内嵌资源按需加载 0Harmony.dll（单 DLL 自包含部署）。
  /// - 自动跳过 PathosMaker 打包进程。
  /// - 将 PathosEngine 关键生命周期挂载并转发到 DotaHookBus。
  /// - 默认静默运行，仅在发生异常或设置环境变量 DOTA_HEROES_HOOK_DEBUG=1 时写入日志。
  /// </summary>
  public static class HookBootstrap
  {
    private const string HarmonyId = "pathos.dotaheroes.hooks";
    private static int InitializedFlag;
    private static Assembly CachedHarmonyAssembly;
    private static readonly object ResolveLock = new();

    private static readonly bool DebugEnabled =
      string.Equals(Environment.GetEnvironmentVariable("DOTA_HEROES_HOOK_DEBUG"), "1", StringComparison.Ordinal);

    private static string LogPath => Path.Combine(Path.GetTempPath(), "DotaHeroesHook.log");

    /// <summary>
    /// 幂等初始化 Harmony 补丁。
    /// 注意：本方法禁止直接引用 HarmonyLib 下的任何类型，以确保先注册内嵌 DLL 解析器。
    /// </summary>
    public static void EnsureInitialized()
    {
      if (Interlocked.Exchange(ref InitializedFlag, 1) != 0)
        return;

      try
      {
        if (IsRunningInPathosMaker())
        {
          WriteDebug("Skipped Harmony init inside PathosMaker.");
          return;
        }

        RegisterEmbeddedHarmonyResolver();
        ApplyPatchesNoInline();
      }
      catch (Exception Ex)
      {
        WriteError($"HookBootstrap initialization failed: {Ex}");
      }
    }

    private static bool IsRunningInPathosMaker()
    {
      try
      {
        var EntryName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrEmpty(EntryName) &&
            EntryName.IndexOf("PathosMaker", StringComparison.OrdinalIgnoreCase) >= 0)
          return true;

        var ProcName = Process.GetCurrentProcess().ProcessName;
        if (!string.IsNullOrEmpty(ProcName) &&
            ProcName.IndexOf("PathosMaker", StringComparison.OrdinalIgnoreCase) >= 0)
          return true;
      }
      catch
      {
        // 忽略进程探测异常
      }

      return false;
    }

    private static void RegisterEmbeddedHarmonyResolver()
    {
      var SelfAsm = typeof(HookBootstrap).Assembly;
      var LoadContext = AssemblyLoadContext.GetLoadContext(SelfAsm);

      if (LoadContext != null)
      {
        LoadContext.Resolving += (Context, Name) =>
        {
          if (!string.Equals(Name.Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
            return null;
          return LoadEmbeddedHarmony(SelfAsm, Context);
        };
      }

      AppDomain.CurrentDomain.AssemblyResolve += (_, Args) =>
      {
        var ReqName = new AssemblyName(Args.Name).Name;
        if (!string.Equals(ReqName, "0Harmony", StringComparison.OrdinalIgnoreCase))
          return null;
        return LoadEmbeddedHarmony(SelfAsm, LoadContext);
      };
    }

    private static Assembly LoadEmbeddedHarmony(Assembly SelfAsm, AssemblyLoadContext Context)
    {
      lock (ResolveLock)
      {
        if (CachedHarmonyAssembly != null)
          return CachedHarmonyAssembly;

        using var Stream = SelfAsm.GetManifestResourceStream("0Harmony.dll");
        if (Stream == null)
        {
          WriteError("Embedded resource '0Harmony.dll' not found.");
          return null;
        }

        if (Context != null)
        {
          CachedHarmonyAssembly = Context.LoadFromStream(Stream);
        }
        else
        {
          using var Ms = new MemoryStream();
          Stream.CopyTo(Ms);
          CachedHarmonyAssembly = Assembly.Load(Ms.ToArray());
        }

        WriteDebug($"Loaded embedded 0Harmony: {CachedHarmonyAssembly.FullName}");
        return CachedHarmonyAssembly;
      }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatchesNoInline()
    {
      HarmonyDispatcher.ApplyAll();
    }

    internal static void WriteDebug(string Message)
    {
      if (!DebugEnabled)
        return;
      AppendLog("DEBUG", Message);
    }

    internal static void WriteError(string Message)
    {
      AppendLog("ERROR", Message);
    }

    private static void AppendLog(string Level, string Message)
    {
      try
      {
        File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}{Environment.NewLine}");
      }
      catch
      {
        // 日志写入失败不应影响游戏运行
      }
    }

    /// <summary>
    /// 隔离 HarmonyLib 类型引用的补丁安装与分发器。
    /// </summary>
    private static class HarmonyDispatcher
    {
      public static void ApplyAll()
      {
        var EngineAsm = typeof(Expansion).Assembly;
        var DirectorType = EngineAsm.GetType("Pathos.Director");
        var EngineType = EngineAsm.GetType("Pathos.Engine");
        var CastSpellCmdType = EngineAsm.GetType("Pathos.CastSpellCommand");
        var CharacterType = EngineAsm.GetType("Pathos.Character");
        var SquareType = EngineAsm.GetType("Pathos.Square");

        var Harmony = new Harmony(HarmonyId);

        // 1. 施法钩子：Director.CastSpell(Character, CastSpellCommand)
        PatchMethod(
          Harmony,
          DirectorType,
          "CastSpell",
          new[] { CharacterType, CastSpellCmdType },
          prefix: nameof(CastSpellPrefix),
          postfix: nameof(CastSpellPostfix));

        // 2. 伤害结算钩子：Engine.DamageCharacter(Character, Character, int, string)
        PatchMethod(
          Harmony,
          EngineType,
          "DamageCharacter",
          new[] { CharacterType, CharacterType, typeof(int), typeof(string) },
          prefix: nameof(DamageCharacterPrefix),
          postfix: nameof(DamageCharacterPostfix));

        // 3. 击杀钩子：Engine.KillCharacter(Character, Character, string)
        PatchMethod(
          Harmony,
          EngineType,
          "KillCharacter",
          new[] { CharacterType, CharacterType, typeof(string) },
          postfix: nameof(KillCharacterPostfix));

        // 4. 复活钩子：Engine.ResurrectCharacter(Character, Square)
        PatchMethod(
          Harmony,
          EngineType,
          "ResurrectCharacter",
          new[] { CharacterType, SquareType },
          postfix: nameof(ResurrectCharacterPostfix));
      }

      private static void PatchMethod(
        Harmony Harmony,
        Type TargetType,
        string MethodName,
        Type[] ParameterTypes,
        string prefix = null,
        string postfix = null)
      {
        if (TargetType == null)
        {
          WriteError($"Target type missing for hook '{MethodName}'.");
          return;
        }

        var TargetMethod = AccessTools.Method(TargetType, MethodName, ParameterTypes);
        if (TargetMethod == null)
        {
          WriteError($"Target method not found: {TargetType.FullName}.{MethodName}");
          return;
        }

        var PrefixMethod = prefix != null ? new HarmonyMethod(typeof(HarmonyDispatcher), prefix) : null;
        var PostfixMethod = postfix != null ? new HarmonyMethod(typeof(HarmonyDispatcher), postfix) : null;

        Harmony.Patch(TargetMethod, prefix: PrefixMethod, postfix: PostfixMethod);
        WriteDebug($"Patched {TargetType.FullName}.{MethodName}");
      }

      private static bool CastSpellPrefix(object __instance, Character Character, object Command, out SpellCastEventArgs __state)
      {
        __state = null;
        var Spell = PathosEngineBridge.GetCastCommandSpell(Command);
        if (Character == null || Spell == null)
          return true;

        var Args = new SpellCastEventArgs(
          Engine: PathosEngineBridge.GetDirectorEngine(__instance),
          Director: __instance,
          Character: Character,
          Spell: Spell,
          Asset: PathosEngineBridge.GetCastCommandAsset(Command),
          Direction: PathosEngineBridge.GetCastCommandDirection(Command));

        __state = Args;
        DotaHookBus.DispatchBeforeCastSpell(Args);
        return !Args.Cancel;
      }

      private static void CastSpellPostfix(SpellCastEventArgs __state)
      {
        if (__state == null || __state.Cancel)
          return;

        DotaHookBus.DispatchAfterCastSpell(__state);
      }

      private static bool DamageCharacterPrefix(
        object __instance,
        Character Attacker,
        Character Defender,
        ref int DamagePoints,
        string Cause,
        out DamageEventArgs __state)
      {
        __state = null;
        if (Defender == null)
          return true;

        var Args = new DamageEventArgs(__instance, Attacker, Defender, DamagePoints, Cause);
        __state = Args;
        DotaHookBus.DispatchBeforeDamage(Args);
        DamagePoints = Args.DamagePoints;
        return !Args.Cancel;
      }

      private static void DamageCharacterPostfix(DamageEventArgs __state)
      {
        if (__state == null || __state.Cancel)
          return;

        DotaHookBus.DispatchAfterDamage(__state);
      }

      private static void KillCharacterPostfix(object __instance, Character Attacker, Character Defender, string Cause)
      {
        if (Defender == null)
          return;

        DotaHookBus.DispatchAfterKill(new KillEventArgs(__instance, Attacker, Defender, Cause));
      }

      private static void ResurrectCharacterPostfix(object __instance, Character Character)
      {
        if (Character == null)
          return;

        DotaHookBus.DispatchAfterResurrect(new ResurrectEventArgs(__instance, Character));
      }
    }
  }
}
