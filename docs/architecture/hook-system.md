# Hook 事件总线、引擎桥接与战斗基建

本文档用于查阅 `Framework/Hooks/` 与 `Framework/DotaCombatHelper.cs` 提供的全部运行时扩展能力，便于调整现有英雄机制或为新英雄快速接入底层能力。

## 1. 核心组件

| 文件 | 职责 |
| :--- | :--- |
| [HookBootstrap.cs](../../Framework/Hooks/HookBootstrap.cs) | 内嵌 `0Harmony.dll` 解析、跳过 `PathosMaker`、幂等挂载 14 组 `PathosEngine` 引擎补丁，并统一引导 3 组客户端 UI 桥接（天赋树、全射程选格、状态栏徽章）。 |
| [DotaHookBus.cs](../../Framework/Hooks/DotaHookBus.cs) | 线程安全、**按字符串 Key 幂等注册**的事件总线。即使 Pathos 多次重建 Campaign，相同 Key 只覆盖旧处理器，不会重复触发。 |
| [HookEventArgs.cs](../../Framework/Hooks/HookEventArgs.cs) | 所有 Hook 事件的强类型参数对象，部分支持修改返回值或 `Cancel = true` 拦截原生逻辑。 |
| [PathosEngineBridge.cs](../../Framework/Hooks/PathosEngineBridge.cs) | 统一封装 `PathosEngine` / `Director` / `Story` / `Decision` / `Character` / `Meter` 等内部成员的反射访问与缓存，避免英雄代码散落反射。 |
| [DotaCombatHelper.cs](../../Framework/DotaCombatHelper.cs) | 收敛跨英雄复用的战斗、敌友判定、熟练度分档、标准基础驱散、目标解析、寻路靠近、空大返蓝与残影绘制辅助方法。 |
| [DotaTauntService.cs](../../Framework/DotaTauntService.cs) | 通用「嘲讽（Taunt）」状态与行为重定向服务：支持任意嘲讽者/受害者、自动唤醒与驱散恐惧、全链路禁魔、强制靠近与近战攻击嘲讽者、按嘲讽者回合统一递减。 |

---

## 2. `DotaHookBus` 全事件速查表

> 命名规范：Key 建议采用 `<hero_id>.<feature>`（如 `morphling.attribute_shift`、`juggernaut.turn_ticks`），确保全局唯一与幂等覆盖。

| 注册方法 | 对应引擎方法 | 可拦截 / 修改字段 | 典型用途 |
| :--- | :--- | :--- | :--- |
| `RegisterBeforeCastSpell` | `Director.CastSpell` (Prefix) | `Cancel` | 完全接管或拦截某法术施放 |
| `RegisterAfterCastSpell` | `Director.CastSpell` (Postfix) | 只读上下文 | 技能施放后的自定义结算（属性转换、驱散、点地召唤、无敌斩启动） |
| `RegisterBeforeDamage` | `Engine.DamageCharacter` (Prefix) | `DamagePoints`, `Cancel` | 暴击倍率、魔法/元素减伤、无敌免疫、技能伤害缩放、普攻抑制 |
| `RegisterAfterDamage` | `Engine.DamageCharacter` (Postfix) | 只读上下文 | 受击触发（如剑心犹在进入粘滞期）、反伤 |
| `RegisterAfterKill` | `Engine.KillCharacter` (Postfix) | 只读上下文 | 击杀触发（刷新、叠层、回蓝等） |
| `RegisterAfterResurrect` | `Engine.ResurrectCharacter` (Postfix) | 只读上下文 | 复活后状态修正 |
| `RegisterSpeedQuery` | `Engine.GetBaseSpeed` (Postfix) | `Speed` | 移动速度 / 攻击速度动态加成（通过 `Args.Attacking` 区分） |
| `RegisterAttackRangeQuery` | `Engine.GetShootRange` / `GetThrowRange` | `Range` | 远程普攻射程加成（如水人高敏加射程） |
| `RegisterCastRange` | `Engine.CastEffects` | `BonusRange` | 法术施法距离加成（如水人高力加施法距离） |
| `RegisterBeforeApplyTransient` | `Engine.ApplyTransient` (Prefix) | `Duration`, `Cancel` | 减益免疫（剑刃风暴/无敌斩）、减速抗性、眩晕时长动态缩放 |
| `RegisterAfterExecuteCommand` | `Director.Execute` (Postfix) | 只读上下文 | 每回合行动结算（光环治疗、持续转伤、脱战叠层、自动多回合连斩） |
| `RegisterBeforeAttack` | `Director.Attack` (Prefix) | `Cancel` | 拦截/改写普通攻击（或触发「被攻击即计次」类被动，如斧王反击螺旋） |
| `RegisterAfterAttack` | `Director.Attack` (Postfix) | 只读上下文 | 普攻后触发特效 |
| `RegisterAfterCreateActor` | `Engine.CreateActor` / `RerollActor` (Postfix) | 只读上下文 | 锁定 DOTA2 英雄固定初始六维与初始 HP/Mana |
| `RegisterDefenceQuery` | `Engine.GetDefenceClass` (Postfix) | `Defence` | 被动加甲、光环护甲与减甲 Debuff（实时反映在面板 Defence） |
| `RegisterSpellCostQuery` | `Character.SpellCost` (Postfix) | `ManaCost` | 减冷却转减蓝耗等法术蓝耗修正（施法扣蓝与 UI 显示自动同步） |
| `RegisterAfterLevelUp` | `Generator.AdvanceLevel` (Postfix) | 只读上下文 | 角色升级解锁天赋点提示与成长联动 |

