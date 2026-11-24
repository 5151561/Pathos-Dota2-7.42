# 变体精灵（Morphling / 水人）

- **参考版本**：DOTA2 7.42（刀塔百科）
- **英雄 ID**：`morphling`
- **实现源码**：[Heroes/MorphlingHero.cs](../../Heroes/MorphlingHero.cs)
- **贴图目录**：`Resources/Heroes/morphling/`

---

## 1. 种族与职业设定

### 专属种族：`变体水灵 (waterkin)`
- **水元素之躯**：天生水系与寒冰抗性、湿滑（Slippery）、精通游泳、水上通行；弱点为电系（Shock）。
- **生态交互**：遇水回血回蓝、遇火蒸发少量法力；饮用水药水或从喷泉汲取可恢复生命与法力。
- **属性上限**：力量 / 敏捷 / 体质上限调整为 `26 / 30 / 26`，为属性转换预留充足空间。
- **开局远程武器**：无限远程弹药 **潮汐水弹 (`tidal orb`)**。

### 专属职业：`变体精灵 (morphling)`
- **等级专长**：
  - Lv 1：先天技能 **潮涨潮落 (`ebb and flow`)**（天赋栏常驻显示，变形期间持续保留）
  - Lv 4：水系抗性
  - Lv 6：变形控制 (`polymorph_control`)
  - Lv 8：水雾幻影 (`displacement`)
  - Lv 12：澄澈心境 (`clarity`)
- **预设英雄**：选人界面可直接选择预设英雄 **Morphling** 一键开局（开局/重摇自动锁定初始六维与初始生命/法力）；也可将 `变体水灵` 种族或 `变体精灵` 职业与其他官方种族/职业自由混搭。

---

## 2. DOTA2 7.42 → Pathos 数值映射溯源表

> 统一换算标尺见 [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)：
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；**技能伤害 $K_{\text{dmg}} = 15$**；时间 **1 秒 = 2 回合**（每回合 0.5 秒）。

### 基础属性与资源（DOTA2 7.42）

| 项目 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **初始三维 → 六维** | 力量 `16` / 敏捷 `33` / 智力 `19` | `STR 10 / DEX 18 / CON 10 / INT 12 / WIS 12 / CHA 10` | `MapStartingStats(16, 33, 19, 10)`，开局/重摇锁定 |
| **1 级初始蓝量** | `75 + 19 × 12 = 303` | **`15`** | `Round(303 / 20) = 15`；职业 `ManaBase = 12`（`waterkin +2`，`WIS 12 → +1`） |
| **蓝量升级成长** | 智力成长 `+2.0 / 级`（`+24` 蓝/级） | 职业 `1d3`（配合 `waterkin +2`） | 中智成长档（`1.9 ~ 2.8`），10 级约 60 蓝 |
| **生命值成长** | 力量成长 `+2.8 / 级` | 职业 `12 + 1d8`（配合 `waterkin 0`） | 结合属性转换血量联动（每点 `±6 HP`） |

### 技能数值映射（DOTA2 7.42）

| 技能 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **先天 · 潮涨潮落** | 敏捷提供移速与攻击距离；力量提供施法距离与减速抗性（变形期间保留） | `DEX > 10`：每点 `+2%` 移速（上限 `+50%`）、每 5 点 `+1` 射程；`STR > 10`：每 5 点 `+1` 施法距离、每点 `+4.5%` 减速抗性（`STR ≥ 24` 免疫） | 忠实保留先天双属性方向与变形继承机制 |
| **Q · 波浪形态** | 蓝耗 `115`；基础伤害 **`75 / 150 / 225 / 300`**；化身波浪无敌位移并造成范围伤害 | 蓝耗 **`6`**（Lv 1）；闪烁 `5 ~ 11` 格 + 落地水波 AOE **`1d6+2 / 2d6+3 / 4d6+1 / 5d6+3 / 6d6+3`**（期望 `5 ~ 24`）+ 穿墙/幻影闪避 **`3 ~ 6` 回合** | `Round(115 / 20) = 6`；伤害由 `MapSkillDamageDice(75..360)` 经 `K_dmg = 15` 自动生成 |
| **W · 变体打击** | 蓝耗 `80`；基础伤害 **`50 / 70 / 90 / 110`** + `0.5× ~ 2.5×` 敏捷伤害；力量比越高眩晕越长（最短 `0.5` 秒，最长 `1.5 / 2 / 2.5 / 3` 秒） | 蓝耗 **`4`**（Lv 1）；基础伤害 **`1d6 / 1d6+1 / 1d6+3 / 2d6 / 2d6+2`** + `0.35× ~ 1.50× DEX`；最短眩晕 **`1` 回合**，最长眩晕 **`3 / 4 / 5 / 6 / 7` 回合** | `80 / 20 = 4`；基础伤害由 `MapSkillDamageDice(50..130)` 生成；`0.5s → 1` 回合，`1.5~3.0s × 2 = 3~6` 回合（Champion `7` 回合） |
| **E1/E2 · 属性转换** | 蓝耗 **`0`**（7.42 零蓝耗）；力敏互转且同步联动血量 | 蓝耗 **`0`**（Lv 1）；每次转换 `1 ~ 5` 点（随变化系熟练度），总量守恒，每点联动 `±6 HP` | 忠实还原 7.42 零蓝耗与极限转力保命 |
| **R1/R2 · 变形 / 解除变形** | 蓝耗 `50` / `0`；化身目标并可随时切回 | 蓝耗 **`6` / `0`**（Lv 1）；直接 `ImpersonateEntity` 复制目标外形、属性、技能与法术 | Pathos `ImpersonateEntity` 强度极高，等效蓝耗 `120 → 6` 蓝；解除变形忠实保留 `0` 蓝 |

