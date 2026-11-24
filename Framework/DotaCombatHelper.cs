using System;
using System.Collections.Generic;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Targeting;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// DOTA2 英雄共享战斗、选目标、驱散与特效辅助工具集。
  /// 统一收敛原本散落在各英雄文件中的通用逻辑，方便后续新增英雄直接复用。
  /// </summary>
  public static class DotaCombatHelper
  {
    /// <summary>
    /// 判断角色当前职业名是否匹配（忽略大小写）。
    /// </summary>
    public static bool IsClass(Character Character, string ClassName)
    {
      return Character?.Class != null &&
             !string.IsNullOrEmpty(ClassName) &&
             string.Equals(Character.Class.Name, ClassName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断角色当前实体名是否匹配（忽略大小写，常用于识别召唤物实体）。
    /// </summary>
    public static bool IsEntity(Character Character, string EntityName)
    {
      return Character?.Entity != null &&
             !string.IsNullOrEmpty(EntityName) &&
             string.Equals(Character.Entity.Name, EntityName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 将角色在指定技能上的 Pathos 熟练度映射为 1~5 阶整数（Proficient=1 .. Champion=5）。
    /// </summary>
    public static int GetSkillTier(DotaHeroContext Ctx, Character Character, Skill Skill)
    {
      if (Ctx == null || Character == null || Skill == null)
        return 1;

      var Qual = PathosEngineBridge.GetQualification(Character, Skill);
      if (Qual == Ctx.Qualifications.champion) return 5;
      if (Qual == Ctx.Qualifications.master) return 4;
      if (Qual == Ctx.Qualifications.expert) return 3;
      if (Qual == Ctx.Qualifications.specialist) return 2;
      return 1;
    }

    /// <summary>
    /// 判断 Target 是否为 Source 的有效存活敌方目标（排除自身、已死亡、中立与友军）。
    /// </summary>
    public static bool IsHostile(Character Source, Character Target)
    {
      if (Source == null || Target == null || Target == Source || Target.Deceased)
        return false;
      if (Target.Life != null && Target.Life.Points <= 0)
        return false;
      if (Target.Neutral || PathosEngineBridge.IsAllied(Source, Target))
        return false;
      return true;
    }

    /// <summary>
    /// 判断 Target 是否为 Source 的有效存活友方单位（含自身与驯化/召唤盟友）。
    /// </summary>
    public static bool IsFriendly(Character Source, Character Target)
    {
      if (Source == null || Target == null || Target.Deceased)
        return false;
      if (Target.Life != null && Target.Life.Points <= 0)
        return false;
      return PathosEngineBridge.IsAllied(Source, Target);
    }

    /// <summary>
    /// 判断一次伤害是否属于非普通物理攻击来源（法术、元素喷吐、陷阱、环境等）。
    /// 适用于魔法减伤、魔法免疫等机制判定。
    /// </summary>
    public static bool IsNonBasicPhysicalIncoming(DamageEventArgs Args)
    {
      if (DotaHookBus.ActiveSpellCast != null)
        return true;

      return DotaHookBus.ActiveAttack == null;
    }

    /// <summary>
    /// 返回 Pathos 中标准的可基础驱散/减益免疫的负面控制与异常状态集合。
    /// </summary>
    public static Property[] GetStandardNegativeDebuffs(DotaHeroContext Ctx)
    {
      if (Ctx == null)
        return Array.Empty<Property>();

      return new[]
      {
        Ctx.Properties.slowness,
        Ctx.Properties.stunned,
        Ctx.Properties.confusion,
        Ctx.Properties.blindness,
        Ctx.Properties.fear,
        Ctx.Properties.silence,
        Ctx.Properties.paralysis,
        Ctx.Properties.sleeping,
        Ctx.Properties.hallucination,
        Ctx.Properties.fumbling,
        Ctx.Properties.sickness,
        Ctx.Properties.petrifying,
        Ctx.Properties.narcolepsy,
        Ctx.Properties.fainting
      };
    }

    /// <summary>
    /// 判断指定 Property 是否属于标准负面异常状态（用于减益免疫/基础驱散）。
    /// </summary>
    public static bool IsNegativeDebuff(DotaHeroContext Ctx, Property Property)
    {
      if (Ctx == null || Property == null)
        return false;

      return Property == Ctx.Properties.slowness ||
             Property == Ctx.Properties.stunned ||
             Property == Ctx.Properties.confusion ||
             Property == Ctx.Properties.blindness ||
             Property == Ctx.Properties.fear ||
             Property == Ctx.Properties.silence ||
             Property == Ctx.Properties.paralysis ||
             Property == Ctx.Properties.sleeping ||
             Property == Ctx.Properties.hallucination ||
             Property == Ctx.Properties.fumbling ||
             Property == Ctx.Properties.sickness ||
             Property == Ctx.Properties.petrifying ||
             Property == Ctx.Properties.narcolepsy ||
             Property == Ctx.Properties.fainting;
    }

    /// <summary>
    /// 对目标执行 DOTA2 风格「基础驱散」，移除全部标准负面异常状态。
    /// </summary>
    public static void DispelNegativeDebuffs(DotaHeroContext Ctx, object Engine, Character Target)
    {
      if (Ctx == null || Engine == null || Target == null)
        return;

      foreach (var Debuff in GetStandardNegativeDebuffs(Ctx))
      {
        if (Debuff != null && PathosEngineBridge.HasTransient(Target, Debuff))
          PathosEngineBridge.RemoveTransient(Engine, Target, Target, Debuff);
      }
    }

    /// <summary>
    /// 获取角色当前已装备的主手武器 Asset（未装备返回 null）。
    /// </summary>
    public static Asset GetEquippedWeapon(DotaHeroContext Ctx, Character Character)
    {
      if (Ctx == null || Character?.Inventory == null)
        return null;

      foreach (var Asset in Character.Inventory.GetEquippedAssets())
      {
        if (Asset?.Item != null && Asset.Item.Stock == Ctx.Stocks.weapon)
          return Asset;
      }

      return null;
    }

    /// <summary>
    /// 掷骰并返回至少为 1 的结果。
    /// </summary>
    public static int RollDice(Dice D)
    {
      int Total = D.Bonus;
      if (D.Number > 0 && D.Base > 0)
      {
        for (int i = 0; i < D.Number; i++)
          Total += Random.Shared.Next(1, D.Base + 1);
      }
      return Math.Max(1, Total);
    }

    /// <summary>
    /// 返还指定法力值（常用于指向性技能「空大保护」）。
    /// </summary>
    public static void RefundMana(Character Character, int ManaAmount)
    {
      if (Character?.Mana == null || ManaAmount <= 0)
        return;

      int Refunded = Math.Min(Character.Mana.Total, Character.Mana.Points + ManaAmount);
      PathosEngineBridge.SetMeterPoints(Character.Mana, Refunded);
    }

    /// <summary>
    /// 按本次施法「实际扣除」的法力值全额返还（空放保护 / 斩杀刷新）。
    /// 自动包含天赋减蓝耗与熟练度修正，避免按 Spell.Mana 或常量返还导致多返/少返。
    /// </summary>
    public static void RefundSpellCost(SpellCastEventArgs Args)
    {
      if (Args?.Character == null)
        return;

      RefundMana(Args.Character, Args.ManaSpent);
    }

    /// <summary>
    /// 判断某格是否可站立/可召唤（非空、非虚空、非阻挡、无其他角色占据）。
    /// </summary>
    public static bool IsOpenSquare(Square Square)
    {
      return Square != null &&
             Square.Character == null &&
             !Square.IsBlocked(Vision: false) &&
             !Square.IsVoid();
    }

    /// <summary>
    /// 收集指定中心周围 Radius 格内的所有存活敌方单位。
    /// </summary>
    public static List<Character> FindHostilesInRadius(Character Source, Square Center, int Radius)
    {
      var Result = new List<Character>();
      if (Source == null || Center == null || Radius <= 0)
        return Result;

      foreach (var Sq in Center.GetAroundSquares(Radius))
      {
        var Candidate = Sq?.Character;
        if (IsHostile(Source, Candidate))
          Result.Add(Candidate);
      }

      return Result;
    }

    /// <summary>
    /// 在施法者周围 Radius 格内随机挑选一名存活敌方单位（无则返回 null）。
    /// </summary>
    public static Character PickRandomNearbyHostile(Character Caster, int Radius)
    {
      if (Caster?.Square == null)
        return null;

      var Candidates = FindHostilesInRadius(Caster, Caster.Square, Radius);
      if (Candidates.Count == 0)
        return null;

      return Candidates[Random.Shared.Next(Candidates.Count)];
    }

    /// <summary>
    /// 在 TargetSquare 周围 1 格内寻找一个可站立空格（若 Caster 已紧邻目标则优先保持原位）。
    /// </summary>
    public static Square FindOpenAdjacentSquare(Character Caster, Square TargetSquare)
    {
      if (TargetSquare == null)
        return null;

      if (Caster?.Square != null && Caster.Square.IsAdjacent(TargetSquare, 1))
        return Caster.Square;

      var OpenSquares = new List<Square>();
      foreach (var Sq in TargetSquare.GetAroundSquares(1))
      {
        if (IsOpenSquare(Sq))
          OpenSquares.Add(Sq);
      }

      if (OpenSquares.Count == 0)
        return null;

      return OpenSquares[Random.Shared.Next(OpenSquares.Count)];
    }

    /// <summary>
    /// 从 From 向 To 寻找一步可通行的最优相邻格（严格缩短切比雪夫距离）。
    /// </summary>
    public static Square FindStepToward(Square From, Square To)
    {
      if (From == null || To == null || From.Map != To.Map)
        return null;

      Square Best = null;
      int BestDist = From.AsRange(To);
      foreach (var Adj in From.GetAroundSquares(1))
      {
        if (!IsOpenSquare(Adj))
          continue;

        int D = Adj.AsRange(To);
        if (D < BestDist)
        {
          BestDist = D;
          Best = Adj;
        }
      }

      return Best;
    }

    /// <summary>
    /// 解析点地技能（如点地召唤、定点位移）的最终可用落点：
    /// 优先使用 DotaRangeTargetingBridge 捕获的玩家点击格，其次施法方向相邻格；若被占据则自动回退到附近空格。
    /// </summary>
    public static Square ResolveOpenPointTarget(Character Caster, Direction Direction, PendingSpellTarget PendingTarget)
    {
      if (Caster?.Square == null)
        return null;

      Square DesiredSq;
      if (PendingTarget?.TargetSquare != null && PendingTarget.TargetSquare.Map == Caster.Square.Map)
      {
        DesiredSq = PendingTarget.TargetSquare;
      }
      else if (Direction != Direction.Self)
      {
        DesiredSq = Caster.Square.Adjacent(Direction, 1) ?? Caster.Square;
      }
      else
      {
        DesiredSq = Caster.Square;
      }

      if (IsOpenSquare(DesiredSq))
        return DesiredSq;

      return FindOpenAdjacentSquare(Caster, DesiredSq) ??
             FindOpenAdjacentSquare(Caster, Caster.Square) ??
             DesiredSq;
    }

    /// <summary>
    /// 解析指向性敌方技能的初始目标：
    /// 1. 优先使用全射程选格捕获的精确敌方目标（含点击敌人旁 1 格智能吸附）。
    /// 2. 回退沿施法方向直线搜索首个敌方目标。
    /// 3. 最后回退搜索射程内最近的敌方目标。
    /// </summary>
    public static Character ResolveHostileTarget(
      Character Caster,
      Direction Direction,
      int CastRange,
      PendingSpellTarget PendingTarget,
      bool AllowDirectionFallback = true)
    {
      if (Caster?.Square == null)
        return null;

      if (PendingTarget != null)
      {
        if (IsHostile(Caster, PendingTarget.TargetCharacter) &&
            PendingTarget.TargetCharacter.Square != null &&
            Caster.Square.AsRange(PendingTarget.TargetCharacter.Square) <= CastRange)
        {
          return PendingTarget.TargetCharacter;
        }

        if (PendingTarget.TargetSquare != null && PendingTarget.TargetSquare.Map == Caster.Square.Map)
        {
          Character Snapped = null;
          int BestDist = int.MaxValue;
          foreach (var Sq in PendingTarget.TargetSquare.GetAroundSquares(1))
          {
            var Cand = Sq?.Character;
            if (!IsHostile(Caster, Cand) || Caster.Square.AsRange(Sq) > CastRange)
              continue;

            int D = PendingTarget.TargetSquare.AsRange(Sq);
            if (D < BestDist)
            {
              BestDist = D;
              Snapped = Cand;
            }
          }

          if (Snapped != null)
            return Snapped;
        }
      }

      if (!AllowDirectionFallback)
        return null;

      // 沿施法方向直线搜索
      var Cursor = Caster.Square;
      for (int Step = 1; Step <= CastRange; Step++)
      {
        var Next = Cursor.Adjacent(Direction, 1);
        if (Next == null || Next == Cursor)
          break;

        if (IsHostile(Caster, Next.Character))
          return Next.Character;

        if (Next.IsBlocked(Vision: false))
          break;

        Cursor = Next;
      }

      // 回退搜索射程内最近敌人
      Character Nearest = null;
      int NearestDist = int.MaxValue;
      foreach (var Sq in Caster.Square.GetAroundSquares(CastRange))
      {
        var Candidate = Sq?.Character;
        if (!IsHostile(Caster, Candidate))
          continue;

        int Dist = Caster.Square.AsRange(Sq);
        if (Dist < NearestDist)
        {
          NearestDist = Dist;
          Nearest = Candidate;
        }
      }

      return Nearest;
    }

    /// <summary>
    /// 沿 Bresenham 直线在两格之间播放 Pathos 原生 Beam 残影/光束连线特效。
    /// 对齐 Pathos 原生 Beam 节奏：默认基础时长 12 帧（0.2 秒），每前进一格递增 StepIncrementTurns 帧，干净收尾不糊屏。
    /// </summary>
    public static void PlayBeamTrail(
      object Engine,
      Square FromSq,
      Square ToSq,
      Beam Beam,
      int MaxSteps = 12,
      Delay? BaseDelay = null,
      int StepIncrementTurns = 1)
    {
      if (Engine == null || FromSq == null || ToSq == null || FromSq.Map != ToSq.Map || Beam == null)
        return;

      var Map = FromSq.Map;
      var Dir = FromSq.AsDirection(ToSq);
      int X0 = FromSq.X, Y0 = FromSq.Y;
      int X1 = ToSq.X, Y1 = ToSq.Y;
      int Dx = Math.Abs(X1 - X0), Dy = Math.Abs(Y1 - Y0);
      int Sx = X0 < X1 ? 1 : -1, Sy = Y0 < Y1 ? 1 : -1;
      int Err = Dx - Dy;

      int BaseTurns = Math.Max(1, (BaseDelay ?? Delay.FromTurns(12)).GetTurns());
      int StepInc = Math.Max(0, StepIncrementTurns);
      int Steps = 0;
      while (Steps < MaxSteps)
      {
        if (Map.IsValid(X0, Y0))
        {
          var StepDelay = Delay.FromTurns(BaseTurns + Steps * StepInc);
          PathosEngineBridge.StoryBeaming(Engine, StepDelay, Map[X0, Y0], Dir, Beam);
        }

        Steps++;
        if (X0 == X1 && Y0 == Y1)
          break;

        int E2 = 2 * Err;
        if (E2 > -Dy) { Err -= Dy; X0 += Sx; }
        if (E2 < Dx) { Err += Dx; Y0 += Sy; }
      }
    }
  }
}
