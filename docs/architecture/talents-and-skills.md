# 技能加点、天赋树与长/短期技能状态体系

本文说明 **DOTA2 技能加点系统**、**四层双修天赋树**、**被动 / 短期状态 Property 的治理**、**状态栏徽章**以及**主动技能的持续 Tick、演出与像素特效**。
API 声明写法速查见 [framework-api.md](framework-api.md)。

---

## 0. 技能加点与双轨成长（`Framework/Skills/`）

### 0.1 规则

- **DOTA2 加点节奏**：1 级开局自带 **1 点技能点**，每升 1 级 +1（与天赋点独立）。Q/W/E/R 初始 0 级（先天技能自带）。
  - **小技能（Q / W / E）**：上限 4 级，英雄等级门槛 `Lv 1 / 3 / 5 / 7`。
  - **大招（R）**：上限 3 级，门槛 `Lv 6 / 12 / 18`。
  - **全属性黄点（`+`）**：上限 7 级，轮转提升（`Lv 1/4: STR+1, CON+1`、`Lv 2/5: DEX+1`、`Lv 3/6: INT+1, WIS+1`、`Lv 7: CHA+1 + 主属性+1`），满级共 `+12` 点；每级同步抬高对应属性的 `LimitForm` 上限。
- **主动技能（学派熟练度 × 原生升星）**：
  - `0 → 1`：`PathosEngineBridge.LearnSpell` 首次学会（0 星）。
  - `1 → 2/3/4`（大招 `1 → 2/3`）：原生升星（`★ / ★★ / ★★★`，原生法术面板显示星级并降低蓝耗）；英雄代码再用 `Ability.StarMultiplier(ch)`（默认每星 +15%）或 `Ability.Level(ch)` 叠加数值，与 Pathos 学派熟练度（1~5 阶）双重成长。
  - `CreateActiveGroup` 支持一次加点同步多个法术（水人 E 两个方向、R 变形/解除）。
- **被动技能（独立等级驱动，零 Property）**：
  - 不挂钩武器/学派熟练度，也不占用 `Codex.Properties`。
  - `1 → 4` 由 `HeroSkillService.GetSkillLevel(ch, slotId)` 驱动数值（如主宰剑舞暴击倍率 `1.4× / 1.6× / 1.8× / 2.0×`）。
- **存档持久化**：技能等级写入 `DotaCharacterStorage`（`Character.Dialogue.Tag`），主动技能星级由原生 `Knowledge` 保存。**不注册任何 Property**。

### 0.2 组件分工

| 文件 | 职责 |
| :--- | :--- |
| [HeroSkillDefinitions.cs](../../Framework/Skills/HeroSkillDefinitions.cs) | `DotaSkillSlot`（`CreateActive` / `CreateActiveGroup` / `CreatePassive` / `CreateStandardAttributeBonus`）、`DotaSkillStat`、`HeroSkillBuild.CreateStandard`。 |
| [HeroSkillRegistry.cs](../../Framework/Skills/HeroSkillRegistry.cs) | 按 `Class.Name` 索引技能表与槽位。 |
| [HeroSkillService.cs](../../Framework/Skills/HeroSkillService.cs) | 查询等级 / 星级 / 可用点数，执行加点（写存储 + 学法升星 + 授被动 + 黄点 + `OnUpgrade`）与升级日志。 |

声明写法见 [framework-api.md §3](framework-api.md)。

---

## 1. 四层双修天赋树（`Framework/Talents/`）

### 1.1 组件分工

