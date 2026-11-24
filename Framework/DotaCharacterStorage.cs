using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 基于 Pathos 原生 Character.Dialogue.Tag 的无限容量存档持久化存储：
  /// - Pathos 的 PropertySet 底层是单个 Int64（Inv.BitmixSet，全局硬上限 64 个 Property），
  ///   官方已占用 57 个，不能把技能加点/天赋树标记塞进 Codex.Properties（超过 64 会按 64 取模回绕撞上幻觉等内置状态）。
  /// - 玩家角色的 Character.Dialogue 原生为 null，且随 Character 完整序列化到 Pathos 存档；
  ///   将技能等级与已学天赋以紧凑文本写入 Dialogue.Tag，实现 0 Property 占用、0 百科污染、0 多余日志与 100% 原生存档兼容。
  /// </summary>
  public static class DotaCharacterStorage
  {
    private const string Prefix = "dota_state:v1|";

    private static readonly Assembly EngineAsm = typeof(Expansion).Assembly;
    private static readonly Type DialogueType = EngineAsm.GetType("Pathos.Dialogue");
    private static readonly ConstructorInfo DialogueCtor =
      DialogueType?.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
    private static readonly FieldInfo CharacterDialogueBackingField =
      typeof(Character).GetField("<Dialogue>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo CharacterDialogueProp =
      typeof(Character).GetProperty("Dialogue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo DialogueTagBackingField =
      DialogueType?.GetField("<Tag>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo DialogueTagProp =
      DialogueType?.GetProperty("Tag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private sealed class CachedState
    {
      public string RawTag;
      public readonly Dictionary<string, int> Skills = new(StringComparer.OrdinalIgnoreCase);
      public readonly HashSet<string> Talents = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<Character, CachedState> Cache = new();

    public static int GetSkillLevel(Character Character, string SkillId)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SkillId))
        return 0;

      var State = LoadState(Character);
      return State.Skills.TryGetValue(SkillId, out int Level) ? Level : 0;
    }

    public static bool SetSkillLevel(Character Character, string SkillId, int Level)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SkillId))
        return false;

      var State = LoadState(Character);
      if (Level <= 0)
        State.Skills.Remove(SkillId);
      else
        State.Skills[SkillId] = Level;

      return SaveState(Character, State);
    }

    public static bool HasTalent(Character Character, string TalentId)
    {
      if (Character == null || string.IsNullOrWhiteSpace(TalentId))
        return false;

      var State = LoadState(Character);
      return State.Talents.Contains(TalentId);
    }

    public static bool AddTalent(Character Character, string TalentId)
    {
      if (Character == null || string.IsNullOrWhiteSpace(TalentId))
        return false;

      var State = LoadState(Character);
      State.Talents.Add(TalentId);
      return SaveState(Character, State);
    }

    private static CachedState LoadState(Character Character)
    {
      var Entry = Cache.GetValue(Character, _ => new CachedState());
      string Raw = ReadRawTag(Character) ?? string.Empty;

      if (string.Equals(Entry.RawTag, Raw, StringComparison.Ordinal))
        return Entry;

      Entry.RawTag = Raw;
      Entry.Skills.Clear();
      Entry.Talents.Clear();

      if (!Raw.StartsWith(Prefix, StringComparison.Ordinal))
        return Entry;

      string Body = Raw.Substring(Prefix.Length);
      var Sections = Body.Split(';', StringSplitOptions.RemoveEmptyEntries);
      foreach (var Section in Sections)
      {
        if (Section.StartsWith("s:", StringComparison.Ordinal))
        {
          var Pairs = Section.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries);
          foreach (var Pair in Pairs)
          {
            int Eq = Pair.IndexOf('=');
            if (Eq <= 0 || Eq >= Pair.Length - 1)
              continue;

            string Key = Pair.Substring(0, Eq);
            if (int.TryParse(Pair.Substring(Eq + 1), out int Lv) && Lv > 0)
              Entry.Skills[Key] = Lv;
          }
        }
        else if (Section.StartsWith("t:", StringComparison.Ordinal))
        {
          var Items = Section.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries);
          foreach (var Item in Items)
          {
            if (!string.IsNullOrWhiteSpace(Item))
              Entry.Talents.Add(Item);
          }
        }
      }

      return Entry;
    }

    private static bool SaveState(Character Character, CachedState State)
    {
      var Parts = new List<string>(2);

      if (State.Skills.Count > 0)
      {
        var SkillPairs = new List<string>(State.Skills.Count);
        foreach (var Kv in State.Skills)
        {
          if (Kv.Value > 0)
            SkillPairs.Add($"{Kv.Key}={Kv.Value}");
        }
        if (SkillPairs.Count > 0)
          Parts.Add("s:" + string.Join(",", SkillPairs));
      }

      if (State.Talents.Count > 0)
        Parts.Add("t:" + string.Join(",", State.Talents));

      string Raw = Prefix + string.Join(";", Parts);
      if (!WriteRawTag(Character, Raw))
        return false;

      State.RawTag = Raw;
      return true;
    }

    private static string ReadRawTag(Character Character)
    {
      try
      {
        var Dialogue = CharacterDialogueProp?.GetValue(Character) ?? CharacterDialogueBackingField?.GetValue(Character);
        if (Dialogue == null)
          return null;

        return DialogueTagProp?.GetValue(Dialogue) as string ?? DialogueTagBackingField?.GetValue(Dialogue) as string;
      }
      catch
      {
        return null;
      }
    }

    private static bool WriteRawTag(Character Character, string Tag)
    {
      if (Character == null || DialogueType == null || DialogueCtor == null)
        return false;

      try
      {
        var Dialogue = CharacterDialogueProp?.GetValue(Character) ?? CharacterDialogueBackingField?.GetValue(Character);
        if (Dialogue == null)
        {
          Dialogue = DialogueCtor.Invoke(null);
          if (CharacterDialogueBackingField != null)
            CharacterDialogueBackingField.SetValue(Character, Dialogue);
          else
            CharacterDialogueProp?.SetValue(Character, Dialogue);
        }

        if (DialogueTagBackingField != null)
          DialogueTagBackingField.SetValue(Dialogue, Tag);
        else
          DialogueTagProp?.SetValue(Dialogue, Tag);

        return true;
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"DotaCharacterStorage.WriteRawTag failed: {Ex}");
        return false;
      }
    }
  }
}
