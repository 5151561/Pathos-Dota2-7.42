# Hook 事件总线与引擎桥接

本文档用于查阅 `Framework/Hooks/` 提供的全部运行时扩展能力，便于调整现有英雄机制或为新英雄设计 Hook。

## 1. 核心组件

| 文件 | 职责 |
| :--- | :--- |
| [HookBootstrap.cs](../../Framework/Hooks/HookBootstrap.cs) | 内嵌 `0Harmony.dll` 解析、跳过 `PathosMaker`、幂等挂载 `PathosEngine` 关键方法并转发至总线。 |
| [DotaHookBus.cs](../../Framework/Hooks/DotaHookBus.cs) | 线程安全、**按字符串 Key 幂等注册**的事件总线。即使 Pathos 多次重建 Campaign，相同 Key 只覆盖旧处理器，不会重复触发。 |
| [HookEventArgs.cs](../../Framework/Hooks/HookEventArgs.cs) | 所有 Hook 事件的强类型参数对象，部分支持修改返回值或 `Cancel = true` 拦截原生逻辑。 |
| [PathosEngineBridge.cs](../../Framework/Hooks/PathosEngineBridge.cs) | 统一封装 `PathosEngine` / `Director` / `Character` / `Meter` 等内部成员的反射访问与缓存，避免英雄代码散落反射。 |

---

## 2. `DotaHookBus` 全事件速查表

> 命名规范：Key 建议采用 `<hero_id>.<feature>`（如 `morphling.attribute_shift`、`juggernaut.turn_ticks`），确保全局唯一与幂等覆盖。

| 注册方法 | 对应引擎方法 | 可拦截 / 修改字段 | 典型用途 |
| :--- | :--- | :--- | :--- |
| `RegisterBeforeCastSpell` | `Director.CastSpell` (Prefix) | `Cancel` | 完全接管或拦截某法术施放 |
| `RegisterAfterCastSpell` | `Director.CastSpell` (Postfix) | 只读上下文 | 技能施放后的自定义结算（属性转换、驱散、召唤物追踪、无敌斩） |
| `RegisterBeforeDamage` | `Engine.DamageCharacter` (Prefix) | `DamagePoints`, `Cancel` | 暴击倍率、魔法/元素减伤、无敌免疫、技能伤害缩放 |
| `RegisterAfterDamage` | `Engine.DamageCharacter` (Postfix) | 只读上下文 | 受击触发（如剑心犹在进入粘滞期）、反伤、吸血 |
| `RegisterAfterKill` | `Engine.KillCharacter` (Postfix) | 只读上下文 | 击杀触发（刷新冷却、叠层、回蓝等） |
| `RegisterAfterResurrect` | `Engine.ResurrectCharacter` (Postfix) | 只读上下文 | 复活后状态修正 |
| `RegisterSpeedQuery` | `Engine.GetBaseSpeed` (Postfix) | `Speed` | 移动速度 / 攻击速度动态加成（区分 `Args.Attacking`） |
| `RegisterAttackRangeQuery` | `Engine.GetShootRange` / `GetThrowRange` | `Range` | 远程普攻射程加成（如水人高敏加射程） |
| `RegisterCastRange` | `Engine.CastEffects` | `BonusRange` | 法术施法距离加成（如水人高力加施法距离） |
| `RegisterBeforeApplyTransient` | `Engine.ApplyTransient` (Prefix) | `Duration`, `Cancel` | 状态免疫（剑刃风暴减益免疫）、减速抗性、眩晕时长动态缩放 |
| `RegisterAfterExecuteCommand` | `Director.Execute` (Postfix) | 只读上下文 | 每回合行动结算（光环回血、持续转伤、脱战叠层） |
| `RegisterBeforeAttack` | `Director.Attack` (Prefix) | `Cancel` | 拦截/改写普通攻击（或触发「被攻击即计次」类被动） |
| `RegisterAfterAttack` | `Director.Attack` (Postfix) | 只读上下文 | 普攻后触发特效 |
| `RegisterAfterCreateActor` | `Engine.CreateActor` / `RerollActor` (Postfix) | 只读上下文 | 锁定 DOTA2 英雄固定初始六维与初始 HP/Mana |
| `RegisterDefenceQuery` | `Engine.GetDefenceClass` (Postfix) | `Defence` | 被动加甲、光环护甲与减甲 Debuff（实时反映在面板 Defence） |

### 线程级上下文追踪

- `DotaHookBus.ActiveSpellCast`：仅在 `Director.CastSpell` 执行期间非空，供 `BeforeDamage` / `BeforeApplyTransient` 识别当前伤害或状态来自哪个具体法术。
- `DotaHookBus.ActiveAttack`：仅在 `Director.Attack` 执行期间非空，供 `BeforeDamage` 区分普通物理攻击与法术/环境伤害。

---

## 3. `PathosEngineBridge` 常用能力

当英雄 Hook 需要操作引擎内部对象时，优先复用或扩展 [PathosEngineBridge.cs](../../Framework/Hooks/PathosEngineBridge.cs)：

- **属性与数值**：读写 `CurrentForm` / `MaximumForm` 属性值、安全调整 `Meter`（HP/MP 上限与当前值）、查询技能熟练度 `GetQualification`。
- **战斗与伤害**：`HarmCharacter`（元素/魔法伤害）、`StrikeCharacter`（物理斩击）、`HealCharacter`、`GetMeleeDamageDice`。
- **位移与状态**：`TeleportCharacter`、`ApplyTransient`、`RemoveTransient`、`HasTransient`。
- **阵营与随从**：`IsAllied`、`IsTameAllied`、`DismissAlly`。
- **日志输出**：向游戏内 `Journal` / `Log` 写入带贴图的战斗事件提示。
