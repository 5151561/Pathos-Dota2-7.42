using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using HarmonyLib;
using Pathos.DotaHeroes.Framework.Localization;

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
      private sealed class CastSpellState
      {
        public SpellCastEventArgs Args;
        public SpellCastEventArgs PreviousActive;
      }

      private sealed class CastRangeState
      {
        public Cast Cast;
        public Dice OriginalRangeDice;
      }

      private sealed class AttackState
      {
        public AttackEventArgs Args;
        public AttackEventArgs PreviousActive;
      }

      public static void ApplyAll()
      {
        var EngineAsm = typeof(Expansion).Assembly;
        var DirectorType = EngineAsm.GetType("Pathos.Director");
        var EngineType = EngineAsm.GetType("Pathos.Engine");
        var CastSpellCmdType = EngineAsm.GetType("Pathos.CastSpellCommand");
        var CommandType = EngineAsm.GetType("Pathos.Command");
        var AttackCmdType = EngineAsm.GetType("Pathos.AttackCommand");
        var CharacterType = EngineAsm.GetType("Pathos.Character");
        var ActorType = EngineAsm.GetType("Pathos.Actor");
        var ElectType = EngineAsm.GetType("Pathos.Elect");
        var SquareType = EngineAsm.GetType("Pathos.Square");
        var PropertySetType = EngineAsm.GetType("Pathos.PropertySet");
        var MasteryType = EngineAsm.GetType("Pathos.Mastery");
        var GeneratorType = EngineAsm.GetType("Pathos.Generator");
        var InteractionType = EngineAsm.GetType("Pathos.Interaction");
        var AfflictionType = EngineAsm.GetType("Pathos.Affliction");

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

        // 5. 移动/行动速度钩子：Engine.GetBaseSpeed(Character, bool, PropertySet)
        PatchMethod(
          Harmony,
          EngineType,
          "GetBaseSpeed",
          new[] { CharacterType, typeof(bool), PropertySetType },
          postfix: nameof(GetBaseSpeedPostfix));

        // 6. 远程射击/投掷距离钩子：Engine.GetShootRange / GetThrowRange
        PatchMethod(
          Harmony,
          EngineType,
          "GetShootRange",
          new[] { CharacterType, typeof(Asset) },
          postfix: nameof(GetShootRangePostfix));

        PatchMethod(
          Harmony,
          EngineType,
          "GetThrowRange",
          new[] { CharacterType, typeof(Asset) },
          postfix: nameof(GetThrowRangePostfix));

        // 7. 施法距离修正钩子：Engine.CastEffects(...)
        PatchMethod(
          Harmony,
          EngineType,
          "CastEffects",
          new[] { CharacterType, typeof(Asset), typeof(Fixture), typeof(Asset), typeof(Cast), typeof(Delay), typeof(Direction), typeof(IEnumerable<Effect>) },
          prefix: nameof(CastEffectsPrefix),
          postfix: nameof(CastEffectsPostfix));

        // 8. 临时状态施加钩子：Engine.ApplyTransient(Character, Character, Property, Dice)
        PatchMethod(
          Harmony,
          EngineType,
          "ApplyTransient",
          new[] { CharacterType, CharacterType, typeof(Property), typeof(Dice) },
          prefix: nameof(ApplyTransientPrefix));

        // 9. 角色行动完成钩子：Director.Execute(Character, Command)
        PatchMethod(
          Harmony,
          DirectorType,
          "Execute",
          new[] { CharacterType, CommandType },
          postfix: nameof(ExecutePostfix));

        // 10. 角色普通攻击钩子：Director.Attack(Character, AttackCommand)
        PatchMethod(
          Harmony,
          DirectorType,
          "Attack",
          new[] { CharacterType, AttackCmdType },
          prefix: nameof(AttackPrefix),
          postfix: nameof(AttackPostfix));

        // 11. 角色开局创建与重摇钩子：Engine.CreateActor / RerollActor（用于固定 DOTA2 英雄初始六维）
        PatchMethod(
          Harmony,
          EngineType,
          "CreateActor",
          new[] { ActorType, ElectType, typeof(bool) },
          postfix: nameof(CreateActorPostfix));

        PatchMethod(
          Harmony,
          EngineType,
          "RerollActor",
          new[] { ActorType },
          postfix: nameof(RerollActorPostfix));

        // 12. 角色防御值查询钩子：Engine.GetDefenceClass(Character, DamageType?)（用于被动加甲/减甲）
        PatchMethod(
          Harmony,
          EngineType,
          "GetDefenceClass",
          new[] { CharacterType, typeof(DamageType?) },
          postfix: nameof(GetDefenceClassPostfix));

        // 13. 法术蓝耗查询钩子：Character.SpellCost(Mastery, Qualification)（用于减冷却转减蓝耗，施法与 UI 自动同步）
        PatchMethod(
          Harmony,
          CharacterType,
          "SpellCost",
          new[] { MasteryType, typeof(Qualification) },
          postfix: nameof(SpellCostPostfix));

        // 14. 角色升级钩子：Generator.AdvanceLevel(Character, bool)（用于天赋解锁提示与升级联动）
        PatchMethod(
          Harmony,
          GeneratorType,
          "AdvanceLevel",
          new[] { CharacterType, typeof(bool) },
          prefix: nameof(AdvanceLevelPrefix),
          postfix: nameof(AdvanceLevelPostfix));

        // 14.5 无敌/不可选中保护钩子：拦截地面陷阱、病痛/疫病感染（狂犬病等）与敌方/环境 Effect 结算
        PatchMethod(
          Harmony,
          EngineType,
          "TrapSquare",
          new[] { CharacterType, SquareType, typeof(Asset), typeof(bool) },
          prefix: nameof(TrapSquarePrefix));

        if (AfflictionType != null)
        {
          PatchMethod(
            Harmony,
            EngineType,
            "AfflictCharacter",
            new[] { CharacterType, AfflictionType },
            prefix: nameof(AfflictCharacterPrefix));
        }

        if (InteractionType != null)
        {
          PatchMethod(
            Harmony,
            EngineType,
            "ApplyEffects",
            new[] { InteractionType, typeof(IEnumerable<Effect>) },
            prefix: nameof(ApplyEffectsPrefix));
        }

        // 15. 天赋树独立面板 UI 补丁（仅在游戏客户端存在 Pathos.dll 时挂载）
        Talents.TalentTreeUiBridge.ApplyUiPatches(Harmony);

        // 16. DOTA2 原生选框扩距施法补丁（仅在游戏客户端存在 Pathos.dll 时挂载）
        Targeting.DotaRangeTargetingBridge.ApplyPatches(Harmony);

        // 17. DOTA2 左上角状态栏动态叠层徽章补丁（仅在游戏客户端存在 Pathos.dll 时挂载）
        UI.DotaStatusBarBridge.ApplyPatches(Harmony);

        // 18. DOTA2 技能帮助面板数值与阶梯详情补丁（仅在游戏客户端存在 Pathos.dll 时挂载）
        UI.DotaSpellHelpBridge.ApplyPatches(Harmony);
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

        try
        {
          Harmony.Patch(TargetMethod, prefix: PrefixMethod, postfix: PostfixMethod);
          WriteDebug($"Patched {TargetType.FullName}.{MethodName}");
        }
        catch (Exception Ex)
        {
          WriteError($"Failed to patch {TargetType.FullName}.{MethodName}: {Ex}");
        }
      }

      private static bool CastSpellPrefix(object __instance, Character Character, object Command, out CastSpellState __state)
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

        var Previous = DotaHookBus.ActiveSpellCast;
        DotaHookBus.ActiveSpellCast = Args;
        __state = new CastSpellState { Args = Args, PreviousActive = Previous };

        DotaHookBus.DispatchBeforeCastSpell(Args);
        if (Args.Cancel)
        {
          DotaHookBus.ActiveSpellCast = Previous;
          return false;
        }

        return true;
      }

      private static void CastSpellPostfix(CastSpellState __state)
      {
        if (__state == null)
          return;

        try
        {
          if (!__state.Args.Cancel)
            DotaHookBus.DispatchAfterCastSpell(__state.Args);
        }
        finally
        {
          DotaHookBus.ActiveSpellCast = __state.PreviousActive;
        }
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

        if (DotaHookBus.QueryInvulnerable(Defender))
          return false;

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

      private static void GetBaseSpeedPostfix(object __instance, Character Character, bool Attacking, ref Speed __result)
      {
        if (Character == null)
          return;

        var Args = new SpeedQueryEventArgs(__instance, Character, Attacking, __result);
        DotaHookBus.DispatchSpeedQuery(Args);
        __result = Args.Speed;
      }

      private static void GetShootRangePostfix(object __instance, Character Character, Asset Asset, ref int __result)
      {
        if (Character == null)
          return;

        var Args = new AttackRangeQueryEventArgs(__instance, Character, Asset, IsThrow: false, Range: __result);
        DotaHookBus.DispatchAttackRangeQuery(Args);
        __result = Args.Range;
      }

      private static void GetThrowRangePostfix(object __instance, Character Character, Asset Asset, ref int __result)
      {
        if (Character == null)
          return;

        var Args = new AttackRangeQueryEventArgs(__instance, Character, Asset, IsThrow: true, Range: __result);
        DotaHookBus.DispatchAttackRangeQuery(Args);
        __result = Args.Range;
      }

      private static void CastEffectsPrefix(object __instance, Character Character, Cast Cast, out CastRangeState __state)
      {
        __state = null;
        if (Character == null || Cast == null || Cast.TargetSelf)
          return;

        var RangeDice = Cast.RangeDice;
        if (RangeDice.IsZero())
          return;

        var Args = new CastRangeEventArgs(__instance, Character, Cast);
        DotaHookBus.DispatchCastRange(Args);

        if (Args.BonusRange != 0)
        {
          var Boosted = new Dice(RangeDice.Number, RangeDice.Base, RangeDice.Bonus + Args.BonusRange);
          if (PathosEngineBridge.SetCastRangeDice(Cast, Boosted))
            __state = new CastRangeState { Cast = Cast, OriginalRangeDice = RangeDice };
        }
      }

      private static void CastEffectsPostfix(CastRangeState __state)
      {
        if (__state?.Cast != null)
          PathosEngineBridge.SetCastRangeDice(__state.Cast, __state.OriginalRangeDice);
      }

      private static bool ApplyTransientPrefix(
        object __instance,
        Character Applier,
        Character Character,
        Property Property,
        ref Dice Duration)
      {
        if (Character == null || Property == null)
          return true;

        // 无敌状态下免疫来自敌方或环境施加的所有临时状态（无论是否在标准 14 项减益列表中）
        if (DotaHookBus.QueryInvulnerable(Character) &&
            (Applier == null || !PathosEngineBridge.IsAllied(Applier, Character)))
          return false;

        var Args = new TransientEventArgs(__instance, Applier, Character, Property, Duration);
        DotaHookBus.DispatchBeforeApplyTransient(Args);
        Duration = Args.Duration;
        return !Args.Cancel;
      }

      private static void ExecutePostfix(object __instance, Character Character, object Command)
      {
        if (Character == null || Command == null)
          return;

        var Engine = PathosEngineBridge.GetDirectorEngine(__instance);
        DotaHookBus.DispatchAfterExecuteCommand(new CommandEventArgs(Engine, __instance, Character, Command));
      }

      private static bool AttackPrefix(object __instance, Character Character, object Command, out AttackState __state)
      {
        __state = null;
        if (Character == null)
          return true;

        var Engine = PathosEngineBridge.GetDirectorEngine(__instance);
        var Direction = PathosEngineBridge.GetAttackCommandDirection(Command);

        // 无敌/不可选中目标保护：拦截敌方近战攻击命中，杜绝狂犬病、偷窃、吞噬、腐蚀等一切命中附加特效
        if (Character.Square != null && Direction != Direction.Self)
        {
          var FirstSq = Character.Square.Adjacent(Direction, 1);
          var Target = FirstSq?.Character;
          if (Target == null && FirstSq != null && !FirstSq.IsBlocked(Vision: false))
            Target = Character.Square.Adjacent(Direction, 2)?.Character;

          if (Target != null && Target != Character && DotaHookBus.QueryInvulnerable(Target))
          {
            var AtkDelay = PathosEngineBridge.GetAttackDelay(Engine, Character);
            PathosEngineBridge.SetBusy(Engine, Character, AtkDelay);
            PathosEngineBridge.StoryAttacking(Engine, Delay.FromTurns(10), Character, Direction);
            if (Target.Square != null)
              PathosEngineBridge.StoryCaption(Engine, Target.Square, Inv.Colour.Silver, DotaLocalization.T("ui.caption.invulnerable"), Delayed: false);
            return false;
          }
        }

        var Args = new AttackEventArgs(
          Engine: Engine,
          Director: __instance,
          Character: Character,
          Direction: Direction);

        var Previous = DotaHookBus.ActiveAttack;
        DotaHookBus.ActiveAttack = Args;
        __state = new AttackState { Args = Args, PreviousActive = Previous };

        DotaHookBus.DispatchBeforeAttack(Args);
        if (Args.Cancel)
        {
          DotaHookBus.ActiveAttack = Previous;
          return false;
        }

        return true;
      }

      private static void AttackPostfix(AttackState __state)
      {
        if (__state == null)
          return;

        try
        {
          if (!__state.Args.Cancel)
            DotaHookBus.DispatchAfterAttack(__state.Args);
        }
        finally
        {
          DotaHookBus.ActiveAttack = __state.PreviousActive;
        }
      }

      private static void CreateActorPostfix(object __instance, Actor Actor)
      {
        if (Actor?.Character == null)
          return;

        DotaHookBus.DispatchAfterCreateActor(new ActorCreateEventArgs(__instance, Actor, Actor.Character, IsReroll: false));
        PathosEngineBridge.RefreshConductStarting(Actor);
      }

      private static void RerollActorPostfix(object __instance, Actor Actor)
      {
        if (Actor?.Character == null)
          return;

        DotaHookBus.DispatchAfterCreateActor(new ActorCreateEventArgs(__instance, Actor, Actor.Character, IsReroll: true));
        PathosEngineBridge.RefreshConductStarting(Actor);
      }

      private static void GetDefenceClassPostfix(object __instance, Character Character, DamageType? DamageType, ref int __result)
      {
        if (Character == null)
          return;

        var Args = new DefenceQueryEventArgs(__instance, Character, DamageType, __result);
        DotaHookBus.DispatchDefenceQuery(Args);
        __result = Args.Defence;
      }

      private static void SpellCostPostfix(Character __instance, object Mastery, Qualification Qualification, ref int __result)
      {
        if (__instance == null || Mastery == null)
          return;

        var Spell = PathosEngineBridge.GetMasterySpell(Mastery);
        if (Spell == null)
          return;

        var Args = new SpellCostQueryEventArgs(__instance, Spell, Mastery, Qualification, __result);
        DotaHookBus.DispatchSpellCostQuery(Args);
        __result = Math.Max(0, Args.ManaCost);
      }

      private static void AdvanceLevelPrefix(Character Character, out int __state)
      {
        __state = Character?.Level ?? 0;
      }

      private static void AdvanceLevelPostfix(object __instance, Character Character, int __state)
      {
        if (Character == null || Character.Level <= __state)
          return;

        var Engine = PathosEngineBridge.GetGeneratorEngine(__instance);
        DotaHookBus.DispatchAfterLevelUp(new LevelUpEventArgs(Engine, Character, __state, Character.Level));
      }

      private static bool TrapSquarePrefix(Character TrapperCharacter)
      {
        return TrapperCharacter == null || !DotaHookBus.QueryInvulnerable(TrapperCharacter);
      }

      private static bool AfflictCharacterPrefix(Character Character)
      {
        return Character == null || !DotaHookBus.QueryInvulnerable(Character);
      }

      private static bool ApplyEffectsPrefix(object Interaction)
      {
        var Target = PathosEngineBridge.GetInteractionTargetCharacter(Interaction);
        if (Target == null || !DotaHookBus.QueryInvulnerable(Target))
          return true;

        var Source = PathosEngineBridge.GetInteractionSourceCharacter(Interaction);
        if (Source != null && PathosEngineBridge.IsAllied(Source, Target))
          return true;

        return false;
      }
    }
  }
}