| 文件 | 职责 |
| :--- | :--- |
| [TalentDefinitions.cs](../../Framework/Talents/TalentDefinitions.cs) | `HeroTalentTree`（`CreateStandard`）、`TalentTier`、`TalentNode`（`StatBonus` / `ManaReductions` / `OnLearn`）、`SpellManaReductionRule`。 |
| [TalentRegistry.cs](../../Framework/Talents/TalentRegistry.cs) | 按 `Class.Name` 索引天赋树与节点（不注册任何 Property）。 |
| [TalentService.cs](../../Framework/Talents/TalentService.cs) | 已学天赋 / 可用点数 / 节点状态查询；不可洗点学习（写 `DotaCharacterStorage` → 结算 `StatBonus` → `OnLearn`）；订阅 `SpellCostQuery`（减冷却 → 减蓝耗）与 `AfterLevelUp`（解锁提示）。 |
| [TalentTreeUiBridge.cs](../../Framework/Talents/TalentTreeUiBridge.cs) | 左下角「休息」按钮右侧的原生 `MenuButton`（`F2` 开关、`ESC` 关闭），打开左侧停靠的 DOTA2 风格「技能与天赋」面板：上半天赋树（左右天赋 + 中间等级徽章），下半技能栏（金色「+」加点、等级格）。技能详情与升级预览在悬停卡片中（逐级数值来自 `DotaSkillSlot.Stats`，当前级金色、下一级绿色）。同时过滤原生 `TalentPanel` / 百科中的短期状态 Property。 |

### 1.2 解锁阶梯与持久化

- **DOTA2 原版阶梯**（`HeroTalentTree.DefaultUnlockLevels` / `DefaultDualUnlockLevels`）：
  - 四层二选一：`Lv 10`（T1）/ `Lv 15`（T2）/ `Lv 20`（T3）/ `Lv 25`（T4）。
  - 双修补点：`Lv 27`（补 T1）/ `Lv 28`（T2）/ `Lv 29`（T3）/ `Lv 30`（T4）。
  - 选定不可反悔；有未分配技能点或天赋点时入口按钮高亮。
- **持久化**：已学天赋 ID 写入 `DotaCharacterStorage`，运行时用 `TalentService.HasTalent(ch, talentId)` 查询。天赋 ID 发布后不得改名。

### 1.3 天赋效果的三种写法

- **减冷却 → 减蓝耗**：`TalentNode.ManaReductions` 中声明 `new SpellManaReductionRule(spellName, cdrRatio)`（`cdrRatio = DotaMechanicsMapper.ComputeCooldownReductionRatio(reducedSec, refCooldownSec)`）。`TalentService` 通过 `SpellCostQuery` 接管 `Character.SpellCost`，扣蓝与面板显示同步（多来源乘算、封顶 60%、正蓝耗保底 1 且至少减 1）。
- **「+N 属性」**：`{ StatBonus = PathosStatLimits.Bonus(Str: 3, Con: 3) }`，上限拓宽、永久提升、生命联动由框架完成（[hook-system.md §9.3](hook-system.md)）。
- **学习瞬间的其他一次性效果**：`OnLearn: (engine, ch) => { ... }`（在 `StatBonus` 之后执行）。
- **持续生效的效果**：在对应 Hook / `ComputeXxx` 里 `TalentService.HasTalent(ch, TalentId)` 分支。

---

## 2. 零自定义 `Codex.Properties` 治理

Pathos 的 `PropertySet` 底层是单个 `Int64`（全局硬上限 64 个，官方已占 57 个）。**本扩展已全部迁移为零自定义 Property（0 占用，总计 57/64）**（见 [framework-api.md §2.3](framework-api.md)）：

| 类别 | 示例 | 实现方式 | 运行时表现 |
| :--- | :--- | :--- | :--- |
| **先天技能 / 被动技能** | 水人「潮涨潮落」、主宰「剑心犹在」「剑舞」、斧王「一人之军」「反击螺旋」 | `HeroSkillService`（被动等级）+ `DotaSpellHelpBridge.RegisterPassive` + `DotaStatusBadge.ForPassive` | 由 Hook 实时生效；动态层数显示在左上角状态栏，点击弹出原生帮助弹窗 |
| **带回合数的技能状态 / 增益减益** | 斧王「狂战士之吼」「战斗饥渴」「淘汰战吼」、主宰「剑刃风暴」「无敌斩」 | `DotaModifierService.Define` + `Apply` | 框架递减、汇总护甲/速度、DoT、驱散与左上角状态栏徽章 |
| **天赋 / 技能等级 / 持久化整数** | 四层天赋、技能加点、斧王淘汰永久护甲 | `DotaCharacterStorage`（`Character.Dialogue.Tag`） | 随存档完整序列化，零 Property |

