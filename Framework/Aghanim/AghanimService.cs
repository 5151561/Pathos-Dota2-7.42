using System;
using System.Collections.Generic;
using Inv;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Framework.Localization;
using Pathos.DotaHeroes.Framework.UI;

namespace Pathos.DotaHeroes.Framework.Aghanim
{
  /// <summary>
  /// 阿哈利姆神杖、福佑与魔晶核心服务（零 Codex.Properties 占用）：
  /// - 注册 3 件真实 Pathos 物品：
  ///   1. aghanims scepter（4200g，单手长杖武器 2d6，必须手持装备在武器槽生效：+2 全六维、+12 HP、+9 Mana + 神杖升级）
  ///   2. aghanims blessing（1600g 卷轴，消耗手持或背包中的神杖，将神杖技能升级永久写入存档并腾出武器槽，收回实体属性）
  ///   3. aghanims shard（1400g 宝石，消耗吸收永久写入存档，不占格子、无属性加成）
  /// - 统一提供 HasScepter / HasEquippedScepter / HasScepterBlessing / HasShard 查询。
  /// - 负责实体神杖属性与血蓝差值守恒同步、Boss 额外掉落、左上角徽章与帮助面板弹窗。
  /// </summary>
  public static class AghanimService
  {
    public const string ScepterItemName = "aghanims scepter";
    public const string BlessingItemName = "aghanims blessing";
    public const string ShardItemName = "aghanims shard";

    public const string ScepterPassiveHelpId = "aghanim.scepter";
    public const string ShardPassiveHelpId = "aghanim.shard";

    private const string BlessingStorageKey = "aghanim.blessing";
    private const string ShardStorageKey = "aghanim.shard";
    private const string ScepterVitalAppliedKey = "aghanim.scepter_vital_applied";

    // DOTA2 7.42 实体神杖：+10 全属性 / +175 HP / +175 Mana -> Pathos：+2 全六维 / +12 HP / +9 Mana
    public const int ScepterStatBonus = 2;
    public const int ScepterHealthBonus = 12;
    public const int ScepterManaBonus = 9;

    // Boss 额外掉落参数（唯一怪/守卫）
    private const int ShardBossMinLevel = 10;
    private const double ShardBossDropChance = 0.25;
    private const int ScepterBossMinLevel = 15;
    private const double ScepterBossDropChance = 0.15;

    private static readonly Random Rng = new();

    public static Glyph ScepterGlyph { get; private set; }
    public static Glyph BlessingGlyph { get; private set; }
    public static Glyph ShardGlyph { get; private set; }

    public static Item ScepterItem { get; private set; }
    public static Item BlessingItem { get; private set; }
    public static Item ShardItem { get; private set; }

    /// <summary>
    /// 在 DotaHeroesExpansion 构造期、所有英雄注册前调用一次：注册物品、公共 Hook、状态栏与帮助面板。
    /// </summary>
    public static void Initialize(DotaHeroContext Ctx)
    {
      if (Ctx == null)
        throw new ArgumentNullException(nameof(Ctx));

      RegisterItems(Ctx);
      RegisterHooks();
      RegisterStatusBadges();
      RegisterHelpSpecs();
    }

    public static bool HasEquippedScepter(Character Character)
      => Character != null && ScepterItem != null && PathosEngineBridge.HasEquippedItem(Character, ScepterItem);

    public static bool HasScepterBlessing(Character Character)
      => Character != null && DotaCharacterStorage.GetCustomInt(Character, BlessingStorageKey) > 0;

    /// <summary>是否已激活阿哈利姆神杖技能升级（手持实体神杖 或 已融合永久福佑）。</summary>
    public static bool HasScepter(Character Character)
      => HasEquippedScepter(Character) || HasScepterBlessing(Character);

    /// <summary>是否已永久吸收阿哈利姆魔晶。</summary>
    public static bool HasShard(Character Character)
      => Character != null && DotaCharacterStorage.GetCustomInt(Character, ShardStorageKey) > 0;