### 线程级上下文追踪

- `DotaHookBus.ActiveSpellCast`：仅在 `Director.CastSpell` 执行期间非空，供 `BeforeDamage` / `BeforeApplyTransient` 识别当前伤害或状态来自哪个具体法术。
- `DotaHookBus.ActiveAttack`：仅在 `Director.Attack` 执行期间非空，供 `BeforeDamage` 区分普通物理攻击与法术/环境伤害。

---

## 3. `PathosEngineBridge` 能力速查

当英雄 Hook 需要操作引擎内部对象时，优先复用或扩展 [PathosEngineBridge.cs](../../Framework/Hooks/PathosEngineBridge.cs)：

| 分类 | 方法 | 说明 |
| :--- | :--- | :--- |
| **属性与资源** | `GetCurrentAbilityScore` / `GetLimitAbilityScore` / `SetPermanentAbilityScore` / `SyncMaximumFormScores` | 同步读写 `CurrentForm` 与 `MaximumForm`，确保属性守恒不被药水/复活白嫖。 |
| | `SetMeter` / `SetMeterPoints` | 直接调整 `Life` / `Mana` 上限与当前值（用于开局重算、水人血量联动、空大返蓝）。 |
| | `GetQualification` | 查询角色在指定 `Skill` 上的熟练度。 |
| **战斗与治疗** | `HarmCharacter` / `StrikeCharacter` / `HealCharacter` / `GetMeleeDamageDice` | 走 Pathos 原生伤害/治疗结算（自动触发 `BeforeDamage`、暴击、元素抗性与日志）。 |
| **位移与状态** | `TeleportCharacter` / `ApplyTransient` / `RemoveTransient` / `HasTransient` | 瞬移、施加/移除/检测短期状态（`Transient`）。 |
| **召唤物生命周期** | `SummonAllyEntity` | 通过原生 `Reactor.SummonEntity` 在指定格**精确召唤 1 个友方随从**，彻底规避 `Plain(Range)` 射线每格重复召唤多个的问题。 |
| | `DespawnSummon` | 以 `Attacker = null` 结算死亡并移出地图，彻底销毁到期/被替换的召唤物（无尸体、无运程惩罚；**禁止改用 `DismissAlly`**）。 |
| **天赋持久化** | `GainTalent` / `HasAcquiredTalent` | 读写原生 `AcquiredTalentSet`，随 Pathos 存档自动保存与读档。 |
| **多回合自动行动** | `QueueWaitDecision` | 为角色排入下一回合的自动 `Wait` 指令，驱动多回合自动连斩（如无敌斩）逐帧播完动画，无需玩家手动点等待。 |
| **原生 Story 像素特效** | `StoryExploding` / `StoryBeaming` / `StoryStriking` / `StoryDetecting` / `StorySigning` / `StoryTeleporting` / `StoryAttacking` / `StoryCaption` | 直接调用 Pathos 原生渲染管线播放 3×3 爆炸、方向光束、单格斩击、范围脉冲、符文、瞬移、挥砍动作与飘字（如 `暴击!`）。 |
| **阵营、日志与本地化** | `IsAllied` / `WriteInternalLog` / `GetLocalizedName(Property/Entity)` | 友军判定、写入带图标的游戏内战斗日志、获取当前语言本地化名称。 |

