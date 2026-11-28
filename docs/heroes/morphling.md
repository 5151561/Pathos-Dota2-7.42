# 变体精灵（Morphling / 水人）

- **参考版本**：DOTA2 7.42（官方 Wiki / Liquipedia 核实）
- **英雄 ID**：`morphling`
- **实现源码**：[Heroes/MorphlingHero.cs](../../Heroes/MorphlingHero.cs)
- **贴图目录**：`Resources/Heroes/morphling/`（共 23 张 128×128 PNG：官方全身立绘 + 潮汐水弹 + 7 张原版像素化技能图标 + 14 张像素 VFX）

---

## 1. 种族与职业设定

### 专属种族：`变体水灵 (waterkin)`
- **水元素之躯**：天生水系与寒冰抗性、湿滑（Slippery）、精通游泳、水上通行；弱点为电系（Shock）。
- **生态交互**：遇水回血回蓝、遇火蒸发少量法力；饮用水药水或从喷泉汲取可恢复生命与法力。
- **属性上限**：由 DOTA2 7.42 三维成长统一推导为 `STR 27 / DEX 34 / CON 27 / INT 25 / WIS 25 / CHA 20`（`waterkin` 声明式上限与职业 Hook 上限同步；学习 Lv 20/25 属性天赋后通过 `TalentNode.StatBonus` 声明式拓宽上限）。
- **开局远程武器**：无限远程弹药 **潮汐水弹 (`tidal orb`)**。

### 专属职业：`变体精灵 (morphling)`
- **等级专长**：
  - Lv 1：先天技能 **潮涨潮落 (`ebb and flow`)**（天赋栏与左上角状态栏常驻显示，变形期间持续保留）
  - Lv 4：水系抗性
  - Lv 6：变形控制 (`polymorph_control`)
  - Lv 8：水雾幻影 (`displacement`)
  - Lv 12：澄澈心境 (`clarity`)
- **预设英雄**：选人界面可直接选择预设英雄 **Morphling** 一键开局（开局/重摇自动锁定初始六维与初始生命/法力）；也可将 `变体水灵` 种族或 `变体精灵` 职业与其他官方种族/职业自由混搭。

---

## 2. DOTA2 7.42 → Pathos 数值映射溯源表

> 统一换算标尺见 [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)：
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；**技能伤害 $K_{\text{dmg}} = 15$**；时间 **1 秒 = 1 回合**。

### 基础属性与资源（DOTA2 7.42）

| 项目 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **初始三维 → 六维** | 力量 `16` / 敏捷 `33` / 智力 `19` | `STR 10 / DEX 18 / CON 10 / INT 12 / WIS 12 / CHA 10` | `MapStartingStats(16, 33, 19, 10)`，开局/重摇锁定 |
| **30 级成长 → 属性上限** | 力量 `+2.8` (`97.2`) / 敏捷 `+4.2` (`154.8`) / 智力 `+2.0` (`77.0`) | `STR 27 / DEX 34 / CON 27 / INT 25 / WIS 25 / CHA 20` | `MapStatLimits` + `RegisterHeroStatLimits`（变形期间保留目标上限），Lv20/25 属性天赋动态拓宽上限 |
| **1 级初始蓝量** | `75 + 19 × 12 = 303` | **`15`** | `Round(303 / 20) = 15`；职业 `ManaBase = 12`（`waterkin +2`，`WIS 12 → +1`） |
| **蓝量升级成长** | 智力成长 `+2.0 / 级`（`+24` 蓝/级） | 职业 `1d3`（配合 `waterkin +2`） | 中智成长档（`1.9 ~ 2.8`），10 级约 60 蓝 |
| **生命值成长** | 力量成长 `+2.8 / 级` | 职业 `12 + 1d8`（配合 `waterkin 0`） | 结合属性转换血量联动（每点 `±6 HP`） |

### 技能数值映射（DOTA2 7.42）

