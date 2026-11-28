using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.Talents;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// DOTA2 英雄主动技能冷却服务：
  /// - 冷却数据以紧凑文本存入 <see cref="DotaCharacterStorage"/>（<c>c:</c> 段），零 Codex.Properties 占用，跨存档完整持久化。
  /// - 施放成功后启动冷却，随角色自身行动回合（<c>AfterExecuteCommand</c>）统一递减；施法当回合不提前扣减。
  /// - 引擎侧在 <c>BeforeCastSpell</c> 拦截冷却中的施法；客户端侧由 <c>DotaCooldownUiBridge</c> 提前在选格前拦截并更新法术面板与快捷栏显示。
  /// - 支持天赋固定减秒与百分比减冷却（<see cref="SpellCooldownReductionRule"/>）以及技能自定义重置（如斧王淘汰之刃斩杀刷新）。
  /// </summary>
  public static class DotaCooldownService
  {
    private sealed class SkipTickState
    {
      public readonly HashSet<string> SpellNames = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly ConditionalWeakTable<Character, SkipTickState> PendingSkips = new();
    private static int InitializedFlag;

    /// <summary>
    /// 幂等注册冷却所需的施法拦截与回合递减 Hook（首次注册 <see cref="DotaAbility"/> 时自动调用）。
    /// </summary>
    public static void EnsureInitialized()
    {
      if (Interlocked.Exchange(ref InitializedFlag, 1) != 0)
        return;

      DotaHookBus.RegisterBeforeCastSpell("dota.cooldown.block_cast", HandleBeforeCastSpell, DotaHookPriority.Early);
      DotaHookBus.RegisterAfterExecuteCommand("dota.cooldown.tick", HandleAfterExecuteCommand);
    }

    /// <summary>查询角色指定法术的剩余冷却回合数（0 表示已就绪）。</summary>
    public static int GetRemaining(Character Character, string SpellName)
      => DotaCharacterStorage.GetCooldown(Character, SpellName);

    /// <summary>查询角色指定法术的剩余冷却回合数（0 表示已就绪）。</summary>
    public static int GetRemaining(Character Character, Spell Spell)
      => Spell != null ? GetRemaining(Character, Spell.Name) : 0;

    /// <summary>判断角色指定法术当前是否处于冷却中。</summary>
    public static bool IsOnCooldown(Character Character, Spell Spell)
      => GetRemaining(Character, Spell) > 0;

    /// <summary>
    /// 启动指定法术的冷却（回合数 ≤ 0 等效于重置）。
    /// 施法当回合结束时的 <c>AfterExecuteCommand</c> 不会提前递减本次刚启动的冷却。
    /// </summary>
    public static void Start(Character Character, string SpellName, int Turns)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SpellName))
        return;

      EnsureInitialized();

      if (Turns <= 0)
      {
        Reset(Character, SpellName);
        return;
      }

      DotaCharacterStorage.SetCooldown(Character, SpellName, Turns);

      // 若正在施法命令执行期间启动冷却，跳过本回合结尾的首次递减，保证刚放完即显示完整回合数
      if (DotaHookBus.ActiveSpellCast != null && DotaHookBus.ActiveSpellCast.Character == Character)
      {
        var Skip = PendingSkips.GetValue(Character, _ => new SkipTickState());
        Skip.SpellNames.Add(SpellName);
      }
    }

    /// <summary>立即重置指定法术的冷却（归零，如斧王淘汰之刃斩杀刷新或技能空放取消）。</summary>
    public static void Reset(Character Character, string SpellName)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SpellName))
        return;

      DotaCharacterStorage.SetCooldown(Character, SpellName, 0);
      if (PendingSkips.TryGetValue(Character, out var Skip))
        Skip.SpellNames.Remove(SpellName);
    }

    /// <summary>清空角色全部技能冷却。</summary>
    public static void ResetAll(Character Character)
    {
      if (Character == null)
        return;

      DotaCharacterStorage.ClearAllCooldowns(Character);
      if (PendingSkips.TryGetValue(Character, out var Skip))
        Skip.SpellNames.Clear();
    }

    /// <summary>按回合数缩减指定法术的当前剩余冷却（最低归零）。</summary>
    public static void Reduce(Character Character, string SpellName, int DeltaTurns)
    {
      if (Character == null || string.IsNullOrWhiteSpace(SpellName) || DeltaTurns <= 0)
        return;

      int Current = GetRemaining(Character, SpellName);
      if (Current <= 0)
        return;

      DotaCharacterStorage.SetCooldown(Character, SpellName, Math.Max(0, Current - DeltaTurns));
    }

    /// <summary>
    /// 计算指定技能在当前角色（含技能等级与已学减冷却天赋）下的实际冷却秒数（DOTA2 秒）。
    /// </summary>
    public static float ComputeCooldownSeconds(Character Character, DotaAbility Ability, int? OverrideLevel = null)
    {
      if (Ability == null || !Ability.HasCooldown)
        return 0.0f;

      int Level = OverrideLevel ?? Math.Max(1, Ability.Level(Character));
      float BaseSeconds = Ability.Spec.CooldownSecondsResolver != null
        ? Ability.Spec.CooldownSecondsResolver(Character, Level)
        : Ability.Spec.CooldownSeconds != null
          ? Ability.Spec.CooldownSeconds[Level]
          : 0.0f;

      if (BaseSeconds <= 0.0f)
        return 0.0f;

      if (Character == null)
        return BaseSeconds;

      var (FlatSeconds, PercentMultiplier) = TalentService.GetCooldownReduction(Character, Ability.Spell);
      float AfterFlat = Math.Max(0.5f, BaseSeconds - FlatSeconds);
      return Math.Max(0.5f, AfterFlat * PercentMultiplier);
    }

    /// <summary>
    /// 计算指定技能在当前角色（含技能等级与已学减冷却天赋）下的实际冷却回合数（1 秒 = 1 回合，正冷却保底 1 回合）。
    /// </summary>
    public static int ComputeCooldownTurns(Character Character, DotaAbility Ability, int? OverrideLevel = null)
    {
      float Seconds = ComputeCooldownSeconds(Character, Ability, OverrideLevel);
      return Seconds <= 0.0f ? 0 : DotaMechanicsMapper.MapCooldownTurns(Seconds, MinTurns: 1);
    }

    /// <summary>
    /// 统一发送「技能冷却中」的浮字与战斗日志提示。
    /// </summary>
    public static void NotifyOnCooldown(object Engine, Character Caster, Spell Spell, int RemainingTurns)
    {
      if (Engine == null || Caster == null || Spell == null || RemainingTurns <= 0)
        return;

      string SpellTitle = ResolveSpellDisplayTitle(Spell);
      string LogText = DotaLocalization.Format("ui.cooldown.on_cooldown_log", SpellTitle, RemainingTurns);
      string CaptionText = DotaLocalization.Format("ui.cooldown.on_cooldown_caption", RemainingTurns);

      if (Caster.Square != null)
        PathosEngineBridge.StoryCaption(Engine, Caster.Square, Colour.OrangeRed, CaptionText, Delayed: false);

      PathosEngineBridge.WriteInternalLog(Engine, Caster, Spell.Glyph, LogText);
    }

    private static string ResolveSpellDisplayTitle(Spell Spell)
    {
      if (Spell == null)
        return string.Empty;

      if (DotaAbility.TryGet(Spell, out var Ability))
      {
        string Resolved = Ability.Spec.TitleResolver?.Invoke();
        if (!string.IsNullOrWhiteSpace(Resolved))
          return Resolved;
      }

      string Localized = PathosEngineBridge.GetLocalizedName(Spell);
      return !string.IsNullOrWhiteSpace(Localized) ? Localized : Spell.Name ?? string.Empty;
    }

    private static void HandleBeforeCastSpell(SpellCastEventArgs Args)
    {
      if (Args?.Character == null || Args.Spell == null)
        return;

      int Remaining = GetRemaining(Args.Character, Args.Spell);
      if (Remaining <= 0)
        return;

      Args.Cancel = true;
      NotifyOnCooldown(Args.Engine, Args.Character, Args.Spell, Remaining);
    }

    private static void HandleAfterExecuteCommand(CommandEventArgs Args)
    {
      var Ch = Args?.Character;
      if (Ch == null || Ch.Deceased)
        return;

      HashSet<string> Skips = null;
      if (PendingSkips.TryGetValue(Ch, out var SkipState) && SkipState.SpellNames.Count > 0)
      {
        Skips = new HashSet<string>(SkipState.SpellNames, StringComparer.OrdinalIgnoreCase);
        SkipState.SpellNames.Clear();
      }

      DotaCharacterStorage.TickAllCooldowns(Ch, Skips);
    }
  }
}
