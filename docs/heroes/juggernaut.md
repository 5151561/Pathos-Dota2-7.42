# 主宰（Juggernaut / 尤涅若）

- **参考版本**：DOTA2 7.42（刀塔百科）
- **英雄 ID**：`juggernaut`
- **实现源码**：[Heroes/JuggernautHero.cs](../../Heroes/JuggernautHero.cs)
- **贴图目录**：`Resources/Heroes/juggernaut/`

---

## 1. 种族与职业设定

- **种族与职业**：官方 `人类 (human)` 种族 + 专属职业 `主宰 (juggernaut)`。
- **开局装备**：`+1 武士刀 (katana)`、皮甲、回复药水；1 级自带 **1 点技能点**（通过左下角 `F2` 面板自由加点学习技能）。
- **等级专长**：
  - Lv 1：先天技能 **剑心犹在 (`bladeform`)**（被动 **剑舞 (`blade dance`)** 在技能加点学到 Lv 1 时授予）
  - Lv 4：自由行动 (`free_action`)
  - Lv 8：幻影闪避 (`displacement`)
  - Lv 12：迅捷 (`quickness`)
- **预设英雄**：选人界面可直接选择预设英雄 **Juggernaut** 一键开局（开局/重摇自动锁定初始六维与初始生命/法力）。

---

## 2. DOTA2 7.42 → Pathos 数值映射溯源表

> 统一换算标尺见 [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)：
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；**技能伤害 $K_{\text{dmg}} = 15$**；时间 **1 秒 = 2 回合**（每回合 0.5 秒）。

### 基础属性与资源（DOTA2 7.42）

| 项目 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **初始三维 → 六维** | 力量 `20` / 敏捷 `32` / 智力 `14` | `STR 12 / DEX 17 / CON 12 / INT 9 / WIS 9 / CHA 10` | `MapStartingStats(20, 32, 14, 10)`，开局/重摇锁定 |
| **30 级成长 → 属性上限** | 力量 `+2.0` (`78.0`) / 敏捷 `+2.8` (`113.2`) / 智力 `+1.4` (`54.6`) | `STR 25 / DEX 29 / CON 25 / INT 22 / WIS 22 / CHA 20` | `MapStatLimits` + `RegisterHeroStatLimits` 接管 `GetLimitForm`，突破人类 20 上限，7 级黄点与药水全额生效 |
| **1 级初始蓝量** | `75 + 14 × 12 = 243` | **`12`** | `Round(243 / 20) = 12`；职业 `ManaBase = 12`（`human +1`，`WIS 9 → -1`） |
| **蓝量升级成长** | 智力成长 `+1.4 / 级`（`+16.8` 蓝/级） | 职业 `1d2`（配合 `human +2`） | 低智成长档（$\le 1.8$），10 级约 35 蓝 |
| **生命值成长** | 力量成长 `+2.0 / 级` | 职业 `14 + 1d10`（配合 `human 2 + 1d2`） | 对齐 Pathos 近战战士基准 |

### 技能数值映射（DOTA2 7.42）

| 技能 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **先天 · 剑心犹在** | 每 `2` 秒叠 1 层（上限 10 层），受击粘滞 `2` 秒；每层 `+1%` 移速、`+2.5%` 基础敏捷 | 每 **`4` 回合** 叠 1 层（上限 10 层），受击粘滞 **`4` 回合**；每层 `+1%` 移速、`+2.5%` 攻速与敏捷近战/无敌斩增伤 | `2s × 2 = 4` 回合 |
| **Q · 剑刃风暴** | 蓝耗 `110`；持续 `5` 秒（天赋 `+1` 秒）；每秒伤害 **`85 / 115 / 145 / 175`**（7.41b/7.42，跳伤频率随攻速缩放）；驱散 + 减益免疫 + `80%` 魔抗 | 蓝耗 **`6`**（Lv 1）；持续 **`10 / 10 / 10 / 10 / 12` 回合**；每回合合并结算 1 次，基础伤害 **`1d4 / 1d4+1 / 1d4+2 / 2d4+1 / 2d4+2`**（再乘以攻速频率系数 `1 + (DEX-10)×5% + 剑心层数×2.5%`）；驱散 + 减益免疫 + `80%` 魔法/元素减伤 | `Round(110 / 20) = 6`；`5s → 10` 回合；`MapDotDamageDicePerTurn(DPS × 攻速倍率, 4)`，按 `0.5s / 15` 折算（Champion 对应 `205 DPS`） |
| **W · 治疗守卫** | 蓝耗 `140`；持续 `18 / 20 / 22 / 24` 秒；每秒回 `2% / 3% / 4% / 5%`（魔晶 `6.5%`）；守卫 `1 HP` | 蓝耗 **`7`**（Lv 1）；持续 **`36 / 40 / 44 / 48 / 48` 回合**；每回合回 **`1.0% / 1.5% / 2.0% / 2.5% / 3.25%`**；`1 HP`（上限 1 个，到期/替换彻底消散） | `140 / 20 = 7`；`18~24s × 2 = 36~48` 回合；每回合按 0.5 秒折算回复量，总奶量与原版一致 |
| **E · 剑舞** | 暴击概率 `20% / 25% / 30% / 35%`；暴击倍率 `180%` | 概率 **`20% / 25% / 30% / 35%`**（由独立技能等级 1~4 级直接驱动，脱离武器熟练度）；倍率 **`1.8×`**（普攻/无敌斩/剑刃风暴均可触发） | 直接忠实映射 7.42 四个技能等级 |
| **R · 无敌斩** | 蓝耗 `200 / 250 / 300`；持续 `3 / 3.25 / 3.5` 秒；额外攻击力 **`+25 / +30 / +35`**；期间绝对无敌并连续快速斩击 | 蓝耗 **`13`**（Lv 3，空大返蓝）；连续瞬移无敌斩击 **`5 ~ 12` 次**；单次伤害 = **武器骰 + 属性修正 + `2 / 2 / 2 / 3 / 3`** | 取中值 `Round(250 / 20) = 13`；附加攻击力 `25 / 30 / 35 / 40 / 45` 经 `MapSkillDamage`（`÷ 15`）映射为 `+2 ~ +3` |

