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
    private static readonly Type AttackCommandType = EngineAsm.GetType("Pathos.AttackCommand");

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

    private static readonly PropertyInfo AttackCmdDirectionProp =
      AttackCommandType?.GetProperty("Direction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo EngineHarmCharacterMethod =
      EngineType?.GetMethod("HarmCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Direction), typeof(Element), typeof(Dice), typeof(Modifier), typeof(string) }, null);

    private static readonly MethodInfo EngineStrikeCharacterMethod =
      EngineType?.GetMethod("StrikeCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Direction), typeof(int), typeof(Delay), typeof(string) }, null);

    private static readonly MethodInfo EngineHealCharacterIntMethod =
      EngineType?.GetMethod("HealCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Direction), typeof(int) }, null);

    private static readonly MethodInfo EngineTeleportCharacterMethod =
      EngineType?.GetMethod("TeleportCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Square), typeof(bool) }, null);

    private static readonly MethodInfo EngineRemoveTransientMethod =
      EngineType?.GetMethod("RemoveTransient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Property) }, null);

    private static readonly MethodInfo EngineApplyTransientMethod =
      EngineType?.GetMethod("ApplyTransient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Property), typeof(Dice) }, null);

    private static readonly MethodInfo EngineDismissAllyMethod =
      EngineType?.GetMethod("DismissAlly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo EngineKillCharacterMethod =
      EngineType?.GetMethod("KillCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(string) }, null);

    private static readonly MethodInfo MapRemoveCharacterMethod =
      typeof(Map).GetMethod("RemoveCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo CharacterSetDeceasedMethod =
      typeof(Character).GetMethod("SetDeceased", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(bool) }, null);

    private static readonly MethodInfo EngineGetMeleeDamageDiceMethod =
      EngineType?.GetMethod("GetMeleeDamageDice", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Asset) }, null);

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

    private static readonly MethodInfo CharacterHasTransientMethod =
      typeof(Character).GetMethod("HasTransient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Property) }, null);

    private static readonly MethodInfo CharacterIsAlliedMethod =
      typeof(Character).GetMethod("IsAllied", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Character) }, null);

    private static readonly MethodInfo CharacterIsTameAlliedMethod =
      typeof(Character).GetMethod("IsTameAllied", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Character) }, null);

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

    public static Direction GetAttackCommandDirection(object Command)
    {
      if (Command == null || AttackCmdDirectionProp == null)
        return default;
      var Val = AttackCmdDirectionProp.GetValue(Command);
      return Val is Direction Dir ? Dir : default;
    }

    public static void HarmCharacter(
      object Engine,
      Character Source,
      Character Target,
      Direction Direction,
      Element Element,
      Dice DamageDice,
      Modifier DecreaseTotal,
      string Cause)
    {
      if (Engine == null || Target == null || EngineHarmCharacterMethod == null)
        return;

      EngineHarmCharacterMethod.Invoke(Engine, new object[] { Source, Target, Direction, Element, DamageDice, DecreaseTotal, Cause });
    }

    public static void StrikeCharacter(
      object Engine,
      Character Attacker,
      Character Defender,
      Direction Direction,
      int DamageTotal,
      Delay DamageDelay,
      string Cause)
    {
      if (Engine == null || Defender == null || EngineStrikeCharacterMethod == null)
        return;

      EngineStrikeCharacterMethod.Invoke(Engine, new object[] { Attacker, Defender, Direction, DamageTotal, DamageDelay, Cause });
    }

    public static void HealCharacter(object Engine, Character Source, Character Target, Direction Direction, int LifePoints)
    {
      if (Engine == null || Target == null || LifePoints <= 0 || EngineHealCharacterIntMethod == null)
        return;

      EngineHealCharacterIntMethod.Invoke(Engine, new object[] { Source, Target, Direction, LifePoints });
    }

    public static void TeleportCharacter(object Engine, Character Character, Square TargetSquare, bool MasterMode = false)
    {
      if (Engine == null || Character == null || TargetSquare == null || EngineTeleportCharacterMethod == null)
        return;

      EngineTeleportCharacterMethod.Invoke(Engine, new object[] { Character, TargetSquare, MasterMode });
    }

    public static void RemoveTransient(object Engine, Character Remover, Character Target, Property Property)
    {
      if (Engine == null || Target == null || Property == null || EngineRemoveTransientMethod == null)
        return;

      EngineRemoveTransientMethod.Invoke(Engine, new object[] { Remover, Target, Property });
    }

    public static void ApplyTransient(object Engine, Character Applier, Character Target, Property Property, Dice Duration)
    {
      if (Engine == null || Target == null || Property == null || EngineApplyTransientMethod == null)
        return;

      EngineApplyTransientMethod.Invoke(Engine, new object[] { Applier, Target, Property, Duration });
    }

    public static void DismissAlly(object Engine, Character Ally)
    {
      if (Engine == null || Ally == null || EngineDismissAllyMethod == null)
        return;

      EngineDismissAllyMethod.Invoke(Engine, new object[] { Ally });
    }

    /// <summary>
    /// 彻底销毁/消散召唤物（到期或被新召唤物替换时使用）：
    /// 以无攻击者（null）结算死亡并从地图移除，避免像 DismissAlly 那样残留中立/野生单位在场上，
    /// 同时不会触发玩家误杀盟友的运程（Karma）惩罚。
    /// </summary>
    public static void DespawnSummon(object Engine, Character Summon, string Cause = "expired")
    {
      if (Summon == null || Summon.Deceased)
        return;

      if (Engine != null && EngineKillCharacterMethod != null)
      {
        EngineKillCharacterMethod.Invoke(Engine, new object[] { null, Summon, Cause });
        if (Summon.Deceased && Summon.Square == null)
          return;
      }

      // 兜底：若仍残留在地图上，强制移出地图并标记死亡
      var Map = Summon.Square?.Map;
      if (Map != null && MapRemoveCharacterMethod != null)
        MapRemoveCharacterMethod.Invoke(Map, new object[] { Summon });

      CharacterSetDeceasedMethod?.Invoke(Summon, new object[] { true });
    }

    public static Dice GetMeleeDamageDice(object Engine, Character Character, Asset WeaponAsset)
    {
      if (Engine == null || Character == null || EngineGetMeleeDamageDiceMethod == null)
        return 1.d8();

      var Result = EngineGetMeleeDamageDiceMethod.Invoke(Engine, new object[] { Character, WeaponAsset });
      return Result is Dice D && !D.IsZero() ? D : 1.d8();
    }

    public static bool HasTransient(Character Character, Property Property)
    {
      if (Character == null || Property == null || CharacterHasTransientMethod == null)
        return false;

      return CharacterHasTransientMethod.Invoke(Character, new object[] { Property }) is bool B && B;
    }

    public static bool IsAllied(Character Source, Character Target)
    {
      if (Source == null || Target == null)
        return false;
      if (Source == Target)
        return true;

      if (CharacterIsAlliedMethod != null &&
          CharacterIsAlliedMethod.Invoke(Source, new object[] { Target }) is bool Allied &&
          Allied)
        return true;

      if (CharacterIsTameAlliedMethod != null &&
          CharacterIsTameAlliedMethod.Invoke(Source, new object[] { Target }) is bool TameAllied &&
          TameAllied)
        return true;

      return false;
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
