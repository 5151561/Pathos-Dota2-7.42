# 帕吉（Pudge / 屠夫）

- **参考版本**：DOTA2 7.42（完美世界 [刀塔百科英雄页](https://www.dota2.com.cn/hero/pudge/) + Valve 官方 datafeed `dota2.com/datafeed/herodata?hero_id=14`，两者逐项一致；7.42a 无帕吉改动）
- **英雄 ID**：`pudge`
- **实现源码**：[Heroes/PudgeHero.cs](../../Heroes/PudgeHero.cs)
- **贴图目录**：`Resources/Heroes/pudge/`

### 7.42 版本变更（官方 patchnotes）

| 技能 | 7.42 改动 |
| :--- | :--- |
| 肉钩（魔晶） | 击中友军时不再返还 50% 冷却；魔晶改为 +100 施法距离 |
| 肢解 | 力量倍数 `0.3/0.6/0.9` → **`0.3/0.5/0.7`**（法术强度全面削弱的一部分） |

---

## 1. 种族与职业设定

- **种族与职业**：官方 `兽人 (orc)` 种族 + 专属职业 `帕吉 (pudge)`。兽人在 Pathos/NetHack 中天生毒抗且吃尸体无同类惩罚，契合「腐烂」毒雾与食尸设定。
- **开局装备**：`+1 斧 (axe)`、`皮甲 (leather_armour)`、治疗药水、回蓝药水、`牛肚口粮 (tripe_ration)`；1 级自带 1 点技能点。Pathos 没有「剁肉刀」物品（`cleaver` 只是字典词条），屠刀用单手斧代替。
- **熟练技能**：读写、斧、轻甲、中甲，以及肉钩的塑能系 (evocation)、腐烂与肢解的死灵系 (necromancy)、肉盾的防护系 (abjuration)。
- **等级专长**：Lv 1 先天 **腐肉堆积**；Lv 4 自由行动 (`free_action`)；Lv 8 明晰 (`clarity`)；Lv 12 生命再生 (`life_regeneration`，对应 DOTA2 帕吉 5.0 的高基础回血)。
- **预设英雄**：选人界面预设英雄 **Pudge**（开局/重摇锁定初始六维与初始生命/法力）。

---

## 2. DOTA2 7.42 → Pathos 数值映射溯源表

> 统一换算标尺见 [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)：
> 初始属性 $\text{Clamp}(\text{Round}(12 + (x - 20) \times 0.45), 6, 18)$；蓝量 $K_{\text{mana}} = 20$；技能伤害 $K_{\text{dmg}} = 15$；时间 **1 秒 = 2 回合**；距离约 `175` 码 / 格；护甲经 `MapArmorToDefence`。
>
> **单位分级约定**（本英雄多个机制依赖）：DOTA2「英雄 / 远古」≙ Pathos **英雄级单位** = 唯一怪物 (`Entity.IsUnique`)、守卫 (`Entity.IsGuardian`) 与非怪物角色；其余都是 DOTA2「小兵 / 野怪」≙ Pathos **普通怪物**。Pathos 没有「精英」标记，斧王的 `Level ≥ 5` 分级会让绝大多数怪物都算精英，不适合「秒杀普通单位」的肉钩，所以帕吉单独用这套分级。

### 基础属性与资源（DOTA2 7.42）

| 项目 | DOTA2 7.42 原版数值 | Pathos 映射数值 | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **初始三维 → 六维** | 力量 `25` / 敏捷 `13` / 智力 `16` | `STR 14 / DEX 9 / CON 14 / INT 10 / WIS 10 / CHA 10` | `MapStartingStats(25, 13, 16, 10)` |
| **30 级成长 → 属性上限** | 力量 `+3.0` (`112`) / 敏捷 `+1.4` (`53.6`) / 智力 `+1.8` (`68.2`) | `STR 29 / DEX 22 / CON 29 / INT 24 / WIS 24 / CHA 20` | `MapStatLimits`；腐肉堆积走 `ActiveFormQuery` 临时加成，不占这里的上限 |
| **1 级初始蓝量** | `75 + 16 × 12 = 267` | **`13`** | `Round(267 / 20) = 13`；`ComputeClassManaBase` 反推职业蓝基 |
| **蓝量升级成长** | 智力成长 `+1.8 / 级` | 职业 `1d2` | 低智成长档（$\le 1.8$） |
| **生命值成长** | 力量成长 `+3.0 / 级`（全英雄最高档之一） | 职业 `16 + 1d12` | 与斧王同档，额外成长由腐肉堆积提供 |
| **其他** | 生命 `700`、回血 `5.0`、魔抗 `25%`、护甲 `2.17`、攻击 `70–76`、BAT `1.7`、射程 `175`（近战）、移速 `280`、转身 `0.7`、视野 `1800/800` | 近战；基础护甲不单独映射（走 DEX） | 与斧王/主宰处理一致 |

### 技能数值映射（DOTA2 7.42）

| 技能 | DOTA2 7.42 原版数值 | Pathos 映射数值（方案） | 换算依据 / 备注 |
| :--- | :--- | :--- | :--- |
| **先天 · 腐肉堆积** (`pudge_innate_graft_flesh`) | 帕吉 `450` 码内敌方英雄死亡，永久 **`+2` 力量**；T4 左天赋 ×1.5 | 帕吉击杀的、或在帕吉 **`3` 格**内死亡的敌人，只要是英雄级单位**或等级 ≥ 帕吉等级的怪物**，叠 1 层 = **`+2` DOTA 力量**（T4 左 ×1.5，已有层数一并生效）。层数存 `DotaCharacterStorage`（`pudge.flesh_heap_stacks`），换算为 Pathos `STR & CON` 加成 `Floor(累计 DOTA 力量 × 0.2)`，按每点 `+6 HP` 联动最大生命 | `450 ÷ 175 ≈ 2.6 → 3` 格；`0.2` 为 1:5 后期标尺（同斧王「+15 力量 → +3」天赋）。DOTA2 一局 15–25 层 ≈ 30–50 力量 → Pathos `+6 ~ +10` |
| **Q · 肉钩** | 纯粹伤害 **`150 / 220 / 290 / 360`**；射程 `1300`；钩速 `1600`；宽度 `100`；前摇 `0.3s`；冷却 `18/16/14/12s`；蓝耗 `120`；勾住**第一个**单位（含友军）拖回身边；**非远古单位直接秒杀**；无视减益免疫；命中后 `500` 码视野 `4s` | 蓝耗 **`6`**；方向施法，射线 **`7` 格**，遇墙/第一个单位停下；敌方：**纯粹伤害 `2d6+3 / 4d6+1 / 5d6+2 / 6d6+3`**（期望 `10 / 15 / 19.5 / 24`，T2 左 `+10`）后拖到帕吉相邻格；**普通怪物直接秒杀**（与死亡一指、死亡射线等原生秒杀同类，不另加等级门槛）；友军：只拖不伤；中立单位（商人等）：钩子弹开，无效果 | `Round(120/20) = 6`；`MapSkillDamageDice(150..360)`；`1300 ÷ 175 ≈ 7.4 → 7` 格 |
| **W · 腐烂** | 开关技能，`0` 蓝耗 `0` 冷却；半径 `250`；魔法伤害 **`30 / 60 / 90 / 120` DPS**（每 `0.2s` 跳）；**同时伤害自己**；减速 **`14% / 20% / 26% / 32%`**（T1 左 `+10%`） | 蓝耗 **`0`**，施法 = 开 / 关；开启期间每回合对周围 **`1` 格**（3×3）敌人造成魔法伤害 **`1 / 2 / 3 / 4`**，并对自己造成等量魔法伤害（**自伤最多扣到剩 `1` HP**，已定；肉盾按 DOTA 每跳格挡结算，见 §2.1）；范围内敌人获得 `MoveSpeedPct` 减速 `14/20/26/32%`（天赋后 `24/30/36/42%`），离开后 1 回合内消失 | `MapDotDamagePerTurn(30..120) = 1/2/3/4`；`250 ÷ 175 ≈ 1.4 → 1` 格；`MapSkillManaCost(0) = 0` |
| **E · 肉盾** (`pudge_flesh_heap`) | 格挡所有来源、所有类型伤害 **`8 / 14 / 20 / 26`**（每次）；持续 **`4 / 5 / 6 / 7s`**；冷却 `20/19/18/17s`；蓝耗 `65/70/75/80`；不可驱散；T4 左 ×1.5 | 蓝耗 **`4`**（中位 `70`）；自身增益 **`8 / 10 / 12 / 14` 回合**，每次受到伤害（物理/魔法/纯粹）格挡期望 **`0.53 / 0.93 / 1.33 / 1.73`**（×1.5 后 `0.8 / 1.4 / 2.0 / 2.6`），小数部分按概率进位（如 `1.33` = 必挡 1 点，另有 33% 再挡 1 点）；不可驱散 | `MapSkillManaCost(70) = 4`；格挡 `DotaBlock / 15` 不取整，见 §2.1；`MapDurationTurns(4..7)` |
| **R · 肢解** | 持续施法 `2.75s`（`6` 跳）；施法距离 `200`；前摇 `0.3s`；魔法伤害每秒 **`80 / 100 / 120` + 力量 × `0.3 / 0.5 / 0.7`**；**按伤害等量治疗自己**；目标以 `75/s` 拉向帕吉、最多 `125` 码；冷却 `30/25/20s`；蓝耗 `100/130/170`；无视减益免疫，仅强驱散可解；T3 右 `+0.75s`（`+2` 跳）、T4 右伤害/治疗 ×1.5 | 蓝耗 **`6`**（中位 `130`）；选相邻 **`1` 格**敌人；引导 **`6` 回合**（T3 右 `7` 回合）：目标施加原生 `stunned` 同等回合，帕吉每回合自动结算魔法伤害 **`(DPS + 倍数 × 等效力量) × 0.5 / 15`** 并等量回血；帕吉被眩晕 / 目标死亡或离开相邻格即中断 | `MapDurationTurns(2.75) = 6`、`(3.5) = 7`；`200 ÷ 175 ≈ 1` 格；拉扯距离不足 1 格，忽略 |

### 2.1 肉盾格挡数值研究

**结论**：$K_{\text{dmg}} = 15$ 的标尺本身没问题，「偏弱」来自取整：`0.53 / 0.93 / 1.33 / 1.73` 四舍五入后变成 `1 / 1 / 1 / 2`，前三级完全一样。改为**按期望保留小数、小数部分概率进位**即可，不需要另调倍率。

**依据 1：Pathos 怪物单次伤害与 DOTA2 ÷15 同量级**。用临时工具读取官方 `Assets/Pathos.Codex` 全部 691 个实体，统计非唯一怪物的近战攻击（跳过远程/吐息、0 伤害的特殊攻击）：

| 怪物等级 | 攻击条目数 | 单次平均伤害 | 单次 ≤ 2 | 单次 ≤ 3 | 每回合合计 |
| :--- | ---: | ---: | ---: | ---: | ---: |
| 0–4 | 144 | `3.2` | 33% | 59% | `4.2` |
| 5–9 | 151 | `4.3` | 21% | 40% | `7.1` |
| 10–14 | 177 | `6.4` | 5% | 15% | `10.1` |
| 15–19 | 146 | `8.1` | 5% | 10% | `14.6` |
| 20–29 | 270 | `9.3` | 1% | 6% | `20.7` |
| 30+ | 133 | `9.6` | 5% | 16% | `26.0` |

**依据 2：格挡占单次伤害的比例与 DOTA2 相当**（DOTA2 侧取典型值：小兵近战约 `20`，英雄普攻前期约 `55`、中期约 `80–100`、后期约 `170`）：

| 阶段 | DOTA2 原版 | Pathos（期望格挡 / 同期怪物单次伤害） |
| :--- | :--- | :--- |
| E1（Lv 1–2） | `8` 挡小兵 `20` ≈ 40%，挡英雄 `55` ≈ 15% | `0.53 / 3.2` ≈ 17% |
| E4（Lv 7–10） | `26` 挡小兵 = 100%，挡英雄 `90` ≈ 29% | `1.73 / 4.3–6.4` ≈ 27–40% |
| 后期满级 + T4 左 ×1.5 | `39` 挡英雄 `170` ≈ 23% | `2.6 / 9.3` ≈ 28% |

**依据 3：单个攻击者在整段持续时间内的总减伤吻合**。DOTA2 E4 持续 `7s`，英雄约 `1.4` 次攻击/秒 → 约 `10` 次 × `26` = `260`，÷15 ≈ `17`；Pathos E4 `14` 回合，常速怪物每回合约 1 次 → `14 × 1.73 ≈ 24`，略偏慷慨但同量级。多段攻击怪物（每回合多次攻击）逐次格挡，与 DOTA2「每个伤害实例」一致。

**实现要点**：
- 格挡值 = `DotaBlock × (天赋 ? 1.5 : 1) / 15`，`floor` 后按小数部分掷概率 +1，伤害最低扣到 `0`。
- 只减生命伤害，不拦截攻击附带的毒、吸能、解离等命中效果（DOTA2 中被完全格挡的攻击也照常触发法球效果）。
- **腐烂自伤按 DOTA 每跳结算格挡**：腐烂每 `0.2s` 一跳，单跳 `6 / 12 / 18 / 24`，正好全部小于同级格挡 `8 / 14 / 20 / 26`。这是原版「开肉盾免疫自己腐烂」的联动。Pathos 每回合（`0.5s` = `2.5` 跳）自伤 = `2.5 × max(0, 单跳 − 格挡) / 15`，同样按概率进位。E 等级 ≥ W 等级时自伤为 0；例如 W4 + E1：`2.5 × 16 / 15 ≈ 2.7`（无肉盾为 `4`）。

**肢解「等效力量」**：Pathos STR 按初始映射反推 DOTA 力量，$\text{DotaStr} = 20 + (\text{STR} - 12) / 0.45$。代入估算：

| 场景 | 等效力量 | 每回合伤害 / 回血 | 6 回合总计 | DOTA2 对照（÷15） |
| :--- | :--- | :--- | :--- | :--- |
| Lv 6，R1，STR 16 | `28.9` | `2.96` | `≈ 18` | `2.75s × (80 + 0.3×40) ≈ 253 → 17` |
| Lv 18，R3，STR 24 | `46.7` | `5.09` | `≈ 31` | `2.75s × (120 + 0.7×76) ≈ 476 → 32` |

### 天赋树（DOTA2 7.42 → Pathos 映射）

| 层级（双修） | 左分支 | 右分支 |
| :---: | :--- | :--- |
| **Lv 25** *(Lv 30)* | **腐肉堆积与肉盾加成 ×1.5**：叠层力量 ×1.5，肉盾格挡 ×1.5 | **肢解伤害/治疗 ×1.5** |
| **Lv 20** *(Lv 29)* | **肉钩冷却 −4s** → 减蓝耗：`ComputeCooldownReductionRatio(4, 12)` = `33%`，`6 → 4` | **肢解持续 +0.75s**：引导 `6 → 7` 回合 |
| **Lv 15** *(Lv 28)* | **肉钩伤害 +150** → 纯粹伤害 `+10` | **+8% 技能吸血**：帕吉技能伤害的 `8%` 回血（小数累积，避免腐烂每回合 1 点伤害也回 1 血） |
| **Lv 10** *(Lv 27)* | **腐烂减速 +10%** | **+5 护甲** → `MapArmorToDefence(5) = +2` Defence |

> 减冷却参考值取满级冷却 `12s`，与水人「变体打击」天赋（`-3s / 8s 满级`）同口径。

### 神杖 / 魔晶（仅记录，不实现）

本扩展暂无 A 杖 / 魔晶物品，数值仅作溯源：
- 肉钩魔晶：施法距离 `+100`；按拖行距离追加伤害（`distance_to_damage = 20`，`min_distance_damage = 10`）。
- 腐烂神杖：半径 `+200`，DPS `+90`，降低敌人 `20%` 生命回复。
- 肢解魔晶：施法距离 `+300`，每秒回复 `5%` 生命，锁定指令 `2.75s`。

---

## 3. 框架基建接入与 Hook 清单

| 需求 | 用法 |
| :--- | :--- |
| 三维 / 上限 / 职业样板 | `DotaHeroProfile(BaseStr: 25, StrGain: 3.0f, BaseAgi: 13, AgiGain: 1.4f, BaseInt: 16, IntGain: 1.8f)` + `RegisterStats()` + `ConfigureClass(...)` |
| 肉钩 | `DotaAbility.Register`（`PointAny`，射程 7 格）；`OnCast` 朝点击格方向延伸到最大射程逐格扫描，遇墙停下；冻结世界的 `DotaPresentation.Sequence` 里按钩速逐格铺铁链 / 钩头，命中时刻结算 `KillCharacter` 或 `DotaDamage.Strike(..., DotaDamageInfo.Pure)`，再 `DashCharacter` + `StoryFlinging` 拖回 |
| 腐烂 | `DotaAbility.Register`（`Self`，0 蓝耗）切换开关；开关状态 `DotaCharacterFlagSet`（读档即关闭）；`AfterExecuteCommand` 每回合结算 `DotaDamageInfo.Magical`；减速 `DotaModifierService.Define("pudge.rot_slow")`；毒雾 `DotaPresentation.Loop` 每 0.5 秒 `DotaVfx.Explode`；自伤带 `DotaDamageFlags.NoBlock`，按 §2.1 每跳公式自行结算格挡 |
| 肉盾 | `DotaModifierService.Define("pudge.meat_shield")`（增益、不可驱散、自动徽章），施放时 `M.DamageBlock = DotaBlock / 15`。**框架新增**：`DotaModifier.DamageBlock` + `DotaDamageFlags.NoBlock`，见 [framework-api.md](../architecture/framework-api.md) |
| 肢解 | `DotaAbility.Register`（`UnitHostile`，1 格）；照主宰无敌斩的做法 `SetBusy` + `Sequence.FreezeWorld(每步 1 回合)`；目标 `ApplyTransient(stunned)`，提前结束时 `RemoveTransient`；回血 `HealCharacter` |
| 腐肉堆积 | `AfterKill` → `DotaCharacterStorage.AddCustomInt(ch, "pudge.flesh_heap_stacks", 1)`；`ActiveFormQuery` 加 STR/CON；`DotaLife.AdjustMax` 同步生命（已同步量存 `pudge.flesh_heap_applied_hp`）；徽章 `DotaStatusBarBridge` |
| 加点 / 天赋 | `HeroSkillBuild.CreateStandard`（Q/W/E 主动 4 级、R 3 级）+ `HeroTalentTree.CreateStandard`；Lv20 左 `SpellManaReductionRule`；Lv25 左 `OnLearn` 重新同步腐肉堆积生命 |
| 帮助面板 / 文本 | `DotaSpellHelpBridge.RegisterSpell` ×4 + `RegisterPassive("pudge.flesh_heap")`；运行时文本 Key 前缀 `pudge.` |

### 注册的 `DotaHookBus` Key

- `pudge.init_flesh_heap`（`AfterCreateActor`）：重摇时清空腐肉堆积，开局同步生命
- `pudge.turn_ticks`（`AfterExecuteCommand`）：腐烂每回合伤害、减速、自伤
- `pudge.flesh_heap_str`（`ActiveFormQuery`）：腐肉堆积 STR / CON 加成
- `pudge.flesh_heap_kill`（`AfterKill`）：腐肉堆积叠层
- `pudge.talent_armor`（`DefenceQuery`）：Lv10 右 +5 护甲
- `pudge.spell_lifesteal`（`AfterDamage`）：Lv15 右技能吸血
- 演出序列 / 循环：`pudge.meat_hook`、`pudge.dismember`、`pudge.rot_cloud`

### 存档标识（禁止改名）

- 技能槽：`pudge.q.meat_hook` / `pudge.w.rot` / `pudge.e.meat_shield` / `pudge.r.dismember`
- 天赋：`pudge.t1.rot_slow` / `pudge.t1.armor` / `pudge.t2.hook_dmg` / `pudge.t2.spell_lifesteal` / `pudge.t3.hook_cd` / `pudge.t3.dismember_dur` / `pudge.t4.flesh_heap` / `pudge.t4.dismember_dmg`
- 存储键：`pudge.flesh_heap_stacks`、`pudge.flesh_heap_applied_hp`
- 法术：`meat hook` / `pudge rot` / `meat shield` / `dismember`；职业 `pudge`

---

## 4. 演出与 VFX（依据官方演示视频）

| 技能 | 官方演示观感 | Pathos 演出 |
| :--- | :--- | :--- |
| **肉钩** | 深灰铁链 + 暗红血迹直线射出，钩中后目标被拖回，血花飞溅 | 前摇 `0.3s` 甩臂；钩头（按出钩方向旋转）按 `1600 码/s`（`≈ 0.11s/格`，7 格约 `0.77s`）逐格飞出，身后每格留一节铁链，保留到钩子收回经过为止；命中处血花 `Strike`；目标 `StoryFlinging` 同速拖回；落空时钩头原路收回；全程冻结世界 |
| **腐烂** | 以帕吉为中心的黄绿色毒雾 | 开启期间 `DotaPresentation.Loop` 每 0.5 秒在 3×3 播放一次黄绿毒雾 `Explode`（淡入淡出，峰值不透明度 0.5），关闭或死亡时停止。毒雾用 `DotaVfxStyle.Ground` 贴地绘制：盖在地板上、压在物品与角色之下，不遮挡帕吉和敌人，也不画在墙上 |
| **肉盾** | 帕吉全身覆上一层红褐色血肉 | 施放时自身 `StrikeOn` 一次红褐血肉包裹；持续期间由 Modifier 徽章提示 |
| **肢解** | 咬住目标时亮粉白闪光 + 红色血溅，带引导条 | 每回合对目标 `StrikeOn` 粉白核心 + 猩红血溅；施放瞬间 `HitStop` 顿帧 |

**配色（单条色阶，无抗锯齿，32×32 网格 ×4）**：
- 铁链 / 钩：`#2E2E34 → #5A5A62 → #8E8E96`，血迹 `#5E0C0C / #9A1616`
- 腐烂毒雾：`#4E5418 → #7F8A22 → #B4B83A → #D8D468`
- 肉盾血肉：`#5A1E16 → #8C3A2A → #B85A42 → #E08A70`
- 肢解：核心 `#FFF0FA / #FFB8E6 / #F070C0`，血溅 `#8A0E14 / #C01828`

---

## 5. 贴图资源清单

### 官方源图（`https://img.dota2.com.cn/dota2static/herostatic/npc_dota_hero_pudge/`）

| 用途 | 源文件 |
| :--- | :--- |
| 英雄形象 | `../index/npc_dota_hero_pudge.png`、`../crops/npc_dota_hero_pudge.png`（动态立绘 `npc_dota_hero_pudge.webm`） |
| 肉钩 / 腐烂 / 肉盾 / 肢解图标 | `pudge_meat_hook.png` / `pudge_rot.png` / `pudge_flesh_heap.png` / `pudge_dismember.png` |
| 腐肉堆积 | 官方无专属图标（页面用通用 `dota2static/facets/innate_icon.png`），需自绘 |
| 演示视频 | `pudge_meat_hook.mp4`、`pudge_rot.mp4`、`pudge_dismember.mp4`；肉盾 `dota2static/file/88759d5f-6d7c-4a96-93bb-2bcef1c1bde7.mp4` |

### 产出（`Resources/Heroes/pudge/`，共 23 张 128×128 PNG）

已对照 Pathos 安装目录的 `*.Atlas` / `Pathos.Codex` / 字典核对重名：`pudge`、`meat hook`、`flesh heap`、`meat shield`、`dismember` 无冲突；**`rot` 是官方 Glyph / Codex 条目**，腐烂的法术名与贴图改用 `pudge rot`。

- **角色与技能图标（6 张）**：官方原图按 4×4 盒式平均缩成 32×32 网格，再最近邻 ×4 放大
  - `pudge.png`：官方透明底立绘（`crops/`）裁上半身，32 网格像素化 + 1 像素暗色描边。**注意**：现有英雄是全身像素小人，这张只是半身像素化立绘，后续可换成手绘全身像
  - `flesh heap.png`：官方无先天图标，取 `index/` 立绘的面部方形裁切
  - `meat hook.png` / `pudge rot.png` / `meat shield.png` / `dismember.png`：官方技能图标
- **VFX（17 张，[tools/VfxGen](../../tools/VfxGen/Program.cs) 生成）**：
  - 肉钩：`meat hook fx beam h/v/f/b`（铁链，四方向可拼接）、`meat hook fx hook`（钩头）、`meat hook fx strike`（血花）
  - 腐烂：`pudge rot fx tl/tc/tr/ml/mc/mr/bl/bc/br`（3×3 毒雾九宫格）
  - 肉盾：`meat shield fx strike`；肢解：`dismember fx strike`

---

## 6. 设计决定

- **肉钩秒杀**：按原版，普通怪物直接秒杀，英雄级单位吃纯粹伤害（Pathos 本身就有死亡一指、死亡射线等秒杀）。
- **腐烂自伤**：最多扣到剩 `1` HP，不会把自己烧死。
- **肉盾格挡**：保留 $K_{\text{dmg}} = 15$，改为期望值 + 小数概率进位，研究见 §2.1。
- **种族**：官方 `orc`（毒抗、食尸无惩罚）。
- **腐肉堆积（实现时修订）**：设计稿里「普通怪物每只 +0.2 力量、按 0.45 折算」会让一局几百次击杀叠出几十点属性，远超 30 级上限。改为只计英雄级单位与等级 ≥ 帕吉的怪物，并按 1:5 后期标尺折算，让一局的总收益落在 DOTA2 同等比例（`+6 ~ +10` STR）。
