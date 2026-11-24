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
| [DotaModifierService.cs](../../Framework/DotaModifierService.cs) | 通用 DOTA2 Modifier（带回合数的增益/减益）服务：回合递减、护甲/移速/攻速汇总、纯粹/元素持续伤害、减益免疫、基础驱散与状态栏徽章，零 `Codex.Properties` 占用。 |
| [DotaCharacterFlagSet.cs](../../Framework/DotaCharacterFlagSet.cs) | 弱引用角色标记集合，替代 `static HashSet<Character>`（铁律 6）。 |
| [DotaDamage.cs](../../Framework/DotaDamage.cs) | 统一伤害管线：伤害类型（物理/魔法/纯粹）与来源标注、暴击（取最高不叠加）与魔抗（乘算叠加）有序修正阶段。 |

---

## 2. `DotaHookBus` 全事件速查表

> 命名规范：Key 建议采用 `<hero_id>.<feature>`（如 `morphling.attribute_shift`、`juggernaut.turn_ticks`），确保全局唯一与幂等覆盖。

| 注册方法 | 对应引擎方法 | 可拦截 / 修改字段 | 典型用途 |
| :--- | :--- | :--- | :--- |
| `RegisterBeforeCastSpell` | `Director.CastSpell` (Prefix) | `Cancel` | 完全接管或拦截某法术施放 |
| `RegisterAfterCastSpell` | `Director.CastSpell` (Postfix) | 只读上下文 | 技能施放后的自定义结算（属性转换、驱散、点地召唤、无敌斩启动） |
| `RegisterBeforeDamage` | `Engine.DamageCharacter` (Prefix) | `DamagePoints`, `Cancel`（只读 `Info` / `CritMultiplier`） | 固定值增伤、普攻抑制；**暴击与魔抗请改用 `DotaDamage` 管线**（§7） |
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

### 执行顺序与取消

- 每个 `Register*` 都接受可选 `Priority`（见 `DotaHookPriority`）：处理器按 **(Priority 升序, 首次注册顺序)** 确定性执行，重复 Key 原位覆盖。
- 约定阶段：`Early`（免疫/取消）→ `Normal`（固定值加减）→ `Amplify`（乘算增伤/暴击）→ `Reduction`（减伤/魔抗）→ `Late`（只读观察）。
- 可取消事件（`BeforeCastSpell` / `BeforeDamage` / `BeforeApplyTransient` / `BeforeExecuteCommand` / `BeforeAttack`）：某处理器设置 `Cancel = true` 后**立即停止**分发后续处理器。
- `SpellCastEventArgs.ManaSpent`（仅 `AfterCastSpell` 有效）为本次施法实际扣除的法力值；空放返蓝统一调用 `DotaCombatHelper.RefundSpellCost(Args)`，禁止按 `Spell.Mana` 或常量返还。

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
| **阵营与来源判定** | `IsHostile(src, tgt)` / `IsFriendly(src, tgt)` | 统一判断存活敌军、存活友军（伤害来源请读 `DamageEventArgs.Info`，`IsNonBasicPhysicalIncoming` 已标记 Obsolete）。 |
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

---

## 6. `DotaModifierService` 通用增益/减益（Modifier）基建

[DotaModifierService.cs](../../Framework/DotaModifierService.cs) 是 DOTA2 「Modifier」概念的统一实现。**凡是带剩余回合数的增益/减益（护甲增减、加速/减速、持续伤害、战吼光环等），一律走本服务**，不要再在英雄里手写 `State` 类 + `ConditionalWeakTable` + 手动 `--RemainingTurns` + 单独的 Defence/Speed 查询。

### 6.1 定义（每种 Modifier 一次）

在英雄 `Register` 中、贴图注册之后：

```csharp
BattleHungerModifier = DotaModifierService.Define(new DotaModifierSpec
{
  Key = "axe.battle_hunger",          // <hero_id>.<feature>
  IsDebuff = true,                    // 受无敌/减益免疫阻挡，可被基础驱散
  IsDispellable = true,
  Stacking = DotaModifierStacking.Replace,  // 或 KeepStrongest
  DotElement = null,                  // null = 纯粹伤害（StrikeCharacter）；否则按元素 HarmCharacter
  DotCause = "battle hunger",
  // 可选：BadgeGlyph / BadgeTitle / BadgeColour / BadgeCount / HelpSpell / HelpProperty → 自动生成状态栏徽章
  // 可选：OnTick(engine, holder, mod) / OnRemoved(engine, holder, mod, reason)
});
```

### 6.2 施加（每次施法按等级填入数值）

```csharp
var Mod = DotaModifierService.Apply(BattleHungerModifier, Caster, Target, DurationTurns, M =>
{
  M.DotDotaDps = EffectiveDps;   // DOTA2 原始 DPS，框架按 K_dmg = 15、每回合 0.5 秒累加小数结算
  M.MoveSpeedPct = -Slow;        // 负数为减速
  // M.Defence（已 MapArmorToDefence 折算）/ M.AttackSpeedPct / M.Stacks / M.Tag
});
if (Mod == null) { /* 目标无效、减益被免疫 */ }
```

