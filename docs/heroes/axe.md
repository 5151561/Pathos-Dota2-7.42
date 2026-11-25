# 斧王（Axe / 蒙哥可汗）

- **参考版本**：DOTA2 7.42（刀塔百科 / Liquipedia）
- **英雄 ID**：`axe`
- **实现源码**：[Heroes/AxeHero.cs](../../Heroes/AxeHero.cs)
- **贴图目录**：`Resources/Heroes/axe/`

---

## 1. 种族与职业设定

- **种族与职业**：官方 `兽人 (orc)` 种族 + 专属职业 `斧王 (axe)`。
- **开局装备**：`+1 斧 (axe)`、`链甲 (chain_mail)`、治疗药水、回蓝药水、口粮；1 级自带 **1 点技能点**（通过左下角 `F2` 面板自由加点学习技能）。
- **等级专长**：
  - Lv 1：先天技能 **一人之军 (`one man army`)**（被动 **反击螺旋 (`counter helix`)** 在技能加点学到 Lv 1 时授予）
  - Lv 4：自由行动 (`free_action`)
  - Lv 8：明晰 (`clarity`)
  - Lv 12：迅捷 (`quickness`)
- **预设英雄**：选人界面可直接选择预设英雄 **Axe** 一键开局（开局/重摇自动锁定初始六维与初始生命/法力）。

---

## 2. DOTA2 7.42 → Pathos 数值映射溯源表

> 统一换算标尺见 [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)：
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；**技能伤害 $K_{\text{dmg}} = 15$**；时间 **1 秒 = 2 回合**（每回合 0.5 秒）；护甲统一经 `MapArmorToDefence` 映射为 Pathos `Defence`。

### 基础属性与资源（DOTA2 7.42）

| 项目 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **初始三维 → 六维** | 力量 `25` / 敏捷 `18` / 智力 `18` | `STR 14 / DEX 11 / CON 14 / INT 11 / WIS 11 / CHA 10` | `MapStartingStats(25, 18, 18, 10)`，开局/重摇锁定 |
| **30 级成长 → 属性上限** | 力量 `+2.7` (`103.3`) / 敏捷 `+1.5` (`61.5`, 7.42 下调) / 智力 `+1.6` (`64.4`) | `STR 28 / DEX 23 / CON 28 / INT 23 / WIS 23 / CHA 20` | `MapStatLimits` + `RegisterHeroStatLimits` 接管 `GetLimitForm` |
| **1 级初始蓝量** | `75 + 18 × 12 = 291` | **`15`** | `Round(291 / 20) = 15`；`ComputeClassManaBase` 反推职业蓝基 |
| **蓝量升级成长** | 智力成长 `+1.6 / 级` | 职业 `1d2` | 低智成长档（$\le 1.8$） |
| **生命值成长** | 力量成长 `+2.7 / 级`（7.42 基础血量 `200`） | 职业 `16 + 1d12` | 高血量力量前排基准 |

### 技能数值映射（DOTA2 7.42）

