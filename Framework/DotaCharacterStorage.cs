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
      public readonly Dictionary<string, int> CustomInts = new(StringComparer.OrdinalIgnoreCase);
      public readonly Dictionary<string, int> Cooldowns = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// 读取英雄自定义的持久化整数计数（如斧王淘汰之刃永久护甲层数），不占用 Codex.Properties。
    /// </summary>
    public static int GetCustomInt(Character Character, string Key, int DefaultValue = 0)
    {
      if (Character == null || string.IsNullOrWhiteSpace(Key))
        return DefaultValue;

      var State = LoadState(Character);
      return State.CustomInts.TryGetValue(Key, out int Value) ? Value : DefaultValue;
    }

    /// <summary>
    /// 写入英雄自定义的持久化整数计数（值为 0 时自动清理键）。
    /// </summary>
    public static bool SetCustomInt(Character Character, string Key, int Value)
    {
      if (Character == null || string.IsNullOrWhiteSpace(Key))
        return false;

      var State = LoadState(Character);
      if (Value == 0)
        State.CustomInts.Remove(Key);
      else
        State.CustomInts[Key] = Value;

      return SaveState(Character, State);
    }

    /// <summary>
    /// 原子累加英雄自定义持久化整数计数并返回新值。
    /// </summary>
    public static int AddCustomInt(Character Character, string Key, int Delta)
    {
      if (Character == null || string.IsNullOrWhiteSpace(Key))
        return 0;

      int Next = GetCustomInt(Character, Key) + Delta;
      SetCustomInt(Character, Key, Next);
      return Next;
    }

    /// <summary>
    /// 读取指定法术的剩余冷却回合数（0 表示已就绪）。
    /// </summary>
    public static int GetCooldown(Character Character, string SpellName)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SpellName))
        return 0;

      var State = LoadState(Character);
      return State.Cooldowns.TryGetValue(SpellName, out int Turns) && Turns > 0 ? Turns : 0;
    }

    /// <summary>
    /// 设置指定法术的剩余冷却回合数（≤ 0 时自动清理键），随存档持久化。
    /// </summary>
    public static bool SetCooldown(Character Character, string SpellName, int RemainingTurns)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SpellName))
        return false;

      var State = LoadState(Character);
      if (RemainingTurns <= 0)
      {
        if (!State.Cooldowns.Remove(SpellName))
          return true;
      }
      else
      {
        if (State.Cooldowns.TryGetValue(SpellName, out int Existing) && Existing == RemainingTurns)
          return true;
        State.Cooldowns[SpellName] = RemainingTurns;
      }

      return SaveState(Character, State);
    }

    /// <summary>
    /// 清空角色全部技能冷却。
    /// </summary>
    public static bool ClearAllCooldowns(Character Character)
    {
      if (Character == null)
        return false;

      var State = LoadState(Character);
      if (State.Cooldowns.Count == 0)
        return true;

      State.Cooldowns.Clear();
      return SaveState(Character, State);
    }

    /// <summary>
    /// 将角色当前所有进行中的技能冷却统一递减 1 回合（跳过 <paramref name="SkipSpellNames"/> 中的法术），
    /// 到 0 自动移除并仅在有变动时写回一次存档 Tag。
    /// </summary>
    public static bool TickAllCooldowns(Character Character, ISet<string> SkipSpellNames = null)
    {
      if (Character == null)
        return false;

      var State = LoadState(Character);
      if (State.Cooldowns.Count == 0)
        return false;

      bool Changed = false;
      var Keys = new List<string>(State.Cooldowns.Keys);
      foreach (var Key in Keys)
      {
        if (SkipSpellNames != null && SkipSpellNames.Contains(Key))
          continue;

        int Next = State.Cooldowns[Key] - 1;
        if (Next <= 0)
          State.Cooldowns.Remove(Key);
        else
          State.Cooldowns[Key] = Next;
        Changed = true;
      }

      if (Changed)
        SaveState(Character, State);

      return Changed;
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
      Entry.CustomInts.Clear();
      Entry.Cooldowns.Clear();

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
        else if (Section.StartsWith("m:", StringComparison.Ordinal))
        {
          var Pairs = Section.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries);
          foreach (var Pair in Pairs)
          {
            int Eq = Pair.IndexOf('=');
            if (Eq <= 0 || Eq >= Pair.Length - 1)
              continue;

            string Key = Pair.Substring(0, Eq);
            if (int.TryParse(Pair.Substring(Eq + 1), out int Val) && Val != 0)
              Entry.CustomInts[Key] = Val;
          }
        }
        else if (Section.StartsWith("c:", StringComparison.Ordinal))
        {
          var Pairs = Section.Substring(2).Split(',', StringSplitOptions.RemoveEmptyEntries);
          foreach (var Pair in Pairs)
          {
            int Eq = Pair.IndexOf('=');
            if (Eq <= 0 || Eq >= Pair.Length - 1)
              continue;

            string Key = Pair.Substring(0, Eq);
            if (int.TryParse(Pair.Substring(Eq + 1), out int Turns) && Turns > 0)
              Entry.Cooldowns[Key] = Turns;
          }
        }
      }

      return Entry;
    }

    private static bool SaveState(Character Character, CachedState State)
    {
      var Parts = new List<string>(3);

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

      if (State.CustomInts.Count > 0)
      {
        var MetaPairs = new List<string>(State.CustomInts.Count);
        foreach (var Kv in State.CustomInts)
        {
          if (Kv.Value != 0)
            MetaPairs.Add($"{Kv.Key}={Kv.Value}");
        }
        if (MetaPairs.Count > 0)
          Parts.Add("m:" + string.Join(",", MetaPairs));
      }

      if (State.Cooldowns.Count > 0)
      {
        var CdPairs = new List<string>(State.Cooldowns.Count);
        foreach (var Kv in State.Cooldowns)
        {
          if (Kv.Value > 0)
            CdPairs.Add($"{Kv.Key}={Kv.Value}");
        }
        if (CdPairs.Count > 0)
          Parts.Add("c:" + string.Join(",", CdPairs));
      }

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
