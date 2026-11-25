# 技能加点、天赋树与长/短期技能状态体系

本文档详细说明 `DotaHeroesExpansion` 中**DOTA2 升级技能加点系统（含原生升星与独立被动升级）**、**四层双修天赋树系统**、**长效技能 vs 短期主动技能状态（长短期技能治理）**、**左上角动态状态栏徽章**以及**多回合主动技能与像素特效**的底层架构与接入规范。

---

## 0. DOTA2 升级技能加点与双轨成长体系（`Framework/Skills/`）

### 0.1 核心定位：双轨双重成长 + 被动彻底解耦
- **纯 DOTA2 加点节奏**：英雄 1 级开局自带 **1 点技能点**，每升 1 级获得 1 点（与天赋树点数完全独立）。Q/W/E/R 初始均为 0 级（先天技能保持 1 级自带）。
  - **小技能（Q / W / E）**：上限 4 级，英雄等级门槛 `Lv 1 / 3 / 5 / 7`。
  - **大招（R）**：上限 3 级，英雄等级门槛 `Lv 6 / 12 / 18`。
  - **全属性加成（黄点 `+`）**：上限 7 级，按 DOTA2 力/敏/智三系轮转永久提升属性（`Lv 1/4: STR+1, CON+1`、`Lv 2/5: DEX+1`、`Lv 3/6: INT+1, WIS+1`、`Lv 7: CHA+1 + 英雄主属性+1`），满级共 `+12` 点 Pathos 属性（契合 DOTA2 满级黄点 `14 × 0.12 ≈ +2` 的统一映射标尺，避免过度稀释祝福能力药水价值；受角色 `LimitForm` 上限保护，同步 `CurrentForm` 与 `MaximumForm`）。
- **主动技能（学派熟练度 × 原生升星双重增幅）**：
  - `0 → 1` 级：调用 `PathosEngineBridge.LearnSpell` 首次学会该法术（0 星）。
  - `1 → 2 / 3 / 4` 级（大招 `1 → 2 / 3` 级）：每次加点调用 `Engine.LearnSpell` 原生升星（`★ / ★★ / ★★★`，在原生法术面板直接展示星星并降低施法蓝耗），同时在英雄 Hook 中叠加额外的升星基础数值加成（伤害倍率、治疗量、斩数/攻击力、射程或步进等），与 Pathos 原生学派熟练度（1~5 阶）双重叠加。
  - 支持多法术联动槽位（`CreateActiveGroup`），例如水人 E（属性转换·敏捷/力量）与 R（变形/解除变形）一次加点同步学会/升星。
- **被动技能（独立技能等级驱动）**：
  - 彻底脱离原版武器/学派熟练度（避免持重刃却挂中刃熟练度的违和问题）。
  - `0 → 1` 级自动通过 `Engine.GainTalent` 授予原生可见被动专长，`1 → 4` 级直接由 `HeroSkillService.GetSkillLevel(character, skillId)` 驱动数值（如主宰剑舞暴击倍率 `1.4× / 1.6× / 1.8× / 2.0×`）。
- **100% 原生存档持久化**：
  - 每个槽位在 Campaign 构建期自动生成 `dota_skill_<id>_lv1..N` 内部 `Property` 标记并注册至 `DotaPropertyVisibility` 隐藏，写入 `Character.AcquiredTalentSet`，读档自动还原。

### 0.2 组件分工

| 文件 | 职责 |
| :--- | :--- |
| [HeroSkillDefinitions.cs](../../Framework/Skills/HeroSkillDefinitions.cs) | 定义 `DotaSkillSlot`、`HeroSkillBuild`、`DotaSkillSlotKind`、`DotaSkillUpgradeState` 与标准 7 级黄点工厂。 |
| [HeroSkillRegistry.cs](../../Framework/Skills/HeroSkillRegistry.cs) | 按 `Class.Name` 索引英雄技能表，自动生成 `dota_skill_*` 持久化标记并登记隐藏。 |
| [HeroSkillService.cs](../../Framework/Skills/HeroSkillService.cs) | 查询技能等级/星级/可用技能点、执行加点（原生 `LearnSpell` 升星 + 被动授专长 + 黄点加属性）及升级日志提醒。 |

---

## 1. DOTA2 四层双修天赋树体系（`Framework/Talents/`）

### 1.1 组件分工