| 技能 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **先天 · 一人之军** | 周围 `700` 码内无友方英雄时获得总护甲 `50%` 的额外力量；友军靠近后粘滞 `3` 秒 | 周围 `4` 格内无存活友军时生效（粘滞 **`6` 回合**）：将超额防御 `(Defence - 10)` 的 `50%` 通过 `ActiveFormQuery` 转化为**真实力量（STR）+ 体质（CON）加成**，直接同步至面板力量/体质、攻击命中、近战伤害与负重，并按每点 `+6 HP` 等比例动态增减最大生命（差值持久化守恒） | `3s × 2 = 6` 回合；原生天赋栏与左上角状态栏实时展示，粘滞期橙色高亮 |
| **Q · 狂战士之吼** | 蓝耗 `90/100/110/120`；持续 `2.1/2.4/2.7/3.0s`；护甲 `+12/13/14/15`（7.42a）；半径 `315`；强制周围敌人攻击斧王 | 蓝耗 **`5`**（Lv 1）；持续 **`4/5/5/6` 回合**（随防护学派熟练度额外 `+0/0/1/1/2` 回合）；防御 **`+5/+5/+5/+6`**（T2 左 `+10` 甲 → `+7/+8/+8/+8`，封顶抬高至 `+10`）；半径 **`2` 格**（T4 右 `3` 格）；范围内敌方被**嘲讽**（解除睡眠/恐惧、禁止施法、强制靠近或近战攻击斧王） | `Round(100 / 20) = 5`；`MapDurationTurns`；`MapArmorToDefence` |
| **W · 战斗饥渴** | 蓝耗 `50/60/70/80`；射程 `600/700/800/900`；持续 `12s`；每秒 **`12/16/20/24` 纯粹伤害**；减速 **`16%/19%/22%/25%`**（7.42）；目标击杀任意单位即驱散 | 蓝耗 **`3`**（Lv 1，空放返蓝）；全射程选敌 `4~7` 格；持续 **`24` 回合**；每回合按 `(DPS × 0.5 / 15)` 累加结算**纯粹伤害**（含升星 `+15%/级` 与 T2 右 `+8 DPS`）；减速 **`16%/19%/22%/25%`**；击杀任意单位立即驱散 | `Round(60 / 20) = 3`；`12s × 2 = 24` 回合；忠实还原 7.42 纯伤与减速 |
| **E · 反击螺旋** | 被动：每受 **`7/6/5/4`** 次攻击触发（**7.42：包含 Miss 的攻击**）；周围 `275` 码造成 **`100/120/140/160` 纯粹伤害** | 独立被动槽位（Lv 1/3/5/7，上限 4 级）：每受 **`7/6/5/4`** 次攻击（近战含 Miss + 远程物理受击）触发 `3×3` 旋斩，造成 **`1d6+3 / 2d6+1 / 2d6+2 / 3d6` 纯粹伤害**（T3 左 `+40` → `+3` 纯伤） | `MapSkillDamageDice(100..160, 6)`；左上角状态栏实时显示 `N/4` 计次 |
| **R · 淘汰之刃** | 蓝耗 `100/125/150`；斩杀线/纯伤 **`275/375/475`**；斩杀成功重置冷却、给予周围友军 `6s` **`+25%` 移速**（7.42）与 `+10/15/20` 护甲（7.42a），并永久获得 `+1/1.5/2` 护甲 | 蓝耗 **`6`**（Lv 1，空放返蓝）；指向选敌 `2` 格；斩杀阈值 **`18/25/32` HP**（熟练度 `+0~8` HP，T4 左 `+10` HP）：低于等于阈值瞬间无视减伤处决、**100% 返蓝**、给予周围 4 格友军 **`12` 回合**（T1 右 `18` 回合）`+25%` 移速与 `+4/+6/+7` 防御，并无上限叠加永久护甲（精英/Boss `+1.0/1.5/2.0`，普通怪 `+0.2/0.3/0.4`，存入 `DotaCharacterStorage`，按 `MapUncappedArmorToDefence` 多段递减换算：0~12 甲每 2.5 甲 +1 防、12~32 甲每 4 甲 +1 防、32~80 甲每 6 甲 +1 防、80 甲以上每 10 甲 +1 防，无封顶）；高于阈值造成 **`5d6+1 / 7d6+1 / 9d6` 纯粹伤害** | `Round(125 / 20) = 6`；`MapSkillDamage(275/375/475)`；`6s × 2 = 12` 回合；`MapUncappedArmorToDefence` |

### 演出与 VFX（DOTA2 7.42a 官方 datafeed 核对施法前摇 `0.3` 秒）

| 技能 | Pathos 演出 |
| :--- | :--- |
| **淘汰之刃** | 斧王前扑挥斧（`0.22` 秒）+ 自上而下由大到小「砸」下的猩红重斧光（`0.45` 秒衰减）；斩杀成功追加 `0.22` 秒顿帧（`DotaPresentation.HitStop`） |
| **狂战士之吼** | 怒吼冲击环 `0.5` 秒内从脚下扩散到嘲讽半径边缘（按半径覆盖 `2R+1` 格）+ `0.12` 秒顿帧 |
| **反击螺旋** | 跟随斧王的猩红斧刃回旋弧，`1440°/s` 高速自转 `0.3` 秒 |

---

## 3. 天赋树（DOTA2 7.42 原版节奏：`Lv 10/15/20/25` + `Lv 27~30` 双修）

| 层级（双修） | 左分支（DOTA2 7.42 → Pathos 映射） | 右分支（DOTA2 7.42 → Pathos 映射） |
| :---: | :--- | :--- |
| **Lv 25** *(Lv 30)* | **淘汰之刃 +150 伤害**：斩杀阈值与纯粹伤害 **`+10`** | **狂战士之吼 +70 范围**（7.42）：嘲讽半径 `2 → 3` 格（7×7） |
| **Lv 20** *(Lv 29)* | **反击螺旋 +40 伤害**：每次旋斩纯伤额外 **`+3`** | **+15 力量**：永久 **`+3 STR & +3 CON`、`+18 HP`** 并同步抬高上限 |
| **Lv 15** *(Lv 28)* | **狂战士之吼 +10 护甲**：额外 +10 DOTA2 护甲且吼叫防御封顶抬高至 `+10` | **战斗饥渴 +8 DPS**：每秒纯伤额外 `+8 DPS`（全程多 `+6~7` 纯伤） |
| **Lv 10** *(Lv 27)* | **每目标饥渴 +8% 移速**：场上每有一名中饥渴敌人，斧王移速 `+8%` | **淘汰战吼 +3 秒**：斩杀战吼持续时间 `12 → 18` 回合 |

---

## 4. 框架基建接入与 Hook 清单