---

## 3. 技能机制明细

### 先天技能 — 剑心犹在 (`bladeform`)
- 每连续 `4` 回合未受到伤害叠加 `1` 层（上限 `10` 层）。
- 每层提供 `+1%` 移动速度（满层 `+10%`）与 `+2.5%` 等效基础敏捷加成（攻击速度与近战/无敌斩额外伤害，满层 `+25%`）。
- 受到伤害后进入 **`4` 回合粘滞期**，期满后再消散；无敌斩期间因全程无敌可稳定吃满层数。

### 被动技能 — 剑舞 (`blade dance`)
- 通过技能加点（Lv 1/3/5/7，上限 4 级）独立解锁与升级：近战普攻、无敌斩斩击与剑刃风暴均有 **`20% / 25% / 30% / 35%`** 概率触发 **`1.8×`** 致命一击（完全不依赖武器熟练度）。

### 技能加点与升星双重增幅（左下角面板 / `F2`）
- **Q 剑刃风暴**（Lv 1/3/5/7，上限 4 级）：0→1 学会，升星每级额外 `+15%` 旋转伤害（与塑能学派熟练度双重叠加），满级（★★★）额外 `+2` 回合持续时间。
- **W 治疗守卫**（Lv 1/3/5/7，上限 4 级）：0→1 学会，升星每级额外 `+0.5%/回合` 回复与 `+4` 回合持续时间（与神圣学派熟练度双重叠加）。
- **E 剑舞**（Lv 1/3/5/7，上限 4 级）：独立被动暴击率 `20% / 25% / 30% / 35%`。
- **R 无敌斩**（Lv 6/12/18，上限 3 级）：0→1 学会，升星每级额外 `+2` 斩与 `+2` 斩击攻击力（与变化学派熟练度双重叠加）。
- **+ 全属性加成（黄点）**（上限 7 级，敏捷主属性）：按力/敏/智三系轮转永久提升属性（`Lv 1/4: STR+1, CON+1`、`Lv 2/5: DEX+1`、`Lv 3/6: INT+1, WIS+1`、`Lv 7: CHA+1, DEX+1`，满级合计 `STR+2 / DEX+3 / CON+2 / INT+2 / WIS+2 / CHA+1`，受属性上限约束）。

### 天赋树（DOTA2 原版节奏：`Lv 10/15/20/25` + `Lv 27~30` 双修，选定不可重置）

| 层级（双修） | 左分支（DOTA2 原版 → Pathos 映射） | 右分支（DOTA2 原版 → Pathos 映射） |
| :---: | :--- | :--- |
| **Lv 25** *(Lv 30 双修)* | **剑舞暴击吸血 +40%**：触发剑舞暴击时回复伤害 40% 的生命 | **无敌斩额外 +3 次斩击**（原 `+1s` 持续）：总斩击次数 +3 |
| **Lv 20** *(Lv 29 双修)* | **剑刃风暴每回合基础伤害 +4**（原 `+120` 每秒伤害）：基础每秒伤害 `+120`（折合每回合基础 `+4`，受攻速频率缩放） | **剑舞暴击倍率 +15%**：剑舞暴击倍率 `1.8× → 1.95×` |
| **Lv 15** *(Lv 28 双修)* | **剑刃风暴移速 +15%**（原 `+45` 移速）：旋转期间额外 +15% 移动速度 | **无敌斩蓝耗 -12.5%**（原 `-15s / 120s` 冷却）：无敌斩蓝耗 `13 → 11` |
| **Lv 10** *(Lv 27 双修)* | **剑心犹在叠层间隔减半**（原 `-1s` 间隔）：每 `2` 回合（原 `4` 回合）叠 1 层 | **治疗守卫蓝耗 -20%**（原 `-12s / 60s` 冷却）：治疗守卫蓝耗 `7 → 6` |

