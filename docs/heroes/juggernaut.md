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
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；**技能伤害 $K_{\text{dmg}} = 15$**；时间 **1 秒 = 1 回合**。

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
| **先天 · 剑心犹在** | 每 `2` 秒叠 1 层（上限 `12` 层，7.42a），受击粘滞 `2` 秒；每层 `+1%` 移速、`+2.5%` 基础敏捷 | 每 **`2` 回合** 叠 1 层（上限 12 层），受击粘滞 **`2` 回合**；每层 `+1%` 移速、`+2.5%` 攻速与敏捷近战/无敌斩增伤 | `2s = 2` 回合 |
| **Q · 剑刃风暴** | 蓝耗 `110`；冷却 `30/26/22/18s`；持续 `5` 秒（天赋 `+1` 秒）；每秒伤害 **`85 / 115 / 145 / 175`**（7.41b/7.42，跳伤频率随攻速缩放）；驱散 + 减益免疫 + `80%` 魔抗 | 蓝耗 **`6`**（Lv 1）；冷却 **`30/26/22/18` 回合**；持续 **`5 / 5 / 5 / 5 / 6` 回合**；每回合合并结算 1 次（`MapDotDamageDicePerTurn(DPS × 攻速倍率, 4)`）；驱散 + 减益免疫 + `80%` 魔法/元素减伤 | `Round(110 / 20) = 6`；`MapCooldownTurns`；`5s = 5` 回合（Champion 对应 `205 DPS`） |
| **W · 治疗守卫** | 蓝耗 `110`（7.42a）；冷却 `60s`；持续 `18 / 20 / 22 / 24` 秒；每秒回 `2% / 3% / 4% / 5%`（魔晶 `6.5%`）；守卫 `1 HP` | 蓝耗 **`6`**（Lv 1）；冷却 **`60` 回合**（T1 右 `48` 回合）；持续 **`18 / 20 / 22 / 24 / 24` 回合**；每回合回 **`2% / 3% / 4% / 5% / 6.5%`**；`1 HP`（上限 1 个，到期/替换彻底消散） | `Round(110 / 20) = 6`；`1s = 1` 回合，总奶量与原版一致 |
| **E · 剑舞** | 暴击概率 **`35%`**（各级相同）；暴击倍率 **`140% / 160% / 180% / 200%`**（7.42a `blade_dance_crit_mult`） | 概率 **`35%`**；倍率 **`1.4× / 1.6× / 1.8× / 2.0×`**（由独立技能等级 1~4 级直接驱动，脱离武器熟练度；T3 右天赋 `+0.15×`）；普攻/无敌斩/剑刃风暴均可触发 | 直接忠实映射 7.42a 四个技能等级 |
| **R · 无敌斩** | 蓝耗 `200 / 275 / 350`（7.42a）；冷却 `120s`；持续 `3 / 3.25 / 3.5` 秒；额外攻击力 **`+25 / +30 / +35`**；期间绝对无敌并连续快速斩击 | 蓝耗 **`14`**（Lv 3，空大返蓝且不进冷却）；冷却 **`120` 回合**（T2 右 `105` 回合）；连续瞬移无敌斩击 **`5 ~ 12` 次**；单次伤害 = **武器骰 + 属性修正 + `2 / 2 / 2 / 3 / 3`** | 取中值 `Round(275 / 20) = 14`；`120s = 120` 回合；附加攻击力 `25 / 30 / 35 / 40 / 45` 经 `MapSkillDamage`（`÷ 15`）映射为 `+2 ~ +3` |

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
| **Lv 15** *(Lv 28 双修)* | **剑刃风暴移速 +15%**（原 `+45` 移速）：旋转期间额外 +15% 移动速度 | **-15秒 无敌斩冷却**：无敌斩冷却 `120 → 105` 回合 |
| **Lv 10** *(Lv 27 双修)* | **剑心犹在叠层间隔减半**（原 `-1s` 间隔）：每 `1` 回合（原 `2` 回合）叠 1 层 | **-12秒 治疗守卫冷却**：治疗守卫冷却 `60 → 48` 回合 |

### 阿哈利姆神杖与魔晶（DOTA2 7.42）

| 升级 | DOTA2 7.42 原版效果 | Pathos 实装 |
| :--- | :--- | :--- |
| **神杖 · 剑刃风暴** | 7.42 重做：剑刃风暴期间允许攻击敌人（即使移动也可攻击），造成 `75%` 攻击伤害 | 解除剑刃风暴普攻置零限制，主动普攻造成 `75%` 伤害（可暴击）；移动或等待时每回合自动对相邻随机 1 名敌人挥砍 `75%` 攻击伤害 |
| **魔晶 · 治疗守卫** | 治疗量每秒 `+1.5%`，守卫耐受次数 `+1` | 每回合额外回复 `+0.75%` 最大生命；守卫获得 1 层防护护盾抵挡首次致死伤害（至少需 2 次攻击击毁） |

---

## 4. 框架基建接入与 Hook 清单