### 接入的公共基建
- **固定初始六维与职业上限**：`DotaHeroProfile.RegisterStats`（`RegisterFixedStartingStats` + `RegisterHeroStatLimits`）；T3 右 +3 STR/CON 由 `TalentNode.StatBonus` 声明式结算
- **零 `Property` 持久化**：淘汰之刃永久护甲层数通过 `DotaCharacterStorage.AddCustomInt(..., "axe.cull_armor_x10", ...)` 随存档持久化
- **全射程选格（`DotaRangeTargetingBridge`）**：`battle hunger`（`UnitHostile`, `4~7` 格）、`culling blade`（`UnitHostile`, `2` 格）
- **左上角状态栏徽章（`DotaStatusBarBridge`）**：`axe.one_man_army`、`axe.counter_helix`、`axe.battle_hunger`（活跃目标数）、`axe.culling_blade`（永久护甲 + 战吼合并），以及 Modifier 自动生成的 `modifier.axe.berserkers_call`
- **技能帮助面板（`DotaSpellHelpBridge`）**：为 Q/W/R 渲染实时数值与 5 阶熟练度详情
- **增益/减益 Modifier（`DotaModifierService`）**：
  - `axe.berserkers_call`（增益，不可驱散，`Replace`）：狂战士之吼自身 `Defence`
  - `axe.battle_hunger`（减益，可驱散，`Replace`）：纯粹持续伤害 `DotDotaDps` + `MoveSpeedPct` 减速；被剑刃风暴等减益免疫阻挡
  - `axe.culling_buff`（增益，可驱散，`KeepStrongest`）：淘汰之刃战吼 `Defence` + `+25%` 移速，施加给斧王与周围 4 格友军

### 注册的 `DotaHookBus` Key
- `axe.init_one_man_army`（`AfterCreateActor`）：开局初始化孤军奋战
- `axe.spell_casts`（`AfterCastSpell`）：Q 狂战士之吼、W 战斗饥渴、R 淘汰之刃结算与空放返蓝
- 嘲讽由 `DotaTauntService` 提供（`dota.taunt_silence` / `dota.taunt_redirect` / `dota.taunt_tick`）：被嘲讽目标全链路禁魔并强制靠近或近战攻击斧王
- `axe.turn_ticks`（`AfterExecuteCommand`）：孤军奋战检测/粘滞（吼叫/战吼/饥渴的计时与每回合纯伤由 `dota.modifiers.tick` 统一推进）
- `axe.counter_helix.melee`（`AfterAttack`）& `axe.counter_helix.ranged`（`AfterDamage`）：受击计次（含 Miss）触发 3×3 纯伤旋斩；`ranged` 分支只计 `Info.Origin == Unclassified`（远程投射物等原生攻击）与带 `Attack` 标记的脚本攻击，技能/持续伤害不计数
- 伤害标注（`DotaDamage`）：战斗饥渴首跳/DoT、反击螺旋、淘汰之刃均为 **纯粹伤害**（`DotaDamageInfo.Pure`），不受任何魔抗减免
- `axe.one_man_army_str`（`ActiveFormQuery`）：孤军奋战真实力量加成（同步至面板力量、攻击命中、近战伤害与负重）
- `axe.defence_query`（`DefenceQuery`）：淘汰之刃永久护甲（临时护甲由 `dota.modifiers.defence` 汇总）
- `axe.speed_query`（`SpeedQuery`）：T1 左每饥渴目标 `+8%` 移速（饥渴减速与战吼 `+25%` 移速由 `dota.modifiers.speed` 加算汇总，减速下限 35%）
- `axe.battle_hunger_dispel`（`AfterKill`）：中饥渴单位击杀任意目标时立即移除饥渴 Modifier

---

## 5. 贴图资源清单

位于 `Resources/Heroes/axe/`（单份 `128x128` 透明 PNG，构建期自动分发至四套图集）：
- **角色与技能图标（6 张）**：`mogul khan.png`（避开内置物品 `axe`）、`one man army.png`、`berserkers call.png`、`battle hunger.png`、`counter helix.png`、`culling blade.png`（5 枚技能图标源自 DOTA2 官方原版图标 `32x32 → 128x128` 像素化）
- **自定义像素特效（22 张）**：
  - 狂战士之吼冲击环：`berserkers call ring.png`（3×3 九宫格回退：`berserkers call fx tl/tc/tr/ml/mc/mr/bl/bc/br.png`）
  - 反击螺旋斧刃回旋：`counter helix whirl.png`（3×3 九宫格回退：`counter helix fx tl/tc/tr/ml/mc/mr/bl/bc/br.png`）
  - 以上与 `culling blade fx strike.png` 由 [tools/VfxGen](../../tools/VfxGen/Program.cs) 程序化生成
  - 战斗饥渴与淘汰之刃打击：`battle hunger fx strike.png`、`culling blade fx strike.png`