| 技能 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **先天 · 潮涨潮落** | 敏捷提供移速（`0.15×AGI`）与远程攻距（`0.25×AGI`）；力量提供施法距离（`0.25×STR`）与减速抗性（`0.25×STR%`）；变形期间保留 | `DEX > 10`：每点 `+2%` 移速（上限 `+50%`）、每 5 点 `+1` 射程；`STR > 10`：每 5 点 `+1` 施法距离、每点 `+4.5%` 减速抗性（`STR ≥ 24` 免疫） | 忠实保留先天双属性方向与变形继承机制，左上角状态栏实时展示当前敏/力倾向 |
| **Q · 波浪形态** | 蓝耗 `115`；冷却 `21/18/15/12s`；射程 `700/775/850/925`；伤害 **`75 / 150 / 225 / 300`** | 蓝耗 **`6`**（Lv 1）；冷却 **`21/18/15/12` 回合**（T4 右 `-40%`）；突进 **`4 / 5 / 6 / 8 / 10` 格**（按熟练度；初始 `700 ÷ 175 码/格 = 4` 格，升星每级再 `+1` 格）+ 4 方向水浪残影 + 沿途波宽半径 1 格水波 **`1d6+2 / 2d6+3 / 4d6+1 / 5d6+3 / 6d6+3`**（期望 `5 ~ 24`）+ 穿墙/幻影闪避 **`3 ~ 6` 回合** | `Round(115 / 20) = 6`；`MapCooldownTurns`；伤害由 `MapSkillDamageDice(75..360)` 自动生成 |
| **W · 变体打击** | 蓝耗 `40/50/60/70`；冷却 `17/14/11/8s`；基础伤害 **`50 / 70 / 90 / 110`** + `0.5× ~ 1.0/1.5/2.0/2.5×` 敏捷伤害；眩晕最短 `0.5s`、最长 **`1.4 / 1.8 / 2.2 / 2.6s`**（7.42a）；击退 `50 ~ 350` | 蓝耗 **`4`**（Lv 1，锚定满级 70 蓝）；冷却 **`17/14/11/8` 回合**（T3 右 `-3` 回合）；基础伤害 **`1d6 / 1d6+1 / 1d6+3 / 2d6 / 2d6+2`** + `0.35× ~ 0.90/1.10/1.30/1.50/1.70× DEX`；最短眩晕 **`1` 回合**，最长眩晕 **`1 / 2 / 2 / 3 / 3` 回合** | `Round(70 / 20) = 4`；`MapCooldownTurns`；基础伤害由 `MapSkillDamageDice(50..130)` 生成；`1.4~3.0s = 1~3` 回合 |
| **E1/E2 · 属性转换** | 转换期间每秒 **`10`** 蓝（7.42a `mana_cost`）；速率 `4/8/12/16` 点/秒；力敏互转同步联动血量 | 蓝耗 **`1`**（每次转换 = 1 回合，无冷却）；每次转换 `1 ~ 5` 点（随变化系熟练度）+ 升星 `+1/级`，总量守恒，每点联动 `±6 HP` | `MapToggleManaPerTurn(10) = 1`；保留极限转力保命，附水花音效与浮字提示 |
| **R1/R2 · 变形 / 解除变形** | 蓝耗 `50` / `0`；冷却 `125/90/55s`；持续 `24s` | 蓝耗 **`6` / `0`**（Lv 1）；冷却 **`125/90/55` 回合** / 无冷却；`ImpersonateEntity` 复制目标外形、属性、技能与法术 | Pathos `ImpersonateEntity` 强度极高，等效蓝耗 `120 → 6` 蓝；`MapCooldownTurns`；解除变形忠实保留 `0` 蓝无冷却 |

### 演出节奏与 VFX 映射（DOTA2 7.42a 官方 datafeed 核对）

