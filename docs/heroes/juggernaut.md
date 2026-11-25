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
| **先天 · 剑心犹在** | 每 `2` 秒叠 1 层（上限 `12` 层，7.42a），受击粘滞 `2` 秒；每层 `+1%` 移速、`+2.5%` 基础敏捷 | 每 **`4` 回合** 叠 1 层（上限 12 层），受击粘滞 **`4` 回合**；每层 `+1%` 移速、`+2.5%` 攻速与敏捷近战/无敌斩增伤 | `2s × 2 = 4` 回合 |
| **Q · 剑刃风暴** | 蓝耗 `110`；持续 `5` 秒（天赋 `+1` 秒）；每秒伤害 **`85 / 115 / 145 / 175`**（7.41b/7.42，跳伤频率随攻速缩放）；驱散 + 减益免疫 + `80%` 魔抗 | 蓝耗 **`6`**（Lv 1）；持续 **`10 / 10 / 10 / 10 / 12` 回合**；每回合合并结算 1 次，基础伤害 **`1d4 / 1d4+1 / 1d4+2 / 2d4+1 / 2d4+2`**（再乘以攻速频率系数 `1 + (DEX-10)×5% + 剑心层数×2.5%`）；驱散 + 减益免疫 + `80%` 魔法/元素减伤 | `Round(110 / 20) = 6`；`5s → 10` 回合；`MapDotDamageDicePerTurn(DPS × 攻速倍率, 4)`，按 `0.5s / 15` 折算（Champion 对应 `205 DPS`） |
| **W · 治疗守卫** | 蓝耗 `110`（7.42a）；持续 `18 / 20 / 22 / 24` 秒；每秒回 `2% / 3% / 4% / 5%`（魔晶 `6.5%`）；守卫 `1 HP` | 蓝耗 **`6`**（Lv 1）；持续 **`36 / 40 / 44 / 48 / 48` 回合**；每回合回 **`1.0% / 1.5% / 2.0% / 2.5% / 3.25%`**；`1 HP`（上限 1 个，到期/替换彻底消散） | `Round(110 / 20) = 6`；`18~24s × 2 = 36~48` 回合；每回合按 0.5 秒折算回复量，总奶量与原版一致 |
| **E · 剑舞** | 暴击概率 **`35%`**（各级相同）；暴击倍率 **`140% / 160% / 180% / 200%`**（7.42a `blade_dance_crit_mult`） | 概率 **`35%`**；倍率 **`1.4× / 1.6× / 1.8× / 2.0×`**（由独立技能等级 1~4 级直接驱动，脱离武器熟练度；T3 右天赋 `+0.15×`）；普攻/无敌斩/剑刃风暴均可触发 | 直接忠实映射 7.42a 四个技能等级 |
| **R · 无敌斩** | 蓝耗 `200 / 275 / 350`（7.42a）；持续 `3 / 3.25 / 3.5` 秒；额外攻击力 **`+25 / +30 / +35`**；期间绝对无敌并连续快速斩击 | 蓝耗 **`14`**（Lv 3，空大返蓝）；连续瞬移无敌斩击 **`5 ~ 12` 次**；单次伤害 = **武器骰 + 属性修正 + `2 / 2 / 2 / 3 / 3`** | 取中值 `Round(275 / 20) = 14`；附加攻击力 `25 / 30 / 35 / 40 / 45` 经 `MapSkillDamage`（`÷ 15`）映射为 `+2 ~ +3` |

### 演出节奏与 VFX 映射（DOTA2 7.42a 官方 datafeed 核对）

