# DOTA2 → Pathos 机制与数值映射规范

本规范用于保证所有 DOTA2 英雄在 Pathos 回合制 Roguelike 体系下拥有统一、可推导、忠实于刀塔百科（DOTA2 Wiki）的数值标尺与手感。所有换算函数统一封装在 [DotaMechanicsMapper.cs](../../Framework/DotaMechanicsMapper.cs) 中。

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

## 3. 持续时间与召唤物生命周期规范

1. **时间换算标尺：1 秒 DOTA2 时间 = 2 回合 Pathos**（`DotaMechanicsMapper.MapDurationTurns(seconds)`）：
   - Pathos 走位与回合流逝极快，1 秒 = 1 回合会导致 4~5 秒的技能走两步就结束；按 **1 秒 = 2 回合** 换算后，5 秒剑刃风暴为 `10` 回合，18~24 秒治疗守卫为 `36~48` 回合，1.5~3 秒控制为 `3~6` 回合；同时每秒持续伤害/治疗按每回合 `0.5 秒` 折算（保持总伤/总奶量与原版一致）。
2. **召唤物到期 / 替换销毁**：
   - 统一调用 `PathosEngineBridge.DespawnSummon(Engine, Summon, "expired")`（以 `Attacker = null` 结算死亡并彻底移出地图，无尸体、无运程惩罚）。
   - **禁止使用 `DismissAlly`**：`DismissAlly` 只会解除盟友关系并把单位留在场上变野生/中立生物，导致地图堆积大量残留召唤物。

---

## 4. 护甲与被动加甲 / 减甲映射（统一走 `Defence`）

Pathos 的怪物攻击常附带**解离、酸蚀、撤销、吸能、麻痹**等命中即触发的惩罚效果——在 Pathos 中**「不被打中（Defence）」远比「挨打后纯减伤」重要**。因此：

1. **敏捷护甲**：直接复用 Pathos 原生 `DEX` 提供的防御修正（$\lfloor(\text{DEX}-10)/2\rfloor$），不重复叠加。
2. **技能被动加甲 / 光环护甲 / 减甲 Debuff**：统一通过 `DotaHookBus.RegisterDefenceQuery`（Hook `Engine.GetDefenceClass`）实时修正面板 `Defence`，调用 `DotaMechanicsMapper.MapArmorToDefence(dotaArmor)`：
   - **12 甲以内**：`2.5` 点 DOTA2 护甲 = `1` 点 Pathos Defence（如龙骑士 +3/+6/+9/+12 甲 → `+1/+2/+4/+5` Defence）。
   - **超出 12 甲**：超出部分按 `4.0` 点护甲 = `1` 点 Defence 分段递减，默认正负封顶 **`+8` / `-8`**，既足够硬又不会击穿 d20 命中模型。
3. **「受击/挨打触发」类被动（如斧王反击螺旋）**：监听 `BeforeAttack` / `AfterAttack` 在敌人发起攻击时计数，**即使攻击被高 Defence 格挡/闪避（Miss）仍正常计次**，避免高护甲与反击机制互相冲突。

---

## 5. 常见 DOTA2 机制在 Pathos 中的实现套路

| DOTA2 机制类型 | 推荐实现方式 | 现有参考 |
| :--- | :--- | :--- |
| **固定初始六维与蓝量** | `DotaMechanicsMapper.MapStartingStats` + `RegisterFixedStartingStats` | 主宰、变体精灵 |
| **先天技能（Innate）** | 声明式注册 1 级职业专长（在天赋栏可见） + Hook 实时驱动效果 | 水人「潮涨潮落」、主宰「剑心犹在」 |
| **被动加甲 / 减甲光环** | `RegisterDefenceQuery` + `DotaMechanicsMapper.MapArmorToDefence` | 框架内置支持 |
| **概率暴击 / 法球被动** | 声明式注册被动专长 + `BeforeDamage`（结合 `ActiveAttack` / `ActiveSpellCast`） | 主宰「剑舞」 |
| **持续光环 / 每回合伤害** | 状态标记（`Property` + `HasTransient`） + `AfterExecuteCommand` 驱动每回合 Tick | 主宰「剑刃风暴」「治疗守卫」 |
| **状态免疫 / 减益驱散** | 施法后 `RemoveTransient` 驱散 + `BeforeApplyTransient` 拦截负面状态 | 主宰「剑刃风暴」、水人高力免疫减速 |
| **无敌位移多段斩击** | `AfterCastSpell` 中维护 `HashSet<Character>` 无敌集合 + `BeforeDamage` 取消伤害 + `TeleportCharacter` + `StrikeCharacter` | 主宰「无敌斩」 |
| **永久属性转换** | `AfterCastSpell` 中同步修改 `CurrentForm` 与 `MaximumForm`，确保总量守恒，避免复活/恢复药水白嫖 | 水人「属性转换」 |
| **角色私有运行时状态** | 使用 `ConditionalWeakTable<Character, TState>` 绑定角色实例，避免内存泄漏 | 主宰 `BladeformStates` / `WardStates` |

---

## 6. 贴图与标识符命名规范

1. **按英雄分目录存放**：所有贴图放在 `Resources/Heroes/<hero_id>/<tileset>/<glyph_name>.png`，必须同时补齐四套图集：`absurd`、`classic`、`geoduck`、`sonyvanda`（尺寸 `128x128`，透明背景 PNG）。
2. **避免与官方内置 Glyph 重名**：
   - `PathosMaker` 打包后所有 Glyph 名称处于同一全局命名空间。
   - 例如官方已有内置怪物 `juggernaut`，因此主宰的英雄贴图使用 `yurnero.png` 以避免冲突。
   - 若两个英雄在同一图集下存在同名 PNG，`dotnet build` 的 `StageAtlasesForPathosMaker` 步骤会直接报错拦截。
3. **Hook Key 命名**：统一使用 `<hero_id>.<module>` 小写蛇形命名（如 `morphling.ebb_flow.speed`），确保幂等注册不冲突。

---

## 7. 英雄文档溯源规范

每新增或调整一名 DOTA2 英雄时，必须在对应的 `docs/heroes/<hero_id>.md` 中强制包含：
1. **参考版本标识**：在文档顶部明确标明所参照的刀塔百科版本号（如 **`DOTA2 7.42`**）。
2. **「DOTA2 7.42 → Pathos 数值映射溯源表」**：分「基础属性与资源」和「技能数值映射」两张表，逐项列出：
   - **DOTA2 原版数值**（初始三维、成长、蓝耗、持续秒数、护甲/减伤/回复比例等）
   - **Pathos 映射数值**（锁定六维、1 级蓝量、蓝耗、回合数、Defence 修正等）
   - **换算依据 / 备注**（注明所用的 `DotaMechanicsMapper` 公式或特殊平衡修正理由），便于后续版本变动时快速溯源调参。