---

## 4. 框架基建接入与 Hook 清单

### 接入的公共基建
- **固定初始六维**：`DotaMechanicsMapper.RegisterFixedStartingStats("juggernaut", ...)`
- **天赋树**：`TalentRegistry.Register`（Lv 5/10/15/20 + Lv 22~25 双修，T1 右/T2 右采用声明式 `SpellManaReductionRule`）
- **长短期技能分类**：
  - 长效天赋（原生天赋栏与百科可见）：`bladeform`、`blade dance`（`Ctx.RegisterPassiveProperty`）
  - 短期主动技能状态（原生天赋栏与百科自动隐藏）：`blade fury`、`omnislash`（`Ctx.RegisterSkillStatusProperty`）
- **全射程选格（`DotaRangeTargetingBridge`）**：
  - `omnislash`：`UnitHostile`，射程 `4 / 5 / 5 / 6 / 7`（随变化系熟练度），`AllowSelf: false`
  - `healing ward`：`PointOpen`，射程 `3 / 3 / 4 / 4 / 5`（随牧师系熟练度），`AllowSelf: true`
- **左上角状态栏徽章（`DotaStatusBarBridge`）**：
  - `juggernaut.bladeform`：层数 $> 0$ 时显示 `×1 ~ ×10`，受击粘滞期以橙色（`DarkOrange`）高亮提示，点击可查看「剑心犹在」说明。
- **自定义像素特效（`DotaHeroContext` VFX 工厂）**：
  - 3×3 剑刃风暴金橙旋风（`blade fury spin`）、无敌斩斩击（`omnislash slash`）、无敌斩瞬移残影光束（`omnislash dash`）、剑舞暴击新月（`blade dance crit`）、治疗守卫脉冲（`healing ward pulse`）。

### 注册的 `DotaHookBus` Key
- `dota.fixed_starting_stats`（`AfterCreateActor`，共享框架）：开局/重摇锁定固定六维并重算初始 HP/Mana。
- `juggernaut.spell_casts`（`AfterCastSpell`）：处理剑刃风暴基础驱散与首跳、治疗守卫唯一性替换与精确召唤（`SummonAllyEntity` / `DespawnSummon`）、无敌斩启动与空大返蓝。
- `juggernaut.turn_ticks`（`AfterExecuteCommand`）：驱动剑心犹在叠层/粘滞、剑刃风暴每回合 `3×3` 转伤、治疗守卫每回合光环回血与到期消散、无敌斩自动多回合连斩（`QueueWaitDecision`）。
- `juggernaut.blade_fury_debuff_immunity`（`BeforeApplyTransient`）：剑刃风暴与无敌斩期间减益免疫。
- `juggernaut.bladeform_speed`（`SpeedQuery`）：剑心犹在移速（每层 `+1%`）与攻速（每层 `+2.5%`）加成，以及 T2 左剑刃风暴移速天赋（`+15%`）。
- `juggernaut.damage_modifiers`（`BeforeDamage`）：无敌斩无敌、剑刃风暴 80% 魔法/元素减伤与普攻抑制、剑心犹在增伤、剑舞暴击（含 T3 暴击倍率与 T4 暴击吸血）。
- `juggernaut.bladeform_on_hit`（`AfterDamage`）：受击后使剑心犹在进入粘滞期。

### 常用调参常量（位于 [JuggernautHero.cs](../../Heroes/JuggernautHero.cs) 顶部）
- `DotaBaseStr = 20` / `DotaBaseAgi = 32` / `DotaBaseInt = 14`：DOTA2 7.42 原版初始三维。
- `BladeformMaxStacks = 10`：剑心犹在最大层数。
- `BladeformIntervalTurns = MapDurationTurns(2.0f)`（`4`）：每叠 1 层所需未受伤回合数。
- `BladeformLingerTurns = MapDurationTurns(2.0f)`（`4`）：受击后粘滞保留回合数。
- `BladeFuryManaCost` / `HealingWardManaCost` / `OmnislashManaCost`：统一由 `DotaMechanicsMapper.MapSkillManaCost` 推导。

---

## 5. 贴图资源清单

位于 `Resources/Heroes/juggernaut/`（单份源文件，构建期自动分发至四套图集）：
- **角色与技能图标**：`yurnero.png`（避开官方内置怪物贴图 `juggernaut`）、`bladeform.png`、`blade fury.png`、`healing ward.png`、`healing ward totem.png`、`blade dance.png`、`omnislash.png`
- **自绘像素特效**：
  - 剑刃风暴 3×3 九宫格：`blade fury fx tl/tc/tr/ml/mc/mr/bl/bc/br.png`
  - 无敌斩斩击与 4 方向残影：`omnislash fx strike.png`、`omnislash fx beam h/v/f/b.png`
  - 剑舞暴击与治疗脉冲：`blade dance fx crit.png`、`healing ward fx pulse.png`