> 禁止调用 `Ctx.Properties.Add`，确保后续新增任意数量英雄都不受 64 位上限约束。

---

## 3. 左上角状态栏徽章（`DotaStatusBarBridge`）

原生 `Transient` 只能显示剩余回合，无法显示层数/充能/变色。`DotaStatusBarBridge` 通过 `StagePanel.ArrangeStatus` 后置补丁把自定义徽章以**原生 `StatusIcon`** 插入左上角：

```csharp
// 仅对指定职业的存活角色调用；返回 null 表示当前不显示
DotaStatusBarBridge.RegisterProvider("juggernaut.bladeform", JuggernautClassName, Ch =>
{
  var State = GetBladeformState(Ch);
  if (State.Stacks <= 0)
    return null;

  // 标题取 TitleKey 并大写；点击图标弹出 DotaSpellHelpBridge 注册的被动帮助弹窗（零 Property）
  return DotaStatusBadge.ForPassive("juggernaut.bladeform", BladeformGlyph, "juggernaut.bladeform",
    "jugg.status.bladeform_fallback", Count: $"×{State.Stacks}",
    Colour: State.LingerTurns > 0 ? Colour.DarkOrange : Colour.Black);
});
```

- 按 `Key` 幂等覆盖。
- 会施加给友军/敌人的徽章不要用带职业过滤的重载；带剩余回合的效果直接用 `DotaModifierSpec.Badge*` 自动生成。

---

## 4. 主动技能实现模式

### 4.1 每回合持续技能（如剑刃风暴）

1. 用 `DotaAbility.Register` 声明 `Targeting = Self`（外壳默认 `Apply.Nothing()`），再用 `DotaModifierService.Define` 声明状态 Modifier，把每回合逻辑写在 `OnTick`。
2. `OnCast`：调用 `DotaModifierService.Apply` 施加持续状态（可同时写入 `MoveSpeedPct` / `Defence` 等），执行施法瞬间逻辑（如基础驱散、立刻结算第 1 跳），并把角色加入一个 `DotaCharacterFlagSet`（防止同一回合 `AfterExecuteCommand` 触发的 `OnTick` 再结算一次）。
3. `OnTick`：先 `FlagSet.Remove(holder)` 跳过施法当回合，其余回合执行每回合 Tick，到期自动移除。参考实现：主宰 `BladeFuryModifier` 与 `SkipNextBladeFuryTick`。

### 4.2 多步演出技能（如无敌斩、波浪形态）：`DotaPresentation`

Pathos 逻辑时钟一回合只有 **5 帧（≈0.08 秒）**，「每步 Wait 一回合」的连斩不到 1 秒就全部结算完、特效叠成一片。凡是一个技能包含多个先后动作（连斩、突进、逐格弹道），一律按**真实秒数**排步骤：

```csharp
DotaPresentation.Sequence(Engine, Caster, "<hero_id>.<feature>")
  .FreezeWorld(WorldTicksPerStep)          // 播放期间冻结世界；每步放行 N 逻辑帧以保留原有回合消耗（0 = 完全冻结）
  .At(0.00f, Ctx => { /* 第 1 步 */ })
  .At(0.25f, Ctx => { /* 第 2 步；Ctx.Stop() 提前收尾；Ctx.After(秒, ...) 插入收尾动作 */ })
  .OnFinish((Ctx, Completed) => { /* 清理状态 / 恢复忙碌时长 */ })
  .Play();
```

1. 节奏从 DOTA2 原版时长推导（如无敌斩 3/3.25/3.5 秒 ÷ 斩数、波浪形态 射程 ÷ 1250 速度），写入英雄文档溯源表。
2. 冻结世界前确认施法者处于忙碌状态（施法指令已自动设置；长序列用 `PathosEngineBridge.SetBusy` 覆盖整段）。
3. 位移：逻辑位置先 `PathosEngineBridge.DashCharacter` 到终点，再用 `StoryFlinging(...)` 让贴图在指定秒数内平滑飞过去。
4. 纯视觉循环（剑刃风暴旋风）用 `DotaPresentation.Loop(...)`；重击顿帧用 `DotaPresentation.HitStop(...)`。
5. 不要再用 `PathosEngineBridge.QueueWaitDecision` 做多回合连段（现已无英雄使用）。

