# DOTA2 → Pathos 机制与数值映射规范

本规范用于保证后续新增或调整 DOTA2 英雄时，在 Pathos 回合制 Roguelike 体系下保持统一的节奏、手感与命名约定。

## 1. 技能蓝耗映射规范

Pathos 1 级英雄的法力上限通常在 `15 ~ 20` 左右，因此不能直接照搬 DOTA2 的蓝耗数值，建议按技能定位映射：

| DOTA2 技能定位 | 建议 Pathos 蓝耗 | 设计原则 |
| :--- | :---: | :--- |
| 开关 / 切换类（如水人 E） | **1 ~ 2 蓝** | 随时可切，通过属性代价或克制收益控平衡 |
| 常规小技能（Q / W） | **4 ~ 7 蓝** | 1 级时可流畅打出 2~3 次技能连招 |
| 高爆发 / 强控小技能 | **8 ~ 10 蓝** | 1 级可用 1~2 次，中后期作为主力输出/控制 |
| 大招（R） | **10 ~ 30 蓝 / Lv 6+ 专长** | 还原 DOTA2 核心大招节奏与关键回合爆发 |

---

## 2. 常见 DOTA2 机制在 Pathos 中的实现套路

| DOTA2 机制类型 | 推荐实现方式 | 现有参考 |
| :--- | :--- | :--- |
| **先天技能（Innate）** | 声明式注册 1 级职业专长（在天赋栏可见） + Hook 实时驱动效果 | 水人「潮涨潮落」、主宰「剑心犹在」 |
| **概率暴击 / 法球被动** | 声明式注册被动专长 + `BeforeDamage`（结合 `ActiveAttack` / `ActiveSpellCast`） | 主宰「剑舞」 |
| **持续光环 / 每回合伤害** | 状态标记（`Property` + `HasTransient`） + `AfterExecuteCommand` 驱动每回合 Tick | 主宰「剑刃风暴」「治疗守卫」 |
| **状态免疫 / 减益驱散** | 施法后 `RemoveTransient` 驱散 + `BeforeApplyTransient` 拦截负面状态 | 主宰「剑刃风暴」、水人高力免疫减速 |
| **无敌位移多段斩击** | `AfterCastSpell` 中维护 `HashSet<Character>` 无敌集合 + `BeforeDamage` 取消伤害 + `TeleportCharacter` + `StrikeCharacter` | 主宰「无敌斩」 |
| **永久属性转换** | `AfterCastSpell` 中同步修改 `CurrentForm` 与 `MaximumForm`，确保总量守恒，避免复活/恢复药水白嫖 | 水人「属性转换」 |
| **角色私有运行时状态** | 使用 `ConditionalWeakTable<Character, TState>` 绑定角色实例，避免内存泄漏 | 主宰 `BladeformStates` / `WardStates` |

---

## 3. 贴图与标识符命名规范

1. **按英雄分目录存放**：所有贴图放在 `Resources/Heroes/<hero_id>/<tileset>/<glyph_name>.png`，必须同时补齐四套图集：`absurd`、`classic`、`geoduck`、`sonyvanda`（尺寸 `128x128`，透明背景 PNG）。
2. **避免与官方内置 Glyph 重名**：
   - `PathosMaker` 打包后所有 Glyph 名称处于同一全局命名空间。
   - 例如官方已有内置怪物 `juggernaut`，因此主宰的英雄贴图使用 `yurnero.png` 以避免冲突。
   - 若两个英雄在同一图集下存在同名 PNG，`dotnet build` 的 `StageAtlasesForPathosMaker` 步骤会直接报错拦截。
3. **Hook Key 命名**：统一使用 `<hero_id>.<module>` 小写蛇形命名（如 `morphling.ebb_flow.speed`），确保幂等注册不冲突。
