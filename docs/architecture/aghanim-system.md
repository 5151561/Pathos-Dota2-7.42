# 阿哈利姆神杖与魔晶系统（`Framework/Aghanim/`）

本系统在**零 `Codex.Properties` 占用（57/64）**前提下，以真实 Pathos 物品、持久化存储与 UI 联动的方式还原 DOTA2 7.42 阿哈利姆神杖（Scepter）、阿哈利姆福佑（Blessing）与阿哈利姆魔晶（Shard）。

---

## 1. 三件共享物品

贴图位于 `Resources/Items/aghanim/`（128×128 透明 PNG），由 `AghanimService.Initialize(Ctx)` 在 `DotaHeroesExpansion` 启动时统一注册：

| 物品 ID | 类型 / 商店库存 | 价格 / 稀有度 / 精华 | 生效方式 |
| :--- | :--- | :--- | :--- |
| `aghanims scepter` | 单手长杖武器（`MeleeWeapon` / `staff` / `2d6` 钝击） / `weapon` | `4200g` / `Rarity 2` / `60` | **必须手持装备在主手或副手武器槽生效**（放背包不生效）：提供 `+2` 全六维、`+12` 最大生命、`+9` 最大法力及英雄神杖升级 |
| `aghanims blessing` | 卷轴（`Scroll`） / `scroll` | `1600g` / `Rarity 2` / `25` | 阅读消耗手持或背包中的 `aghanims scepter`，将神杖技能升级永久写入 `DotaCharacterStorage` 并**腾出武器槽**（收回实体神杖属性） |
| `aghanims shard` | 宝石（`Gem`） / `gem` | `1400g` / `Rarity 4` / `20` | 擦拭消耗吸收，永久解锁魔晶升级（写入 `DotaCharacterStorage`，不占装备槽） |

- **Boss 额外掉落**：DOTA2 英雄击杀 `Entity.IsUnique || Entity.IsGuardian` 的怪物时，Lv ≥ 10 且未拥有魔晶有 25% 概率掉落魔晶；Lv ≥ 15 且未拥有神杖/福佑有 15% 概率掉落神杖。

---

## 2. 英雄接入 API

1. 在英雄 `Register(Ctx)` 末尾调用 `AghanimRegistry.Register(new HeroAghanimSpec { ... })`：
   - `Scepter` / `Shard` 指定 `AffectedSpellName`（主动技能）或 `AffectedPassiveId`（被动帮助 ID），`DotaSpellHelpBridge` 会自动在对应技能的帮助面板追加「阿哈利姆神杖 / 魔晶」区块。
   - `TalentTreeUiBridge`（`F2` 面板）会自动在底部渲染神杖与魔晶双图标槽与悬停详情卡片。
2. 运行时在技能/Hook 中直接查询：
   - `AghanimService.HasScepter(ch)`（手持神杖 || 已融合福佑）
   - `AghanimService.HasEquippedScepter(ch)` / `AghanimService.HasScepterBlessing(ch)`
   - `AghanimService.HasShard(ch)`

---

## 3. 持久化与属性守恒

- `DotaCharacterStorage` 自定义整数键（不可改名）：
  - `aghanim.blessing`（`1` = 已融合阿哈利姆福佑）
  - `aghanim.shard`（`1` = 已吸收阿哈利姆魔晶）
  - `aghanim.scepter_vital_applied`（`1` = 已施加实体神杖 `+12 HP / +9 Mana`，卸下或融合福佑时精确扣除，确保读档与切换武器 100% 守恒）
- 实体神杖 `+2` 全六维通过 `DotaHookBus.RegisterActiveFormQuery` 实时注入，不修改永久属性。
