using System;
using System.Collections.Generic;
using System.Threading;
using Pathos.DotaHeroes.Framework.Hooks;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// DOTA2 伤害类型。
  /// </summary>
  public enum DotaDamageType
  {
    /// <summary>未知来源（陷阱、环境、怪物吐息/凝视等原生效果）：按魔法伤害处理，受魔抗减免。</summary>
    Unknown,

    /// <summary>物理伤害：普攻、无敌斩等攻击类伤害，不受魔抗减免。</summary>
    Physical,

    /// <summary>魔法伤害：受魔抗减免（剑刃风暴旋风、波浪形态、变体打击等）。</summary>
    Magical,

    /// <summary>纯粹伤害：无视魔抗（战斗饥渴、反击螺旋、淘汰之刃等）。</summary>
    Pure
  }

  /// <summary>
  /// 伤害附加标记。
  /// </summary>
  [Flags]
  public enum DotaDamageFlags
  {
    None = 0,

    /// <summary>攻击类伤害（普攻、无敌斩斩击）：可触发「受到攻击」类被动。</summary>
    Attack = 1 << 0,

    /// <summary>技能/法术伤害。</summary>
    Spell = 1 << 1,

    /// <summary>持续伤害跳字。</summary>
    DoT = 1 << 2,

    /// <summary>允许触发暴击（剑舞等）。</summary>
    CanCrit = 1 << 3
  }

  /// <summary>
  /// 伤害的来源归类方式。
  /// </summary>
  public enum DotaDamageOrigin
  {
    /// <summary>由扩展包通过 <see cref="DotaDamage"/> 显式标注（英雄技能、Modifier 持续伤害）。</summary>
    Scripted,

    /// <summary>原生普通近战攻击（<c>Director.Attack</c> 期间）。</summary>
    BasicAttack,

    /// <summary>原生法术施放期间（<c>Director.CastSpell</c>）且未显式标注。</summary>
    NativeSpell,

    /// <summary>无法归类的原生伤害（远程投射物、陷阱、怪物特殊攻击、环境等）。</summary>
    Unclassified
  }

  /// <summary>
  /// 一次伤害结算的来源描述，可在 <c>BeforeDamage</c> / <c>AfterDamage</c> 中通过 <c>DamageEventArgs.Info</c> 读取。
  /// </summary>
  public sealed class DotaDamageInfo
  {
    public DotaDamageInfo(string SourceKey, DotaDamageType Type, DotaDamageFlags Flags = DotaDamageFlags.None)
      : this(SourceKey, Type, Flags, DotaDamageOrigin.Scripted)
    {
    }

    internal DotaDamageInfo(string SourceKey, DotaDamageType Type, DotaDamageFlags Flags, DotaDamageOrigin Origin)
    {
      this.SourceKey = SourceKey ?? string.Empty;
      this.Type = Type;
      this.Flags = Flags;
      this.Origin = Origin;
    }

    /// <summary>来源标识（通常为法术名，如 <c>"omnislash"</c>；原生普攻为 <c>"attack"</c>）。</summary>
    public string SourceKey { get; }
    public DotaDamageType Type { get; }
    public DotaDamageFlags Flags { get; }
    public DotaDamageOrigin Origin { get; }

    public bool Has(DotaDamageFlags Flag) => (Flags & Flag) == Flag;
    public bool IsFrom(string Key) => string.Equals(SourceKey, Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否受魔法抗性减免（魔法伤害与无法归类的原生非攻击伤害）。</summary>
    public bool IsMagicResistable => Type == DotaDamageType.Magical || Type == DotaDamageType.Unknown;

    public static DotaDamageInfo Physical(string SourceKey, DotaDamageFlags Flags = DotaDamageFlags.Attack | DotaDamageFlags.CanCrit)
      => new(SourceKey, DotaDamageType.Physical, Flags);

    public static DotaDamageInfo Magical(string SourceKey, DotaDamageFlags Flags = DotaDamageFlags.Spell)
      => new(SourceKey, DotaDamageType.Magical, Flags);

    public static DotaDamageInfo Pure(string SourceKey, DotaDamageFlags Flags = DotaDamageFlags.Spell)
      => new(SourceKey, DotaDamageType.Pure, Flags);
  }

  /// <summary>
  /// 一次暴击判定结果（由 <see cref="DotaDamage.RegisterCritSource"/> 的回调返回；未触发返回 null）。
  /// </summary>
  public sealed class DotaCritRoll
  {
    public DotaCritRoll(float Multiplier, Action<DamageEventArgs> OnCrit = null)
    {
      this.Multiplier = Multiplier;
      this.OnCrit = OnCrit;
    }

    public float Multiplier { get; }

    /// <summary>该暴击被采用后调用（此时 <c>DamagePoints</c> 已乘以倍率），用于特效、日志、暴击吸血等。</summary>
    public Action<DamageEventArgs> OnCrit { get; }
  }

  /// <summary>
  /// DOTA2 统一伤害管线：
  /// 1. <b>来源标注</b>：英雄技能通过 <see cref="Strike"/> / <see cref="Harm"/> / <see cref="BeginScope"/> 结算伤害，
  ///    <c>DamageEventArgs.Info</c> 自动携带伤害类型（物理/魔法/纯粹）、来源 Key 与标记；原生普攻/法术/其他伤害自动归类。
  /// 2. <b>有序修正阶段</b>（挂在 <c>BeforeDamage</c>）：
  ///    - <see cref="DotaHookPriority.Amplify"/>：<see cref="RegisterCritSource"/> 暴击——各来源独立判定，<b>取最高倍率、不叠加</b>（DOTA2 规则）；
  ///    - <see cref="DotaHookPriority.Reduction"/>：<see cref="RegisterMagicResist"/> 魔法抗性——多来源<b>乘算叠加</b>，仅作用于魔法/未知伤害，纯粹与物理伤害不受影响。
  /// </summary>
  public static class DotaDamage
  {
    [ThreadStatic]
    private static List<DotaDamageInfo> ScopeStack;

    private static readonly DotaHookHandlerList<Func<DamageEventArgs, DotaCritRoll>> CritSources = new();
    private static readonly DotaHookHandlerList<Func<DamageEventArgs, float>> MagicResistSources = new();

    private static int InitializedFlag;

    /// <summary>当前线程最内层的伤害标注（无则为 null）。</summary>
    public static DotaDamageInfo CurrentScope
      => ScopeStack != null && ScopeStack.Count > 0 ? ScopeStack[^1] : null;

    /// <summary>
    /// 开启一个伤害标注作用域：作用域内由引擎结算的所有伤害都会携带 <paramref name="Info"/>。
    /// 用于无法直接改用 <see cref="Strike"/> / <see cref="Harm"/> 的原生结算路径。
    /// </summary>
    public static Scope BeginScope(DotaDamageInfo Info)
    {
      ScopeStack ??= new List<DotaDamageInfo>(4);
      ScopeStack.Add(Info);
      return new Scope(ScopeStack.Count);
    }

    /// <summary>以固定点数结算一次带标注的伤害（走 <c>Engine.StrikeCharacter</c>，不经元素抗性）。</summary>
    public static void Strike(object Engine, Character Attacker, Character Defender, Direction Direction, int Damage, DotaDamageInfo Info, Delay? DamageDelay = null)
    {
      using (BeginScope(Info))
        PathosEngineBridge.StrikeCharacter(Engine, Attacker, Defender, Direction, Damage, DamageDelay ?? Delay.Zero, Info?.SourceKey);
    }

    /// <summary>以元素伤害骰结算一次带标注的伤害（走 <c>Engine.HarmCharacter</c>，经元素抗性/弱点）。</summary>
    public static void Harm(object Engine, Character Source, Character Target, Direction Direction, Element Element, Dice DamageDice, DotaDamageInfo Info)
    {
      using (BeginScope(Info))
        PathosEngineBridge.HarmCharacter(Engine, Source, Target, Direction, Element, DamageDice, Modifier.Zero, Info?.SourceKey);
    }

    /// <summary>
    /// 注册暴击来源：回调内自行判断资格并掷概率，触发时返回 <see cref="DotaCritRoll"/>，否则返回 null。
    /// 同一次伤害的多个暴击来源只采用倍率最高的一个。
    /// </summary>
    public static void RegisterCritSource(string Key, Func<DamageEventArgs, DotaCritRoll> TryRoll)
    {
      EnsureInitialized();
      CritSources.Register(Key, TryRoll, DotaHookPriority.Normal);
    }

    /// <summary>
    /// 注册魔法抗性来源：回调返回防御者对本次伤害的魔抗比例（0~1，如剑刃风暴 0.8、水人天赋 0.15）。
    /// 多来源按 1 - Π(1 - r) 乘算叠加，仅作用于 <see cref="DotaDamageInfo.IsMagicResistable"/> 的伤害。
    /// </summary>
    public static void RegisterMagicResist(string Key, Func<DamageEventArgs, float> Resistance)
    {
      EnsureInitialized();
      MagicResistSources.Register(Key, Resistance, DotaHookPriority.Normal);
    }

    /// <summary>计算防御者对本次伤害的总魔抗（未注册或伤害不受魔抗影响时为 0）。</summary>
    public static float GetMagicResistance(DamageEventArgs Args)
    {
      if (Args?.Info == null || !Args.Info.IsMagicResistable || Args.Defender == null)
        return 0.0f;

      float Remaining = 1.0f;
      foreach (var Entry in MagicResistSources.GetSnapshot())
      {
        try
        {
          float R = Math.Clamp(Entry.Handler(Args), 0.0f, 1.0f);
          Remaining *= 1.0f - R;
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Magic resist '{Entry.Key}' failed: {Ex}");
        }
      }

      return 1.0f - Remaining;
    }

    /// <summary>
    /// 为原生伤害事件归类来源（由 HookBootstrap 在 BeforeDamage 分发前调用）。
    /// </summary>
    internal static DotaDamageInfo Classify(Character Attacker, string Cause)
    {
      var Scoped = CurrentScope;
      if (Scoped != null)
        return Scoped;

      var Attack = DotaHookBus.ActiveAttack;
      if (Attack != null && Attacker != null && Attack.Character == Attacker)
        return new DotaDamageInfo("attack", DotaDamageType.Physical, DotaDamageFlags.Attack | DotaDamageFlags.CanCrit, DotaDamageOrigin.BasicAttack);

      var Cast = DotaHookBus.ActiveSpellCast;
      if (Cast != null)
        return new DotaDamageInfo(Cast.Spell?.Name ?? Cause, DotaDamageType.Magical, DotaDamageFlags.Spell, DotaDamageOrigin.NativeSpell);

      return new DotaDamageInfo(Cause, DotaDamageType.Unknown, DotaDamageFlags.None, DotaDamageOrigin.Unclassified);
    }

    private static void EnsureInitialized()
    {
      if (Interlocked.Exchange(ref InitializedFlag, 1) != 0)
        return;

      DotaHookBus.RegisterBeforeDamage("dota.damage.crit", ApplyCrit, DotaHookPriority.Amplify);
      DotaHookBus.RegisterBeforeDamage("dota.damage.magic_resist", ApplyMagicResist, DotaHookPriority.Reduction);
    }

    private static void ApplyCrit(DamageEventArgs Args)
    {
      if (Args.Attacker == null || Args.Attacker == Args.Defender || Args.DamagePoints <= 0 || CritSources.IsEmpty)
        return;

      DotaCritRoll Best = null;
      foreach (var Entry in CritSources.GetSnapshot())
      {
        try
        {
          var Roll = Entry.Handler(Args);
          if (Roll != null && Roll.Multiplier > 1.0f && (Best == null || Roll.Multiplier > Best.Multiplier))
            Best = Roll;
        }
        catch (Exception Ex)
        {
          HookBootstrap.WriteError($"Crit source '{Entry.Key}' failed: {Ex}");
        }
      }

      if (Best == null)
        return;

      Args.DamagePoints = Math.Max(Args.DamagePoints + 1, (int)MathF.Round(Args.DamagePoints * Best.Multiplier));
      Args.CritMultiplier = Best.Multiplier;

      try
      {
        Best.OnCrit?.Invoke(Args);
      }
      catch (Exception Ex)
      {
        HookBootstrap.WriteError($"Crit OnCrit failed: {Ex}");
      }
    }

    private static void ApplyMagicResist(DamageEventArgs Args)
    {
      if (Args.DamagePoints <= 0)
        return;

      float Resist = GetMagicResistance(Args);
      if (Resist <= 0.0f)
        return;

      int Reduced = (int)MathF.Round(Args.DamagePoints * (1.0f - Resist));
      if (Reduced <= 0)
        Args.Cancel = true;
      else
        Args.DamagePoints = Reduced;
    }

    /// <summary>
    /// <see cref="BeginScope"/> 返回的作用域句柄，配合 <c>using</c> 自动出栈。
    /// </summary>
    public readonly struct Scope : IDisposable
    {
      private readonly int Depth;

      internal Scope(int Depth) => this.Depth = Depth;

      public void Dispose()
      {
        var Stack = ScopeStack;
        if (Stack == null || Depth <= 0)
          return;

        // 容错：即使内层作用域未正确释放，也回退到本层之前的深度
        if (Stack.Count >= Depth)
          Stack.RemoveRange(Depth - 1, Stack.Count - Depth + 1);
      }
    }
  }
}
