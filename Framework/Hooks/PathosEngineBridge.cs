using System;
using System.Reflection;

namespace Pathos.DotaHeroes.Framework.Hooks
{
  /// <summary>
  /// 封装对 PathosEngine 内部（internal）成员的安全反射访问与缓存，
  /// 供各 DOTA2 英雄 Hook 逻辑复用，避免业务代码散落反射细节。
  /// </summary>
  public static class PathosEngineBridge
  {
    private static readonly Assembly EngineAsm = typeof(Expansion).Assembly;
    private static readonly Type EngineType = EngineAsm.GetType("Pathos.Engine");
    private static readonly Type DirectorType = EngineAsm.GetType("Pathos.Director");
    private static readonly Type JournalType = EngineAsm.GetType("Pathos.Journal");
    private static readonly Type LogType = EngineAsm.GetType("Pathos.Log");
    private static readonly Type CastSpellCommandType = EngineAsm.GetType("Pathos.CastSpellCommand");

    private static readonly FieldInfo DirectorEngineField =
      DirectorType?.GetField("Engine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo EngineJournalProp =
      EngineType?.GetProperty("Journal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo JournalWriteInternalEventMethod =
      JournalType?.GetMethod("WriteInternalEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo LogWriteEventMethod =
      LogType?.GetMethod("WriteEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Glyph), typeof(string) }, null);

    private static readonly MethodInfo AbilityScoreSetter =
      typeof(Ability).GetProperty("Score", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true);

    private static readonly PropertyInfo CastCmdSpellProp =
      CastSpellCommandType?.GetProperty("Spell", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo CastCmdAssetProp =
      CastSpellCommandType?.GetProperty("Asset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo CastCmdDirectionProp =
      CastSpellCommandType?.GetProperty("Direction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo MeterSetBothMethod =
      typeof(Meter).GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null);

    private static readonly MethodInfo MeterSetPointsMethod =
      typeof(Meter).GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

    public static object GetDirectorEngine(object Director)
    {
      return Director != null && DirectorEngineField != null ? DirectorEngineField.GetValue(Director) : null;
    }

    public static Spell GetCastCommandSpell(object Command)
    {
      return Command != null && CastCmdSpellProp != null ? CastCmdSpellProp.GetValue(Command) as Spell : null;
    }

    public static Asset GetCastCommandAsset(object Command)
    {
      return Command != null && CastCmdAssetProp != null ? CastCmdAssetProp.GetValue(Command) as Asset : null;
    }

    public static Direction GetCastCommandDirection(object Command)
    {
      if (Command == null || CastCmdDirectionProp == null)
        return default;
      var Val = CastCmdDirectionProp.GetValue(Command);
      return Val is Direction Dir ? Dir : default;
    }

    /// <summary>
    /// 直接设置某项 Ability 的分值（可同时用于 CurrentForm 与 MaximumForm）。
    /// </summary>
    public static bool SetAbilityScore(Ability Target, int Score)
    {
      if (Target == null || AbilityScoreSetter == null)
        return false;

      AbilityScoreSetter.Invoke(Target, new object[] { Score });
      return true;
    }

    /// <summary>
    /// 将指定属性在 MaximumForm 中的基准值同步为 CurrentForm 当前值，
    /// 防止复活或恢复属性药水把主动转走的属性白嫖回满。
    /// </summary>
    public static void SyncMaximumFormScores(Character Character, params string[] AttributeNames)
    {
      if (Character?.CurrentForm == null || Character.MaximumForm == null || AttributeNames == null || AttributeNames.Length == 0)
        return;

      foreach (var CurAbility in Character.CurrentForm.Abilities)
      {
        var Attr = CurAbility.Attribute;
        if (Attr == null)
          continue;

        bool Matched = false;
        for (int I = 0; I < AttributeNames.Length; I++)
        {
          if (string.Equals(Attr.Name, AttributeNames[I], StringComparison.OrdinalIgnoreCase))
          {
            Matched = true;
            break;
          }
        }

        if (!Matched)
          continue;

        var MaxAbility = Character.MaximumForm[Attr];
        if (MaxAbility != null && MaxAbility.Score != CurAbility.Score)
          SetAbilityScore(MaxAbility, CurAbility.Score);
      }
    }

    /// <summary>
    /// 直接设置 Meter（Life / Mana）的当前值与上限值。
    /// </summary>
    public static bool SetMeter(Meter Target, int Points, int Total)
    {
      if (Target == null || MeterSetBothMethod == null)
        return false;

      MeterSetBothMethod.Invoke(Target, new object[] { Points, Total });
      return true;
    }

    /// <summary>
    /// 直接设置 Meter（Life / Mana）的当前值。
    /// </summary>
    public static bool SetMeterPoints(Meter Target, int Points)
    {
      if (Target == null || MeterSetPointsMethod == null)
        return false;

      MeterSetPointsMethod.Invoke(Target, new object[] { Points });
      return true;
    }

    /// <summary>
    /// 向指定角色的游戏内事件日志写入一条信息。
    /// </summary>
    public static void WriteInternalLog(object Engine, Character Character, Glyph Icon, string Text)
    {
      if (Engine == null || Character == null || string.IsNullOrEmpty(Text))
        return;
      if (EngineJournalProp == null || JournalWriteInternalEventMethod == null || LogWriteEventMethod == null)
        return;

      var Journal = EngineJournalProp.GetValue(Engine);
      if (Journal == null)
        return;

      Action<Actor> WriteAction = Actor =>
      {
        if (Actor?.Log != null)
          LogWriteEventMethod.Invoke(Actor.Log, new object[] { Icon, Text });
      };

      JournalWriteInternalEventMethod.Invoke(Journal, new object[] { Character, WriteAction });
    }
  }
}
