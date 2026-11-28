using System;
using System.Reflection;
using Inv;

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
    private static readonly Type UseAssetCommandType = EngineAsm.GetType("Pathos.UseAssetCommand");
    private static readonly Type GeneratorType = EngineAsm.GetType("Pathos.Generator");
    private static readonly Type MasteryType = EngineAsm.GetType("Pathos.Mastery");
    private static readonly Type KnowledgeType = EngineAsm.GetType("Pathos.Knowledge");
    private static readonly Type StoryType = EngineAsm.GetType("Pathos.Story");
    private static readonly Type DecisionType = EngineAsm.GetType("Pathos.Decision");
    private static readonly Type InteractionType = EngineAsm.GetType("Pathos.Interaction");
    private static readonly Type SummonEntityEffectType = EngineAsm.GetType("Pathos.SummonEntityEffect");
    private static readonly Type TranslationType = EngineAsm.GetType("Pathos.Translation");
    private static readonly Type ConductType = EngineAsm.GetType("Pathos.Conduct");

    private static readonly PropertyInfo ActorConductProp =
      typeof(Actor).GetProperty("Conduct", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo ConductStartingMethod =
      ConductType?.GetMethod("Starting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        Type.EmptyTypes, null);

    private static readonly MethodInfo TranslationGetPropertyNameMethod =
      TranslationType?.GetMethod("GetName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Property) }, null);

    private static readonly MethodInfo TranslationGetEntityNameMethod =
      TranslationType?.GetMethod("GetName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Entity) }, null);

    private static readonly MethodInfo TranslationGetSpellNameMethod =
      TranslationType?.GetMethod("GetName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Spell) }, null);

    private static readonly MethodInfo TranslationGetSpellDescriptionMethod =
      TranslationType?.GetMethod("GetDescription", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Spell) }, null);

    private static readonly ConstructorInfo InteractionSourceSquareCtor =
      InteractionType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Square), typeof(string) }, null);

    private static readonly PropertyInfo InteractionSourceCharacterProp =
      InteractionType?.GetProperty("SourceCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo InteractionTargetCharacterProp =
      InteractionType?.GetProperty("TargetCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo EngineGetAttackDelayMethod =
      EngineType?.GetMethod("GetAttackDelay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo EngineGetMoveDelayMethod =
      EngineType?.GetMethod("GetMoveDelay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo EngineSetBusyMethod =
      EngineType?.GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Delay) }, null);

    private static readonly ConstructorInfo SummonEntityEffectCtor =
      SummonEntityEffectType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        Type.EmptyTypes, null);

    private static readonly PropertyInfo SummonEffectCountProp =
      SummonEntityEffectType?.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo SummonEffectEntityArrayProp =
      SummonEntityEffectType?.GetProperty("EntityArray", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo SummonEffectConstructedProp =
      SummonEntityEffectType?.GetProperty("Constructed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo EngineApplyEffectsMethod =
      EngineType?.GetMethod("ApplyEffects", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { InteractionType, typeof(System.Collections.Generic.IEnumerable<Effect>) }, null);

    private static readonly PropertyInfo CharacterDecisionProp =
      typeof(Character).GetProperty("Decision", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo DecisionWaitMethod =
      DecisionType?.GetMethod("Wait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    private static readonly PropertyInfo EngineStoryProp =
      EngineType?.GetProperty("Story", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo StoryExplodingMethod =
      StoryType?.GetMethod("Exploding", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Square), typeof(Explosion) }, null);

    private static readonly MethodInfo StoryBeamingMethod =
      StoryType?.GetMethod("Beaming", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Square), typeof(Direction), typeof(Beam) }, null);

    private static readonly MethodInfo StoryStrikingMethod =
      StoryType?.GetMethod("Striking", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Square), typeof(Strike) }, null);

    private static readonly MethodInfo StoryDetectingMethod =
      StoryType?.GetMethod("Detecting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Square), typeof(int), typeof(Colour) }, null);

    private static readonly MethodInfo StorySigningMethod =
      StoryType?.GetMethod("Signing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Square), typeof(Glyph), typeof(Colour), typeof(bool) }, null);

    private static readonly MethodInfo StoryTeleportingMethod =
      StoryType?.GetMethod("Teleporting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Character) }, null);

    private static readonly MethodInfo StoryAttackingMethod =
      StoryType?.GetMethod("Attacking", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Character), typeof(Direction) }, null);

    private static readonly MethodInfo StoryFlingingMethod =
      StoryType?.GetMethod("Flinging", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Delay), typeof(Square), typeof(Square), typeof(Character), typeof(Colour) }, null);

    private static readonly MethodInfo StoryCancelMethod =
      StoryType?.GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo StoryCaptionMethod =
      StoryType?.GetMethod("Caption", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Square), typeof(Colour), typeof(string), typeof(bool) }, null);

    private static readonly MethodInfo StoryEmittingMethod =
      StoryType?.GetMethod("Emitting", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Square), typeof(Sonic), typeof(int), typeof(bool), typeof(bool) }, null);

    private static readonly FieldInfo DirectorEngineField =
      DirectorType?.GetField("Engine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo GeneratorEngineField =
      GeneratorType?.GetField("Engine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo MasterySpellProp =
      MasteryType?.GetProperty("Spell", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo MasteryLevelProp =
      MasteryType?.GetProperty("Level", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo CharacterKnowledgeProp =
      typeof(Character).GetProperty("Knowledge", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo KnowledgeLearnSpellMethod =
      KnowledgeType?.GetMethod("LearnSpell", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Spell), typeof(int) }, null);

    private static readonly MethodInfo CharacterGetMasteryMethod =
      typeof(Character).GetMethod("GetMastery", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Spell) }, null);

    private static readonly MethodInfo CharacterSpellCostMethod =
      MasteryType == null ? null : typeof(Character).GetMethod("SpellCost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { MasteryType, typeof(Qualification) }, null);

    private static readonly MethodInfo EngineGainTalentMethod =
      EngineType?.GetMethod("GainTalent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Property) }, null);

    private static readonly MethodInfo CharacterHasAcquiredTalentMethod =
      typeof(Character).GetMethod("HasAcquiredTalent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Property) }, null);

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

    private static readonly PropertyInfo UseAssetCmdAssetProp =
      UseAssetCommandType?.GetProperty("Asset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo UseAssetCmdMotionProp =
      UseAssetCommandType?.GetProperty("Motion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo EngineGeneratorProp =
      EngineType?.GetProperty("Generator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo GeneratorPlaceSpecificAssetMethod =
      GeneratorType?.GetMethod("PlaceSpecificAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Square), typeof(Item), typeof(int?) }, null);

    private static readonly MethodInfo EngineLoseCarriedAssetMethod =
      EngineType?.GetMethod("LoseCarriedAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Asset) }, null);

    private static readonly MethodInfo EngineRemoveEquipmentAssetMethod =
      EngineType?.GetMethod("RemoveEquipmentAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Equipment), typeof(int) }, null);

    private static readonly ConstructorInfo AttackCmdDirectionCtor =
      AttackCommandType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Direction) }, null);

    private static readonly MethodInfo DirectorAttackMethod =
      DirectorType?.GetMethod("Attack", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), AttackCommandType }, null);

    private static readonly MethodInfo EngineGetDefenceClassMethod =
      EngineType?.GetMethod("GetDefenceClass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(DamageType?) }, null);

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

    private static readonly MethodInfo EngineMoveCharacterMethod =
      EngineType?.GetMethod("MoveCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Square) }, null);

    private static readonly MethodInfo EngineUnstuckCharacterMethod =
      EngineType?.GetMethod("UnstuckCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo EngineRevealCharacterMethod =
      EngineType?.GetMethod("RevealCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo EngineRemoveTransientMethod =
      EngineType?.GetMethod("RemoveTransient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Property) }, null);

    private static readonly MethodInfo EngineApplyTransientMethod =
      EngineType?.GetMethod("ApplyTransient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character), typeof(Property), typeof(Dice) }, null);

    private static readonly MethodInfo EngineDismissAllyMethod =
      EngineType?.GetMethod("DismissAlly", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character) }, null);

    private static readonly MethodInfo EngineImpersonateCharacterMethod =
      EngineType?.GetMethod("ImpersonateCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character) }, null);

    private static readonly MethodInfo EnginePolymorphCharacterMethod =
      EngineType?.GetMethod("PolymorphCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Entity) }, null);

    private static readonly MethodInfo EngineUnpolymorphCharacterMethod =
      EngineType?.GetMethod("UnpolymorphCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(bool) }, null);

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

    private static readonly MethodInfo CharacterIsPolymorphedMethod =
      typeof(Character).GetMethod("IsPolymorphed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    private static readonly ConstructorInfo FormDefaultCtor =
      typeof(Form).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    private static readonly MethodInfo FormAssignMethod =
      typeof(Form).GetMethod("Assign", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Form) }, null);

    private static readonly MethodInfo CharacterGetQualificationMethod =
      typeof(Character).GetMethod("GetQualification", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Skill) }, null);

    private static readonly MethodInfo CharacterHasTransientMethod =
      typeof(Character).GetMethod("HasTransient", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Property) }, null);

    private static readonly MethodInfo CharacterIsAlliedMethod =
      typeof(Character).GetMethod("IsAllied", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Character) }, null);

    private static readonly MethodInfo CharacterIsTameAlliedMethod =
      typeof(Character).GetMethod("IsTameAllied", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Character) }, null);

    private static readonly PropertyInfo CharacterActorProp =
      typeof(Character).GetProperty("Actor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly PropertyInfo ActorPlayerProp =
      typeof(Actor).GetProperty("Player", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly Type PlayerType = EngineAsm.GetType("Pathos.Player");
    private static readonly Type MemoryType = EngineAsm.GetType("Pathos.Memory");

    private static readonly MethodInfo PlayerGetMemoryMethod =
      PlayerType?.GetMethod("GetMemory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Map) }, null);

    private static readonly MethodInfo MemoryHasLocationMethod =
      MemoryType?.GetMethod("HasLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Square) }, null);

    private static readonly MethodInfo SquareIsObservableMethod =
      typeof(Square).GetMethod("IsObservable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Square) }, null);

    private static readonly MethodInfo EngineIsObservableCharacterMethod =
      EngineType?.GetMethod("IsObservableCharacter", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new[] { typeof(Character), typeof(Character) }, null);

    public static object GetDirectorEngine(object Director)
    {
      return Director != null && DirectorEngineField != null ? DirectorEngineField.GetValue(Director) : null;
    }

    public static object GetGeneratorEngine(object Generator)
    {
      return Generator != null && GeneratorEngineField != null ? GeneratorEngineField.GetValue(Generator) : null;
    }

    public static Spell GetMasterySpell(object Mastery)
    {
      return Mastery != null && MasterySpellProp != null ? MasterySpellProp.GetValue(Mastery) as Spell : null;
    }

    /// <summary>
    /// 通过 Pathos 原生 Character.Knowledge.LearnSpell(Spell, 1) 直接学习或升星指定法术：
    /// 首次调用为角色学会该法术（Mastery.Level = 1，0 星），后续每次调用自动递增 Level（1★ / 2★ / 3★...）并随存档持久化。
    /// 直接操作 Knowledge 可绕过 Engine.LearnSpell 的「读魔法书失败惩罚」（低智力/低等级时触发幻觉/困惑等）。
    /// </summary>
    public static bool LearnSpell(object Engine, Character Character, Spell Spell)
    {
      if (Character == null || Spell == null || CharacterKnowledgeProp == null || KnowledgeLearnSpellMethod == null)
        return false;

      try
      {
        var Knowledge = CharacterKnowledgeProp.GetValue(Character);
        if (Knowledge == null)
          return false;

        KnowledgeLearnSpellMethod.Invoke(Knowledge, new object[] { Spell, 1 });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"LearnSpell failed: {Ex}");
        return false;
      }
    }

    /// <summary>
    /// 查询角色当前在指定法术上的原生 Mastery.Level（未学会返回 0；学会 0 星返回 1；1★ 返回 2；2★ 返回 3...）。
    /// </summary>
    public static int GetSpellMasteryLevel(Character Character, Spell Spell)
    {
      if (Character == null || Spell == null || CharacterGetMasteryMethod == null || MasteryLevelProp == null)
        return 0;

      try
      {
        var Mastery = CharacterGetMasteryMethod.Invoke(Character, new object[] { Spell });
        if (Mastery == null)
          return 0;

        return MasteryLevelProp.GetValue(Mastery) is int Level ? Level : 0;
      }
      catch
      {
        return 0;
      }
    }

    /// <summary>
    /// 查询角色施放指定法术的真实蓝耗（原生 Character.SpellCost(Mastery, Qualification)，含 SpellCostQuery Hook 的天赋减耗），
    /// 与施法实际扣蓝、原生法术面板显示保持一致。
    /// 未学会该法术（无 Mastery）或反射失败时，回退为以 Spell.Mana 为基准直接派发 SpellCostQuery。
    /// </summary>
    public static int GetSpellManaCost(Character Character, Spell Spell, Skill Skill)
    {
      if (Character == null || Spell == null)
        return Spell?.Mana ?? 0;

      var Qualification = GetQualification(Character, Skill);
      try
      {
        var Mastery = CharacterGetMasteryMethod?.Invoke(Character, new object[] { Spell });
        if (Mastery != null && CharacterSpellCostMethod != null &&
            CharacterSpellCostMethod.Invoke(Character, new[] { Mastery, Qualification }) is int Cost)
          return Cost;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteDebug($"GetSpellManaCost('{Spell.Name}') fell back to SpellCostQuery: {Ex.Message}");
      }

      var Args = new SpellCostQueryEventArgs(Character, Spell, null, Qualification, Spell.Mana);
      DotaHookBus.DispatchSpellCostQuery(Args);
      return Math.Max(0, Args.ManaCost);
    }

    /// <summary>
    /// 通过 Pathos 原生 Engine.GainTalent 为角色授予永久专长标记（自动随存档保存与加载）。
    /// </summary>
    public static bool GainTalent(object Engine, Character Character, Property Property)
    {
      if (Engine == null || Character == null || Property == null || EngineGainTalentMethod == null)
        return false;

      try
      {
        EngineGainTalentMethod.Invoke(Engine, new object[] { Character, Character, Property });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"GainTalent failed: {Ex}");
        return false;
      }
    }

    /// <summary>
    /// 检查角色是否已在原生 AcquiredTalentSet 中拥有指定专长标记。
    /// </summary>
    public static bool HasAcquiredTalent(Character Character, Property Property)
    {
      if (Character == null || Property == null || CharacterHasAcquiredTalentMethod == null)
        return false;

      try
      {
        return CharacterHasAcquiredTalentMethod.Invoke(Character, new object[] { Property }) is bool B && B;
      }
      catch
      {
        return false;
      }
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

    public static bool IsAttackCommand(object Command)
    {
      return Command != null && AttackCommandType != null && AttackCommandType.IsInstanceOfType(Command);
    }

    /// <summary>
    /// 强制指定角色通过原生 Director.Attack 向指定方向发起一次近战攻击（用于狂战士之吼嘲讽重定向）。
    /// </summary>
    public static bool ForceDirectorAttack(object Director, Character Attacker, Direction Direction)
    {
      if (Director == null || Attacker == null || DirectorAttackMethod == null || AttackCmdDirectionCtor == null)
        return false;

      try
      {
        var Cmd = AttackCmdDirectionCtor.Invoke(new object[] { Direction });
        DirectorAttackMethod.Invoke(Director, new object[] { Attacker, Cmd });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"ForceDirectorAttack failed: {Ex}");
        return false;
      }
    }

    /// <summary>
    /// 查询角色当前总防御值（Defence），包含装备、敏捷与 Hook 加甲。
    /// </summary>
    public static int GetDefenceClass(object Engine, Character Character)
    {
      if (Engine == null || Character == null || EngineGetDefenceClassMethod == null)
        return 10;

      try
      {
        return EngineGetDefenceClassMethod.Invoke(Engine, new object[] { Character, null }) is int Def ? Def : 10;
      }
      catch
      {
        return 10;
      }
    }

    /// <summary>
    /// 通过原生 Engine.KillCharacter 直接斩杀目标（用于淘汰之刃低于斩杀线时的无视减伤处决）。
    /// </summary>
    public static bool KillCharacter(object Engine, Character Attacker, Character Defender, string Cause)
    {
      if (Engine == null || Defender == null || Defender.Deceased || EngineKillCharacterMethod == null)
        return false;

      try
      {
        EngineKillCharacterMethod.Invoke(Engine, new object[] { Attacker, Defender, Cause });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"KillCharacter failed: {Ex}");
        return false;
      }
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

    /// <summary>
    /// 静音瞬移/突进位移（用于无敌斩、波浪形态等高频或专属特效位移）：
    /// 直接调用 Engine.MoveCharacter + UnstuckCharacter + RevealCharacter，
    /// 避免触发 Engine.TeleportCharacter 硬编码的刺耳传送音效（Manifest.Sonics.Teleport）与传送闪烁动画。
    /// </summary>
    public static void DashCharacter(object Engine, Character Character, Square TargetSquare)
    {
      if (Engine == null || Character == null || TargetSquare == null || Character.Deceased)
        return;

      if (EngineMoveCharacterMethod != null)
      {
        EngineMoveCharacterMethod.Invoke(Engine, new object[] { Character, TargetSquare });
        EngineUnstuckCharacterMethod?.Invoke(Engine, new object[] { Character });
        EngineRevealCharacterMethod?.Invoke(Engine, new object[] { Character });
      }
      else
      {
        TeleportCharacter(Engine, Character, TargetSquare, MasterMode: false);
      }
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

    public static bool ImpersonateCharacter(object Engine, Character Character, Character Target)
    {
      if (Engine == null || Character == null || Target == null || EngineImpersonateCharacterMethod == null)
        return false;

      try
      {
        EngineImpersonateCharacterMethod.Invoke(Engine, new object[] { Character, Target });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"ImpersonateCharacter failed: {Ex}");
        return false;
      }
    }

    public static bool PolymorphCharacter(object Engine, Character Character, Entity Entity)
    {
      if (Engine == null || Character == null || Entity == null || EnginePolymorphCharacterMethod == null)
        return false;

      try
      {
        EnginePolymorphCharacterMethod.Invoke(Engine, new object[] { Character, Entity });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"PolymorphCharacter failed: {Ex}");
        return false;
      }
    }

    public static bool UnpolymorphCharacter(object Engine, Character Character, bool Restore = false)
    {
      if (Engine == null || Character == null || EngineUnpolymorphCharacterMethod == null)
        return false;

      try
      {
        EngineUnpolymorphCharacterMethod.Invoke(Engine, new object[] { Character, Restore });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"UnpolymorphCharacter failed: {Ex}");
        return false;
      }
    }

    /// <summary>
    /// 通过 Pathos 原生 Reactor.SummonEntity 管线在指定格子精确召唤 1 个友方随从
    /// （避免 Plain(Range) 射线在路径每个空格上重复执行 SummonEntity 导致一次召唤多个）。
    /// </summary>
    public static Character SummonAllyEntity(
      object Engine,
      Character Owner,
      Square TargetSquare,
      Entity Entity,
      bool Constructed = true,
      string Cause = null)
    {
      if (Engine == null || Owner == null || TargetSquare == null || Entity == null)
        return null;
      if (InteractionSourceSquareCtor == null || SummonEntityEffectCtor == null || EngineApplyEffectsMethod == null)
        return null;

      try
      {
        var BeforeSet = new System.Collections.Generic.HashSet<Character>();
        if (TargetSquare.Character != null)
          BeforeSet.Add(TargetSquare.Character);
        foreach (var Sq in TargetSquare.GetAroundSquares(3))
        {
          if (Sq?.Character != null)
            BeforeSet.Add(Sq.Character);
        }

        var Effect = (Effect)SummonEntityEffectCtor.Invoke(null);
        SummonEffectCountProp?.SetValue(Effect, Dice.One);
        SummonEffectConstructedProp?.SetValue(Effect, Constructed);
        SummonEffectEntityArrayProp?.SetValue(Effect, new[] { Entity });

        var Interaction = InteractionSourceSquareCtor.Invoke(new object[] { Owner, TargetSquare, Cause });
        EngineApplyEffectsMethod.Invoke(Engine, new object[] { Interaction, new[] { Effect } });

        if (TargetSquare.Character != null &&
            TargetSquare.Character.Entity == Entity &&
            !BeforeSet.Contains(TargetSquare.Character))
          return TargetSquare.Character;

        foreach (var Sq in TargetSquare.GetAroundSquares(3))
        {
          var Cand = Sq?.Character;
          if (Cand != null && Cand.Entity == Entity && !BeforeSet.Contains(Cand))
            return Cand;
        }
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"SummonAllyEntity failed: {Ex}");
      }

      return null;
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
    /// 判断指定格子是否已被玩家探索过（存在于 Player.GetMemory(Map).HasLocation(Square) 中，或当前视线可见）。
    /// 未探索的黑色战争迷雾区域返回 false，彻底防止技能指示器泄露未探索地形。
    /// </summary>
    public static bool IsSquareExplored(Character Viewer, Square Square)
    {
      if (Square == null)
        return false;

      if (Viewer?.Square != null && IsSquareObservable(Viewer.Square, Square))
        return true;

      if (Viewer == null || Square.Map == null || CharacterActorProp == null || ActorPlayerProp == null ||
          PlayerGetMemoryMethod == null || MemoryHasLocationMethod == null)
        return true;

      try
      {
        var Actor = CharacterActorProp.GetValue(Viewer);
        var Player = Actor != null ? ActorPlayerProp.GetValue(Actor) : null;
        var Memory = Player != null ? PlayerGetMemoryMethod.Invoke(Player, new object[] { Square.Map }) : null;
        if (Memory == null)
          return false;

        return MemoryHasLocationMethod.Invoke(Memory, new object[] { Square }) is bool HasLoc && HasLoc;
      }
      catch
      {
        return true;
      }
    }

    /// <summary>
    /// 判断目标格对施法者所在格是否视线通畅（未被墙体/闭门阻挡）。
    /// </summary>
    public static bool IsSquareObservable(Square FromSquare, Square TargetSquare)
    {
      if (FromSquare == null || TargetSquare == null)
        return false;
      if (FromSquare == TargetSquare)
        return true;
      if (SquareIsObservableMethod == null)
        return true;

      return SquareIsObservableMethod.Invoke(TargetSquare, new object[] { FromSquare }) is bool B && B;
    }

    /// <summary>
    /// 通过 Pathos 原生 Engine.IsObservableCharacter 判断观察者当前是否能看见目标角色
    /// （综合视线、黑暗、隐身与心灵感应等原生视野规则）。
    /// </summary>
    public static bool IsObservableCharacter(object Engine, Character Viewer, Character Target)
    {
      if (Viewer == null || Target == null)
        return false;
      if (Viewer == Target)
        return true;

      if (Engine != null && EngineIsObservableCharacterMethod != null)
      {
        try
        {
          return EngineIsObservableCharacterMethod.Invoke(Engine, new object[] { Viewer, Target }) is bool B && B;
        }
        catch
        {
        }
      }

      return IsSquareObservable(Viewer.Square, Target.Square);
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
    /// 判断角色当前是否处于变形/变身状态（如变形术、水人 Morph）。
    /// </summary>
    public static bool IsPolymorphed(Character Character)
    {
      if (Character == null || CharacterIsPolymorphedMethod == null)
        return false;

      return CharacterIsPolymorphedMethod.Invoke(Character, null) is bool B && B;
    }

    /// <summary>
    /// 基于现有 Form 克隆出一份独立 Form 并覆写六维属性分值（绝不修改原模板 Form）。
    /// </summary>
    public static Form CloneFormWithScores(Form TemplateForm, int Str, int Dex, int Con, int Int, int Wis, int Cha)
    {
      if (TemplateForm == null || FormDefaultCtor == null || FormAssignMethod == null)
        return TemplateForm;

      try
      {
        var Cloned = (Form)FormDefaultCtor.Invoke(null);
        FormAssignMethod.Invoke(Cloned, new object[] { TemplateForm });

        SetAbilityScore(FindAbility(Cloned, "strength"), Str);
        SetAbilityScore(FindAbility(Cloned, "dexterity"), Dex);
        SetAbilityScore(FindAbility(Cloned, "constitution"), Con);
        SetAbilityScore(FindAbility(Cloned, "intelligence"), Int);
        SetAbilityScore(FindAbility(Cloned, "wisdom"), Wis);
        SetAbilityScore(FindAbility(Cloned, "charisma"), Cha);
        return Cloned;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"CloneFormWithScores failed: {Ex}");
        return TemplateForm;
      }
    }

    /// <summary>
    /// 基于现有 Form 克隆出一份独立 Form 并叠加六维属性增量（绝不修改原 Form）。
    /// </summary>
    public static Form CloneFormWithDeltas(Form TemplateForm, int BonusStr, int BonusDex, int BonusCon, int BonusInt, int BonusWis, int BonusCha)
    {
      if (TemplateForm == null || FormDefaultCtor == null || FormAssignMethod == null)
        return TemplateForm;

      try
      {
        var Cloned = (Form)FormDefaultCtor.Invoke(null);
        FormAssignMethod.Invoke(Cloned, new object[] { TemplateForm });

        ApplyAbilityDelta(Cloned, "strength", BonusStr);
        ApplyAbilityDelta(Cloned, "dexterity", BonusDex);
        ApplyAbilityDelta(Cloned, "constitution", BonusCon);
        ApplyAbilityDelta(Cloned, "intelligence", BonusInt);
        ApplyAbilityDelta(Cloned, "wisdom", BonusWis);
        ApplyAbilityDelta(Cloned, "charisma", BonusCha);
        return Cloned;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"CloneFormWithDeltas failed: {Ex}");
        return TemplateForm;
      }
    }

    private static void ApplyAbilityDelta(Form Form, string AttributeName, int Delta)
    {
      if (Delta == 0)
        return;

      var Ability = FindAbility(Form, AttributeName);
      if (Ability != null)
        SetAbilityScore(Ability, Math.Max(1, Ability.Score + Delta));
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
    /// 重新同步 Actor.Conduct 的开局基准（StartingLife / StartingMana / StartingAttributes / StartingSpells 等）。
    /// 在 CreateActor / RerollActor 修改固定六维与初始资源后必须调用，避免结算名望时因差值出现巨额负分。
    /// </summary>
    public static bool RefreshConductStarting(Actor Actor)
    {
      if (Actor == null || ActorConductProp == null || ConductStartingMethod == null)
        return false;

      var Conduct = ActorConductProp.GetValue(Actor);
      if (Conduct == null)
        return false;

      ConductStartingMethod.Invoke(Conduct, null);
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

    /// <summary>
    /// 调用 Pathos 原生 Story.Exploding 播放 3×3 贴图爆炸特效。
    /// 注意：Delay 为动画持续帧数（60 帧 = 1 秒，原版爆炸默认 Delay.HalfSecond = 30 帧），传 Delay.Zero 会瞬间消失。
    /// </summary>
    public static void StoryExploding(object Engine, Delay Delay, Square Square, Explosion Explosion)
    {
      if (Engine == null || Square == null || Explosion == null || EngineStoryProp == null || StoryExplodingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryExplodingMethod.Invoke(Story, new object[] { Delay, Square, Explosion });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Beaming 播放方向光束/残影连线贴图特效。
    /// 注意：Delay 为动画持续帧数（60 帧 = 1 秒，建议 Delay.HalfSecond 起步并沿射线逐格递增），传 Delay.Zero 会瞬间消失。
    /// </summary>
    public static void StoryBeaming(object Engine, Delay Delay, Square Square, Direction Direction, Beam Beam)
    {
      if (Engine == null || Square == null || Beam == null || EngineStoryProp == null || StoryBeamingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryBeamingMethod.Invoke(Story, new object[] { Delay, Square, Direction, Beam });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Striking 播放单格贴图打击/斩击特效。
    /// 注意：Delay 为动画持续帧数（60 帧 = 1 秒，原版打击默认 Delay.HalfSecond = 30 帧），传 Delay.Zero 会瞬间消失且无缩放渐隐。
    /// </summary>
    public static void StoryStriking(object Engine, Delay Delay, Square Square, Strike Strike)
    {
      if (Engine == null || Square == null || Strike == null || EngineStoryProp == null || StoryStrikingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryStrikingMethod.Invoke(Story, new object[] { Delay, Square, Strike });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Detecting 播放范围脉冲扩散特效。
    /// </summary>
    public static void StoryDetecting(object Engine, Square Square, int Distance, Colour Colour)
    {
      if (Engine == null || Square == null || Distance <= 0 || EngineStoryProp == null || StoryDetectingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryDetectingMethod.Invoke(Story, new object[] { Square, Distance, Colour });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Signing 播放法术符文/贴图动画。
    /// </summary>
    public static void StorySigning(object Engine, Delay Delay, Square Square, Glyph Glyph, Colour Colour, bool Forward = true)
    {
      if (Engine == null || Square == null || Glyph == null || EngineStoryProp == null || StorySigningMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StorySigningMethod.Invoke(Story, new object[] { Delay, Square, Glyph, Colour, Forward });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Teleporting 播放瞬移闪烁动画。
    /// </summary>
    public static void StoryTeleporting(object Engine, Delay Delay, Character Character)
    {
      if (Engine == null || Character == null || EngineStoryProp == null || StoryTeleportingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryTeleportingMethod.Invoke(Story, new object[] { Delay, Character });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Attacking 播放角色挥砍/突刺动作动画。
    /// </summary>
    public static void StoryAttacking(object Engine, Delay Delay, Character Character, Direction Direction)
    {
      if (Engine == null || Character == null || EngineStoryProp == null || StoryAttackingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryAttackingMethod.Invoke(Story, new object[] { Delay, Character, Direction });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Flinging：角色贴图在 Delay 帧内从 From 平滑飞到 To（逻辑位置应已在 To）。
    /// 用于突进/波浪形态等「有过程」的位移演出；会先取消该角色正在播放的其他角色动画，避免重影。
    /// </summary>
    public static void StoryFlinging(object Engine, Delay Delay, Square From, Square To, Character Character, Colour Colour = null)
    {
      if (Engine == null || Character == null || From == null || To == null || From == To ||
          EngineStoryProp == null || StoryFlingingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story == null)
        return;

      StoryCancelMethod?.Invoke(Story, new object[] { Character });
      StoryFlingingMethod.Invoke(Story, new object[] { Delay, From, To, Character, Colour });
    }

    /// <summary>
    /// 取消某角色正在播放的全部角色动画（移动/挥砍/飞行），用于连续动作之间避免同一角色被画两次。
    /// </summary>
    public static void StoryCancelCharacter(object Engine, Character Character)
    {
      if (Engine == null || Character == null || EngineStoryProp == null || StoryCancelMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryCancelMethod.Invoke(Story, new object[] { Character });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Caption 在格子上方弹出原生飘字（如 CRIT!）。
    /// </summary>
    public static void StoryCaption(object Engine, Square Square, Colour Colour, string Text, bool Delayed = false)
    {
      if (Engine == null || Square == null || string.IsNullOrEmpty(Text) || EngineStoryProp == null || StoryCaptionMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryCaptionMethod.Invoke(Story, new object[] { Square, Colour, Text, Delayed });
    }

    /// <summary>
    /// 调用 Pathos 原生 Story.Emitting 在指定格子播放指定 Sonic 音效。
    /// </summary>
    public static void StoryEmitting(
      object Engine,
      Square Square,
      Sonic Sonic,
      int Audibility = 10,
      bool Delayed = false,
      bool Exclamation = false)
    {
      if (Engine == null || Square == null || Sonic == null || EngineStoryProp == null || StoryEmittingMethod == null)
        return;

      var Story = EngineStoryProp.GetValue(Engine);
      if (Story != null)
        StoryEmittingMethod.Invoke(Story, new object[] { Square, Sonic, Audibility, Delayed, Exclamation });
    }

    /// <summary>
    /// 为角色排入下一回合的自动 Wait 指令（用于无敌斩等多回合自动连斩技能，期间自动推进回合无需玩家手动操作）。
    /// </summary>
    public static void QueueWaitDecision(Character Character)
    {
      if (Character == null || CharacterDecisionProp == null || DecisionWaitMethod == null)
        return;

      var Decision = CharacterDecisionProp.GetValue(Character);
      if (Decision != null)
        DecisionWaitMethod.Invoke(Decision, null);
    }

    public static Character GetInteractionSourceCharacter(object Interaction)
      => Interaction != null && InteractionSourceCharacterProp != null
        ? InteractionSourceCharacterProp.GetValue(Interaction) as Character
        : null;

    public static Character GetInteractionTargetCharacter(object Interaction)
      => Interaction != null && InteractionTargetCharacterProp != null
        ? InteractionTargetCharacterProp.GetValue(Interaction) as Character
        : null;

    public static Delay GetAttackDelay(object Engine, Character Character)
    {
      if (Engine == null || Character == null || EngineGetAttackDelayMethod == null)
        return Delay.FromTurns(12);

      return EngineGetAttackDelayMethod.Invoke(Engine, new object[] { Character }) is Delay D ? D : Delay.FromTurns(12);
    }

    /// <summary>角色「一回合」的逻辑帧数（原生 Wait / 移动的忙碌时长，正常速度 5 帧）。</summary>
    public static Delay GetMoveDelay(object Engine, Character Character)
    {
      if (Engine == null || Character == null || EngineGetMoveDelayMethod == null)
        return Delay.FromTurns(5);

      return EngineGetMoveDelayMethod.Invoke(Engine, new object[] { Character }) is Delay D ? D : Delay.FromTurns(5);
    }

    public static void SetBusy(object Engine, Character Character, Delay Delay)
    {
      if (Engine == null || Character == null || EngineSetBusyMethod == null)
        return;

      EngineSetBusyMethod.Invoke(Engine, new object[] { Character, Delay });
    }

    public static string GetLocalizedName(Property Property)
    {
      if (Property == null)
        return string.Empty;

      try
      {
        if (TranslationGetPropertyNameMethod?.Invoke(null, new object[] { Property }) is string Localized &&
            !string.IsNullOrEmpty(Localized))
          return Localized;
      }
      catch { }

      return Property.Name ?? string.Empty;
    }

    public static string GetLocalizedName(Entity Entity)
    {
      if (Entity == null)
        return string.Empty;

      try
      {
        if (TranslationGetEntityNameMethod?.Invoke(null, new object[] { Entity }) is string Localized &&
            !string.IsNullOrEmpty(Localized))
          return Localized;
      }
      catch { }

      return Entity.Name ?? string.Empty;
    }

    public static string GetLocalizedName(Spell Spell)
    {
      if (Spell == null)
        return string.Empty;

      try
      {
        if (TranslationGetSpellNameMethod?.Invoke(null, new object[] { Spell }) is string Localized &&
            !string.IsNullOrEmpty(Localized))
          return Localized;
      }
      catch { }

      return Spell.Name ?? string.Empty;
    }

    /// <summary>法术描述的当前语言译文（Codex 字典 SpellDescription），取不到时回退英文原文。</summary>
    public static string GetLocalizedDescription(Spell Spell)
    {
      if (Spell == null)
        return string.Empty;

      try
      {
        if (TranslationGetSpellDescriptionMethod?.Invoke(null, new object[] { Spell }) is string Localized &&
            !string.IsNullOrEmpty(Localized))
          return Localized;
      }
      catch { }

      return Spell.Description ?? string.Empty;
    }

    public static Asset GetUseAssetCommandAsset(object Command)
      => Command != null && UseAssetCmdAssetProp != null
        ? UseAssetCmdAssetProp.GetValue(Command) as Asset
        : null;

    public static Motion GetUseAssetCommandMotion(object Command)
      => Command != null && UseAssetCmdMotionProp != null
        ? UseAssetCmdMotionProp.GetValue(Command) as Motion
        : null;

    /// <summary>
    /// 检查角色当前是否正在穿戴/手持指定物品（仅检查装备槽，放在背包中的物品不计入）。
    /// </summary>
    public static bool HasEquippedItem(Character Character, Item Item)
    {
      if (Character?.Inventory == null || Item == null)
        return false;

      foreach (var Asset in Character.Inventory.GetEquippedAssets())
      {
        if (Asset != null && Asset.Item == Item)
          return true;
      }

      return false;
    }

    /// <summary>
    /// 检查角色在装备槽或随身背包中是否拥有指定物品。
    /// </summary>
    public static bool HasOwnedItem(Character Character, Item Item)
    {
      if (Character?.Inventory == null || Item == null)
        return false;

      if (HasEquippedItem(Character, Item))
        return true;

      var Carried = Character.Inventory.GetCarriedAssets();
      if (Carried != null)
      {
        foreach (var Asset in Carried)
        {
          if (Asset != null && Asset.Item == Item)
            return true;
        }
      }

      return false;
    }

    /// <summary>
    /// 从角色身上消耗 1 件指定物品（优先消耗背包中的，若仅装备在身上则卸下并销毁）。
    /// </summary>
    public static bool ConsumeOwnedItem(object Engine, Character Character, Item Item)
    {
      if (Engine == null || Character?.Inventory == null || Item == null)
        return false;

      // 1. 优先从背包消耗
      var Carried = Character.Inventory.GetCarriedAssets();
      if (Carried != null)
      {
        foreach (var Asset in Carried)
        {
          if (Asset != null && Asset.Item == Item)
          {
            EngineLoseCarriedAssetMethod?.Invoke(Engine, new object[] { Character, Asset });
            return true;
          }
        }
      }

      // 2. 否则从装备槽卸下并移除
      foreach (var Eq in Character.Inventory.GetEquipments())
      {
        if (Eq?.Asset != null && Eq.Asset.Item == Item)
        {
          if (EngineRemoveEquipmentAssetMethod != null)
          {
            EngineRemoveEquipmentAssetMethod.Invoke(Engine, new object[] { Character, Eq, 1 });
            return true;
          }
          return false;
        }
      }

      return false;
    }

    /// <summary>
    /// 在指定格子生成并放置 1 件指定物品（用于 Boss 额外掉落）。
    /// </summary>
    public static bool DropItemAt(object Engine, Square Square, Item Item)
    {
      if (Engine == null || Square == null || Item == null || EngineGeneratorProp == null || GeneratorPlaceSpecificAssetMethod == null)
        return false;

      try
      {
        var Generator = EngineGeneratorProp.GetValue(Engine);
        if (Generator == null)
          return false;

        GeneratorPlaceSpecificAssetMethod.Invoke(Generator, new object[] { Square, Item, (int?)1 });
        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"PathosEngineBridge.DropItemAt failed: {Ex}");
        return false;
      }
    }

    /// <summary>
    /// 调整角色最大法力值（与 DotaLife.AdjustMax 对称，支持 KeepRatio / RaiseBoth / ClampCurrent）。
    /// </summary>
    public static void AdjustMaxMana(Character Character, int Delta, DotaLifeAdjustMode Mode)
    {
      var Mana = Character?.Mana;
      if (Mana == null || Delta == 0)
        return;

      int OldTotal = Mana.Total;
      int OldPoints = Mana.Points;
      int NewTotal;
      int NewPoints;

      switch (Mode)
      {
        case DotaLifeAdjustMode.KeepRatio:
        {
          NewTotal = Math.Max(0, OldTotal + Delta);
          float Ratio = OldTotal > 0 ? Math.Clamp((float)OldPoints / OldTotal, 0.0f, 1.0f) : 1.0f;
          NewPoints = NewTotal <= 0 ? 0 : Math.Clamp((int)MathF.Round(NewTotal * Ratio), 0, NewTotal);
          break;
        }

        case DotaLifeAdjustMode.RaiseBoth:
          NewTotal = Math.Max(0, OldTotal + Delta);
          NewPoints = Math.Clamp(OldPoints + Delta, 0, NewTotal);
          break;

        case DotaLifeAdjustMode.ClampCurrent:
          NewTotal = Math.Max(0, OldTotal + Delta);
          NewPoints = Math.Clamp(OldPoints, 0, NewTotal);
          break;

        default:
          return;
      }

      SetMeter(Mana, NewPoints, NewTotal);
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