| 文件 | 职责 |
| :--- | :--- |
| [TalentDefinitions.cs](../../Framework/Talents/TalentDefinitions.cs) | 定义 `HeroTalentTree`、`TalentTier`、`TalentNode`、`TalentBranch`、`TalentNodeState` 与声明式 `SpellManaReductionRule`。 |
| [TalentRegistry.cs](../../Framework/Talents/TalentRegistry.cs) | 按 `Class.Name` 索引所有英雄天赋树；自动在 `Codex.Properties` 生成 `dota_talent_<id>` 内部标记并登记至 `DotaPropertyVisibility` 隐藏。 |
| [TalentService.cs](../../Framework/Talents/TalentService.cs) | 查询已学天赋与可用点数、执行不可洗点学习（调用 `Engine.GainTalent` 写入 `AcquiredTalentSet`）、自动订阅 `SpellCostQuery`（减冷却 → 减蓝耗）与 `AfterLevelUp`（升级解锁日志提示）。 |
| [TalentTreeUiBridge.cs](../../Framework/Talents/TalentTreeUiBridge.cs) | 挂载 `StagePanel`、`TalentPanel`、`HelpLibrary`：在左下角「休息」按钮右侧注入原生 `MenuButton`（紧贴 `EdgeWidth` 不裁切），提供与角色/法术/日志同位互斥的左侧停靠DOTA2 风格单页面板（上方天赋树：左右天赋 + 中间等级徽章，双修等级小字标注；下方技能栏：金色「+」直接加点、金色等级格；技能图标与天赋格支持悬停高亮，技能详情与升级预览统一放在 DOTA2 风格悬停卡片中（`Inv.Button.Tooltip.Content`，按「Lv1 / Lv2 / Lv3 / Lv4」列出逐级数值，当前级金色、可升级时下一级绿色；数值来自 `DotaSkillSlot.Stats`），面板本体不再有详情框），支持 `F2` 开关与 `ESC` 关闭。新英雄声明技能槽时应在 `Stats` 中用 `DotaSkillStat.Of(labelKey, Lv => ...)` 列出随等级变化的数值，并与 `DescribeLevel` / 施法共用同一套按等级函数。 | |

### 1.2 解锁阶梯与存档持久化

- **DOTA2 原版解锁阶梯**（`HeroTalentTree.CreateStandard`）：
  - **常规四层二选一**：`Lv 10`（T1）/ `Lv 15`（T2）/ `Lv 20`（T3）/ `Lv 25`（T4）。
  - **满级后双修补点**：`Lv 27`（补 T1）/ `Lv 28`（补 T2）/ `Lv 29`（补 T3）/ `Lv 30`（补 T4）。
  - 选定后不可反悔 / 不可洗点，有未分配技能点或天赋点时左下角按钮自动高亮提示。
- **100% 原生存档兼容**：
  - 每个 `TalentNode` 在 Campaign 构建期绑定一个 `dota_talent_<sanitized_id>` 内部 `Property`。
  - 学习时通过 `PathosEngineBridge.GainTalent` 写入 `Character.AcquiredTalentSet`，读档后 `TalentService.HasTalent(char, talentId)` 自动生效，无需外置存档文件。

### 1.3 声明式「减冷却 → 减蓝耗」与自定义效果

- **减冷却转减蓝耗**：只需在 `TalentNode` 的 `ManaReductions` 中声明 `new SpellManaReductionRule(spellName, cdrRatio)`（`cdrRatio` 用 `DotaMechanicsMapper.ComputeCooldownReductionRatio(reducedSec, refCooldownSec)` 计算），`TalentService` 会通过 `SpellCostQuery` 自动接管 `Character.SpellCost`，**施法扣蓝与法术面板蓝耗显示自动同步**（多来源乘算叠加封顶 60%，正蓝耗保底 1 且至少 -1）。
- **声明式属性天赋**：「+N 力量/敏捷」等天赋在节点上声明 `{ StatBonus = PathosStatLimits.Bonus(Str: 3, Con: 3) }`，框架自动拓宽属性上限、学习瞬间永久提升（夹取到新上限）并按 `DotaMechanicsMapper.HpPerStrengthPoint` 联动最大/当前生命（详见 [hook-system.md §9.3](hook-system.md)）。
- **学习瞬间触发的其他一次性效果**：可传入 `OnLearn: (engine, character) => { ... }`（如授予专长等；在 `StatBonus` 结算之后执行）。
- **持续/战斗期生效的天赋**：在对应 Hook 中直接调用 `TalentService.HasTalent(character, TalentId)` 分支判断。

---

## 2. 长短期技能与状态治理规范（极其重要）

在 Pathos 中，所有 `Codex.Properties` 默认都会出现在**角色原生「天赋」面板（`TalentPanel`，尤其是大师模式）**以及**游戏内百科「天赋/特性」（`HelpLibrary.Properties`）**中。如果把主动技能的短期状态也当普通 `Property` 注册，就会污染天赋列表，让玩家误以为它们是可获得的永久天赋。

因此，框架通过 **`DotaHeroContext` + `DotaPropertyVisibility` + `DotaStatusBarBridge`** 将技能与状态严格划分为 **三类**，分别走不同注册通道：

