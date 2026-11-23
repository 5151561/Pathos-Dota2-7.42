# 主宰（Juggernaut / 尤涅若）

- **参考版本**：DOTA2 7.42（刀塔百科）
- **英雄 ID**：`juggernaut`
- **实现源码**：[Heroes/JuggernautHero.cs](../../Heroes/JuggernautHero.cs)
- **贴图目录**：`Resources/Heroes/juggernaut/`

---

## 1. 种族与职业设定

- **种族与职业**：官方 `人类 (human)` 种族 + 专属职业 `主宰 (juggernaut)`。
- **开局装备**：`+1 武士刀 (katana)`、皮甲、回复药水及全套专属技能书。
- **等级专长**：
  - Lv 1：先天技能 **剑心犹在 (`bladeform`)** + 被动专长 **剑舞 (`blade dance`)**
  - Lv 4：自由行动 (`free_action`)
  - Lv 8：幻影闪避 (`displacement`)
  - Lv 12：迅捷 (`quickness`)
- **预设英雄**：选人界面可直接选择预设英雄 **Juggernaut** 一键开局（开局/重摇自动锁定初始六维与初始生命/法力）。

---

## 2. DOTA2 7.42 → Pathos 数值映射溯源表

> 统一换算标尺见 [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)：
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；时间 **1 秒 = 2 回合**（每回合 0.5 秒）。

### 基础属性与资源（DOTA2 7.42）

| 项目 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **初始三维 → 六维** | 力量 `20` / 敏捷 `32` / 智力 `14` | `STR 12 / DEX 17 / CON 12 / INT 9 / WIS 9 / CHA 10` | `MapStartingStats(20, 32, 14, 10)`，开局/重摇锁定 |
| **1 级初始蓝量** | `75 + 14 × 12 = 243` | **`12`** | `Round(243 / 20) = 12`；职业 `ManaBase = 12`（`human +1`，`WIS 9 → -1`） |
| **蓝量升级成长** | 智力成长 `+1.4 / 级`（`+16.8` 蓝/级） | 职业 `1d2`（配合 `human +2`） | 低智成长档（$\le 1.8$），10 级约 35 蓝 |
| **生命值成长** | 力量成长 `+2.0 / 级` | 职业 `14 + 1d10`（配合 `human 2 + 1d2`） | 对齐 Pathos 近战战士基准 |

### 技能数值映射（DOTA2 7.42）

| 技能 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **先天 · 剑心犹在** | 每 `2` 秒叠 1 层（上限 10 层），受击粘滞 `2` 秒；每层 `+1%` 移速、`+2.5%` 基础敏捷 | 每 **`4` 回合** 叠 1 层（上限 10 层），受击粘滞 **`4` 回合**；每层 `+1%` 移速、`+2.5%` 攻速与敏捷近战/无敌斩增伤 | `2s × 2 = 4` 回合 |
| **Q · 剑刃风暴** | 蓝耗 `110`；持续 `5` 秒（天赋 `+1` 秒）；驱散 + 减益免疫 + `80%` 魔抗 | 蓝耗 **`6`**（Lv 1）；持续 **`10 / 10 / 10 / 10 / 12` 回合**；驱散 + 减益免疫 + `80%` 魔法/元素减伤 | `Round(110 / 20) = 6`；`5s → 10` 回合（Champion 对应 `+1s` 天赋 → `12` 回合） |
| **W · 治疗守卫** | 蓝耗 `140`；持续 `18 / 20 / 22 / 24` 秒；每秒回 `2% / 3% / 4% / 5%`（魔晶 `6.5%`）；守卫 `1 HP` | 蓝耗 **`7`**（Lv 1）；持续 **`36 / 40 / 44 / 48 / 48` 回合**；每回合回 **`1.0% / 1.5% / 2.0% / 2.5% / 3.25%`**；`1 HP`（上限 1 个，到期/替换彻底消散） | `140 / 20 = 7`；`18~24s × 2 = 36~48` 回合；每回合按 0.5 秒折算回复量，总奶量与原版一致 |
| **E · 剑舞** | 暴击概率 `20% / 25% / 30% / 35%`；暴击倍率 `180%` | 概率 **`20% ~ 35%`**（随中刃熟练度成长）；倍率 **`1.8×`**（普攻/无敌斩/剑刃风暴均可触发） | 直接忠实映射 |
| **R · 无敌斩** | 蓝耗 `200 / 250 / 300`；期间绝对无敌并连续快速斩击 | 蓝耗 **`13`**（Lv 3，空大返蓝）；连续瞬移无敌斩击 **`5 ~ 12` 次** | 取中值 `Round(250 / 20) = 13` |

