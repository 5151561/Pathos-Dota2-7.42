namespace Pathos.DotaHeroes.Framework.Hooks
{
  /// <summary>
  /// 施法事件上下文（对应 Pathos.Director.CastSpell）。
  /// 在 BeforeCastSpell 中可将 Cancel 设为 true 以完全接管施法逻辑。
  /// </summary>
  public sealed class SpellCastEventArgs
  {
    public SpellCastEventArgs(
      object Engine,
      object Director,
      Character Character,
      Spell Spell,
      Asset Asset,
      Direction Direction)
    {
      this.Engine = Engine;
      this.Director = Director;
      this.Character = Character;
      this.Spell = Spell;
      this.Asset = Asset;
      this.Direction = Direction;
    }

    public object Engine { get; }
    public object Director { get; }
    public Character Character { get; }
    public Spell Spell { get; }
    public Asset Asset { get; }
    public Direction Direction { get; }

    /// <summary>
    /// 仅在 BeforeCastSpell 有效：设为 true 将跳过引擎原生 CastSpell 实现。
    /// </summary>
    public bool Cancel { get; set; }
  }

  /// <summary>
  /// 伤害结算事件上下文（对应 Pathos.Engine.DamageCharacter）。
  /// 在 BeforeDamage 中可修改 DamagePoints 或将 Cancel 设为 true 免疫本次伤害。
  /// </summary>
  public sealed class DamageEventArgs
  {
    public DamageEventArgs(
      object Engine,
      Character Attacker,
      Character Defender,
      int DamagePoints,
      string Cause)
    {
      this.Engine = Engine;
      this.Attacker = Attacker;
      this.Defender = Defender;
      this.DamagePoints = DamagePoints;
      this.Cause = Cause;
    }

    public object Engine { get; }
    public Character Attacker { get; }
    public Character Defender { get; }
    public int DamagePoints { get; set; }
    public string Cause { get; }

    /// <summary>
    /// 仅在 BeforeDamage 有效：设为 true 将跳过本次伤害结算。
    /// </summary>
    public bool Cancel { get; set; }
  }

  /// <summary>
  /// 击杀事件上下文（对应 Pathos.Engine.KillCharacter）。
  /// </summary>
  public sealed class KillEventArgs
  {
    public KillEventArgs(object Engine, Character Attacker, Character Defender, string Cause)
    {
      this.Engine = Engine;
      this.Attacker = Attacker;
      this.Defender = Defender;
      this.Cause = Cause;
    }

    public object Engine { get; }
    public Character Attacker { get; }
    public Character Defender { get; }
    public string Cause { get; }
  }

  /// <summary>
  /// 复活事件上下文（对应 Pathos.Engine.ResurrectCharacter）。
  /// </summary>
  public sealed class ResurrectEventArgs
  {
    public ResurrectEventArgs(object Engine, Character Character)
    {
      this.Engine = Engine;
      this.Character = Character;
    }

    public object Engine { get; }
    public Character Character { get; }
  }

  /// <summary>
  /// 角色移动/行动速度查询事件上下文（对应 Pathos.Engine.GetBaseSpeed）。
  /// </summary>
  public sealed class SpeedQueryEventArgs
  {
    public SpeedQueryEventArgs(object Engine, Character Character, bool Attacking, Speed BaseSpeed)
    {
      this.Engine = Engine;
      this.Character = Character;
      this.Attacking = Attacking;
      this.Speed = BaseSpeed;
    }

    public object Engine { get; }
    public Character Character { get; }
    public bool Attacking { get; }
    public Speed Speed { get; set; }
  }

  /// <summary>
  /// 远程攻击距离查询事件上下文（对应 Pathos.Engine.GetShootRange / GetThrowRange）。
  /// </summary>
  public sealed class AttackRangeQueryEventArgs
  {
    public AttackRangeQueryEventArgs(object Engine, Character Character, Asset Asset, bool IsThrow, int Range)
    {
      this.Engine = Engine;
      this.Character = Character;
      this.Asset = Asset;
      this.IsThrow = IsThrow;
      this.Range = Range;
    }

    public object Engine { get; }
    public Character Character { get; }
    public Asset Asset { get; }
    public bool IsThrow { get; }
    public int Range { get; set; }
  }

  /// <summary>
  /// 法术施法距离修正事件上下文（对应 Pathos.Engine.CastEffects）。
  /// </summary>
  public sealed class CastRangeEventArgs
  {
    public CastRangeEventArgs(object Engine, Character Character, Cast Cast)
    {
      this.Engine = Engine;
      this.Character = Character;
      this.Cast = Cast;
    }

    public object Engine { get; }
    public Character Character { get; }
    public Cast Cast { get; }
    public int BonusRange { get; set; }
  }

  /// <summary>
  /// 状态施加事件上下文（对应 Pathos.Engine.ApplyTransient）。
  /// 可修改 Duration 或将 Cancel 设为 true 以免疫该状态。
  /// </summary>
  public sealed class TransientEventArgs
  {
    public TransientEventArgs(object Engine, Character Applier, Character Target, Property Property, Dice Duration)
    {
      this.Engine = Engine;
      this.Applier = Applier;
      this.Target = Target;
      this.Property = Property;
      this.Duration = Duration;
    }

    public object Engine { get; }
    public Character Applier { get; }
    public Character Target { get; }
    public Property Property { get; }
    public Dice Duration { get; set; }
    public bool Cancel { get; set; }
  }
}
