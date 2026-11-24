# DOTA2 → Pathos 机制与数值映射规范

本规范用于保证所有 DOTA2 英雄在 Pathos 回合制 Roguelike 体系下拥有统一、可推导、忠实于刀塔百科（DOTA2 Wiki）的数值标尺与手感。所有换算函数统一封装在 [DotaMechanicsMapper.cs](../../Framework/DotaMechanicsMapper.cs) 中。

> [!IMPORTANT]
> **版本基准铁律（DOTA2 7.42 Wiki）**：
> 本扩展包所有英雄的初始三维、属性成长、技能伤害、蓝耗、冷却、持续时间、被动概率与天赋树数值，**必须严格对照 DOTA2 7.42 官方 Wiki / 刀塔百科核对原始数值**，严禁凭印象估算或混用其他版本的过时数据！每位英雄必须在 `docs/heroes/<hero_id>.md` 中附带完整的 7.42 → Pathos 数值映射溯源表。

---

## 1. 英雄初始属性映射（固定分配，告别 4d6 摇骰子）

Pathos 原生开局为 `6 × (4d6 取高 3)`（单项 3~18，期望 12.24），且六维职责为：
- **STR**：近战伤害与负重；**DEX**：防御（Defence）与远程命中/伤害；**CON**：最大生命与生命回复；
- **INT**：经验与技能学习效率；**WIS**：**最大法力值与法力回复**；**CHA**：商店价格与魅惑/驯化。

### 线性锚定公式
直接取刀塔百科的英雄 1 级三维（`BaseSTR / BaseAGI / BaseINT`），通过 `DotaMechanicsMapper.MapStartingStats` 映射并锁定：

$$\text{Map}(x) = \text{Clamp}\Big(\text{Round}\big(12 + (x - 20) \times 0.45\big),\ 6,\ 18\Big)$$

- `STR` = `CON` = `Map(BaseSTR)`
- `DEX` = `Map(BaseAGI)`
- `INT` = `WIS` = `Map(BaseINT)`
- `CHA` 默认 `10`（可按背景人设微调 ±2）

### 运行时锁定方式
在英雄 `RegisterHooks` 中调用一行即可：
```csharp
DotaMechanicsMapper.RegisterFixedStartingStats(ClassName, StartingStats);
```
框架会在 `Engine.CreateActor` 与 `Engine.RerollActor` 的 Postfix 中自动覆写 `CurrentForm` / `MaximumForm`，并按 Pathos 原生公式重算 1 级初始 `Life` 与 `Mana`。

---

## 2. 蓝量池与技能蓝耗映射（统一标尺 $K_{\text{mana}} = 20$）

以 **20 点 DOTA2 魔法 = 1 点 Pathos 法力** 作为全局唯一标尺，不再按技能类型拍脑袋定价：

1. **1 级目标蓝量**：
   $$\text{TargetMana}_1 = \text{Round}\Big(\frac{75 + 12 \times \text{BaseINT}}{20}\Big)$$
   使用 `DotaMechanicsMapper.ComputeClassManaBase(DotaBaseInt, RaceManaBase, StartingStats.Wis)` 反推职业 `ManaAdvancement.HardBase`，使 1 级实际初始蓝量精准对齐目标值。
2. **升级蓝量成长 `LevelDice`**（按 DOTA2 智力成长分档）：
   - 低智成长（$\le 1.8$）：`+1` 或 `1d2`
   - 中智成长（$1.9 \sim 2.8$）：`1d3`
   - 高智成长（$\ge 2.9$）：`1d4+1` 或 `1d6`
3. **主动技能蓝耗**：
   - 常规主动：`DotaMechanicsMapper.MapSkillManaCost(dotaMana)` → $\max(1, \text{Round}(\text{DotaMana} / 20))$；**0 蓝耗技能忠实返回 `0`**（如 7.42 水人属性转换）。
   - 多等级变蓝耗技能：取中位等级或 1 级蓝耗作为基准。
   - 持续每秒耗蓝（Toggle）：`DotaMechanicsMapper.MapToggleManaPerTurn(manaPerSec)`（按 **1 秒 = 2 回合** 即每回合 0.5 秒折算）。
   - Pathos 超模机制技能（如 `ImpersonateEntity` / 永久召唤）：可在等效蓝耗上乘以强度修正系数。
4. **法术等级对齐**：`S.Level = DotaMechanicsMapper.MapSpellLevel(S.Mana)`（对齐 Pathos 原生 `5 / 10 / 15 ... 35` 阶梯），且必须在职业 `Startup.SetSkill` 中给予对应学派至少 `Proficient`（避免生疏 ×2 蓝耗惩罚）。

---

## 3. 技能伤害映射（统一标尺 $K_{\text{dmg}} = 15$）