| 项目 | DOTA2 7.42a 原版 | Pathos 演出 | 说明 |
| :--- | :--- | :--- | :--- |
| **波浪形态飞行** | `speed = 1250`，施法距离 `700 / 775 / 850 / 925` | 每格飞行时间 = `175 码 ÷ 1250` = `0.14` 秒/格，初始 4 格整段约 `0.56` 秒 | `DotaPresentation` 冻结世界播放：水人贴图 Flinging 飞过路径，浪头经过的格子留下水流残影（`0.5` 秒淡出），并在**浪头扫到时**才结算该格敌人伤害；落地仅播放 3×3 水花演出，不再有额外大范围水爆 |
| **波浪形态伤害范围** | `width = 200`（只伤害路径上的敌人，没有落点 AOE；dota2.com datafeed 7.42a） | 路径每格 + 落点的半径 `1` 格（3 格宽浪头），各阶相同；旧版落点 `2 / 3` 格大范围水爆已移除 | `200 码 ≈ 1 格`（全项目约 `175` 码/格） |

---

## 3. 四层双修天赋树（DOTA2 7.42）

通过左下角 **「技能与天赋」** 面板（`F2`）学习，遵循 DOTA2 原版 `Lv 10 / 15 / 20 / 25` 四层二选一 + `Lv 27 / 28 / 29 / 30` 双修补点节奏：

| 层级 | 解锁 / 双修 | 左分支（7.42 原版 → Pathos 实装） | 右分支（7.42 原版 → Pathos 实装） |
| :---: | :---: | :--- | :--- |
| **T1** | Lv 10 / Lv 27 | **`+12秒 变形持续时间`** (`morph.t1.morph_duration`)<br>原版 `24s → 36s`（+50% 覆盖率）折算为变形蓝耗 `6 → 4`（`-33.3%`）且变形期间额外 `+2` 防御 | **`+15% 魔法抗性`** (`morph.t1.magic_resist`)<br>受到法术、元素与环境等非普通物理伤害减免 `15%` |
| **T2** | Lv 15 / Lv 28 | **`+75 攻击距离`** (`morph.t2.attack_range`)<br>远程攻击距离额外 `+1` 格（变形为远程生物同样保留） | **`+250 波浪形态距离`** (`morph.t2.waveform_range`)<br>波浪形态突进射程额外 `+2` 格（选框/落点/帮助面板同步生效） |
| **T3** | Lv 20 / Lv 29 | **`+15 敏捷`** (`morph.t3.agility`)<br>按 `1:5` 后期属性标尺永久提升 `DEX +3`，并动态拓宽 `DEX` 上限 `+3` | **`-3秒 变体打击冷却`** (`morph.t3.adaptive_strike_mana`)<br>变体打击冷却减少 `3` 回合（`17/14/11/8 → 14/11/8/5` 回合） |
| **T4** | Lv 25 / Lv 30 | **`+35 力量`** (`morph.t4.strength`)<br>按 `1:5` 后期属性标尺永久提升 `STR/CON +7`、最大与当前生命 `+42 HP`，并动态拓宽 `STR/CON` 上限 `+7` | **`-40% 波浪形态冷却`** (`morph.t4.waveform_mana`)<br>波浪形态冷却时间降低 `40%`（`21/18/15/12 → 13/11/9/7` 回合） |

### 阿哈利姆神杖与魔晶（DOTA2 7.42）

| 升级 | DOTA2 7.42 原版效果 | Pathos 实装 |
| :--- | :--- | :--- |
| **神杖 · 变形** | 7.42 重做：变形期间施放指向性技能时，间隔 `0.2s` 出现 `2` 个弱化复制体对 `450` 码内敌人再次施放（伤害 `50% / 25%`） | 变形期间施放变体打击或窃取的指向性伤害法术时，延迟 `0.2s / 0.4s` 触发 2 次水影回响，对原目标 `3` 格内随机敌人（无则对原目标）依次造成 `50% / 25%` 水系伤害 |
| **魔晶 · 属性转换** | 眩晕期间可转换属性，蓝耗降低 `50%` | 属性转换（E1/E2）蓝耗降至 `0`，且处于眩晕/麻痹/沉默等控制状态仍可施放 |

