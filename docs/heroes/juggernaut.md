# 主宰（Juggernaut / 尤涅若）

- **参考版本**：DOTA2 7.42
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
- **预设英雄**：选人界面可直接选择预设英雄 **Juggernaut** 一键开局。

---

## 2. 技能机制明细

### 先天技能 — 剑心犹在 (`bladeform`)
- 每连续 `2` 回合未受到伤害叠加 `1` 层（上限 `10` 层）。
- 每层提供 `+1%` 移动速度（满层 `+10%`）与 `+2.5%` 等效基础敏捷加成（攻击速度与近战/无敌斩额外伤害，满层 `+25%`）。
- 受到伤害后进入 **`2` 回合粘滞期**，期满后再消散；无敌斩期间因全程无敌可稳定吃满层数。

### 被动专长 — 剑舞 (`blade dance`)
- 近战普攻、无敌斩斩击与剑刃风暴均有 **`20% ~ 35%`** 概率（随中刃熟练度成长）触发 **`1.8×`** 致命一击。

### 主动技能（开局法术书自带）

| 键位 | 技能名（Glyph / 内部名） | 蓝耗 | 机制说明 |
| :---: | :--- | :---: | :--- |
| **Q** | 剑刃风暴 (`blade fury`) | 6 | 立即基础驱散自身负面状态并舞起持续 `4 ~ 6` 回合的剑刃风暴；期间免疫负面状态、获得 `80%` 魔法/元素减伤，每回合行动自动对周围 `3×3` 敌人造成魔法伤害（可触发剑舞暴击），且普通物理普攻不造成常规伤害。 |
| **W** | 治疗守卫 (`healing ward`) | 7 | 召唤 `1 HP` 可移动友方随从「治疗守卫」（同时上限 `1` 个，持续 `8 ~ 16` 回合），每回合为周围 `2` 格友方回复 `4% ~ 8%` 最大生命值。 |
| **R** | 无敌斩 (`omnislash`) | 14 | 突进并在周围 `3` 格敌方目标间连续瞬移发动 `5 ~ 12` 次武器物理斩击，全程免疫一切伤害，每次斩击均可触发「剑舞」暴击与「剑心犹在」加成（射程内无目标时自动返还法力）。 |

---

## 3. Hook 注册清单与关键调参常量

### 注册的 `DotaHookBus` Key
- `juggernaut.spell_casts`（`AfterCastSpell`）：处理剑刃风暴首次驱散与转伤、治疗守卫唯一性替换、无敌斩连续瞬移斩击。
- `juggernaut.turn_ticks`（`AfterExecuteCommand`）：驱动剑心犹在叠层/粘滞、剑刃风暴每回合 `3×3` 转伤、治疗守卫每回合光环回血。
- `juggernaut.blade_fury_debuff_immunity`（`BeforeApplyTransient`）：剑刃风暴期间减益免疫。
- `juggernaut.bladeform_speed`（`SpeedQuery`）：剑心犹在移速（每层 `+1%`）与攻速（每层 `+2.5%`）加成。
- `juggernaut.damage_modifiers`（`BeforeDamage`）：无敌斩无敌、剑刃风暴魔抗与普攻抑制、剑舞暴击、剑心犹在增伤。
- `juggernaut.bladeform_on_hit`（`AfterDamage`）：受击后使剑心犹在进入粘滞期。

### 常用调参常量（位于 [JuggernautHero.cs](../../Heroes/JuggernautHero.cs) 顶部）
- `BladeformMaxStacks = 10`：剑心犹在最大层数。
- `BladeformIntervalTurns = 2`：每叠 1 层所需未受伤回合数。
- `BladeformLingerTurns = 2`：受击后粘滞保留回合数。
- `OmnislashManaCost = 14`：无敌斩蓝耗（无目标时按此值返还）。

---

## 4. 贴图资源清单

位于 `Resources/Heroes/juggernaut/<absurd|classic|geoduck|sonyvanda>/`：
- `yurnero.png`（使用 `yurnero` 避免与 Pathos 官方内置怪物贴图 `juggernaut` 重名冲突）
- `bladeform.png`
- `blade fury.png`
- `healing ward.png`
- `blade dance.png`
- `omnislash.png`