以 **15 点 DOTA2 技能伤害 = 1 点 Pathos 技能伤害** 作为全局统一标尺，所有主动/持续/附加伤害均通过 `DotaMechanicsMapper` 推导，不再拍脑袋手填骰子：

1. **单发爆发 / 附加伤害**（`MapSkillDamage` / `MapSkillDamageDice`）：
   $$\text{TargetDmg} = \frac{\text{DotaDamage}}{15}$$
   - 需要骰子波动时调用 `MapSkillDamageDice(dotaDamage, dieFaces)` 自动生成期望贴合 $\text{TargetDmg}$ 的 `NdF + Bonus` 伤害骰（默认 `d6`）。
   - 例：水人波浪形态 `75 / 150 / 225 / 300` → 期望 `5 / 10 / 15 / 20`（`1d6+2 / 2d6+3 / 4d6+1 / 5d6+3`）；主宰无敌斩附加攻击力 `25 / 30 / 35` → `+2 / +2 / +2`（满熟练 `+3`）。
2. **持续伤害 / 高频跳伤（DPS）**（`MapDotDamagePerTurn` / `MapDotDamageDicePerTurn`）：
   - 按 **1 秒 = 2 回合（每回合 0.5 秒）** 合并为**每回合结算 1 次**，避免高频 0.2s 跳伤刷屏与小数截断：
     $$\text{BaseDmg}_{\text{turn}} = \frac{\text{DotaDPS} \times 0.5}{15}$$
   - 若 DOTA2 原版机制支持**攻速缩放跳伤频率**（如 7.36+/7.42 剑刃风暴），则将每回合基准伤害再乘以角色敏捷/攻速倍率，兼顾还原度与中后期输出节奏。
3. **游戏内与文档双可见**：
   - 凡通过 Hook 结算伤害（声明式壳子无 `HarmEntity`）的技能，**必须在法术 `Description` 中明确写出每级/每回合伤害骰与倍率**，并在 `docs/heroes/<hero_id>.md` 的溯源表中列出原版与映射后的伤害数值。

---

## 4. 持续时间、治疗 / 吸血与召唤物生命周期规范

1. **时间换算标尺：1 秒 DOTA2 时间 = 2 回合 Pathos**（`DotaMechanicsMapper.MapDurationTurns(seconds)`）：
   - Pathos 走位与回合流逝极快，1 秒 = 1 回合会导致 4~5 秒的技能走两步就结束；按 **1 秒 = 2 回合** 换算后，5 秒剑刃风暴为 `10` 回合，18~24 秒治疗守卫为 `36~48` 回合，1.5~3 秒控制为 `3~6` 回合；同时每秒持续伤害/治疗按每回合 `0.5 秒` 折算（保持总伤/总奶量与原版一致）。
2. **治疗、光环回复与吸血规范**：
   - **百分比持续治疗（HoT / 光环）**：将 DOTA2 每秒回复比例除以 `TurnsPerSecond`（`2.0`）折算为每回合回复比例（如治疗守卫每秒 `2%~5%`、魔晶 `6.5%` → 每回合 `1.0%~2.5%`、Champion `3.25%`），单次保底回复 `1 HP`；在 `AfterExecuteCommand` 中遍历友军（`DotaCombatHelper.IsFriendly`），仅对不满血目标调用 `PathosEngineBridge.HealCharacter`，并可配合 `StoryDetecting` + `StoryStriking` 给出治疗反馈。
   - **暴击吸血 / 伤害吸血（Lifesteal）**：在 `BeforeDamage` 最终伤害确定后（或 `AfterDamage`）按实际伤害乘以吸血比例（保底 `1 HP`）调用 `PathosEngineBridge.HealCharacter(engine, attacker, attacker, selfDir, healAmt)`（仅当不满血时触发）。
   - **直接生命上限联动（如水人属性转换）**：涉及最大生命与当前生命同步增减时使用 `PathosEngineBridge.SetMeter(Life, newPoints, newTotal)`（而非 `HealCharacter`），保证转敏时只截断上限不扣残血、最低保底 `1 HP`。
   - **生态/道具交互回复**：可在声明式 `AddReaction` / `AddObviousUse` 中直接使用 `HealEntity` / `EnergiseEntity`（如变体水灵遇水/水药水/喷泉回血回蓝）。
