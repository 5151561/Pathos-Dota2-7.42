# AGENTS.md — Pathos DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

本文件为 AI 编程助手提供本仓库的架构速查、开发铁律与工作流指引。修改现有机制或新增 DOTA2 英雄前，请先遵循本文档规范，优先复用 `Framework/` 已有基建。

---

## 1. 项目概览与构建命令

- **项目性质**：面向桌面端 **Pathos: NetHack Codex** 的 `.NET 10` 扩展包（单 DLL 内嵌 `Lib.Harmony 2.4.3`）。
- **核心架构**：声明式 Codex 外壳 + `Harmony` 运行时引擎 Hook（`DotaHookBus`） + `Pathos.dll` 客户端原生 UI 桥接。
- **构建与自动部署**：
  ```powershell
  dotnet build .\DotaHeroesExpansion.csproj -c Release
  ```
  - 自动完成：C# 编译 → `StageAtlasesForPathosMaker`（校验 `Resources/` 无重名并分发至四套 `Atlases/`）→ `PathosMaker.exe` 打包图集/字典并部署到 `<PathosInstallation>\Expansions\`。
  - **注意**：部署阶段若 Pathos 游戏进程正在运行，目标 DLL 会被占用导致复制失败；编译与图集校验通过后，关闭游戏重新执行一次 `dotnet build` 即可完成部署。
- **运行时调试日志**：启动 Pathos 前设置 `$env:DOTA_HEROES_HOOK_DEBUG = "1"`，日志输出至 `%TEMP%\DotaHeroesHook.log`。

---

## 2. 目录与模块职责

```text
DotaHeroesExpansion.cs         # 扩展包总入口（HookBootstrap + HeroRegistry）
Framework/
├── IDotaHeroDefinition.cs     # 英雄接口（Id / Register / RegisterHooks）
├── DotaHeroContext.cs         # Codex 封装（长短期 Property / VFX 工厂 / 预设英雄 / 六维）
├── DotaMechanicsMapper.cs     # DOTA2 7.42 → Pathos 统一数值换算与固定六维锁定
├── DotaCombatHelper.cs        # 通用战斗工具（敌友判定 / 熟练度 / 基础驱散 / 目标解析 / 返蓝 / 残影）
├── Hooks/
│   ├── HookBootstrap.cs       # Harmony 引导器、14 组引擎补丁与 3 组客户端桥接入口
│   ├── DotaHookBus.cs         # 幂等按键事件总线 + 线程上下文（ActiveSpellCast / ActiveAttack）
│   ├── HookEventArgs.cs       # 强类型 Hook 事件参数
│   └── PathosEngineBridge.cs  # 引擎内部成员反射缓存（伤害/治疗/召唤/销毁/学法升星/Story 特效/自动回合）
├── Skills/                    # DOTA2 升级技能加点系统（Q/W/E/R 原生升星 + 被动独立升级 + 7 级全属性黄点）
├── Talents/                   # 四层双修天赋树（Lv 10/15/20/25 + Lv 27~30 双修）与左下角「技能与天赋」双 Tab 面板
├── Targeting/                 # DotaRangeTargetingBridge（全射程方格选框与点击捕获）
└── UI/
    ├── PathosClientReflection.cs  # Pathos.dll 查找、PatchSafe 与 Theme.GetImage 共享封装
    ├── DotaPropertyVisibility.cs  # 原生天赋栏与百科隐藏注册表（dota_talent_* + 短期技能状态）
    └── DotaStatusBarBridge.cs     # 左上角状态栏动态叠层徽章（原生 StatusIcon）
Heroes/                        # 各英雄实现（MorphlingHero.cs、JuggernautHero.cs 等）
Resources/Heroes/<hero_id>/    # 单份 128x128 透明 PNG（Git 跟踪，构建期自动分发至 Atlases/）
Assets/                        # PathosMaker 生成的 .Atlas 与 .Dictionary（含 zh-Simplified 中文词条）
docs/                          # 完整架构、基建、指南与英雄 7.42 溯源文档
```

---

## 3. 开发新英雄 / 改机制必须遵守的铁律

### 3.1 优先复用 `Framework/` 基建，禁止在 `Heroes/` 中重复造轮子
- **长短期技能与状态分类（严禁裸调 `Ctx.Properties.Add`）**：
  - 长效先天 / 被动天赋（需出现在角色原生天赋栏与百科）：使用 `Ctx.RegisterPassiveProperty(...)` + `Class.AddFeat(1, prop)`。
  - 短期主动技能状态（仅施法期间作为 `Transient` 生效）：**必须**使用 `Ctx.RegisterSkillStatusProperty(...)`，自动登记到 `DotaPropertyVisibility`，防止污染原生 `TalentPanel`（含大师模式）与百科「天赋/特性」栏目。