    private static void RegisterItems(DotaHeroContext Ctx)
    {
      ScepterGlyph = Ctx.RegisterGlyph(ScepterItemName);
      BlessingGlyph = Ctx.RegisterGlyph(BlessingItemName);
      ShardGlyph = Ctx.RegisterGlyph(ShardItemName);

      // 1. 阿哈利姆神杖：单手魔法长杖武器（4200g），必须手持装备才生效
      ScepterItem = Ctx.Items.Add(I =>
      {
        I.Name = ScepterItemName;
        I.Description = "The scepter of a wizard with demigod-like powers. Wielding it in a weapon hand grants +2 to all attributes, +12 max life, +9 max mana, and upgrades a DOTA2 hero's signature ability.";
        I.Glyph = ScepterGlyph;
        I.Stock = Ctx.Stocks.weapon;
        I.Type = ItemType.MeleeWeapon;
        I.Grade = Ctx.Grades.exotic;
        I.Material = Ctx.Materials.mithril;
        I.Size = Size.Medium;
        I.Weight = Weight.FromPounds(4);
        I.Price = Gold.FromCoins(4200);
        I.Rarity = 2;
        I.Essence = Essence.FromUnits(60);
        I.Irreplicable = true;
        I.Sonic = Ctx.Sonics.magic;

        I.SetEquip(EquipAction.Wield, Delay.OneSecond, Ctx.Sonics.chime, Audibility: 10);
        I.SetOneHandedWeapon(
          Ctx.Skills.staff,
          Ctx.Sonics.hit,
          Ctx.Elements.magical,
          DamageType.Bludgeon,
          2.d6(),
          _ => { });
      });

      // 2. 阿哈利姆福佑：融合卷轴（1600g），消耗手持/背包神杖，永久固化神杖技能升级并腾出武器槽
      BlessingItem = Ctx.Items.Add(I =>
      {
        I.Name = BlessingItemName;
        I.Description = "An arcane synthesis scroll. Reading it consumes an Aghanim's Scepter you wield or carry, permanently granting its hero ability upgrade while freeing your weapon slot (without the scepter's raw stats).";
        I.Glyph = BlessingGlyph;
        I.Stock = Ctx.Stocks.scroll;
        I.Type = ItemType.Scroll;
        I.Grade = Ctx.Grades.exotic;
        I.Material = Ctx.Materials.paper;
        I.Size = Size.Tiny;
        I.Weight = Weight.FromUnits(20);
        I.Price = Gold.FromCoins(1600);
        I.Rarity = 2;
        I.Essence = Essence.FromUnits(25);
        I.Irreplicable = true;
        I.Sonic = Ctx.Sonics.magic;

        I.AddObviousUse(Ctx.Motions.read, Delay.OneSecond, Ctx.Sonics.chime, U =>
        {
          U.Consume();
          U.SetCast().SetTargetSelf(true);
          U.Apply.Nothing();
        });
      });

      // 3. 阿哈利姆魔晶：消耗宝石（1400g），吸收后永久解锁魔晶升级
      ShardItem = Ctx.Items.Add(I =>
      {
        I.Name = ShardItemName;
        I.Description = "With origins known only to a single wizard, fragments of this impossible crystal are nearly as coveted as the renowned scepter itself. Rubbing it permanently consumes the shard to unlock a DOTA2 hero's Aghanim's Shard upgrade.";
        I.Glyph = ShardGlyph;
        I.Stock = Ctx.Stocks.gem;
        I.Type = ItemType.Gem;
        I.Grade = Ctx.Grades.exotic;
        I.Material = Ctx.Materials.gemstone;
        I.Size = Size.Tiny;
        I.Weight = Weight.FromUnits(50);
        I.Price = Gold.FromCoins(1400);
        I.Rarity = 4;
        I.Essence = Essence.FromUnits(20);
        I.Irreplicable = true;
        I.Sonic = Ctx.Sonics.magic;

        I.AddObviousUse(Ctx.Motions.rub, Delay.OneSecond, Ctx.Sonics.chime, U =>
        {
          U.Consume();
          U.SetCast().SetTargetSelf(true);
          U.Apply.Nothing();
        });
      });
    }