| 类别 | 语义与典型示例 | 注册入口 | 原生天赋栏 / 百科可见性 | 运行时表现 |
| :--- | :--- | :--- | :--- | :--- |
| **1. 长效技能（先天 / 被动）** | 角色常驻的先天技能与被动技能（如水人「潮涨潮落」、主宰「剑心犹在」「剑舞」） | `Ctx.RegisterPassiveProperty(name, desc, icon)` + `Class.AddFeat(1, prop)` | **可见**（角色天赋栏与百科中正常展示） | 通过 `DotaHookBus` 实时生效；若带动态叠层，额外接入 `DotaStatusBarBridge` 在左上角展示实时层数。 |
| **2. 短期主动技能状态（Transient-only）** | 主动技能施放后短暂持续的自身状态（如「剑刃风暴」「无敌斩」） | `Ctx.RegisterSkillStatusProperty(name, desc, icon)` | **自动隐藏**（`DotaPropertyVisibility` 在 `TalentPanel` 与 `HelpLibrary` 中自动剔除；技能本身仍在「法术」栏目展示） | 仅在施法期间通过 `ApplyTransient` 挂载到角色身上，在左上角显示原版倒计时状态图标，到期自动消失。 |
| **3. 内部持久化标记** | 天赋树节点存档标记（`dota_talent_*`） | `TalentRegistry.Register(...)` 自动生成 | **自动隐藏**（`DotaPropertyVisibility` 自动剔除） | 写入 `AcquiredTalentSet`，由独立天赋树面板管理展示。 |

> **接入铁律**：写新英雄时，**永远不要直接裸调 `Ctx.Properties.Add`**；按长/短期语义分别调用 `Ctx.RegisterPassiveProperty` 或 `Ctx.RegisterSkillStatusProperty`，即可自动完成可见性治理。

---

## 3. 左上角状态栏动态叠层徽章（`Framework/UI/DotaStatusBarBridge.cs`）

对于带**动态层数、充能或粘滞期变色**的状态（如主宰「剑心犹在」`×1 ~ ×10`，受击粘滞期变橙色），Pathos 原生 `Transient` 只能显示剩余回合数，无法展示自定义层数。

`DotaStatusBarBridge` 通过 Postfix 挂载 `StagePanel.ArrangeStatus`，将自定义徽章以**原生 `StatusIcon`** 形式插入 `StagePanel.LeftColumn.TopIcons`：

```csharp
DotaStatusBarBridge.RegisterProvider("juggernaut.bladeform", Character =>
{
  if (!DotaCombatHelper.IsClass(Character, JuggernautClassName) || Character.Deceased)
    return null;

  var State = GetBladeformState(Character);
  if (State.Stacks <= 0)
    return null; // 返回 null 表示当前不显示

  return new DotaStatusBadge
  {
    Key = "juggernaut.bladeform",
    Glyph = BladeformGlyph,
    Text = PathosEngineBridge.GetLocalizedName(BladeformTalentProperty).ToUpper(),
    Count = $"×{State.Stacks}",
    Colour = State.LingerTurns > 0 ? Colour.DarkOrange : Colour.Black,
    HelpProperty = BladeformTalentProperty // 支持点击图标弹出原生属性说明窗口
  };
});
```

- **幂等注册**：按 `Key` 覆盖，多次重建 Campaign 不会重复创建图标。
- **点击查百科**：只要填了 `HelpProperty`，玩家点击左上角状态图标即可调用原生 `HelpQuery.ShowProperty` 查看说明。

---

## 4. 主动技能实现模式：持续 Tick、自动多回合连斩与自定义像素 VFX

### 4.1 每回合持续技能（如剑刃风暴）
1. 声明式外壳：`SetTargetSelf(true)` + `ApplyTransient(statusProp, Dice.Fixed(turns))`。
2. `AfterCastSpell`：执行施法瞬间逻辑（如基础驱散 + 立刻结算第 1 跳），并将角色加入 `SkipNextTick` 集合（防止同一回合 `AfterExecuteCommand` 紧接着再结算一次）。
3. `AfterExecuteCommand`：若 `HasTransient(actor, statusProp)`，先检查 `SkipNextTick.Remove(actor)`，否则执行每回合 Tick。

### 4.2 多步演出技能（如无敌斩、波浪形态）：`DotaPresentation` 时间轴
Pathos 的游戏逻辑时钟正常速度一回合只有 **5 帧（≈0.08 秒）**，用「每步自动 Wait 一回合」推进的连斩会在不到 1 秒内全部结算完、特效叠成一片。
凡是「一个技能包含多个有先后的动作」（连斩、突进飞行、逐格扫过的弹道），一律用演出时间轴按**真实秒数**排步骤：