### 4.3 自定义像素特效（`DotaHeroContext` VFX 工厂 + `DotaVfx` 样式）

单份 PNG 放在 `Resources/Heroes/<hero_id>/`：
- **单格打击/脉冲**（`Strike`）：`Ctx.RegisterStrikeVfx("name", "glyph name")` → `DotaVfx.Strike(...)` / 跟随角色 `DotaVfx.StrikeOn(...)`
- **4 方向光束/残影**（`Beam`）：`Ctx.RegisterBeamVfx("name", h, v, f, b)` → `DotaVfx.Beam(...)` / 沿直线 `DotaVfx.BeamTrail(...)`
- **3×3 爆炸**（`Explosion`）：准备 `<prefix> tl/tc/tr/ml/mc/mr/bl/bc/br.png`，`Ctx.RegisterExplosion3x3Vfx("name", "<prefix>")` → `DotaVfx.Explode(...)`
- **原生飘字与波纹**：`PathosEngineBridge.StoryCaption(...)`、`StoryDetecting(...)`、`StoryAttacking(...)`

**每个自定义 Strike / Beam / Explosion 都必须注册 `DotaVfxStyle`**：原生 `Striking/Beaming/Exploding` 全程 100% 不透明、没有衰减，不注册样式的刀光/拖尾会满屏高亮交错。

```csharp
DotaVfx.Style(OmnislashStrike, new DotaVfxStyle { FadeIn = 0.06f, FadeOutStart = 0.35f, ScaleFrom = 0.85f, ScaleTo = 1.2f, AngleJitter = 30 });
DotaVfx.Style(WhirlStrike,     new DotaVfxStyle { Span = 3, SpinDegreesPerSecond = 900, PeakOpacity = 0.85f });
```

| 字段 | 作用 |
| :--- | :--- |
| `FadeIn` / `FadeOutStart` / `PeakOpacity` | 透明度包络（淡入比例、开始淡出的进度、峰值不透明度）。频繁叠加的拖尾建议峰值 0.5~0.8。 |
| `ScaleFrom` / `ScaleTo` | 生命周期内缩放（三次缓出），如重劈 1.45→1.0「砸下去」、冲击环 0.3→1.05「扩散」。 |
| `Span` | Strike 占格边长（3 = 覆盖 3×3）；`DotaVfx.Strike(..., Span: 2R+1)` 可按技能半径覆盖。 |
| `SpinDegreesPerSecond` | 按 Story 绝对时钟旋转，循环生成的旋风无缝衔接。 |
| `RandomAngle` / `AngleJitter` | 斩击随机角度 / 在指定方向（`DotaVfx.AngleOf(from, to)`）上抖动，避免多刀重叠。 |
| `Ground` | 地面层（仅 Explosion）：普通特效在所有角色之后整体覆盖，地面层改为逐格画在该格地板之后、物品与角色之前，适合贴地的光环 / 毒雾，不遮挡单位。实现：动画阶段只记录格子，下一帧 `ScenePanel.DrawLocation` 后置补画（1 帧延迟）；忽略缩放，跳过墙体与关闭的门。参考实现：帕吉「腐烂」。 |

贴图由 [tools/VfxGen](../../tools/VfxGen/Program.cs) 按 Pathos 原生特效像素风生成：单格特效画在 32×32 网格上、最近邻 ×4 放大到 128；跨 3×3 旋转的贴图用 64 网格（×2），3×3 切片用 96 网格。每种特效只用一条 5~7 色色阶（暗色描边、亮色作芯），无抗锯齿，过渡与淡出用 4×4 有序抖动，边缘噪声扰动，底层垫很淡的柔光。造型与配色对照刀塔百科（wiki.dota2.com.cn）技能演示视频。修改后在仓库根目录执行：

```powershell
dotnet run --project tools/VfxGen -- .
```