- **带回合数的增益/减益（Modifier）**：护甲增减、加速/减速、持续伤害、战吼类增益等，**必须**用 `DotaModifierService.Define` + `Apply`（回合递减、Defence/Speed 汇总、DoT、驱散、减益免疫与状态栏徽章由框架负责），禁止在英雄内手写 State 类 + 手动递减 + 单独 Defence/Speed 查询。详见 [hook-system.md §6](docs/architecture/hook-system.md)。
- **战斗与目标工具**：直接复用 `DotaCombatHelper`（`IsClass`、`IsEntity`、`GetSkillTier`、`IsHostile`、`IsFriendly`、`DispelNegativeDebuffs`、`IsNegativeDebuff`、`ResolveHostileTarget`、`ResolveOpenPointTarget`、`RefundSpellCost`、`PlayBeamTrail`）；空放返蓝一律 `RefundSpellCost(Args)`。
- **技能声明（`DotaAbility`）**：主动技能**必须**用 `DotaAbility.Register` 声明（法术外壳、射程表、全射程选格、施法分发一次完成），施法逻辑写在 `OnCast`，目标解析用 `Cast.TryResolveHostileTarget` / `ResolveOpenPointTarget`；熟练度/等级/升星/射程用 `Ability.Tier/Level/StarMultiplier/Range`，按阶数值表用 `DotaTable`。禁止手写 5 阶 `Configure(Qualification)`、手动 `DotaRangeTargetingBridge.RegisterSpell`、或 `AfterCastSpell` + 比较法术名。详见 [hook-system.md §8](docs/architecture/hook-system.md)。
- **伤害结算（`DotaDamage`）**：技能伤害**必须**用 `DotaDamage.Strike` / `Harm` 并传入 `DotaDamageInfo.Physical/Magical/Pure(...)`，禁止裸调 `PathosEngineBridge.StrikeCharacter` / `HarmCharacter`；暴击用 `DotaDamage.RegisterCritSource`（取最高不叠加），魔抗/魔法减伤用 `DotaDamage.RegisterMagicResist`（乘算叠加），禁止在 `BeforeDamage` 里手写暴击或按 `ActiveAttack`/`ActiveSpellCast` 推断伤害类型。详见 [hook-system.md §7](docs/architecture/hook-system.md)。
- **自定义像素 VFX**：使用 `Ctx.RegisterStrikeVfx`、`Ctx.RegisterBeamVfx`、`Ctx.RegisterExplosion3x3Vfx`，配合 `PathosEngineBridge.Story*` 播放。
- **客户端补丁**：若需新增 `Pathos.dll` 反射或补丁，复用 `PathosClientReflection`。

### 3.2 统一数值标尺（`DotaMechanicsMapper`）与 DOTA2 7.42 Wiki 强约束
> **铁律（严禁违背）**：所有英雄基础属性、成长、蓝耗、伤害、持续时间、暴击率、天赋与技能机制，**必须严格对照 DOTA2 7.42 官方 Wiki / 刀塔百科核实原始数值**，严禁凭记忆估算、自行编造或混用旧版本（如 7.35/7.38/7.40 等）的过时数值！若不确定 7.42 数值，必须先查阅 Wiki 核对再写代码，并在英雄文档 `docs/heroes/<hero_id>.md` 中如实列出 7.42 溯源表。

所有数值必须经 `DotaMechanicsMapper` 推导，不拍脑袋手填：
- **初始六维**：`MapStartingStats(BaseStr, BaseAgi, BaseInt)` + `RegisterFixedStartingStats(ClassName, StartingStats)`。
- **蓝量与蓝耗**：$K_{\text{mana}} = 20$（`ComputeClassManaBase` / `MapSkillManaCost` / `MapSpellLevel`，0 蓝耗忠实保留 0）。
- **技能伤害**：$K_{\text{dmg}} = 15$（`MapSkillDamage` / `MapSkillDamageDice` / `MapDotDamageDicePerTurn`）。
- **时间与持续伤害/治疗**：`1 秒 = 2 回合`（`MapDurationTurns`，每回合按 `0.5 秒` 折算 DPS/HoT）。
- **护甲加减**：走 `RegisterDefenceQuery` + `MapArmorToDefence`（分段递减，正负封顶 ±8）。
- **减冷却天赋**：统一折算为减蓝耗，在 `TalentNode.ManaReductions` 上声明 `SpellManaReductionRule`（配合 `ComputeCooldownReductionRatio`）。
- **技能加点与双轨成长**：所有英雄统一接入 `HeroSkillRegistry`（1 级自带 1 点技能点，小技能 Lv 1/3/5/7、大招 Lv 6/12/18、7 级全属性黄点；主动技能 0→1 学会、1→N 原生升星并叠加升星基础数值增幅，与学派熟练度双重成长；被动技能由独立技能等级 0→4 级驱动，禁止强行挂钩武器/学派熟练度）。天赋树统一采用 DOTA2 原版 `Lv 10/15/20/25` + `Lv 27~30` 双修补点。