| 项目 | DOTA2 7.42a 原版 | Pathos 演出 | 说明 |
| :--- | :--- | :--- | :--- |
| **无敌斩持续时间** | `duration = 3 / 3.25 / 3.5` 秒（`attack_rate_multiplier = 1.4`） | 按 R 技能等级取 `3 / 3.25 / 3.5` 秒，**平均分配到总斩数**，单斩间隔夹在 `0.2 ~ 0.42` 秒 | `DotaPresentation` 时间轴冻结世界逐斩播放；每斩仍放行 1 回合逻辑时间（回合消耗与旧版一致） |
| **无敌斩单斩** | 瞬步至目标身旁挥砍 | `0.08` 秒 Flinging 瞬步 + 断续金色刀光残影（`0.3` 秒淡出）→ 挥砍动作 + 沿攻击方向的金橙利刃斩痕（白热芯，`0.32` 秒，±30° 抖动）；暴击换淡金长弧刀光 + 红色血花 | 原生拖尾全程满亮度，改由 `DotaVfxStyle` 淡入淡出 |
| **无敌斩跳斩范围** | `omni_slash_radius = 425`；只跳向可见敌人；期间主宰自身 `200` 码高空视野 | 下一斩只在当前位置 **`2` 格内且视线不被墙挡** 或 **`1` 格内（高空视野，可穿墙）** 的敌人中随机选择；更远不跳，避免满屏乱窜 | `425 ÷ 175 ≈ 2` 格；`200 ÷ 175 ≈ 1` 格 |
| **无敌斩 × 剑刃风暴** | 两者互斥：剑刃风暴旋转中施放无敌斩会结束剑刃风暴 | 开大时立即移除 `blade fury` 状态（伤害、魔抗、减益免疫与旋风演出一并结束） | — |
| **剑刃风暴旋风** | `blade_fury_damage_tick = 0.2` 秒 | 每 `0.2` 秒（真实时间）刷新一张跟随主宰的 3×3 剑刃旋风，`900°/s` 顺时针旋转；命中敌人身上闪细刃切痕 | 伤害仍按回合结算（`5s → 10` 回合），旋风演出与伤害解耦，等待输入时也持续旋转 |

---

## 3. 技能机制明细

### 先天技能 — 剑心犹在 (`bladeform`)
- 每连续 `4` 回合未受到伤害叠加 `1` 层（上限 `10` 层）。
- 每层提供 `+1%` 移动速度（满层 `+10%`）与 `+2.5%` 等效基础敏捷加成（攻击速度与近战/无敌斩额外伤害，满层 `+25%`）。
- 受到伤害后进入 **`4` 回合粘滞期**，期满后再消散；无敌斩期间因全程无敌可稳定吃满层数。

### 被动技能 — 剑舞 (`blade dance`)
- 通过技能加点（Lv 1/3/5/7，上限 4 级）独立解锁与升级：近战普攻、无敌斩斩击与剑刃风暴均有 **`35%`** 概率触发 **`1.4× / 1.6× / 1.8× / 2.0×`**（随剑舞等级）致命一击（完全不依赖武器熟练度）。

### 技能加点与升星双重增幅（左下角面板 / `F2`）
- **Q 剑刃风暴**（Lv 1/3/5/7，上限 4 级）：0→1 学会，升星每级额外 `+15%` 旋转伤害（与塑能学派熟练度双重叠加），满级（★★★）额外 `+2` 回合持续时间。
- **W 治疗守卫**（Lv 1/3/5/7，上限 4 级）：0→1 学会，升星每级额外 `+0.5%/回合` 回复与 `+4` 回合持续时间（与神圣学派熟练度双重叠加）。
- **E 剑舞**（Lv 1/3/5/7，上限 4 级）：独立被动：暴击率固定 `35%`，暴击倍率 `1.4× / 1.6× / 1.8× / 2.0×`。
- **R 无敌斩**（Lv 6/12/18，上限 3 级）：0→1 学会，升星每级额外 `+2` 斩与 `+2` 斩击攻击力（与变化学派熟练度双重叠加）。
- **+ 全属性加成（黄点）**（上限 7 级，敏捷主属性）：按力/敏/智三系轮转永久提升属性（`Lv 1/4: STR+1, CON+1`、`Lv 2/5: DEX+1`、`Lv 3/6: INT+1, WIS+1`、`Lv 7: CHA+1, DEX+1`，满级合计 `STR+2 / DEX+3 / CON+2 / INT+2 / WIS+2 / CHA+1`，受属性上限约束）。

### 天赋树（DOTA2 原版节奏：`Lv 10/15/20/25` + `Lv 27~30` 双修，选定不可重置）