| 字段 | 汇总方式 |
| :--- | :--- |
| `Defence` | 全部 Modifier 相加，经 `dota.modifiers.defence` 叠加到面板防御 |
| `MoveSpeedPct` / `AttackSpeedPct` | 全部 Modifier **加算**后乘到速度上（对齐 DOTA2 百分比移速加算），倍率下限 `MinMoveSpeedMultiplier = 0.35` |
| `DotDotaDps` | 持有者每回合行动后结算，小数自动累加 |

### 6.3 生命周期

- **回合递减**：随**持有者自身**行动回合（`dota.modifiers.tick`），顺序为 `OnTick` → 持续伤害 → `RemainingTurns--` → 到期移除（`Expired`）。
- **叠加**：`Replace` 完全覆盖；`KeepStrongest` 剩余回合取最大、各数值按绝对值取最大。
- **驱散**：`DotaCombatHelper.DispelNegativeDebuffs` 同时清除原生负面 `Transient` 与全部 `IsDebuff && IsDispellable` 的 Modifier；也可直接调用 `DotaModifierService.Dispel(engine, unit, Debuffs: true/false)`。
- **减益免疫**：`IsDebuff` 的 Modifier 会被 `DotaHookBus.QueryInvulnerable` 与 `DotaModifierService.RegisterDebuffImmunity(key, predicate)` 阻挡（主宰剑刃风暴已注册）。
- **施加者死亡**：`RemoveOnSourceDeath = true` 时立即失效（链接/嘲讽类效果）。
- **查询**：`Get` / `Has` / `GetAll` / `Remove` / `CountAppliedBy(source, spec)`（如斧王 T1 天赋统计活跃饥渴目标数）/ `GetTotalDefence` / `GetMoveSpeedMultiplier`。

> 运行时状态保存在 `ConditionalWeakTable` 中，与原先英雄私有状态一致：**不随存档持久化**（读档后短期 Modifier 清空）。需要跨存档的永久数值仍走 `DotaCharacterStorage`。

---

## 7. `DotaDamage` 统一伤害管线

[DotaDamage.cs](../../Framework/DotaDamage.cs) 解决两件事：**这次伤害是什么**（类型 + 来源），以及**修正按什么顺序结算**。

### 7.1 结算带标注的伤害

英雄技能**一律**通过以下方法结算伤害，禁止直接调 `PathosEngineBridge.StrikeCharacter` / `HarmCharacter`：

```csharp
DotaDamage.Strike(engine, caster, target, dir, damage, DotaDamageInfo.Pure("culling blade"));               // 固定点数
DotaDamage.Harm(engine, caster, target, dir, Ctx.Elements.water, dice, DotaDamageInfo.Magical("waveform")); // 元素骰
DotaDamage.Strike(engine, caster, target, dir, slash, DotaDamageInfo.Physical("omnislash"));               // 攻击类（默认 Attack | CanCrit）
using (DotaDamage.BeginScope(info)) { /* 无法直接改写的原生结算路径 */ }
```

| 类型 | 用途 | 魔抗 |
| :--- | :--- | :--- |
| `Physical` | 普攻、无敌斩等攻击 | 不减免 |
| `Magical` | 法术伤害（剑刃风暴、波浪形态、变体打击） | 减免 |
| `Pure` | 纯粹伤害（战斗饥渴、反击螺旋、淘汰之刃） | 不减免 |
| `Unknown` | 无法归类的原生伤害（远程投射物、陷阱、怪物吐息等） | 减免 |

标记：`Attack`（触发「受到攻击」类被动）、`Spell`、`DoT`（`DotaModifierService` 持续伤害自动带上）、`CanCrit`。

未显式标注的伤害由框架自动归类到 `DamageEventArgs.Info.Origin`：`BasicAttack`（`Director.Attack` 期间，Physical + Attack + CanCrit）、`NativeSpell`（`Director.CastSpell` 期间，Magical）、`Unclassified`（其余，Unknown）。

### 7.2 有序修正阶段

| 阶段 | 注册方式 | 规则 |
| :--- | :--- | :--- |
| `Normal` | `DotaHookBus.RegisterBeforeDamage`（默认优先级） | 固定值增减（剑心犹在）、取消（普攻抑制） |
| `Amplify` | `DotaDamage.RegisterCritSource(key, args => roll or null)` | 各来源独立掷概率，**只取最高倍率**（DOTA2 暴击不叠加）；`OnCrit` 在倍率生效后调用，用于暴击吸血/特效/日志 |
| `Reduction` | `DotaDamage.RegisterMagicResist(key, args => ratio)` | 多来源 `1 - Π(1 - r)` 乘算叠加，只作用于 `Magical` / `Unknown`；结果为 0 时取消伤害 |

`AfterDamage` 中可读 `Args.Info` 判断来源（如斧王反击螺旋只把 `Unclassified` 与带 `Attack` 标记的脚本伤害计为「受到攻击」），`Args.CritMultiplier` 为本次采用的暴击倍率（未暴击为 0）。