### 3.3 关键引擎避坑清单
1. **严禁滥用 `Codex.Properties`（64 位 `Inv.BitmixSet` 硬上限）**：Pathos 的 `PropertySet` 底层是单个 `Int64`（全局最多 64 个 `Property`），官方已占用 57 个，全扩展包仅余 7 个名额！超过 64 个会发生 `Index & 63` 回绕，直接撞上 `hallucination`（幻觉）等官方状态！因此**严禁**为天赋树节点或技能等级注册内部 `Property`，持久化数据必须走 `DotaCharacterStorage`（存入 `Character.Dialogue.Tag`）。
2. **学习/升星法术必须用 `Character.Knowledge.LearnSpell(Spell, 1)`（已封装在 `PathosEngineBridge.LearnSpell`）**：禁止直接调 `Engine.LearnSpell`（会触发低智力读魔法书失败惩罚，如幻觉/困惑）。
3. **点地召唤禁止在声明式 `Plain` 射线上直接挂 `SummonEntity`**：`Plain(Range)` 会对路径每格执行一次 `Apply`，导致一次召唤多个。正确做法：声明式用 `Plain(Range)` + `Apply.Nothing()`，在 `AfterCastSpell` 中调用 `PathosEngineBridge.SummonAllyEntity(...)`。
4. **召唤物到期 / 替换销毁必须用 `PathosEngineBridge.DespawnSummon`**：禁止用 `DismissAlly`（会残留中立/野生单位在地图上）。
5. **贴图资源与视觉素材（严格遵循官方 Wiki）**：
   - 英雄形象、召唤物、技能图标与天赋/状态图标等各种素材，**必须严格按照 DOTA2 官方 Wiki（Liquipedia / 刀塔百科）的官方原图与造型制作或获取**，严禁凭想象乱画。
   - 技能视觉特效（VFX，如弹道、斩击、爆炸、光环等），可参考官方 Wiki 页面中的技能演示图/动图与配色风格来绘制。
   - 单份 `128x128` 透明 PNG 放在 `Resources/Heroes/<hero_id>/`，**不要手动修改 `Atlases/`**（构建期清空重建且已 `.gitignore`）。文件名必须全局唯一且不与官方内置 Glyph 冲突（如主宰用 `yurnero.png` 避开内置怪物 `juggernaut`）。
6. **角色私有运行时状态**：必须用 `ConditionalWeakTable<Character, TState>` 绑定，避免内存泄漏；Hook Key 统一 `<hero_id>.<feature>` 保证幂等覆盖。
7. **客户端与引擎双可见**：凡通过 Hook 结算伤害的技能，必须在法术 `Description` 中明确写出伤害骰与倍率。

---

## 4. 新增英雄完成标准（Checklist）

1. 贴图与特效放入 `Resources/Heroes/<hero_id>/`。
2. 在 `Heroes/<Hero>Hero.cs` 实现 `IDotaHeroDefinition`，接入 `DotaHeroContext`、`DotaMechanicsMapper`、`TalentRegistry`、`DotaRangeTargetingBridge`、`DotaStatusBarBridge`、`DotaHookBus` 与 `DotaCombatHelper`。
3. 在 `DotaHeroesExpansion.cs` 的 `HeroRegistry` 中注册。
4. 执行 `dotnet build -c Release` 生成字典占位 → 补全 `Assets/DotaHeroesExpansion.zh-Simplified.Dictionary` 中文词条 → 再次构建部署。
5. 新增 `docs/heroes/<hero_id>.md`（**必须包含 DOTA2 版本号与完整数值映射溯源表**），并更新 `docs/heroes/README.md`、`docs/README.md`、`README.md`。

---

## 5. 必读参考文档

- 架构总览：[docs/architecture/overview.md](docs/architecture/overview.md)
- Hook 事件总线与战斗基建：[docs/architecture/hook-system.md](docs/architecture/hook-system.md)
- 天赋树与长/短期技能状态：[docs/architecture/talents-and-skills.md](docs/architecture/talents-and-skills.md)
- 全射程选格与范围施法：[docs/architecture/range-targeting.md](docs/architecture/range-targeting.md)
- DOTA2 → Pathos 数值与机制规范：[docs/architecture/design-guidelines.md](docs/architecture/design-guidelines.md)
- 新增英雄完整指南：[docs/guides/adding-a-hero.md](docs/guides/adding-a-hero.md)