### 接入的公共基建
- **英雄档案与初始六维**：`DotaHeroProfile` + `Profile.RegisterStats()` + `Profile.ConfigureClass(...)`
- **技能加点与天赋树**：`HeroSkillRegistry.Register` + `TalentRegistry.Register`（Lv 10/15/20/25 + Lv 27~30 双修，T1 右/T2 右采用声明式 `SpellManaReductionRule`）
- **零自定义 Property**：先天「剑心犹在」与被动「剑舞」均通过 `DotaSpellHelpBridge.RegisterPassive` + `DotaStatusBadge.ForPassive` 提供帮助与徽章，不占用任何 `Codex.Properties`。
- **主动技能声明与全射程选格（`DotaAbility`）**：
  - `blade fury`：`Self`
  - `healing ward`：`PointOpen`，射程 `3 / 3 / 4 / 4 / 5`（随牧师系熟练度），`AllowSelfTarget: true`
  - `omnislash`：`UnitHostile`，射程 `4 / 5 / 5 / 6 / 7`（随变化系熟练度）
- **增益/减益与技能状态（`DotaModifierService`，零 Property）**：
  - `juggernaut.blade_fury`：不可驱散增益，自动生成左上角倒计时徽章、`OnTick` 驱动每回合 `3×3` 旋风伤害、`MoveSpeedPct` 承载 T2 左 `+15%` 移速；注册减益免疫 `juggernaut.blade_fury_debuff_immunity`（`RegisterDebuffImmunity`），期间免疫一切 Hook 减益 Modifier（如斧王战斗饥渴）。
  - `juggernaut.omnislash`：不可驱散状态，左上角徽章实时显示剩余斩数（`×N`），无敌斩结束时移除；期间经 `InvulnerableQuery` 免疫全部伤害与减益。
  - **刻意未迁移**：先天「剑心犹在」是无持续时间的永久叠层计数器（`ConditionalWeakTable<Character, BladeformState>`，受击粘滞期仅为内部子状态，徽章颜色随粘滞期动态变化），移速/攻速由 `juggernaut.bladeform_speed` 独立提供；治疗守卫到期属于召唤物生命周期。
- **左上角状态栏徽章（`DotaStatusBarBridge`）**：
  - `juggernaut.bladeform`：层数 $> 0$ 时显示 `×1 ~ ×12`，受击粘滞期以橙色（`DarkOrange`）高亮提示，点击可查看「剑心犹在」说明。
  - 剑刃风暴与无敌斩徽章由 `DotaModifierService` 自动生成，点击打开对应法术帮助面板。
- **自定义像素特效（`DotaHeroContext` VFX 工厂 + `DotaVfx`）**：
  - 剑刃风暴持续旋风（`blade fury whirl`）、命中切痕（`blade fury cut`）、3×3 回退爆炸（`blade fury spin`）、无敌斩斩击（`omnislash slash`）、无敌斩瞬移残影光束（`omnislash dash`）、剑舞暴击淡金刀光 + 血花（`blade dance crit`）、治疗守卫脉冲（`healing ward pulse`）。

### 注册的 `DotaHookBus` Key
- `dota.fixed_starting_stats`（`AfterCreateActor`，共享框架）：开局/重摇锁定固定六维并重算初始 HP/Mana。
- `dota.abilities.cast`（`AfterCastSpell`，共享框架）：按 `DotaAbility.OnCast` 分发剑刃风暴、治疗守卫与无敌斩（无敌斩连斩由 `DotaPresentation.Sequence` 按真实时间推进）。
- `juggernaut.turn_ticks`（`AfterExecuteCommand`）：驱动剑心犹在叠层/粘滞、治疗守卫每回合光环回血与到期消散（剑刃风暴每回合 `3×3` 转伤由 `BladeFuryModifier.OnTick` 驱动）。
- `juggernaut.omnislash_invulnerable`（`InvulnerableQuery`）：无敌斩期间引擎级无敌/不可选中保护。
- `juggernaut.blade_fury_debuff_immunity`（`BeforeApplyTransient`）：剑刃风暴与无敌斩期间拦截原生负面 `Transient`。
- `juggernaut.bladeform_speed`（`SpeedQuery`）：剑心犹在移速（每层 `+1%`）与攻速（每层 `+2.5%`）加成。
- `juggernaut.damage_modifiers`（`BeforeDamage`, Normal）：剑刃风暴期间原生普攻置零（`Info.Origin == BasicAttack`）、剑心犹在对普攻与无敌斩追加敏捷伤害。
- `juggernaut.blade_fury`（`DotaDamage.RegisterMagicResist`）：剑刃风暴期间 `80%` 魔抗（只减魔法/未知伤害，纯粹与物理不减）。
- `juggernaut.blade_dance`（`DotaDamage.RegisterCritSource`）：剑舞对所有 `CanCrit` 伤害（普攻、无敌斩、剑刃风暴旋风）判定 `1.4x~2.0x`（随剑舞等级，T3 右 `+0.15x`）暴击，`OnCrit` 处理 T4 暴击吸血、特效与日志；与其他暴击来源取最高、不叠加。
- 伤害标注：剑刃风暴旋风 `DotaDamageInfo.Magical(..., Spell | CanCrit)`、无敌斩斩击 `DotaDamageInfo.Physical`（攻击类，可暴击）。
- `juggernaut.bladeform_on_hit`（`AfterDamage`）：受击后使剑心犹在进入粘滞期。

### 常用调参常量（位于 [JuggernautHero.cs](../../Heroes/JuggernautHero.cs) 顶部）
- `Profile`：`BaseStr: 20 (+2.0)` / `BaseAgi: 32 (+2.8)` / `BaseInt: 14 (+1.4)`（DOTA2 7.42 原版初始三维与成长）。
- `BladeformMaxStacks = 12`：剑心犹在最大层数（7.42a）。
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
