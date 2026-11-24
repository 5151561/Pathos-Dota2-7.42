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
│   └── PathosEngineBridge.cs  # 引擎内部成员反射缓存（伤害/治疗/召唤/销毁/Story 特效/自动回合）
├── Talents/                   # 四层双修天赋树（Lv 5/10/15/20 + Lv 22~25 双修）与左下角独立面板
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
- **战斗与目标工具**：直接复用 `DotaCombatHelper`（`IsClass`、`IsEntity`、`GetSkillTier`、`IsHostile`、`IsFriendly`、`DispelNegativeDebuffs`、`IsNegativeDebuff`、`ResolveHostileTarget`、`ResolveOpenPointTarget`、`RefundMana`、`PlayBeamTrail`）。
- **自定义像素 VFX**：使用 `Ctx.RegisterStrikeVfx`、`Ctx.RegisterBeamVfx`、`Ctx.RegisterExplosion3x3Vfx`，配合 `PathosEngineBridge.Story*` 播放。
- **客户端补丁**：若需新增 `Pathos.dll` 反射或补丁，复用 `PathosClientReflection`。

### 3.2 统一数值标尺（`DotaMechanicsMapper`）
参考版本为 **DOTA2 7.42**，所有数值必须可推导，不拍脑袋手填：
- **初始六维**：`MapStartingStats(BaseStr, BaseAgi, BaseInt)` + `RegisterFixedStartingStats(ClassName, StartingStats)`。
- **蓝量与蓝耗**：$K_{\text{mana}} = 20$（`ComputeClassManaBase` / `MapSkillManaCost` / `MapSpellLevel`，0 蓝耗忠实保留 0）。
- **技能伤害**：$K_{\text{dmg}} = 15$（`MapSkillDamage` / `MapSkillDamageDice` / `MapDotDamageDicePerTurn`）。
- **时间与持续伤害/治疗**：`1 秒 = 2 回合`（`MapDurationTurns`，每回合按 `0.5 秒` 折算 DPS/HoT）。
- **护甲加减**：走 `RegisterDefenceQuery` + `MapArmorToDefence`（分段递减，正负封顶 ±8）。
- **减冷却天赋**：统一折算为减蓝耗，在 `TalentNode.ManaReductions` 上声明 `SpellManaReductionRule`（配合 `ComputeCooldownReductionRatio`）。

### 3.3 关键引擎避坑清单
1. **点地召唤禁止在声明式 `Plain` 射线上直接挂 `SummonEntity`**：`Plain(Range)` 会对路径每格执行一次 `Apply`，导致一次召唤多个。正确做法：声明式用 `Plain(Range)` + `Apply.Nothing()`，在 `AfterCastSpell` 中调用 `PathosEngineBridge.SummonAllyEntity(...)`。
2. **召唤物到期 / 替换销毁必须用 `PathosEngineBridge.DespawnSummon`**：禁止用 `DismissAlly`（会残留中立/野生单位在地图上）。
3. **贴图资源**：单份 `128x128` 透明 PNG 放在 `Resources/Heroes/<hero_id>/`，**不要手动修改 `Atlases/`**（构建期清空重建且已 `.gitignore`）。文件名必须全局唯一且不与官方内置 Glyph 冲突（如主宰用 `yurnero.png` 避开内置怪物 `juggernaut`）。
4. **角色私有运行时状态**：必须用 `ConditionalWeakTable<Character, TState>` 绑定，避免内存泄漏；Hook Key 统一 `<hero_id>.<feature>` 保证幂等覆盖。
5. **客户端与引擎双可见**：凡通过 Hook 结算伤害的技能，必须在法术 `Description` 中明确写出伤害骰与倍率。

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