---

## 3. 技能机制明细

### 先天技能 — 潮涨潮落 (`ebb and flow`)
变形期间持续保留，随实时属性动态生效：
- **敏捷（DEX > 10）**：
  - 每点额外敏捷提供 `+2%` 移动速度（上限 `+50%`）。
  - 每 `5` 点额外敏捷提供 `+1` 格远程攻击距离。
- **力量（STR > 10）**：
  - 每 `5` 点额外力量提供 `+1` 格法术施法距离（含变形后窃取的法术）。
  - 提供减速抗性（每点 `+4.5%`，上限 `75%`；当 `STR ≥ 24` 时直接免疫减速）。

### 主动技能（开局法术书自带全套）

| 键位 | 技能名（Glyph / 内部名） | 蓝耗 | 机制说明 |
| :---: | :--- | :---: | :--- |
| **Q** | 波浪形态 (`waveform`) | 6 | 闪烁突进 + 落地水波 AOE 伤害 + 持续 `3 ~ 6` 回合的穿墙与幻影闪避（`phasing` + `displacement`）。 |
| **W** | 变体打击 (`adaptive strike`) | 4 | 单技能合一：基础水伤 + 击退 + 眩晕；Hook 按实时力敏比动态结算——`DEX ≥ 1.5×STR` 达最高爆发伤害倍率，`STR ≥ 1.5×DEX` 达最长眩晕时长（`3 ~ 7` 回合）。 |
| **E1 / E2** | 属性转换·敏捷 / 力量 (`morph agility` / `morph strength`) | 0 | Hook 全权接管：按变化系熟练度每次转换 `1 ~ 5` 点，同步修改 `CurrentForm` 与 `MaximumForm`（总量守恒、卡住上下限，复活或恢复属性药水不再白嫖）；转力同步提升最大生命与当前生命（每点 `6 HP`），转敏仅降低生命上限（当前生命仅在超过新上限时截断，保底 `1 HP` / 上限保底 `10 HP`）。 |
| **R1 / R2** | 变形 / 解除变形 (`morph` / `morph revert`) | 6 / 0 | 指向目标直接化身复制其外形、属性、技能与法术，并可随时零蓝耗切回本体，期间「潮涨潮落」持续保留。 |

---

## 4. Hook 注册清单与关键调参常量

### 注册的 `DotaHookBus` Key
- `dota.fixed_starting_stats`（`AfterCreateActor`，共享框架）：开局/重摇锁定固定六维并重算初始 HP/Mana。
- `morphling.attribute_shift`（`AfterCastSpell`）：处理 E1 / E2 属性转换与血量联动。
- `morphling.ebb_flow.speed`（`SpeedQuery`）：高敏捷移速加成。
- `morphling.ebb_flow.attack_range`（`AttackRangeQuery`）：高敏捷远程射程加成。
- `morphling.ebb_flow.cast_range`（`CastRange`）：高力量施法距离加成。
- `morphling.transient_hooks`（`BeforeApplyTransient`）：高力量减速抗性 + 变体打击眩晕时长缩放。
- `morphling.adaptive_strike`（`BeforeDamage`）：变体打击按力敏比缩放伤害。

### 常用调参常量（位于 [MorphlingHero.cs](../../Heroes/MorphlingHero.cs) 顶部）
- `DotaBaseStr = 16` / `DotaBaseAgi = 33` / `DotaBaseInt = 19`：DOTA2 7.42 原版初始三维。
- `MinAttributeScore = 3`：属性转换下限。
- `MinMaxLifePoints = 10`：转敏时最大生命值保底。
- `HpPerShiftedPoint = 6`：每点力量转换对应的生命值变化量。
- `WaveformManaCost` / `AdaptiveStrikeManaCost` / `AttributeShiftManaCost` / `MorphManaCost` / `MorphRevertManaCost`：统一由 `DotaMechanicsMapper.MapSkillManaCost` 推导。

---

## 5. 贴图资源清单

位于 `Resources/Heroes/morphling/`（构建期自动分发至四套图集）：
- `morphling.png`
- `waveform.png`
- `adaptive strike.png`
- `morph agility.png`
- `morph strength.png`
- `tidal orb.png`