| 层级（双修） | 左分支（DOTA2 原版 → Pathos 映射） | 右分支（DOTA2 原版 → Pathos 映射） |
| :---: | :--- | :--- |
| **Lv 25** *(Lv 30 双修)* | **剑舞暴击吸血 +40%**：触发剑舞暴击时回复伤害 40% 的生命 | **无敌斩额外 +3 次斩击**（原 `+1s` 持续）：总斩击次数 +3 |
| **Lv 20** *(Lv 29 双修)* | **剑刃风暴每回合基础伤害 +4**（原 `+120` 每秒伤害）：基础每秒伤害 `+120`（折合每回合基础 `+4`，受攻速频率缩放） | **剑舞暴击倍率 +15%**：剑舞暴击倍率 `+0.15×`（如 4 级 `2.0× → 2.15×`） |
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
- **增益/减益 Modifier（`DotaModifierService`）**：
  - 主宰自身不定义任何 Modifier，仅注册减益免疫 `juggernaut.blade_fury_debuff_immunity`（`RegisterDebuffImmunity`）：剑刃风暴期间免疫一切 Hook 实现的减益 Modifier（如斧王战斗饥渴）；无敌斩期间经 `InvulnerableQuery` 同样免疫。
  - **刻意未迁移**：先天「剑心犹在」是无持续时间的永久叠层计数器（`ConditionalWeakTable<Character, BladeformState>`，粘滞期仅为内部子状态，徽章颜色动态变化，层数在 `juggernaut.turn_ticks` 同回合内被剑刃风暴读取），其移速/攻速仍由 `juggernaut.bladeform_speed` 独立相乘；T2 左剑刃风暴 `+15%` 移速绑定原生 `blade fury` Transient；治疗守卫到期属于召唤物生命周期；无敌斩为连斩会话——均不属于「带回合数的增益/减益」。
- **自定义像素特效（`DotaHeroContext` VFX 工厂）**：
  - 3×3 剑刃风暴红色新月扫痕 + 钢剑（`blade fury spin`）、无敌斩斩击（`omnislash slash`）、无敌斩瞬移残影光束（`omnislash dash`）、剑舞暴击淡金刀光 + 血花（`blade dance crit`）、治疗守卫脉冲（`healing ward pulse`）。

### 注册的 `DotaHookBus` Key
- `dota.fixed_starting_stats`（`AfterCreateActor`，共享框架）：开局/重摇锁定固定六维并重算初始 HP/Mana。
- `juggernaut.spell_casts`（`AfterCastSpell`）：处理剑刃风暴基础驱散与首跳、治疗守卫唯一性替换与精确召唤（`SummonAllyEntity` / `DespawnSummon`）、无敌斩启动与空大返蓝。
- `juggernaut.turn_ticks`（`AfterExecuteCommand`）：驱动剑心犹在叠层/粘滞、剑刃风暴每回合 `3×3` 转伤、治疗守卫每回合光环回血与到期消散、无敌斩自动多回合连斩（`QueueWaitDecision`）。
- `juggernaut.blade_fury_debuff_immunity`（`BeforeApplyTransient`）：剑刃风暴与无敌斩期间减益免疫。
- `juggernaut.bladeform_speed`（`SpeedQuery`）：剑心犹在移速（每层 `+1%`）与攻速（每层 `+2.5%`）加成，以及 T2 左剑刃风暴移速天赋（`+15%`）。
- `juggernaut.damage_modifiers`（`BeforeDamage`, Normal）：剑刃风暴期间原生普攻置零（`Info.Origin == BasicAttack`）、剑心犹在对普攻与无敌斩追加敏捷伤害。
- `juggernaut.blade_fury`（`DotaDamage.RegisterMagicResist`）：剑刃风暴期间 `80%` 魔抗（只减魔法/未知伤害，纯粹与物理不减）。
- `juggernaut.blade_dance`（`DotaDamage.RegisterCritSource`）：剑舞对所有 `CanCrit` 伤害（普攻、无敌斩、剑刃风暴旋风）判定 `1.4x~2.0x`（随剑舞等级，T3 右 `+0.15x`）暴击，`OnCrit` 处理 T4 暴击吸血、特效与日志；与其他暴击来源取最高、不叠加。
- 伤害标注：剑刃风暴旋风 `DotaDamageInfo.Magical(..., Spell | CanCrit)`、无敌斩斩击 `DotaDamageInfo.Physical`（攻击类，可暴击）；无敌斩无敌由 `juggernaut.omnislash_invulnerable` 在引擎层拦截。
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
  - 剑刃风暴旋风（单张跨 3×3 旋转）与命中切痕：`blade fury whirl.png`、`blade fury cut.png`；3×3 九宫格回退：`blade fury fx tl/tc/tr/ml/mc/mr/bl/bc/br.png`
  - 无敌斩斩痕与 4 方向残影：`omnislash fx strike.png`、`omnislash fx beam h/v/f/b.png`
  - 剑舞暴击与治疗脉冲：`blade dance fx crit.png`、`healing ward fx pulse.png`
  - 除 `healing ward fx pulse.png` 外均由 [tools/VfxGen](../../tools/VfxGen/Program.cs) 程序化生成（`dotnet run --project tools/VfxGen -- .`）
