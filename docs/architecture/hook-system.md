# Hook 事件总线、引擎桥接与战斗基建

本文档用于查阅 `Framework/Hooks/` 与 `Framework/DotaCombatHelper.cs` 提供的全部运行时扩展能力，便于调整现有英雄机制或为新英雄快速接入底层能力。

## 1. 核心组件

| 文件 | 职责 |
| :--- | :--- |
| [HookBootstrap.cs](../../Framework/Hooks/HookBootstrap.cs) | 内嵌 `0Harmony.dll` 解析、跳过 `PathosMaker`、幂等挂载 `PathosEngine` 引擎补丁，并统一引导客户端 UI 桥接（技能与天赋面板、全射程选格、状态栏徽章、技能帮助面板）与演出/VFX 补丁（`DotaPresentation`、`DotaVfx`）。 |
| [DotaHookBus.cs](../../Framework/Hooks/DotaHookBus.cs) | 线程安全、**按字符串 Key 幂等注册**的事件总线。即使 Pathos 多次重建 Campaign，相同 Key 只覆盖旧处理器，不会重复触发。 |
| [HookEventArgs.cs](../../Framework/Hooks/HookEventArgs.cs) | 所有 Hook 事件的强类型参数对象，部分支持修改返回值或 `Cancel = true` 拦截原生逻辑。 |
| [PathosEngineBridge.cs](../../Framework/Hooks/PathosEngineBridge.cs) | 统一封装 `PathosEngine` / `Director` / `Story` / `Decision` / `Character` / `Meter` 等内部成员的反射访问与缓存，避免英雄代码散落反射。 |
| [DotaCombatHelper.cs](../../Framework/DotaCombatHelper.cs) | 收敛跨英雄复用的战斗、敌友判定、熟练度分档、标准基础驱散、目标解析、寻路靠近、空大返蓝与残影绘制辅助方法。 |
| [DotaTauntService.cs](../../Framework/DotaTauntService.cs) | 通用「嘲讽（Taunt）」状态与行为重定向服务：支持任意嘲讽者/受害者、自动唤醒与驱散恐惧、全链路禁魔、强制靠近与近战攻击嘲讽者、按嘲讽者回合统一递减。 |
| [DotaModifierService.cs](../../Framework/DotaModifierService.cs) | 通用 DOTA2 Modifier（带回合数的增益/减益）服务：回合递减、护甲/移速/攻速汇总、纯粹/元素持续伤害、减益免疫、基础驱散与状态栏徽章，零 `Codex.Properties` 占用。 |
| [DotaCharacterFlagSet.cs](../../Framework/DotaCharacterFlagSet.cs) | 弱引用角色标记集合，替代 `static HashSet<Character>`（铁律 6）。 |
| [DotaDamage.cs](../../Framework/DotaDamage.cs) | 统一伤害管线：伤害类型（物理/魔法/纯粹）与来源标注、暴击（取最高不叠加）与魔抗（乘算叠加）有序修正阶段。 |
| [DotaAbility.cs](../../Framework/DotaAbility.cs) / [DotaTable.cs](../../Framework/DotaTable.cs) | 技能一次性声明：5 阶法术外壳、全射程选格、按法术名施法分发、目标解析与空放返蓝、熟练度/等级/升星/射程查询；1 起始自动夹取的数值表。 |

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
| `RegisterBeforeExecuteCommand` | `Director.Execute` (Prefix) | `Cancel` | 行动前拦截或重定向（如嘲讽强制向嘲讽者移动或近战普攻） |
| `RegisterAfterExecuteCommand` | `Director.Execute` (Postfix) | 只读上下文 | 每回合行动结算（光环治疗、脱战叠层、Modifier 回合递减） |
| `RegisterBeforeAttack` | `Director.Attack` (Prefix) | `Cancel` | 拦截/改写普通攻击（或触发「被攻击即计次」类被动，如斧王反击螺旋） |
| `RegisterAfterAttack` | `Director.Attack` (Postfix) | 只读上下文 | 普攻后触发特效 |
| `RegisterAfterCreateActor` | `Engine.CreateActor` / `RerollActor` (Postfix) | 只读上下文 | 锁定 DOTA2 英雄固定初始六维与初始 HP/Mana |
| `RegisterDefenceQuery` | `Engine.GetDefenceClass` (Postfix) | `Defence` | 被动加甲、光环护甲与减甲 Debuff（实时反映在面板 Defence） |
| `RegisterSpellCostQuery` | `Character.SpellCost` (Postfix) | `ManaCost` | 减冷却转减蓝耗等法术蓝耗修正（施法扣蓝与 UI 显示自动同步） |
| `RegisterLimitFormQuery` | `Character.GetLimitForm` (Postfix) | `Form` | 英雄职业属性上限、天赋 `StatBonus` 与 7 级黄点上限叠加 |
| `RegisterActiveFormQuery` | `Character.GetActiveForm` (Postfix) | `Form` | 实时临时六维加成（不改永久值，如斧王「一人之军」） |
| `RegisterAfterLevelUp` | `Generator.AdvanceLevel` (Postfix) | 只读上下文 | 角色升级解锁技能点/天赋点提示与成长联动 |
| `RegisterInvulnerableQuery` | `DamageCharacter` / `Attack` / `ApplyTransient` / `TrapSquare` / `AfflictCharacter` / `ApplyEffects` | 返回 `bool` | 引擎级无敌/不可选中保护（免疫伤害、攻击、负面状态、陷阱、病痛与敌方/环境 Effect；如无敌斩、波浪形态） |
| `RegisterSilenceQuery` | `CastSpell` / `CastEffects` / `CanCast` | 返回 `bool` | 全链路沉默/禁魔（拦截法术、魔杖/卷轴、怪物吐息/凝视等；如嘲讽） |

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
| **属性与资源** | `GetCurrentAbilityScore` / `GetLimitAbilityScore` / `SetPermanentAbilityScore` / `SyncMaximumFormScores` | 同步读写 `CurrentForm` 与 `MaximumForm`，确保属性守恒不被药水/复活白嫖。**英雄代码请用强类型 `DotaStats`（§9）**。 |
| | `SetMeter` / `SetMeterPoints` | 直接调整 `Life` / `Mana` 上限与当前值（用于开局重算、空大返蓝）；最大生命联动请用 `DotaLife.AdjustMax`（§9）。 |
| | `GetSpellManaCost(ch, spell, skill)` | 原生 `Character.SpellCost` 真实蓝耗（含天赋减耗），帮助面板经 `DotaHelpFormat.ActualMana` 调用。 |
| | `GetQualification` | 查询角色在指定 `Skill` 上的熟练度。 |
| **战斗与治疗** | `HarmCharacter` / `StrikeCharacter` / `HealCharacter` / `KillCharacter` / `GetMeleeDamageDice` / `ForceDirectorAttack` | 走 Pathos 原生伤害/治疗/斩杀结算（英雄伤害请走 `DotaDamage` 管线，禁止裸调 `Harm/StrikeCharacter`）；`ForceDirectorAttack` 用于嘲讽强制近战攻击。 |
| **位移、变形与状态** | `TeleportCharacter` / `DashCharacter` / `ApplyTransient` / `RemoveTransient` / `HasTransient` | 瞬移、静音突进（`DashCharacter` 无传送音，配合 `StoryFlinging` 飞跃）、施加/移除/检测短期 `Transient`。 |
| | `PolymorphCharacter` / `UnpolymorphCharacter` / `ImpersonateCharacter` / `IsPolymorphed` | 变形为指定实体、解除变形、模仿目标角色、查询当前是否处于变形状态（如水人「变形」）。 |
| **学法与被动** | `LearnSpell` / `GetSpellMasteryLevel` / `GainTalent` / `HasAcquiredTalent` | `LearnSpell` 走 `Knowledge.LearnSpell` 安全学法与原生升星（禁止 `Engine.LearnSpell` 触发低智力惩罚）；`GainTalent` 授予原生被动 `Property`。 |
| **召唤物生命周期** | `SummonAllyEntity` | 通过原生 `Reactor.SummonEntity` 在指定格**精确召唤 1 个友方随从**，彻底规避 `Plain(Range)` 射线每格重复召唤多个的问题。 |
| | `DespawnSummon` | 以 `Attacker = null` 结算死亡并移出地图，彻底销毁到期/被替换的召唤物（无尸体、无运程惩罚；**禁止改用 `DismissAlly`**）。 |
| **视野与节奏** | `IsSquareExplored` / `IsSquareObservable` / `IsObservableCharacter` / `GetMoveDelay` / `GetAttackDelay` / `SetBusy` | 探索/视线判定；读取角色单回合逻辑帧数与设置忙碌时长（演出期间锁定玩家输入、收尾恢复）。 |
| **原生 Story 像素特效** | `StoryExploding` / `StoryBeaming` / `StoryStriking` / `StoryDetecting` / `StorySigning` / `StoryTeleporting` / `StoryAttacking` / `StoryCaption` / `StoryFlinging` / `StoryCancelCharacter` / `StoryEmitting` | 直接调用 Pathos 原生渲染与音效管线（爆炸、光束、斩击、脉冲、符文、瞬移、挥砍、飘字、平滑飞行、取消角色动画、音效）。自定义贴图特效请走 `DotaVfx`（§13）。 |
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
  // M.Defence（已 MapArmorToDefence 折算）/ M.AttackSpeedPct / M.DamageBlock / M.Stacks / M.Tag
});
if (Mod == null) { /* 目标无效、减益被免疫 */ }
```

| 字段 | 汇总方式 |
| :--- | :--- |
| `Defence` | 全部 Modifier 相加，经 `dota.modifiers.defence` 叠加到面板防御 |
| `MoveSpeedPct` / `AttackSpeedPct` | 全部 Modifier **加算**后乘到速度上（对齐 DOTA2 百分比移速加算），倍率下限 `MinMoveSpeedMultiplier = 0.35` |
| `DotDotaDps` | 持有者每回合行动后结算，小数自动累加 |
| `DamageBlock` | 每次受到伤害的格挡量（Pathos 点，可为小数，通常 = DOTA2 原值 ÷ 15）。全部 Modifier 相加，经 `dota.modifiers.block`（`BeforeDamage`，优先级 `Reduction + 10`，排在魔抗之后）对物理/魔法/纯粹伤害逐次扣除：整数部分必挡，小数部分按概率 +1（`DotaModifierService.RollBlock`），扣到 0 时取消本次伤害，但不拦截命中附带效果。已自行结算过格挡的伤害带 `DotaDamageFlags.NoBlock` 跳过。参考实现：帕吉「肉盾」 |

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

---

## 8. `DotaAbility` 技能声明与 `DotaTable` 数值表

[DotaAbility.cs](../../Framework/DotaAbility.cs) 把一个 DOTA2 技能的全部「元数据」收敛到一处声明，消灭同一射程/学派在法术外壳、选格、施法、帮助面板中各写一遍导致的漂移。

### 8.1 声明（英雄 `Register` 中，每技能一次）

```csharp
HungerAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
{
  SpellName = "battle hunger",                 // 法术名（选格 / 分发 / 帮助面板 / 伤害来源 Key）
  DisplayTitle = "Battle Hunger", TitleResolver = () => DotaLocalization.T("axe.skill.w.title"),
  Description = "...", Icon = BattleHungerGlyph,
  School = Ctx.Schools.enchantment,            // Spell.School
  Skill = Ctx.Skills.enchantment,              // 决定熟练度阶数
  Precept = new Precept(Purpose.Blast, Ctx.Elements.fire),
  ManaCost = BattleHungerManaCost,             // 已 MapSkillManaCost 换算
  Targeting = DotaAbilityTargeting.UnitHostile,// Self / UnitHostile / PointOpen / PointAny
  Range = DotaTable.Of(4, 5, 5, 6, 7),         // 5 阶熟练度基础射程（Self 可省略）
  RangeBonus = ch => ...,                      // 可选：升星 / 天赋 / 先天属性加射程
  AllowSelfTarget = false,                     // 可选：点地技能允许点自身格
  SkillSlotId = SkillHungerId,                 // HeroSkillBuild 技能槽（等级 / 升星）
  ConfigureApply = (tier, apply) => ...,       // 可选：声明式效果（默认 Apply.Nothing()）
  OnCast = Cast => HandleBattleHungerCast(Cast)
});
```

框架自动完成：
- **5 阶法术外壳**：`Self` → `SetTargetSelf(true)`；指向类 → `Plain(Dice.Fixed(Range[tier]))`（+ `AllowSelfTarget` 时 `SetTargetSelf(true)`）；`Apply` 默认 `Nothing()`，避免 `Plain` 射线每格重复结算。
- **全射程选格**：射程 = `Range[熟练度阶数] + RangeBonus(ch)`，与施法结算使用同一来源。
- **施法分发**：单个 `dota.abilities.cast`（`AfterCastSpell`）按法术名字典分发到 `OnCast`，英雄不再注册 `AfterCastSpell` + 比较法术名。

### 8.2 施法上下文 `DotaAbilityCast`

| 成员 | 说明 |
| :--- | :--- |
| `Args` / `Engine` / `Caster` / `Direction` | 原始施法参数 |
| `Tier` / `Range` | 施法瞬间的熟练度阶数与实际射程 |
| `TryResolveHostileTarget(out target, noTargetLogKey)` | 四级目标解析；失败时 **按本次实际扣蓝返还** 并写日志 |
| `ResolveHostileTarget()` / `ResolveOpenPointTarget()` / `ConsumePendingTarget()` | 不返蓝的底层解析（点击结果只取出一次） |
| `Refund()` / `RefundAndLog(key)` / `Log(text)` | 返蓝与带技能图标的日志 |

### 8.3 查询

`Ability.Tier(ch)`、`Ability.Level(ch)` / `Level(ch, max)`（夹取到 `[1, max]`）、`Ability.StarSteps(ch)`、`Ability.StarMultiplier(ch)`（默认每星 `+15%`，即 `HeroSkillService.DefaultStarBonusPerStep`）、`Ability.BaseRange(tier)`、`Ability.Range(ch)`、`Ability.Spell`。加点面板描述文本用 `HeroSkillService.GetStarMultiplierForLevel(lv)`。

### 8.4 `DotaTable`

`DotaTable.Of(a, b, c, ...)[n]` 为 1 起始、越界自动夹取的数值表，统一替代 `Arr[Math.Clamp(n - 1, 0, Len - 1)]`：5 阶熟练度表用 `[tier]`，技能等级表用 `[skillLevel]`；全阶同值用 `DotaTable.Uniform(v)`。

---

## 9. 属性与生命：`DotaStats` / `DotaLife` / `TalentNode.StatBonus`

### 9.1 `DotaStats`（强类型六维）

[DotaStats.cs](../../Framework/DotaStats.cs) 以 `DotaStat` 枚举替代 `"strength"` / `"dexterity"` 等裸字符串（底层仍转发 `PathosEngineBridge` 的按名称接口）：

| 方法 | 说明 |
| :--- | :--- |
| `DotaStats.Str(ch)` / `Dex(ch)` / `Con(ch)` / `Get(ch, DotaStat.X)` | `CurrentForm` 当前值 |
| `DotaStats.Limit(ch, DotaStat.X)` | `LimitForm` 上限（含职业上限、天赋 `StatBonus`、7 级黄点） |
| `DotaStats.SetPermanent(ch, DotaStat.X, score)` | 同步写入 `CurrentForm` 与 `MaximumForm` |
| `DotaStats.RaisePermanentClamped(ch, DotaStat.X, delta)` | 写入 `Min(上限, 当前 + delta)` |

### 9.2 `DotaLife.AdjustMax`（最大生命联动）

[DotaLife.cs](../../Framework/DotaLife.cs) 收敛三种既有写法，力量 → 生命统一使用 `DotaMechanicsMapper.HpPerStrengthPoint = 6`：

| 模式 | 规则 | 使用者 |
| :--- | :--- | :--- |
| `KeepRatio` | 新上限 `Max(10, 上限 + Δ)`，当前值按原百分比缩放并夹取到 `[1, 新上限]`（已死亡保持 0） | 斧王「一人之军」 |
| `RaiseBoth` | 新上限 `上限 + Δ`，当前值 `Min(新上限, 当前 + Δ)` | 水人转力、+力量天赋 |
| `ClampCurrent` | 新上限 `Max(10, 上限 + Δ)`，当前值仅夹取到 `[1, 新上限]` | 水人转敏 |

`DotaLife.AdjustMaxByStrength(ch, strDelta, mode)` 自动乘以 `HpPerStrengthPoint`。

### 9.3 声明式属性天赋 `TalentNode.StatBonus`

```csharp
T3Right: new TalentNode(
  Id: TalentT3Strength,
  TitleResolver: () => DotaLocalization.T("axe.talent.t3r.title"),
  DescriptionResolver: () => DotaLocalization.Format("axe.talent.t3r.desc", 3, 3 * DotaMechanicsMapper.HpPerStrengthPoint))
{
  StatBonus = PathosStatLimits.Bonus(Str: 3, Con: 3)
},
```

天赋框架自动完成：

1. **上限拓宽**：`DotaMechanicsMapper` 的属性上限查询叠加 `TalentService.GetLearnedStatBonus(ch)`（已学节点之和），无需再写 `RegisterHeroStatLimitBonus`。
2. **学习瞬间永久提升**：节点写入存档后（新上限已生效）按 `Min(上限, 当前 + 加成)` 写入；在 `OnLearn` 之前执行。
3. **生命联动**：`StatBonus.Str > 0` 时按 `DotaLifeAdjustMode.RaiseBoth` 提升 `Str × HpPerStrengthPoint` 的最大与当前生命。

> 迁移说明：旧版斧王 T3 右直接 `SetMeter(Points + 18, Total + 18)`，水人 T4 左则 `Min(Total + 42, Points + 42)`；二者在当前生命不超上限时完全等价，现统一为后者（`RaiseBoth`）。

---

## 10. 英雄档案 `DotaHeroProfile`

[DotaHeroProfile.cs](../../Framework/DotaHeroProfile.cs) 每个英雄声明一次 DOTA2 7.42 初始三维与成长，统一推导初始六维、职业上限与蓝量基准：

```csharp
// DOTA2 7.42 斧王初始三维与成长：STR 25 (+2.7) / AGI 18 (+1.5) / INT 18 (+1.6)
private static readonly DotaHeroProfile Profile = new(
  AxeClassName,
  BaseStr: 25, StrGain: 2.7f,
  BaseAgi: 18, AgiGain: 1.5f,
  BaseInt: 18, IntGain: 1.6f);   // 可选 Charisma: 10 / CharismaLimit: 20