---

## 3. 技能机制明细

### 先天技能 — 剑心犹在 (`bladeform`)
- 每连续 `4` 回合未受到伤害叠加 `1` 层（上限 `10` 层）。
- 每层提供 `+1%` 移动速度（满层 `+10%`）与 `+2.5%` 等效基础敏捷加成（攻击速度与近战/无敌斩额外伤害，满层 `+25%`）。
- 受到伤害后进入 **`4` 回合粘滞期**，期满后再消散；无敌斩期间因全程无敌可稳定吃满层数。

### 被动专长 — 剑舞 (`blade dance`)
- 近战普攻、无敌斩斩击与剑刃风暴均有 **`20% ~ 35%`** 概率（随中刃熟练度成长）触发 **`1.8×`** 致命一击。

### 主动技能（开局法术书自带）

| 键位 | 技能名（Glyph / 内部名） | 蓝耗 | 机制说明 |
| :---: | :--- | :---: | :--- |
| **Q** | 剑刃风暴 (`blade fury`) | 6 | 立即基础驱散自身负面状态并舞起持续 `10 ~ 12` 回合的剑刃风暴；期间免疫负面状态、获得 `80%` 魔法/元素减伤，每回合行动自动对周围 `3×3` 敌人造成魔法伤害（可触发剑舞暴击），且普通物理普攻不造成常规伤害。 |
| **W** | 治疗守卫 (`healing ward`) | 7 | 召唤 `1 HP` 可移动友方随从「治疗守卫」（同时上限 `1` 个，持续 `36 ~ 48` 回合，到期或重招时通过 `DespawnSummon` 彻底消散），每回合为周围 `2` 格友方回复 `1% ~ 3.25%` 最大生命值。 |
| **R** | 无敌斩 (`omnislash`) | 13 | 突进并在周围 `3` 格敌方目标间连续瞬移发动 `5 ~ 12` 次武器物理斩击，全程免疫一切伤害，每次斩击均可触发「剑舞」暴击与「剑心犹在」加成（射程内无目标时自动返还法力）。 |

---

## 4. Hook 注册清单与关键调参常量

### 注册的 `DotaHookBus` Key
- `dota.fixed_starting_stats`（`AfterCreateActor`，共享框架）：开局/重摇锁定固定六维并重算初始 HP/Mana。
- `juggernaut.spell_casts`（`AfterCastSpell`）：处理剑刃风暴首次驱散与转伤、治疗守卫唯一性替换（`DespawnSummon`）、无敌斩连续瞬移斩击。
- `juggernaut.turn_ticks`（`AfterExecuteCommand`）：驱动剑心犹在叠层/粘滞、剑刃风暴每回合 `3×3` 转伤、治疗守卫每回合光环回血与到期消散。
- `juggernaut.blade_fury_debuff_immunity`（`BeforeApplyTransient`）：剑刃风暴期间减益免疫。
- `juggernaut.bladeform_speed`（`SpeedQuery`）：剑心犹在移速（每层 `+1%`）与攻速（每层 `+2.5%`）加成。
- `juggernaut.damage_modifiers`（`BeforeDamage`）：无敌斩无敌、剑刃风暴魔抗与普攻抑制、剑舞暴击、剑心犹在增伤。
- `juggernaut.bladeform_on_hit`（`AfterDamage`）：受击后使剑心犹在进入粘滞期。

### 常用调参常量（位于 [JuggernautHero.cs](../../Heroes/JuggernautHero.cs) 顶部）
- `DotaBaseStr = 20` / `DotaBaseAgi = 32` / `DotaBaseInt = 14`：DOTA2 7.42 原版初始三维。
- `BladeformMaxStacks = 10`：剑心犹在最大层数。
- `BladeformIntervalTurns = MapDurationTurns(2.0f)`（`4`）：每叠 1 层所需未受伤回合数。
- `BladeformLingerTurns = MapDurationTurns(2.0f)`（`4`）：受击后粘滞保留回合数。
- `BladeFuryManaCost` / `HealingWardManaCost` / `OmnislashManaCost`：统一由 `DotaMechanicsMapper.MapSkillManaCost` 推导。

---

## 5. 贴图资源清单

位于 `Resources/Heroes/juggernaut/<absurd|classic|geoduck|sonyvanda>/`：
- `yurnero.png`（使用 `yurnero` 避免与 Pathos 官方内置怪物贴图 `juggernaut` 重名冲突）
- `bladeform.png`
- `blade fury.png`
- `healing ward.png`
- `blade dance.png`
- `omnislash.png`
