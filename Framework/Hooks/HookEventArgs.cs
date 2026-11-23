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

  /// <summary>
  /// 角色指令执行完成事件上下文（对应 Pathos.Director.Execute）。
  /// 每次角色（含玩家与随从）完成一回合行动后触发，适合驱动每回合光环与叠层逻辑。
  /// </summary>
  public sealed class CommandEventArgs
  {
    public CommandEventArgs(object Engine, object Director, Character Character, object Command)
    {
      this.Engine = Engine;
      this.Director = Director;
      this.Character = Character;
      this.Command = Command;
    }

    public object Engine { get; }
    public object Director { get; }
    public Character Character { get; }
    public object Command { get; }
  }

  /// <summary>
  /// 角色普通攻击事件上下文（对应 Pathos.Director.Attack）。
  /// </summary>
  public sealed class AttackEventArgs
  {
    public AttackEventArgs(object Engine, object Director, Character Character, Direction Direction)
    {
      this.Engine = Engine;
      this.Director = Director;
      this.Character = Character;
      this.Direction = Direction;
    }

    public object Engine { get; }
    public object Director { get; }
    public Character Character { get; }
    public Direction Direction { get; }

    /// <summary>
    /// 仅在 BeforeAttack 有效：设为 true 将跳过本次攻击结算。
    /// </summary>
    public bool Cancel { get; set; }
  }

  /// <summary>
  /// 角色开局创建或重摇事件上下文（对应 Pathos.Engine.CreateActor / RerollActor）。
  /// 适合锁定 DOTA2 英雄固定初始六维属性并同步重算初始 Life / Mana。
  /// </summary>
  public sealed class ActorCreateEventArgs
  {
    public ActorCreateEventArgs(object Engine, Actor Actor, Character Character, bool IsReroll)
    {
      this.Engine = Engine;
      this.Actor = Actor;
      this.Character = Character;
      this.IsReroll = IsReroll;
    }

    public object Engine { get; }
    public Actor Actor { get; }
    public Character Character { get; }
    public bool IsReroll { get; }
  }

  /// <summary>
  /// 角色防御值（Defence）查询事件上下文（对应 Pathos.Engine.GetDefenceClass）。
  /// 适合实现 DOTA2 被动加甲、光环护甲与减甲 Debuff（直接反映在面板 Defence 与命中判定中）。
  /// </summary>
  public sealed class DefenceQueryEventArgs
  {
    public DefenceQueryEventArgs(object Engine, Character Character, DamageType? DamageType, int Defence)
    {
      this.Engine = Engine;
      this.Character = Character;
      this.DamageType = DamageType;
      this.Defence = Defence;
    }

    public object Engine { get; }
    public Character Character { get; }
    public DamageType? DamageType { get; }
    public int Defence { get; set; }
  }
}