    private static void RegisterHooks()
    {
      // 1. 实体神杖手持期间提供 +2 全六维（不修改永久属性，卸下立即撤销）
      DotaHookBus.RegisterActiveFormQuery("dota.aghanim.scepter_stats", Args =>
      {
        if (!HasEquippedScepter(Args.Character))
          return;

        Args.BonusStr += ScepterStatBonus;
        Args.BonusDex += ScepterStatBonus;
        Args.BonusCon += ScepterStatBonus;
        Args.BonusInt += ScepterStatBonus;
        Args.BonusWis += ScepterStatBonus;
        Args.BonusCha += ScepterStatBonus;
      });

      // 2. 实体神杖手持状态变化时同步 +12 HP / +9 Mana（差值持久化守恒）
      DotaHookBus.RegisterAfterExecuteCommand("dota.aghanim.sync_vitals", Args =>
      {
        if (Args.Character != null && !Args.Character.Deceased)
          SyncEquippedScepterVitals(Args.Character);
      });

      // 3. 拦截魔晶吸收与福佑融合的前置条件校验（不满足时取消使用、保留物品）
      DotaHookBus.RegisterBeforeUseAsset("dota.aghanim.before_use", Args =>
      {
        var Item = Args.Asset?.Item;
        if (Item == null || Args.Character == null)
          return;

        if (Item == ShardItem)
        {
          if (!AghanimRegistry.HasHeroSpec(Args.Character))
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, ShardGlyph, DotaLocalization.T("aghanim.log.not_dota_hero"));
            Args.Cancel = true;
            return;
          }

          if (HasShard(Args.Character))
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, ShardGlyph, DotaLocalization.T("aghanim.log.shard_already"));
            Args.Cancel = true;
            return;
          }
        }
        else if (Item == BlessingItem)
        {
          if (!AghanimRegistry.HasHeroSpec(Args.Character))
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, BlessingGlyph, DotaLocalization.T("aghanim.log.not_dota_hero"));
            Args.Cancel = true;
            return;
          }

          if (HasScepterBlessing(Args.Character))
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, BlessingGlyph, DotaLocalization.T("aghanim.log.blessing_already"));
            Args.Cancel = true;
            return;
          }

          if (!PathosEngineBridge.HasOwnedItem(Args.Character, ScepterItem))
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Args.Character, BlessingGlyph, DotaLocalization.T("aghanim.log.blessing_need_scepter"));
            Args.Cancel = true;
            return;
          }
        }
      });

      // 4. 魔晶吸收与福佑融合生效
      DotaHookBus.RegisterAfterUseAsset("dota.aghanim.after_use", Args =>
      {
        var Item = Args.Asset?.Item;
        var Ch = Args.Character;
        if (Item == null || Ch == null)
          return;

        if (Item == ShardItem)
        {
          DotaCharacterStorage.SetCustomInt(Ch, ShardStorageKey, 1);
          if (AghanimRegistry.TryGetSpec(Ch, out var Spec) && Spec.Shard != null)
          {
            Spec.Shard.OnGranted?.Invoke(Args.Engine, Ch);
            PathosEngineBridge.WriteInternalLog(
              Args.Engine,
              Ch,
              ShardGlyph,
              DotaLocalization.Format("aghanim.log.shard_consumed", Spec.Shard.Title));
          }
          else
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Ch, ShardGlyph, DotaLocalization.T("aghanim.log.shard_consumed_generic"));
          }

          if (Ch.Square != null)
          {
            PathosEngineBridge.StoryDetecting(Args.Engine, Ch.Square, 2, Colour.DeepSkyBlue);
            PathosEngineBridge.StoryCaption(Args.Engine, Ch.Square, Colour.DeepSkyBlue, DotaLocalization.T("aghanim.caption.shard"), Delayed: false);
          }
        }
        else if (Item == BlessingItem)
        {
          if (!PathosEngineBridge.ConsumeOwnedItem(Args.Engine, Ch, ScepterItem))
            return;

          DotaCharacterStorage.SetCustomInt(Ch, BlessingStorageKey, 1);
          SyncEquippedScepterVitals(Ch);

          if (AghanimRegistry.TryGetSpec(Ch, out var Spec) && Spec.Scepter != null)
          {
            Spec.Scepter.OnGranted?.Invoke(Args.Engine, Ch);
            PathosEngineBridge.WriteInternalLog(
              Args.Engine,
              Ch,
              BlessingGlyph,
              DotaLocalization.Format("aghanim.log.blessing_consumed", Spec.Scepter.Title));
          }
          else
          {
            PathosEngineBridge.WriteInternalLog(Args.Engine, Ch, BlessingGlyph, DotaLocalization.T("aghanim.log.blessing_consumed_generic"));
          }

          if (Ch.Square != null)
          {
            PathosEngineBridge.StoryDetecting(Args.Engine, Ch.Square, 3, Colour.CornflowerBlue);
            PathosEngineBridge.StoryCaption(Args.Engine, Ch.Square, Colour.Gold, DotaLocalization.T("aghanim.caption.blessing"), Delayed: false);
          }
        }
      });

      // 5. 高等级唯一怪/守卫（Boss）额外掉落未拥有的魔晶/神杖
      DotaHookBus.RegisterAfterKill("dota.aghanim.boss_drop", Args =>
      {
        var Killer = Args.Attacker;
        var Victim = Args.Defender;
        var DropSq = Args.DefenderSquare ?? Victim?.Square;
        if (Killer == null || Victim?.Entity == null || DropSq == null)
          return;

        if (!AghanimRegistry.HasHeroSpec(Killer))
          return;

        bool IsBoss = Victim.Entity.IsUnique || Victim.Entity.IsGuardian;
        if (!IsBoss)
          return;

        int BossLv = Math.Max(Victim.Level, Victim.Entity.Level);
        if (BossLv >= ScepterBossMinLevel &&
            !HasScepter(Killer) &&
            !PathosEngineBridge.HasOwnedItem(Killer, ScepterItem) &&
            Rng.NextDouble() < ScepterBossDropChance)
        {
          if (PathosEngineBridge.DropItemAt(Args.Engine, DropSq, ScepterItem))
          {
            PathosEngineBridge.WriteInternalLog(
              Args.Engine,
              Killer,
              ScepterGlyph,
              DotaLocalization.Format("aghanim.log.boss_drop_scepter", DotaCombatHelper.GetDisplayName(Victim)));
          }
          return;
        }

        if (BossLv >= ShardBossMinLevel &&
            !HasShard(Killer) &&
            !PathosEngineBridge.HasOwnedItem(Killer, ShardItem) &&
            Rng.NextDouble() < ShardBossDropChance)
        {
          if (PathosEngineBridge.DropItemAt(Args.Engine, DropSq, ShardItem))
          {
            PathosEngineBridge.WriteInternalLog(
              Args.Engine,
              Killer,
              ShardGlyph,
              DotaLocalization.Format("aghanim.log.boss_drop_shard", DotaCombatHelper.GetDisplayName(Victim)));
          }
        }
      });
    }

    /// <summary>
    /// 按当前是否手持实体神杖，原子同步 +12 最大生命与 +9 最大法力（差值记入 DotaCharacterStorage 保证绝对守恒）。
    /// </summary>
    public static void SyncEquippedScepterVitals(Character Character)
    {
      if (Character == null)
        return;

      bool Equipped = HasEquippedScepter(Character);
      bool Applied = DotaCharacterStorage.GetCustomInt(Character, ScepterVitalAppliedKey) > 0;
      if (Equipped == Applied)
        return;

      if (Equipped)
      {
        DotaLife.AdjustMax(Character, ScepterHealthBonus, DotaLifeAdjustMode.KeepRatio);
        PathosEngineBridge.AdjustMaxMana(Character, ScepterManaBonus, DotaLifeAdjustMode.KeepRatio);
        DotaCharacterStorage.SetCustomInt(Character, ScepterVitalAppliedKey, 1);
      }
      else
      {
        DotaLife.AdjustMax(Character, -ScepterHealthBonus, DotaLifeAdjustMode.KeepRatio);
        PathosEngineBridge.AdjustMaxMana(Character, -ScepterManaBonus, DotaLifeAdjustMode.KeepRatio);
        DotaCharacterStorage.SetCustomInt(Character, ScepterVitalAppliedKey, 0);
      }
    }

    private static void RegisterStatusBadges()
    {
      DotaStatusBarBridge.RegisterProvider("dota.aghanim.scepter", Ch =>
      {
        if (Ch == null || Ch.Deceased || !HasScepter(Ch))
          return null;

        bool IsBlessing = HasScepterBlessing(Ch);
        bool IsEquipped = HasEquippedScepter(Ch);
        string Count = IsBlessing
          ? DotaLocalization.T("aghanim.badge.blessing")
          : DotaLocalization.T("aghanim.badge.equipped");

        return DotaStatusBadge.ForPassive(
          "dota.aghanim.scepter",
          IsBlessing && !IsEquipped ? (BlessingGlyph ?? ScepterGlyph) : ScepterGlyph,
          ScepterPassiveHelpId,
          "aghanim.status.scepter",
          Count: Count,
          Colour: IsBlessing ? Colour.DarkGoldenrod : Colour.DarkBlue);
      });

      DotaStatusBarBridge.RegisterProvider("dota.aghanim.shard", Ch =>
      {
        if (Ch == null || Ch.Deceased || !HasShard(Ch))
          return null;

        return DotaStatusBadge.ForPassive(
          "dota.aghanim.shard",
          ShardGlyph,
          ShardPassiveHelpId,
          "aghanim.status.shard",
          Count: DotaLocalization.T("aghanim.badge.absorbed"),
          Colour: Colour.DarkCyan);
      });
    }

    private static void RegisterHelpSpecs()
    {
      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        Id = ScepterPassiveHelpId,
        IconGlyph = ScepterGlyph,
        TitleResolver = () => DotaLocalization.T("aghanim.help.scepter.title"),
        DescriptionResolver = () => DotaLocalization.T("aghanim.help.scepter.desc"),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (Ch == null)
            return null;

          bool Equipped = HasEquippedScepter(Ch);
          bool Blessing = HasScepterBlessing(Ch);
          string StateText = Blessing && Equipped
            ? DotaHelpColor.Aghanim(DotaLocalization.T("aghanim.state.both"))
            : Blessing
              ? DotaHelpColor.Skill(DotaLocalization.T("aghanim.state.blessing"))
              : Equipped
                ? DotaHelpColor.Aghanim(DotaLocalization.T("aghanim.state.equipped"))
                : DotaLocalization.T("aghanim.state.inactive");

          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("aghanim.help.key_status"), StateText)
          };

          if (Equipped)
          {
            Rows.Add((
              DotaLocalization.T("aghanim.help.key_scepter_stats"),
              DotaLocalization.Format(
                "aghanim.help.val_scepter_stats",
                DotaHelpColor.Stat($"+{ScepterStatBonus}"),
                DotaHelpColor.Heal($"+{ScepterHealthBonus}"),
                DotaHelpColor.Mana($"+{ScepterManaBonus}"))));
          }

          if (AghanimRegistry.TryGetSpec(Ch, out var Spec) && Spec.Scepter != null)
            Rows.Add((DotaLocalization.T("aghanim.help.key_upgrade"), DotaHelpColor.Aghanim(Spec.Scepter.Title)));

          return Rows;
        },
        BuildMechanicsNotes = HelpCtx =>
        {
          var Notes = new List<string>();
          if (HelpCtx.Character != null &&
              AghanimRegistry.TryGetSpec(HelpCtx.Character, out var Spec) &&
              Spec.Scepter != null)
          {
            if (!string.IsNullOrWhiteSpace(Spec.Scepter.Summary))
              Notes.Add(Spec.Scepter.Summary);

            var Bullets = Spec.Scepter.BuildDetailBullets?.Invoke(HelpCtx.Character);
            if (Bullets != null)
              Notes.AddRange(Bullets);
          }

          Notes.Add(DotaLocalization.T("aghanim.help.scepter.acquire"));
          return Notes;
        }
      });

      DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec
      {
        Id = ShardPassiveHelpId,
        IconGlyph = ShardGlyph,
        TitleResolver = () => DotaLocalization.T("aghanim.help.shard.title"),
        DescriptionResolver = () => DotaLocalization.T("aghanim.help.shard.desc"),
        BuildLiveStats = HelpCtx =>
        {
          var Ch = HelpCtx.Character;
          if (Ch == null)
            return null;

          bool Active = HasShard(Ch);
          var Rows = new List<(string, string)>
          {
            (DotaLocalization.T("aghanim.help.key_status"),
              Active
                ? DotaHelpColor.Aghanim(DotaLocalization.T("aghanim.state.absorbed"))
                : DotaLocalization.T("aghanim.state.inactive"))
          };

          if (AghanimRegistry.TryGetSpec(Ch, out var Spec) && Spec.Shard != null)
            Rows.Add((DotaLocalization.T("aghanim.help.key_upgrade"), DotaHelpColor.Aghanim(Spec.Shard.Title)));

          return Rows;
        },
        BuildMechanicsNotes = HelpCtx =>
        {
          var Notes = new List<string>();
          if (HelpCtx.Character != null &&
              AghanimRegistry.TryGetSpec(HelpCtx.Character, out var Spec) &&
              Spec.Shard != null)
          {
            if (!string.IsNullOrWhiteSpace(Spec.Shard.Summary))
              Notes.Add(Spec.Shard.Summary);

            var Bullets = Spec.Shard.BuildDetailBullets?.Invoke(HelpCtx.Character);
            if (Bullets != null)
              Notes.AddRange(Bullets);
          }

          Notes.Add(DotaLocalization.T("aghanim.help.shard.acquire"));
          return Notes;
        }
      });
    }
  }
}
