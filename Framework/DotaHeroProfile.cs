using System;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// DOTA2 英雄基础属性档案（每个英雄声明一次）：
  /// 记录 DOTA2 7.42 初始三维与每级成长，并统一推导 Pathos 固定初始六维、30 级职业属性上限与职业蓝量基准。
  /// 替代各英雄重复的「Base/Gain 常量 + MapStartingStats + MapStatLimits + RegisterFixedStartingStats + RegisterHeroStatLimits」样板。
  /// </summary>
  public sealed class DotaHeroProfile
  {
    public DotaHeroProfile(
      string ClassName,
      int BaseStr,
      float StrGain,
      int BaseAgi,
      float AgiGain,
      int BaseInt,
      float IntGain,
      int Charisma = 10,
      int CharismaLimit = 20)
    {
      if (string.IsNullOrWhiteSpace(ClassName))
        throw new ArgumentException("ClassName cannot be null or empty.", nameof(ClassName));

      this.ClassName = ClassName;
      this.BaseStr = BaseStr;
      this.StrGain = StrGain;
      this.BaseAgi = BaseAgi;
      this.AgiGain = AgiGain;
      this.BaseInt = BaseInt;
      this.IntGain = IntGain;

      StartingStats = DotaMechanicsMapper.MapStartingStats(BaseStr, BaseAgi, BaseInt, Charisma);
      StatLimits = DotaMechanicsMapper.MapStatLimits(BaseStr, StrGain, BaseAgi, AgiGain, BaseInt, IntGain, CharismaLimit, StartingCha: Charisma);
    }

    /// <summary>Pathos 职业名（Class.Name，存档标识，禁止改名）。</summary>
    public string ClassName { get; }

    public int BaseStr { get; }
    public float StrGain { get; }
    public int BaseAgi { get; }
    public float AgiGain { get; }
    public int BaseInt { get; }
    public float IntGain { get; }

    /// <summary>Pathos 固定初始六维（开局 / 重摇自动锁定）。</summary>
    public PathosStartingStats StartingStats { get; }

    /// <summary>Pathos 职业专属六维上限（DOTA2 30 级自然属性推导）。</summary>
    public PathosStatLimits StatLimits { get; }

    /// <summary>
    /// 注册固定初始六维与职业属性上限（在 <c>IDotaHeroDefinition.RegisterHooks</c> 中调用）。
    /// 天赋属性上限加成由 <c>TalentNode.StatBonus</c> 自动叠加，无需额外注册。
    /// </summary>
    public void RegisterStats()
    {
      DotaMechanicsMapper.RegisterFixedStartingStats(ClassName, StartingStats);
      DotaMechanicsMapper.RegisterHeroStatLimits(ClassName, StatLimits);
    }

    /// <summary>反推职业 ManaAdvancement.HardBase，使 1 级实际初始蓝量精准等于 DOTA2 映射值。</summary>
    public int ComputeClassManaBase(int RaceManaBase)
      => DotaMechanicsMapper.ComputeClassManaBase(BaseInt, RaceManaBase, StartingStats.Wis);

    /// <summary>
    /// 职业通用样板：名称、描述、背包、生命成长、蓝量成长（按种族蓝量基准反推）与全性别头像。
    /// 学派/武器熟练度、属性分配、专长与开局物品仍由各英雄自行声明。
    /// </summary>
    public void ConfigureClass(
      DotaHeroContext Ctx,
      ClassEditor C,
      string Description,
      Glyph AvatarGlyph,
      int LifeBase,
      Dice LifeDice,
      int RaceManaBase,
      Dice ManaDice)
    {
      if (Ctx == null)
        throw new ArgumentNullException(nameof(Ctx));
      if (C == null)
        throw new ArgumentNullException(nameof(C));

      C.Name = ClassName;
      C.Description = Description;
      C.Backpack = Ctx.Items.Backpack;
      C.LifeAdvancement.Set(LifeBase, LifeDice);
      C.ManaAdvancement.Set(ComputeClassManaBase(RaceManaBase), ManaDice);
      Ctx.AddClassAvatars(C, AvatarGlyph);
    }
  }
}