---

## 4. `DotaCombatHelper` 通用战斗基建速查

[DotaCombatHelper.cs](../../Framework/DotaCombatHelper.cs) 封装了新英雄最高频的通用逻辑，避免各英雄文件重复造轮子：

| 分类 | 方法 | 用途 |
| :--- | :--- | :--- |
| **身份与熟练度** | `IsClass(char, className)` / `IsEntity(char, entityName)` / `GetSkillTier(ctx, char, skill)` | 识别英雄职业、召唤物实体，以及将熟练度转为 `1~5` 阶整数。 |
| **阵营与来源判定** | `IsHostile(src, tgt)` / `IsFriendly(src, tgt)` / `IsNonBasicPhysicalIncoming(args)` | 统一判断存活敌军、存活友军、以及非普通物理攻击来源（法术/元素/环境）。 |
| **标准基础驱散** | `GetStandardNegativeDebuffs(ctx)` / `IsNegativeDebuff(ctx, prop)` / `DispelNegativeDebuffs(ctx, engine, tgt)` | 统一维护 Pathos 14 种负面控制与异常状态，一键实现「基础驱散」与「减益免疫」。 |
| **装备、骰子与返蓝** | `GetEquippedWeapon(ctx, char)` / `RollDice(dice)` / `RefundMana(char, mana)` | 读取主手武器、安全掷骰（保底 1）、指向性技能空大返蓝。 |
| **格子与范围搜索** | `IsOpenSquare(sq)` / `FindHostilesInRadius(src, center, r)` / `PickRandomNearbyHostile(caster, r)` / `FindOpenAdjacentSquare(caster, targetSq)` / `FindStepToward(from, to)` | AOE 搜敌、随机跳斩选敌、寻找目标旁可站立空位、向目标靠近一步。 |
| **全射程目标解析** | `ResolveHostileTarget(caster, dir, range, pending)` / `ResolveOpenPointTarget(caster, dir, pending)` | 消费 `DotaRangeTargetingBridge.ConsumePendingTarget`：敌方目标支持「精确点击 → 周围 1 格智能吸附 → 方向射线 → 射程内最近敌人」四级解析；点地目标支持「点击格 → 方向相邻格 → 周围空位回退」。 |
| **连线特效** | `PlayBeamTrail(engine, fromSq, toSq, beam, maxSteps)` | 沿 Bresenham 直线播放 4 方向 `Beam` 瞬移残影/弹道连线。 |

---

## 5. `DotaTauntService` 通用嘲讽基建

[DotaTauntService.cs](../../Framework/DotaTauntService.cs) 将 DOTA2 嘲讽机制（如斧王狂战士之吼、军团决斗、寒冬飞龙寒冬诅咒等）抽离为即插即用的公共服务：

- **一键施加**：`DotaTauntService.ApplyTaunt(ctx, engine, taunter, victim, durationTurns, sourceKey: ..., onFallbackAttack: ...)` 自动完成首次 Hook 幂等注册、唤醒沉睡/移除恐惧、头顶飘字与嘲讽者回合追踪。
- **全链路禁魔 + 强制普攻**：自动挂载 `dota.taunt_silence` 与 `dota.taunt_redirect`，禁止被嘲讽单位施放任何法术/吐息/凝视/魔杖/卷轴，并强制其向 `taunter` 移动或近战普攻（嘲讽者死亡即刻解除）。
- **状态查询与清理**：提供 `IsTaunted`、`TryGetTaunter`、`GetRemainingTurns`、`ClearTaunt`、`ClearTauntsByTaunter` 与 `CountActiveTauntedTargets`。