---

## 4. 技能机制明细

### 先天技能 — 潮涨潮落 (`ebb and flow`)
变形期间持续保留，随实时属性动态生效，并在左上角状态栏实时显示当前敏/力配比徽章：
- **敏捷（DEX > 10）**：
  - 每点额外敏捷提供 `+2%` 移动速度（上限 `+50%`）。
  - 每 `5` 点额外敏捷提供 `+1` 格远程攻击距离。
- **力量（STR > 10）**：
  - 每 `5` 点额外力量提供 `+1` 格法术施法距离（含变形后窃取的法术）。
  - 提供减速抗性（每点 `+4.5%`，上限 `75%`；当 `STR ≥ 24` 时直接免疫减速）。

### 主动技能（1 级自带 1 点技能点，通过 `F2` 成长面板学习与升星强化）

| 键位 | 技能名（Glyph / 内部名） | 蓝耗 | 机制与升星双重增幅 |
| :---: | :--- | :---: | :--- |
| **Q** | 波浪形态 (`waveform`) | 6（T4R 降至 4） | 化作浪头突进（附 4 方向水浪残影 Beam），对路径两侧波宽半径 `1` 格的敌人造成水系伤害（7.42a `width = 200`，无落点 AOE）+ 持续 `3 ~ 6` 回合的穿墙与幻影闪避（`phasing` + `displacement`）。升星（Lv 1~4）：每级额外 `+15%` 水波伤害与 `+1` 格突进射程。 |
| **W** | 变体打击 (`adaptive strike`) | 4（T3R 降至 3） | 单技能合一：基础水伤 + 击退 + 眩晕 + 命中单格水爆 Strike 特效；Hook 按实时力敏比与塑能熟练度动态结算——`DEX ≥ 1.5×STR` 达当前阶最高敏捷倍率（`0.90× ~ 1.70× DEX`），`STR ≥ 1.5×DEX` 达最长眩晕时长（`2 ~ 6` 回合）。升星（Lv 1~4）：每级额外 `+15%` 敏捷爆发伤害与 `+1` 回合最大眩晕上限。 |
| **E1 / E2** | 属性转换·敏捷 / 力量 (`morph agility` / `morph strength`) | 0 | 一次加点同步学习/升星双技能；Hook 全权接管：按变化系熟练度（1~5 阶）+ 升星步进（每级 `+1`）单次转换，同步修改 `CurrentForm` 与 `MaximumForm`（总量守恒），转力同步提升最大生命与当前生命（每点 `6 HP`），附水花音效与浮字反馈。 |
| **R1 / R2** | 变形 / 解除变形 (`morph` / `morph revert`) | 6（T1L 降至 4）/ 0 | Lv 6/12/18 解锁与升星，一次加点同步学习/升星双技能；升星每级额外 `+1` 格施法射程并享受原生升星减蓝耗，期间「潮涨潮落」持续保留且状态栏显示变形徽章。 |
| **+** | 全属性加成（黄点） | — | 上限 7 级（敏捷主属性），按力/敏/智三系轮转永久提升（`STR+2 / DEX+3 / CON+2 / INT+2 / WIS+2 / CHA+1`，受属性上限约束）。 |

---

## 5. 框架基建接入与 Hook 清单