// RegisterHooks：固定初始六维 + 职业属性上限
Profile.RegisterStats();

// 职业注册：Name / Description / Backpack / 生命成长 / 蓝量成长（按种族蓝量反推）/ 全性别头像
Ctx.Classes.Add(C =>
{
  Profile.ConfigureClass(Ctx, C, Description: "...", AvatarGlyph: Glyph,
    LifeBase: 16, LifeDice: 1.d12(), RaceManaBase: 1, ManaDice: 1.d2());
  C.SetDistribution(...);   // 属性分配、专长、熟练度与开局物品仍由英雄声明
});
```

`Profile.StartingStats` / `Profile.StatLimits` 可直接用于自定义种族的 `DefaultForm` / `LimitForm`（如水人 waterkin）。

---

## 11. 技能帮助面板辅助：`DotaHelpFormat` / `DotaSpellHelpBridge.Tiers`

| API | 说明 |
| :--- | :--- |
| `DotaHelpFormat.Tiles(n)` / `Turns(n)` / `Dex(n)` | 共享 `ui.help.tiles_val` / `turns_val` / `dex_val`（参数为 object，可传 `"1~4"` 区间文本） |
| `DotaHelpFormat.CastRangeSuffix(n)` | 熟练度阶梯标题后缀（`ui.help.cast_range_suffix`） |
| `DotaHelpFormat.ActualMana(ch, ability)` | 真实蓝耗：`PathosEngineBridge.GetSpellManaCost` → 原生 `Character.SpellCost`（含天赋减耗），与实际扣蓝一致；**禁止**在面板中手动重算 `MapCooldownReductionToManaCost` |
| `DotaSpellHelpBridge.Tiers(tier => header, tier => bullet)` | 生成 5 阶熟练度明细（单条说明的常见形态） |
| `DotaSpellHelpBridge.Tiers(tier => new DotaSpellHelpTier { ... })` | 多条说明的通用形态（`Tier` 字段自动填写） |
| `BuildTiers = HelpCtx => ...` + `Ability.RangeAt(ch, tier)` | 熟练度表**必须**按当前角色实算：每阶数值传入 `HelpCtx.Character` 走与施法相同的 `ComputeXxx(ch, tier)`（含升星、天赋、属性），射程用 `RangeAt`；当前阶与实时面板完全一致，百科（Character 为 null）自动退化为 1 级基础值 |
| `DotaSpellHelpBridge.RegisterPassive(new DotaPassiveHelpSpec { ... })` | 被动 / 先天 Property 帮助（补丁 `HelpContext.CompileProperty`）：保留原生描述，追加实时面板（角色取当前冒险主控玩家）、机制与 `Levels(max, ...)` 按技能等级明细 |
| `DotaCombatHelper.GetDisplayName(ch, fallbackKey = "ui.target_fallback")` | 战斗日志目标名：`Character.Name` → 本地化实体名 → 回退词 |

**施法与面板同源**：凡帮助面板展示的数值与施法结算使用同一公式（如斧王战斗饥渴 DPS、主宰剑刃风暴旋转骰 / 无敌斩斩数与单斩构成、水人变体打击爆发与眩晕），必须抽成英雄内一个 `ComputeXxx(ch, tier)` 方法供两处调用（示例：`AxeHero.ComputeCall` / `ComputeHungerDps`、`JuggernautHero.ComputeBladeFurySpin` / `ComputeOmnislash`、`MorphlingHero.ComputeAdaptiveStrike`），避免改了施法忘了面板。

---

## 12. 状态栏徽章辅助

```csharp
// 仅对指定职业的存活角色调用 Provider（替代 if (!IsX(ch) || ch.Deceased) return null;）
DotaStatusBarBridge.RegisterProvider("juggernaut.bladeform", JuggernautClassName, Ch =>
{
  var State = GetBladeformState(Ch);
  if (State.Stacks <= 0)
    return null;

  // 标题 = Property 本地化名（缺失时回退 Key）并大写；点击查看该 Property
  return DotaStatusBadge.ForPassive("juggernaut.bladeform", BladeformGlyph, BladeformTalentProperty,
    "jugg.status.bladeform_fallback", Count: $"×{State.Stacks}", Colour: Colour.Black);
});
```

- `DotaStatusBadge.TitleFrom(property, fallbackKey)` / `DotaStatusBadge.Title(key)`：只需标题时使用（如 `HelpSpell` 类徽章）。
- 会施加给友军的徽章（如斧王淘汰战吼）不要用职业过滤重载。
- 带剩余回合的增益/减益优先用 `DotaModifierSpec.Badge*` 自动生成徽章（见 §6）。

---

## 13. 技能演出节奏与 VFX：`DotaPresentation` / `DotaVfx`

### 13.1 为什么需要
Pathos 有两条时钟：**`Story.Clock`**（动画时钟，客户端每帧 +1，60 帧 ≈ 1 秒，等待玩家输入时也在走）和 **`Adventure.Clock`**（游戏逻辑时钟，只在 `Engine.Process()` 中推进，正常速度一回合仅 5 帧 ≈ 0.08 秒）。
因此「每步自动 Wait 一回合」的多段技能在不到 1 秒内就会全部结算完；同时原生 `Striking/Beaming/Exploding` 渲染**全程 100% 不透明、没有衰减**，多段刀光/拖尾会叠成满屏高亮。

### 13.2 `DotaPresentation`（[Framework/Presentation/DotaPresentation.cs](../../Framework/Presentation/DotaPresentation.cs)）
| API | 说明 |
| :--- | :--- |
| `Sequence(engine, owner, key)...At(秒, step)...Play()` | 把技能拆成挂在真实时间上的步骤；同 owner + key 重复播放会先结束旧序列。步骤上下文 `Stop()` 提前收尾（已 `After` 排入的收尾动作仍会播完），`After(秒, step)` 插入跟随动作。 |
| `.FreezeWorld(worldTicksPerStep)` | 播放期间跳过 `Engine.Process()`（怪物不行动、回合不流逝、玩家不能输入），动画照常播放；每执行一步放行 N 逻辑帧，用来保留技能原有的回合消耗。 |
| `.OnFinish((ctx, completed) => ...)` | 正常走完 / Stop / 被替换 / 超时（12 秒安全阀）都会回调，用于清理 Session 与恢复忙碌时长。 |
| `Loop(engine, owner, key, 间隔秒, while, tick)` | 纯视觉循环（不冻结世界），如剑刃风暴每 0.2 秒刷新一次跟随旋风。 |
| `HitStop(engine, owner, 秒)` | 重击顿帧（冻结世界零点几秒），如淘汰之刃斩杀。 |
| `Ticks(秒)` / `Seconds(秒)` | 秒 → Story 帧数 / 动画 `Delay`。 |

实现：`Story.NextTurn` 后置补丁每帧按 Story 时钟推进序列与循环；`Engine.Process` 前置补丁在有冻结序列时跳过逻辑推进。读档/新开局时旧引擎的序列自动作废。

### 13.3 `DotaVfx`（[Framework/Presentation/DotaVfx.cs](../../Framework/Presentation/DotaVfx.cs)）
- `DotaVfx.Style(strike|beam|explosion, new DotaVfxStyle { ... })`：注册渲染样式（淡入淡出、缩放、旋转、跨格尺寸、峰值透明度），客户端补丁 `Animator.Execute` 接管其绘制；未注册样式的特效仍走原生渲染。
- 播放（时长单位为秒）：`Strike(engine, sq, strike, 秒, 角度?, Span?)`、`StrikeOn(engine, character, strike, 秒)`（整个生命周期跟随角色）、`Beam`、`BeamTrail`、`Explode`。
- 工具：`LinePath(from, to)`（Bresenham 路径）、`AngleOf(from, to)`（攻击方向 → 贴图旋转角）。
- 样式字段说明与贴图生成工具见 [talents-and-skills.md §4.3](talents-and-skills.md)。
