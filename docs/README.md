# 文档中心（Docs）

本目录收录 `DotaHeroesExpansion` 的架构设计、底层基建规范、开发指南与各英雄详细机制说明，便于后续**维护现有机制**或**快速接入新英雄**。

## 目录导航

### 1. 架构与底层基建（`architecture/`）

- [整体架构与项目结构](architecture/overview.md)：混合架构分层图、`Framework/` 全模块职责、资源按英雄归档与自动聚合构建流。
- [Hook 事件总线、引擎桥接与战斗基建](architecture/hook-system.md)：`HookBootstrap`、`DotaHookBus` 全事件清单、`PathosEngineBridge`（含召唤/销毁/`Story` 特效/自动回合）与 `DotaCombatHelper` 通用战斗工具。
- [天赋树与长/短期技能状态体系](architecture/talents-and-skills.md)：4 层双修天赋树架构、**长效技能 vs 短期主动技能状态分类与隐藏治理**、左上角动态状态栏徽章（`DotaStatusBarBridge`）、自动多回合连斩与自定义像素 VFX。
- [全射程选格与范围施法体系](architecture/range-targeting.md)：`DotaRangeTargetingBridge` 原理、三种选目标模式、智能吸附/空大返蓝、点地召唤防重复坑与动态施法距离。
- [DOTA2 → Pathos 机制与数值映射规范](architecture/design-guidelines.md)：初始六维、蓝量、伤害、时间、**治疗/吸血与召唤物**、护甲、减冷却转减蓝耗、常见机制实现套路与溯源规范。

### 2. 开发与运维指南（`guides/`）

- [编译、部署与调试指南](guides/build-and-debug.md)：本地路径配置、`dotnet build` 打包流程与 `DOTA_HEROES_HOOK_DEBUG` 日志排查。
- [新增 DOTA2 英雄指南](guides/adding-a-hero.md)：直接接入 `Framework` 全套基建（贴图/VFX、长短期状态、天赋树、全射程选格、状态栏、Hook、翻译与溯源文档）的标准 Checklist。

### 3. 英雄机制文档（`heroes/`）

- [英雄总览清单](heroes/README.md)
- [变体精灵（Morphling / 水人）](heroes/morphling.md)
- [主宰（Juggernaut / 尤涅若）](heroes/juggernaut.md)
