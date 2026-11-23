# 变体精灵（Morphling / 水人）

- **参考版本**：DOTA2 7.42
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
- **预设英雄**：选人界面可直接选择预设英雄 **Morphling** 一键开局；也可将 `变体水灵` 种族或 `变体精灵` 职业与其他官方种族/职业自由混搭。

---

## 2. 技能机制明细

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
| **Q** | 波浪形态 (`waveform`) | 6 | 闪烁突进 + 落地水波 AOE 伤害 + 短暂穿墙与幻影闪避（`phasing` + `displacement`）。 |
| **W** | 变体打击 (`adaptive strike`) | 5 | 单技能合一：基础水伤 + 击退 + 眩晕；Hook 按实时力敏比动态结算——`DEX ≥ 1.5×STR` 达最高爆发伤害倍率，`STR ≥ 1.5×DEX` 达最长眩晕时长。 |
| **E1 / E2** | 属性转换·敏捷 / 力量 (`morph agility` / `morph strength`) | 2 | Hook 全权接管：按变化系熟练度每次转换 `1 ~ 5` 点，同步修改 `CurrentForm` 与 `MaximumForm`（总量守恒、卡住上下限，复活或恢复属性药水不再白嫖）；转力同步提升最大生命与当前生命（每点 `6 HP`），转敏同步降低生命上限与当前生命（最低保底 `1 HP` / 上限保底 `10 HP`）。 |
| **R1 / R2** | 变形 / 解除变形 (`morph` / `morph revert`) | 10 / 2 | 指向目标直接化身复制其外形、属性、技能与法术，并可随时切回本体，期间「潮涨潮落」持续保留。 |

---

## 3. Hook 注册清单与关键调参常量

### 注册的 `DotaHookBus` Key
- `morphling.attribute_shift`（`AfterCastSpell`）：处理 E1 / E2 属性转换与血量联动。
- `morphling.ebb_flow.speed`（`SpeedQuery`）：高敏捷移速加成。
- `morphling.ebb_flow.attack_range`（`AttackRangeQuery`）：高敏捷远程射程加成。
- `morphling.ebb_flow.cast_range`（`CastRange`）：高力量施法距离加成。
- `morphling.transient_hooks`（`BeforeApplyTransient`）：高力量减速抗性 + 变体打击眩晕时长缩放。
- `morphling.adaptive_strike`（`BeforeDamage`）：变体打击按力敏比缩放伤害。

### 常用调参常量（位于 [MorphlingHero.cs](../../Heroes/MorphlingHero.cs) 顶部）
- `MinAttributeScore = 3`：属性转换下限。
- `MinMaxLifePoints = 10`：转敏时最大生命值保底。
- `HpPerShiftedPoint = 6`：每点力量转换对应的生命值变化量。

---

## 4. 贴图资源清单

位于 `Resources/Heroes/morphling/<absurd|classic|geoduck|sonyvanda>/`：
- `morphling.png`
- `waveform.png`
- `adaptive strike.png`
- `morph agility.png`
- `morph strength.png`
- `tidal orb.png`