### 接入的公共基建
- **固定初始六维与动态上限**：`DotaHeroProfile.RegisterStats`（`RegisterFixedStartingStats` + `RegisterHeroStatLimits`）；Lv 20/25 属性天赋由 `TalentNode.StatBonus` 声明式拓宽上限
- **长效天赋**：`ebb and flow`（`Ctx.RegisterPassiveProperty`，不新增额外 `Codex.Properties`）
- **四层双修天赋树**：`TalentRegistry.Register`
- **左上角状态栏徽章**：`DotaStatusBarBridge`（`morphling.ebb_flow`、`morphling.morphed`）
- **自定义像素 VFX**：`WaveformDashBeam`（4 方向 Beam）、`WaveformSplashExplosion`（3×3 九宫格 Explosion）、`AdaptiveStrikeImpactStrike`（单格 Strike）
- **全射程选格（`DotaRangeTargetingBridge`）**：`waveform`（`PointOpen`）、`adaptive strike`（`UnitHostile`）、`morph`（`UnitHostile`）
- **增益/减益 Modifier（`DotaModifierService`）**：不定义任何 Modifier。变体精灵没有手写的剩余回合计数器：T1 左 `+2` 防御按「是否处于变形」实时判定（非计时）；波浪形态的相位/位移与变体打击眩晕均为原生 Transient，由引擎自行计时；`WaveformOriginSquares` 仅缓存施法起点格。

### 注册的 `DotaHookBus` Key
- `dota.fixed_starting_stats`（`AfterCreateActor`）与 `dota.hero_stat_limits`（`LimitFormQuery`）：锁定固定初始六维、30 级职业上限与 Lv 20/25 天赋动态上限拓宽。
- `morphling.before_cast`（`BeforeCastSpell`）：记录波浪形态起点格。
- `morphling.range_and_vfx`（`AfterCastSpell`）：波浪形态精确落点校准、4 方向水浪残影 Beam 与 3×3 潮汐水爆特效。
- `morphling.attribute_shift`（`AfterCastSpell`）：E1 / E2 属性守恒转换、血量联动与浮字反馈。
- `morphling.talent_morph_defence`（`DefenceQuery`）：T1 左天赋变形期间 `+2` 防御。
- `morphling.ebb_flow.speed`（`SpeedQuery`）：高敏捷移速加成。
- `morphling.ebb_flow.attack_range`（`AttackRangeQuery`）：高敏捷远程射程加成 + T2 左天赋 `+1` 格。
- `morphling.ebb_flow.cast_range`（`CastRange`）：高力量施法距离加成。
- `morphling.transient_hooks`（`BeforeApplyTransient`）：高力量减速抗性 + 变体打击 7.42 眩晕时长缩放。
- `morphling.talent_magic_resist`（`DotaDamage.RegisterMagicResist`）：T1 右天赋 `15%` 魔抗，与其他魔抗乘算叠加，只减魔法/未知伤害。
- 伤害标注：波浪形态、变体打击均为 `DotaDamageInfo.Magical`（水元素 `HarmCharacter`）。

---

## 6. 贴图与像素 VFX 清单（23 张）

位于 `Resources/Heroes/morphling/`（全部 128×128 PNG，构建期自动分发至四套图集）：
- **英雄与武器（2 张）**：`morphling.png`（官方全身立绘 64×64 像素化）、`tidal orb.png`
- **技能图标（7 张，DOTA2 7.42 官方原版图标 32×32 像素化）**：`ebb and flow.png`、`waveform.png`、`adaptive strike.png`、`morph agility.png`、`morph strength.png`、`morph.png`、`morph revert.png`
- **自定义像素 VFX（14 张）**：
  - `waveform fx beam h.png` / `v.png` / `f.png` / `b.png`（4 方向水浪残影）
  - `waveform fx tl.png` / `tc.png` / `tr.png` / `ml.png` / `mc.png` / `mr.png` / `bl.png` / `bc.png` / `br.png`（3×3 潮汐水爆）
  - `adaptive strike fx strike.png`（变体打击单格水爆）
  - 水流残影与 3×3 水爆由 [tools/VfxGen](../../tools/VfxGen/Program.cs) 程序化生成（`dotnet run --project tools/VfxGen -- .`）
