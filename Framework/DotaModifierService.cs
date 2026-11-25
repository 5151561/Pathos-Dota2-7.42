using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 同一 Modifier 重复施加到同一单位时的叠加规则。
  /// </summary>
  public enum DotaModifierStacking
  {
    /// <summary>新实例完全覆盖旧实例（数值与剩余回合全部重置），如战斗饥渴重新施放。</summary>
    Replace,

    /// <summary>保留更强的一方：剩余回合取最大，各项数值按绝对值取最大（来源更新为最新施加者），如淘汰之刃战吼。</summary>
    KeepStrongest
  }

  /// <summary>
  /// Modifier 被移除的原因。
  /// </summary>
  public enum DotaModifierRemoveReason
  {
    Expired,
    Dispelled,
    Removed,
    SourceDied
  }

  /// <summary>
  /// DOTA2 风格 Modifier 定义（每种 Modifier 定义一次，通常保存为英雄类的字段）。
  /// 只描述「是什么」：Key、增益/减益、是否可驱散、叠加规则、持续伤害类型与状态栏徽章外观；
  /// 具体数值（护甲、移速、DPS、回合数）在每次 <see cref="DotaModifierService.Apply"/> 时按施法者等级填入。
  /// </summary>
  public sealed class DotaModifierSpec
  {
    /// <summary>全局唯一 Key，建议 <c>&lt;hero_id&gt;.&lt;feature&gt;</c>（如 <c>axe.battle_hunger</c>）。</summary>
    public string Key { get; init; }

    /// <summary>是否为减益：减益受「减益免疫 / 无敌」阻挡，并可被 <see cref="DotaCombatHelper.DispelNegativeDebuffs"/> 基础驱散。</summary>
    public bool IsDebuff { get; init; }

    /// <summary>是否可被基础驱散（默认 true；强驱散类效果请设为 false）。</summary>
    public bool IsDispellable { get; init; } = true;

    public DotaModifierStacking Stacking { get; init; } = DotaModifierStacking.Replace;

    /// <summary>施加者死亡时是否立即移除（如嘲讽、链接类效果）。</summary>
    public bool RemoveOnSourceDeath { get; init; }

    /// <summary>持续伤害元素：为 null 时走 <c>StrikeCharacter</c>（纯粹伤害，无视元素抗性），否则走 <c>HarmCharacter</c> 按元素结算。</summary>
    public Element DotElement { get; init; }

    /// <summary>持续伤害在日志/死因中显示的来源名（通常为法术名）。</summary>
    public string DotCause { get; init; }

    /// <summary>左上角状态栏徽章图标；为 null 时不自动生成徽章（可由英雄自行注册组合徽章）。</summary>
    public Glyph BadgeGlyph { get; init; }

    public Func<string> BadgeTitle { get; init; }
    public Colour BadgeColour { get; init; } = Colour.Black;

    /// <summary>徽章计数文本；默认显示剩余回合（如 <c>6T</c>）。</summary>
    public Func<DotaModifier, string> BadgeCount { get; init; }

    public Spell HelpSpell { get; init; }
    public Property HelpProperty { get; init; }

    /// <summary>每回合（持有者行动后、持续伤害结算前）的自定义逻辑。</summary>
    public Action<object, Character, DotaModifier> OnTick { get; init; }

    /// <summary>移除时回调（到期 / 驱散 / 主动移除 / 施加者死亡）。</summary>
    public Action<object, Character, DotaModifier, DotaModifierRemoveReason> OnRemoved { get; init; }
  }

  /// <summary>
  /// 挂在某个单位身上的 Modifier 实例（运行时状态，不占用 Codex.Properties）。
  /// </summary>
  public sealed class DotaModifier
  {
    internal DotaModifier(DotaModifierSpec Spec, Character Source, int DurationTurns)
    {
      this.Spec = Spec;
      this.SourceRef = Source != null ? new WeakReference<Character>(Source) : null;
      this.RemainingTurns = DurationTurns;
    }

    public DotaModifierSpec Spec { get; }
    public string Key => Spec.Key;

    /// <summary>施加者（已被回收时为 null）。</summary>
    public Character Source => SourceRef != null && SourceRef.TryGetTarget(out var S) ? S : null;

    public int RemainingTurns { get; set; }

    /// <summary>防御加成（已按 <c>DotaMechanicsMapper.MapArmorToDefence</c> 折算的 Pathos 防御值，负数为减甲）。</summary>
    public int Defence { get; set; }

    /// <summary>移动速度百分比（0.25 = +25%，-0.16 = 16% 减速）；所有 Modifier 加算后统一乘到移速上。</summary>
    public float MoveSpeedPct { get; set; }

    /// <summary>攻击速度百分比（同上，作用于 <c>SpeedQuery</c> 的 Attacking 分支）。</summary>
    public float AttackSpeedPct { get; set; }

    /// <summary>DOTA2 原始每秒伤害（DPS）；框架按 K_dmg 与 1 秒 = 2 回合逐回合累加小数后结算整数伤害。</summary>
    public float DotDotaDps { get; set; }

    /// <summary>
    /// 每次受到伤害的格挡量（Pathos 伤害点，允许小数 = DOTA2 原值 ÷ K_dmg 的期望值，如帕吉肉盾 8 → 0.53）。
    /// 对所有伤害类型（物理/魔法/纯粹）逐次生效：整数部分必挡，小数部分按概率再挡 1 点，伤害最低扣到 0；
    /// 只减少生命伤害，不拦截攻击附带的命中效果。多个 Modifier 的格挡相加。
    /// </summary>
    public float DamageBlock { get; set; }

    public int Stacks { get; set; } = 1;

    /// <summary>英雄自定义附加数据。</summary>
    public object Tag { get; set; }

    internal WeakReference<Character> SourceRef;
    internal float DotAccumulator;
  }

  /// <summary>
  /// DOTA2 通用 Modifier（带回合数的增益/减益）基建服务：
  /// - 统一回合递减（随持有者自身行动回合）、到期移除与施加者死亡清理；
  /// - 自动汇总全部 Modifier 的防御、移速（加算，减速下限 35%）、攻速；
  /// - 内置纯粹/元素持续伤害（小数累加，结算精确到 K_dmg = 15 与 0.5 秒/回合）；
  /// - 减益受「无敌」与 <see cref="RegisterDebuffImmunity"/> 阻挡，可被基础驱散；
  /// - 可选自动生成左上角状态栏徽章；
  /// - 全部状态存于 <see cref="ConditionalWeakTable{TKey, TValue}"/>，零 Codex.Properties 占用。
  /// </summary>
  public static class DotaModifierService
  {
    /// <summary>移速倍率下限（DOTA2 减速无法把单位降至停滞，对齐原斧王战斗饥渴的 35% 保底）。</summary>
    public const float MinMoveSpeedMultiplier = 0.35f;

    private sealed class SourceIndex
    {
      public readonly List<WeakReference<Character>> Holders = new();
    }

    private static readonly ConditionalWeakTable<Character, List<DotaModifier>> Holders = new();
    private static readonly ConditionalWeakTable<Character, SourceIndex> SourceIndexes = new();
    private static readonly DotaHookHandlerList<Func<Character, bool>> DebuffImmunities = new();

    private static int InitializedFlag;

    /// <summary>
    /// 幂等注册 Modifier 所需的回合推进、防御与速度查询 Hook（首次 Define / Apply 时自动调用）。
    /// </summary>
    public static void EnsureInitialized()
    {
      if (Interlocked.Exchange(ref InitializedFlag, 1) != 0)
        return;

      DotaHookBus.RegisterAfterExecuteCommand("dota.modifiers.tick", Args => TickHolder(Args.Engine, Args.Character));
      DotaHookBus.RegisterDefenceQuery("dota.modifiers.defence", HandleDefenceQuery);
      DotaHookBus.RegisterSpeedQuery("dota.modifiers.speed", HandleSpeedQuery);
      // 伤害格挡排在魔抗（Reduction）之后：先按比例减伤，再逐次扣除固定格挡
      DotaHookBus.RegisterBeforeDamage("dota.modifiers.block", HandleDamageBlock, DotaHookPriority.Reduction + 10);
    }

    /// <summary>单位当前全部 Modifier 的伤害格挡合计（Pathos 伤害点，可为小数）。</summary>
    public static float GetTotalDamageBlock(Character Character)
    {
      float Total = 0.0f;
      foreach (var M in GetAll(Character))
        Total += M.DamageBlock;
      return Total;
    }

    /// <summary>
    /// 把小数格挡量掷成本次实际格挡的整数点数：整数部分必挡，小数部分按概率 +1（保持期望值不变）。
    /// </summary>
    public static int RollBlock(float Block)
    {
      if (Block <= 0.0f)
        return 0;

      int Whole = (int)MathF.Floor(Block);
      return Whole + (Random.Shared.NextSingle() < Block - Whole ? 1 : 0);
    }

    private static void HandleDamageBlock(DamageEventArgs Args)
    {
      if (Args.Defender == null || Args.DamagePoints <= 0 || (Args.Info != null && Args.Info.Has(DotaDamageFlags.NoBlock)))
        return;

      float Block = GetTotalDamageBlock(Args.Defender);
      if (Block <= 0.0f)
        return;

      int Blocked = RollBlock(Block);
      if (Blocked <= 0)
        return;

      int Remaining = Args.DamagePoints - Blocked;
      if (Remaining <= 0)
        Args.Cancel = true;
      else
        Args.DamagePoints = Remaining;
    }

    /// <summary>
    /// 声明一种 Modifier（通常在英雄 <c>Register</c> 中、贴图注册之后调用一次）；
    /// 若设置了 <see cref="DotaModifierSpec.BadgeGlyph"/> 会自动注册状态栏徽章。
    /// </summary>
    public static DotaModifierSpec Define(DotaModifierSpec Spec)
    {
      if (Spec == null)
        throw new ArgumentNullException(nameof(Spec));
      if (string.IsNullOrWhiteSpace(Spec.Key))
        throw new ArgumentException("Modifier key cannot be null or empty.", nameof(Spec));

      EnsureInitialized();

      if (Spec.BadgeGlyph != null)
        DotaStatusBarBridge.RegisterProvider("modifier." + Spec.Key, Ch => BuildBadge(Ch, Spec));

      return Spec;
    }

    /// <summary>
    /// 注册「减益免疫」判定（如剑刃风暴、无敌斩）：返回 true 时该单位不会被施加任何 IsDebuff 的 Modifier。
    /// </summary>
    public static void RegisterDebuffImmunity(string Key, Func<Character, bool> Predicate)
      => DebuffImmunities.Register(Key, Predicate, DotaHookPriority.Normal);

    /// <summary>
    /// 判断单位当前是否免疫减益 Modifier（无敌或任一减益免疫判定成立）。
    /// </summary>
    public static bool IsDebuffImmune(Character Character)
    {
      if (Character == null)
        return false;
      if (DotaHookBus.QueryInvulnerable(Character))
        return true;

      foreach (var Entry in DebuffImmunities.GetSnapshot())
      {
        try
        {
          if (Entry.Handler(Character))
            return true;
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Debuff immunity '{Entry.Key}' failed: {Ex}");
        }
      }

      return false;
    }

    /// <summary>
    /// 对 <paramref name="Target"/> 施加 Modifier，并用 <paramref name="Configure"/> 填入本次数值。
    /// 返回实际生效的实例；目标无效、回合数 ≤ 0 或减益被免疫时返回 null。
    /// </summary>
    public static DotaModifier Apply(
      DotaModifierSpec Spec,
      Character Source,
      Character Target,
      int DurationTurns,
      Action<DotaModifier> Configure = null)
    {
      if (Spec == null || Target == null || Target.Deceased || DurationTurns <= 0)
        return null;

      EnsureInitialized();

      if (Spec.IsDebuff && IsDebuffImmune(Target))
        return null;

      var Candidate = new DotaModifier(Spec, Source, DurationTurns);
      Configure?.Invoke(Candidate);

      var List = Holders.GetValue(Target, _ => new List<DotaModifier>());
      int Index = List.FindIndex(M => M.Spec == Spec);
      DotaModifier Result;

      if (Index < 0)
      {
        List.Add(Candidate);
        Result = Candidate;
      }
      else if (Spec.Stacking == DotaModifierStacking.KeepStrongest)
      {
        var Existing = List[Index];
        Existing.SourceRef = Candidate.SourceRef;
        Existing.RemainingTurns = Math.Max(Existing.RemainingTurns, Candidate.RemainingTurns);
        Existing.Defence = Strongest(Existing.Defence, Candidate.Defence);
        Existing.MoveSpeedPct = Strongest(Existing.MoveSpeedPct, Candidate.MoveSpeedPct);
        Existing.AttackSpeedPct = Strongest(Existing.AttackSpeedPct, Candidate.AttackSpeedPct);
        Existing.DotDotaDps = Math.Max(Existing.DotDotaDps, Candidate.DotDotaDps);
        Existing.DamageBlock = Math.Max(Existing.DamageBlock, Candidate.DamageBlock);
        Existing.Stacks = Math.Max(Existing.Stacks, Candidate.Stacks);
        Existing.Tag = Candidate.Tag ?? Existing.Tag;
        Result = Existing;
      }
      else
      {
        List[Index] = Candidate;
        Result = Candidate;
      }

      if (Source != null)
        TrackSource(Source, Target);

      return Result;
    }

    /// <summary>获取单位身上指定 Modifier 的有效实例（无则返回 null）。</summary>
    public static DotaModifier Get(Character Character, DotaModifierSpec Spec)
    {
      if (Character == null || Spec == null || !Holders.TryGetValue(Character, out var List))
        return null;

      foreach (var M in List)
      {
        if (M.Spec == Spec && IsAlive(M))
          return M;
      }

      return null;
    }

    public static bool Has(Character Character, DotaModifierSpec Spec)
      => Get(Character, Spec) != null;

    /// <summary>枚举单位身上全部有效 Modifier 的快照。</summary>
    public static IReadOnlyList<DotaModifier> GetAll(Character Character)
    {
      if (Character == null || !Holders.TryGetValue(Character, out var List) || List.Count == 0)
        return Array.Empty<DotaModifier>();

      var Result = new List<DotaModifier>(List.Count);
      foreach (var M in List)
      {
        if (IsAlive(M))
          Result.Add(M);
      }
      return Result;
    }

    /// <summary>主动移除指定 Modifier；原本存在时返回 true 并触发 OnRemoved。</summary>
    public static bool Remove(object Engine, Character Character, DotaModifierSpec Spec, DotaModifierRemoveReason Reason = DotaModifierRemoveReason.Removed)
    {
      if (Character == null || Spec == null || !Holders.TryGetValue(Character, out var List))
        return false;

      int Index = List.FindIndex(M => M.Spec == Spec);
      if (Index < 0)
        return false;

      var Removed = List[Index];
      List.RemoveAt(Index);
      NotifyRemoved(Engine, Character, Removed, Reason);
      return Removed.RemainingTurns > 0;
    }

    /// <summary>
    /// 基础驱散：移除单位身上全部可驱散的减益（<paramref name="Debuffs"/> = true）或增益（false）。返回移除数量。
    /// </summary>
    public static int Dispel(object Engine, Character Character, bool Debuffs = true)
    {
      if (Character == null || !Holders.TryGetValue(Character, out var List) || List.Count == 0)
        return 0;

      var Dispelled = new List<DotaModifier>();
      List.RemoveAll(M =>
      {
        if (!M.Spec.IsDispellable || M.Spec.IsDebuff != Debuffs)
          return false;
        Dispelled.Add(M);
        return true;
      });

      foreach (var M in Dispelled)
        NotifyRemoved(Engine, Character, M, DotaModifierRemoveReason.Dispelled);

      return Dispelled.Count;
    }

    /// <summary>统计由 <paramref name="Source"/> 施加、当前仍生效的指定 Modifier 数量（如斧王「战斗饥渴」活跃目标数）。</summary>
    public static int CountAppliedBy(Character Source, DotaModifierSpec Spec)
    {
      if (Source == null || Spec == null || !SourceIndexes.TryGetValue(Source, out var Index))
        return 0;

      int Count = 0;
      Index.Holders.RemoveAll(Wr =>
      {
        if (!Wr.TryGetTarget(out var Holder) || Holder == null || Holder.Deceased)
          return true;

        var M = Get(Holder, Spec);
        if (M != null && M.Source == Source)
          Count++;

        return GetAllAppliedBy(Holder, Source) == 0;
      });

      return Count;
    }

    /// <summary>单位当前全部 Modifier 的防御加成合计。</summary>
    public static int GetTotalDefence(Character Character)
    {
      int Total = 0;
      foreach (var M in GetAll(Character))
        Total += M.Defence;
      return Total;
    }

    /// <summary>单位当前全部 Modifier 的移速倍率（加算后套用 <see cref="MinMoveSpeedMultiplier"/> 下限）。</summary>
    public static float GetMoveSpeedMultiplier(Character Character)
    {
      float Pct = 0.0f;
      foreach (var M in GetAll(Character))
        Pct += M.MoveSpeedPct;
      return Math.Max(MinMoveSpeedMultiplier, 1.0f + Pct);
    }

    /// <summary>单位当前全部 Modifier 的攻速倍率（加算，下限同移速）。</summary>
    public static float GetAttackSpeedMultiplier(Character Character)
    {
      float Pct = 0.0f;
      foreach (var M in GetAll(Character))
        Pct += M.AttackSpeedPct;
      return Math.Max(MinMoveSpeedMultiplier, 1.0f + Pct);
    }

    private static void HandleDefenceQuery(DefenceQueryEventArgs Args)
    {
      if (Args.Character == null || Args.Character.Deceased)
        return;

      int Bonus = GetTotalDefence(Args.Character);
      if (Bonus != 0)
        Args.Defence += Bonus;
    }

    private static void HandleSpeedQuery(SpeedQueryEventArgs Args)
    {
      var Ch = Args.Character;
      if (Ch == null || Ch.Deceased)
        return;

      float Mult = Args.Attacking ? GetAttackSpeedMultiplier(Ch) : GetMoveSpeedMultiplier(Ch);
      if (MathF.Abs(Mult - 1.0f) > 0.001f)
        Args.Speed = Speed.FromRate(Args.Speed.GetRate() * Mult);
    }

    private static void TickHolder(object Engine, Character Holder)
    {
      if (Holder == null || Holder.Deceased || !Holders.TryGetValue(Holder, out var List) || List.Count == 0)
        return;

      foreach (var M in List.ToArray())
      {
        if (Holder.Deceased)
          return;

        if (!List.Contains(M))
          continue;

        if (M.Spec.RemoveOnSourceDeath && (M.Source == null || M.Source.Deceased))
        {
          List.Remove(M);
          NotifyRemoved(Engine, Holder, M, DotaModifierRemoveReason.SourceDied);
          continue;
        }

        try
        {
          M.Spec.OnTick?.Invoke(Engine, Holder, M);
          DealDotDamage(Engine, Holder, M);
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Modifier '{M.Key}' tick failed: {Ex}");
        }

        M.RemainingTurns--;
        if (M.RemainingTurns <= 0 && List.Remove(M))
          NotifyRemoved(Engine, Holder, M, DotaModifierRemoveReason.Expired);
      }
    }

    private static void DealDotDamage(object Engine, Character Holder, DotaModifier M)
    {
      if (M.DotDotaDps <= 0.0f || Holder.Deceased)
        return;

      // 按 K_dmg = 15 与每回合 0.5 秒精确累加小数伤害
      M.DotAccumulator += (M.DotDotaDps / DotaMechanicsMapper.TurnsPerSecond) / DotaMechanicsMapper.DamageScale;
      int Damage = (int)MathF.Floor(M.DotAccumulator);
      if (Damage <= 0)
        return;

      M.DotAccumulator -= Damage;
      string Cause = M.Spec.DotCause ?? M.Key;

      if (M.Spec.DotElement == null)
        DotaDamage.Strike(Engine, M.Source, Holder, Direction.Self, Damage, DotaDamageInfo.Pure(Cause, DotaDamageFlags.Spell | DotaDamageFlags.DoT));
      else
        DotaDamage.Harm(Engine, M.Source, Holder, Direction.Self, M.Spec.DotElement, Dice.Fixed(Damage), DotaDamageInfo.Magical(Cause, DotaDamageFlags.Spell | DotaDamageFlags.DoT));
    }

    private static DotaStatusBadge BuildBadge(Character Character, DotaModifierSpec Spec)
    {
      if (Character == null || Character.Deceased)
        return null;

      var M = Get(Character, Spec);
      if (M == null)
        return null;

      return new DotaStatusBadge
      {
        Key = "modifier." + Spec.Key,
        Glyph = Spec.BadgeGlyph,
        Text = (Spec.BadgeTitle?.Invoke() ?? Spec.Key).ToUpper(),
        Count = Spec.BadgeCount != null ? Spec.BadgeCount(M) : $"{M.RemainingTurns}T",
        Colour = Spec.BadgeColour ?? Colour.Black,
        HelpSpell = Spec.HelpSpell,
        HelpProperty = Spec.HelpProperty
      };
    }

    private static void NotifyRemoved(object Engine, Character Holder, DotaModifier M, DotaModifierRemoveReason Reason)
    {
      if (M.Spec.OnRemoved == null)
        return;

      try
      {
        M.Spec.OnRemoved(Engine, Holder, M, Reason);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"Modifier '{M.Key}' OnRemoved failed: {Ex}");
      }
    }

    private static bool IsAlive(DotaModifier M)
    {
      if (M.RemainingTurns <= 0)
        return false;
      if (M.Spec.RemoveOnSourceDeath)
      {
        var S = M.Source;
        if (S == null || S.Deceased)
          return false;
      }
      return true;
    }

    private static void TrackSource(Character Source, Character Holder)
    {
      var Index = SourceIndexes.GetValue(Source, _ => new SourceIndex());
      Index.Holders.RemoveAll(Wr => !Wr.TryGetTarget(out var Existing) || Existing == null || Existing.Deceased || Existing == Holder);
      Index.Holders.Add(new WeakReference<Character>(Holder));
    }

    private static int GetAllAppliedBy(Character Holder, Character Source)
    {
      int Count = 0;
      foreach (var M in GetAll(Holder))
      {
        if (M.Source == Source)
          Count++;
      }
      return Count;
    }

    private static int Strongest(int A, int B) => Math.Abs(B) > Math.Abs(A) ? B : A;
    private static float Strongest(float A, float B) => MathF.Abs(B) > MathF.Abs(A) ? B : A;
  }
}
