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

    private static readonly FieldInfo CastRangeDiceBackingField =
      typeof(Cast).GetField("<RangeDice>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo CharacterGetLimitFormMethod =
      typeof(Character).GetMethod("GetLimitForm", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    private static readonly MethodInfo CharacterGetQualificationMethod =
      typeof(Character).GetMethod("GetQualification", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Skill) }, null);

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
    /// 临时设置 Cast.RangeDice（配合 Postfix 恢复，用于实现动态施法距离加成）。
    /// </summary>
    public static bool SetCastRangeDice(Cast Target, Dice RangeDice)
    {
      if (Target == null || CastRangeDiceBackingField == null)
        return false;

      CastRangeDiceBackingField.SetValue(Target, RangeDice);
      return true;
    }

    /// <summary>
    /// 按名称获取角色 CurrentForm 中的某项属性分值（未找到返回 0）。
    /// </summary>
    public static int GetCurrentAbilityScore(Character Character, string AttributeName)
    {
      var Ability = FindAbility(Character?.CurrentForm, AttributeName);
      return Ability?.Score ?? 0;
    }

    /// <summary>
    /// 按名称获取角色 LimitForm 中的某项属性上限值（未找到返回 25）。
    /// </summary>
    public static int GetLimitAbilityScore(Character Character, string AttributeName)
    {
      if (Character == null || CharacterGetLimitFormMethod == null)
        return 25;

      var LimitForm = CharacterGetLimitFormMethod.Invoke(Character, null) as Form;
      var Ability = FindAbility(LimitForm, AttributeName);
      return Ability?.Score ?? 25;
    }

    /// <summary>
    /// 获取角色在指定技能上的熟练度等级（Qualification）。
    /// </summary>
    public static Qualification GetQualification(Character Character, Skill Skill)
    {
      if (Character == null || Skill == null || CharacterGetQualificationMethod == null)
        return null;

      return CharacterGetQualificationMethod.Invoke(Character, new object[] { Skill }) as Qualification;
    }

    /// <summary>
    /// 同步设置角色某项属性在 CurrentForm 与 MaximumForm 中的分值，
    /// 确保总量守恒且复活/恢复属性药水不会白嫖回满。
    /// </summary>
    public static bool SetPermanentAbilityScore(Character Character, string AttributeName, int NewScore)
    {
      if (Character?.CurrentForm == null || Character.MaximumForm == null)
        return false;

      var CurAbility = FindAbility(Character.CurrentForm, AttributeName);
      if (CurAbility == null)
        return false;

      var MaxAbility = Character.MaximumForm[CurAbility.Attribute];
      bool Ok = SetAbilityScore(CurAbility, NewScore);
      if (MaxAbility != null)
        Ok &= SetAbilityScore(MaxAbility, NewScore);
      return Ok;
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
    /// 将指定属性在 MaximumForm 中的基准值同步为 CurrentForm 当前值。
    /// </summary>
    public static void SyncMaximumFormScores(Character Character, params string[] AttributeNames)
    {
      if (Character?.CurrentForm == null || Character.MaximumForm == null || AttributeNames == null || AttributeNames.Length == 0)
        return;

      foreach (var Name in AttributeNames)
      {
        var CurAbility = FindAbility(Character.CurrentForm, Name);
        if (CurAbility == null)
          continue;

        var MaxAbility = Character.MaximumForm[CurAbility.Attribute];
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

    private static Ability FindAbility(Form Form, string AttributeName)
    {
      if (Form == null || string.IsNullOrEmpty(AttributeName))
        return null;

      foreach (var Ability in Form.Abilities)
      {
        if (Ability.Attribute != null &&
            string.Equals(Ability.Attribute.Name, AttributeName, StringComparison.OrdinalIgnoreCase))
          return Ability;
      }

      return null;
    }
  }
}