```csharp
DotaPresentation.Sequence(Engine, Caster, "<hero_id>.<feature>")
  .FreezeWorld(WorldTicksPerStep)          // 播放期间冻结世界；每步放行 N 逻辑帧以保留原有回合消耗（0 = 完全冻结）
  .At(0.00f, Ctx => { /* 第 1 步 */ })
  .At(0.25f, Ctx => { /* 第 2 步；Ctx.Stop() 提前收尾；Ctx.After(秒, ...) 插入收尾动作 */ })
  .OnFinish((Ctx, Completed) => { /* 清理 Session / 恢复忙碌时长 */ })
  .Play();
```

1. 节奏必须从 DOTA2 原版时长推导（如无敌斩 3/3.25/3.5 秒 ÷ 斩数、波浪形态 射程 ÷ 1250 速度），写入英雄文档溯源表。
2. 会冻结世界的序列开始前确认施法者处于忙碌状态（施法指令已自动设置；长序列用 `PathosEngineBridge.SetBusy` 覆盖整段），防止玩家中途插入指令。
3. 位移演出：逻辑位置先 `DashCharacter` 到终点，再用 `PathosEngineBridge.StoryFlinging(...)` 让贴图在指定秒数内平滑飞过去。
4. 纯视觉的持续循环（如剑刃风暴旋风）用 `DotaPresentation.Loop(...)`，不冻结世界、等待输入时也持续播放；重击顿帧用 `DotaPresentation.HitStop(...)`。
5. 旧的 **Session + `QueueWaitDecision`** 模式仅保留给「确实需要每步消耗一个完整玩家回合」的技能，新代码优先使用时间轴。

### 4.3 自定义像素特效（`DotaHeroContext` VFX 工厂 + `DotaVfx` 样式）
所有自定义特效走 Pathos 原生 `Story` 管线，单份 PNG 放在 `Resources/Heroes/<hero_id>/`：
- **单格打击/脉冲**（`Strike`）：`Ctx.RegisterStrikeVfx("name", "glyph name")` → `DotaVfx.Strike(...)` / 跟随角色 `DotaVfx.StrikeOn(...)`
- **4 方向光束/瞬移残影**（`Beam`）：`Ctx.RegisterBeamVfx("name", h, v, f, b)` → `DotaVfx.Beam(...)` / `DotaVfx.BeamTrail(...)`
- **3×3 范围爆炸**（`Explosion`）：准备 `<prefix> tl/tc/tr/ml/mc/mr/bl/bc/br.png` 9 张贴图，`Ctx.RegisterExplosion3x3Vfx("name", "<prefix>")` → `DotaVfx.Explode(...)`
- **原生飘字与范围波纹**：`PathosEngineBridge.StoryCaption(...)`、`PathosEngineBridge.StoryDetecting(...)`、`PathosEngineBridge.StoryAttacking(...)`

**必须为每个自定义 Strike / Beam / Explosion 注册 `DotaVfxStyle`**：原生 `Striking/Beaming/Exploding` 渲染全程 100% 不透明、没有任何衰减，不注册样式的刀光/拖尾会「满屏高亮交错」。

```csharp
DotaVfx.Style(OmnislashStrike, new DotaVfxStyle { FadeIn = 0.06f, FadeOutStart = 0.35f, ScaleFrom = 0.85f, ScaleTo = 1.2f, AngleJitter = 30 });
DotaVfx.Style(WhirlStrike,     new DotaVfxStyle { Span = 3, SpinDegreesPerSecond = 900, PeakOpacity = 0.85f });
```

| 字段 | 作用 |
| :--- | :--- |
| `FadeIn` / `FadeOutStart` / `PeakOpacity` | 透明度包络（淡入比例、开始淡出的进度、峰值不透明度）。叠加频繁的拖尾建议峰值 0.5~0.8。 |
| `ScaleFrom` / `ScaleTo` | 生命周期内缩放（三次缓出），如重劈 1.45→1.0「砸下去」、冲击环 0.3→1.05「扩散」。 |
| `Span` | Strike 占格边长（3 = 覆盖 3×3）；`DotaVfx.Strike(..., Span: 2R+1)` 可按技能半径逐次覆盖。 |
| `SpinDegreesPerSecond` | 按 Story 绝对时钟旋转，循环重复生成的旋风能无缝衔接。 |
| `RandomAngle` / `AngleJitter` | 斩击随机角度 / 在指定攻击方向（`DotaVfx.AngleOf(from, to)`）上抖动，避免多刀完全重叠。 |

贴图本身由 [tools/VfxGen](../../tools/VfxGen/Program.cs) 程序化生成（8× 超采样 → 64 逻辑像素 → 2× 最近邻放大，保持与现有贴图一致的 2px 像素颗粒；跨 3×3 旋转的环形贴图直接输出 128 全分辨率）。修改造型/配色后在仓库根目录执行：

```powershell
dotnet run --project tools/VfxGen -- .
```