3. **召唤物召唤与到期 / 替换销毁**：
   - **点地精确召唤**：通过 `PathosEngineBridge.SummonAllyEntity` 召唤唯一随从（详见 [全射程选格与范围施法体系](range-targeting.md#33-关键避坑点地召唤技能禁止在声明式-plain-射线上直接挂-summonentity)）。
   - **到期 / 替换彻底销毁**：统一调用 `PathosEngineBridge.DespawnSummon(Engine, Summon, "expired")`（以 `Attacker = null` 结算死亡并彻底移出地图，无尸体、无运程惩罚）。
   - **禁止使用 `DismissAlly`**：`DismissAlly` 只会解除盟友关系并把单位留在场上变野生/中立生物，导致地图堆积大量残留召唤物。

---

## 5. 护甲与被动加甲 / 减甲映射（统一走 `Defence`）

Pathos 的怪物攻击常附带**解离、酸蚀、撤销、吸能、麻痹**等命中即触发的惩罚效果——在 Pathos 中**「不被打中（Defence）」远比「挨打后纯减伤」重要**。因此：

1. **敏捷护甲**：直接复用 Pathos 原生 `DEX` 提供的防御修正（$\lfloor(\text{DEX}-10)/2\rfloor$），不重复叠加。
2. **技能被动加甲 / 光环护甲 / 减甲 Debuff**：统一通过 `DotaHookBus.RegisterDefenceQuery`（Hook `Engine.GetDefenceClass`）实时修正面板 `Defence`，调用 `DotaMechanicsMapper.MapArmorToDefence(dotaArmor)`：
   - **12 甲以内**：`2.5` 点 DOTA2 护甲 = `1` 点 Pathos Defence（如龙骑士 +3/+6/+9/+12 甲 → `+1/+2/+4/+5` Defence）。
   - **超出 12 甲**：超出部分按 `4.0` 点护甲 = `1` 点 Defence 分段递减，默认正负封顶 **`+8` / `-8`**，既足够硬又不会击穿 d20 命中模型。
3. **「受击/挨打触发」类被动（如斧王反击螺旋）**：监听 `BeforeAttack` / `AfterAttack` 在敌人发起攻击时计数，**即使攻击被高 Defence 格挡/闪避（Miss）仍正常计次**，避免高护甲与反击机制互相冲突。

---

## 6. 天赋树系统与「减冷却 → 减蓝耗」映射规范

所有 DOTA2 英雄共享 `Framework/Talents/` 通用天赋框架与独立原生风格侧边面板（左下角「休息」按钮右侧的原生 `MenuButton` 或快捷键 `F2` 打开，原生 `TalentPanel` 与百科自动隐藏内部持久化标记；完整架构详见 [天赋树与长/短期技能状态体系](talents-and-skills.md)）：

1. **Pathos 节奏解锁阶梯（选定不可重置）**：
   - **四层二选一**：`Lv 5 / 10 / 15 / 20` 依次解锁 T1 ~ T4。
   - **双修补点**：`Lv 22 / 23 / 24 / 25` 依次解锁 T1 ~ T4 另一侧分支。
2. **减冷却（CDR）→ 减蓝耗统一换算**（`DotaMechanicsMapper.ComputeCooldownReductionRatio` + `MapCooldownReductionToManaCost`）：
   - Pathos 无技能冷却机制，凡 DOTA2 原版为**减冷却**的天赋，统一按减冷却百分比折算为**减蓝耗**：
     $$\text{CDR\%} = \frac{\Delta T}{T_{\text{ref}}},\qquad \text{FinalMana} = \max\Big(1,\ \text{Round}\big(\text{BaseMana} \times (1 - \text{CDR\%})\big)\Big)$$
   - **保护规则**：0 蓝耗技能保持 `0`；正蓝耗技能保底为 `1`；若原蓝耗 $> 1$ 且 $\text{CDR\%} > 0$，保证**至少减少 `1` 点蓝耗**；多来源乘算叠加封顶 `60%`。
   - 英雄只需在 `TalentNode.ManaReductions` 上声明 `SpellManaReductionRule`，`TalentService` 会通过 `DotaHookBus.RegisterSpellCostQuery` 自动接管 `Character.SpellCost`，施法扣蓝与 UI 显示蓝耗自动同步。
3. **存档持久化**：
   - 每个天赋节点注册为 `dota_talent_*` 内部 `Property`，学习时调用 `Engine.GainTalent` 写入 `Character.AcquiredTalentSet`，100% 跟随 Pathos 原生存档保存与加载。

---

## 7. 常见 DOTA2 机制在 Pathos 中的实现套路

| DOTA2 机制类型 | 推荐实现方式（优先复用 Framework 基建） | 现有参考 |
| :--- | :--- | :--- |
| **固定初始六维与蓝量** | `DotaMechanicsMapper.MapStartingStats` + `RegisterFixedStartingStats` | 主宰、变体精灵 |
| **长效技能（先天 / 被动）** | `Ctx.RegisterPassiveProperty` + `Class.AddFeat(1, ...)` + `DotaHookBus` 实时驱动 | 水人「潮涨潮落」、主宰「剑心犹在」「剑舞」 |
| **短期主动技能状态** | `Ctx.RegisterSkillStatusProperty`（自动从原生天赋栏与百科隐藏）+ `ApplyTransient` | 主宰「剑刃风暴」「无敌斩」 |
| **左上角动态叠层徽章** | `DotaStatusBarBridge.RegisterProvider`（复用原生 `StatusIcon`，支持计数与点击查说明） | 主宰「剑心犹在」 |
| **全射程选格 / 点地施法** | `DotaRangeTargetingBridge.RegisterSpell` + `DotaCombatHelper.ResolveHostileTarget` / `ResolveOpenPointTarget` + `RefundMana` | 主宰「无敌斩」「治疗守卫」、水人 Q/W/R |
| **被动加甲 / 减甲光环** | `RegisterDefenceQuery` + `DotaMechanicsMapper.MapArmorToDefence` | 框架内置支持 |
| **概率暴击 / 吸血 / 法球** | `BeforeDamage`（结合 `ActiveAttack` / `ActiveSpellCast`）+ `HealCharacter` | 主宰「剑舞」（含 T4 暴击吸血） |
| **持续光环 / 每回合转伤 / 治疗** | `AfterExecuteCommand` + `DotaCombatHelper.FindHostilesInRadius` / `IsFriendly` + `HealCharacter` | 主宰「剑刃风暴」「治疗守卫」 |
| **基础驱散 / 减益免疫** | `DotaCombatHelper.DispelNegativeDebuffs` + `BeforeApplyTransient`（`DotaCombatHelper.IsNegativeDebuff`） | 主宰「剑刃风暴」、水人高力免疫减速 |
| **多回合自动连斩 / 引导** | `ConditionalWeakTable` Session + `BeforeDamage` 无敌 + `PathosEngineBridge.QueueWaitDecision` | 主宰「无敌斩」 |
| **召唤物精确召唤与消散** | `PathosEngineBridge.SummonAllyEntity` + `PathosEngineBridge.DespawnSummon` | 主宰「治疗守卫」 |
| **自定义像素特效（VFX）** | `Ctx.RegisterStrikeVfx` / `RegisterBeamVfx` / `RegisterExplosion3x3Vfx` + `PathosEngineBridge.Story*` / `DotaCombatHelper.PlayBeamTrail` | 主宰全套技能、水人水爆 |
| **永久属性转换** | `AfterCastSpell` 中 `SetPermanentAbilityScore`（同步 `CurrentForm` / `MaximumForm`）+ `SetMeter` | 水人「属性转换」 |
| **角色私有运行时状态** | 使用 `ConditionalWeakTable<Character, TState>` 绑定角色实例，避免内存泄漏 | 主宰 `BladeformStates` / `WardStates` / `ActiveOmnislashSessions` |

---

## 8. 贴图与标识符命名规范

1. **按英雄分目录单份存放**：所有贴图放在 `Resources/Heroes/<hero_id>/<glyph_name>.png`（尺寸 `128x128`，透明背景 PNG），无需手动复制四份；构建时 MSBuild 会自动分发到 `absurd`、`classic`、`geoduck`、`sonyvanda` 四套图集。
2. **避免与官方内置 Glyph 重名**：
   - `PathosMaker` 打包后所有 Glyph 名称处于同一全局命名空间。
   - 例如官方已有内置怪物 `juggernaut`，因此主宰的英雄贴图使用 `yurnero.png` 以避免冲突。
   - 若不同目录下存在同名 PNG，`dotnet build` 的 `StageAtlasesForPathosMaker` 步骤会直接报错拦截。
3. **Hook Key 命名**：统一使用 `<hero_id>.<module>` 小写蛇形命名（如 `morphling.ebb_flow.speed`），确保幂等注册不冲突。

---

## 9. 英雄文档溯源规范

每新增或调整一名 DOTA2 英雄时，必须在对应的 `docs/heroes/<hero_id>.md` 中强制包含：
1. **参考版本标识**：在文档顶部明确标明所参照的刀塔百科版本号（如 **`DOTA2 7.42`**）。
2. **「DOTA2 7.42 → Pathos 数值映射溯源表」**：分「基础属性与资源」和「技能数值映射」两张表，逐项列出：
   - **DOTA2 原版数值**（初始三维、成长、蓝耗、持续秒数、护甲/减伤/回复比例等）
   - **Pathos 映射数值**（锁定六维、1 级蓝量、蓝耗、回合数、Defence 修正等）
   - **换算依据 / 备注**（注明所用的 `DotaMechanicsMapper` 公式或特殊平衡修正理由），便于后续版本变动时快速溯源调参。
