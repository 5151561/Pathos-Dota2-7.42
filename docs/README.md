# 文档中心（Docs）

本目录收录 `DotaHeroesExpansion` 的架构设计、机制规范、开发指南与各英雄详细说明，便于后续**调整现有机制**或**接入新英雄**。

## 目录导航

### 1. 架构与设计（`architecture/`）

- [整体架构与项目结构](architecture/overview.md)：混合架构（声明式 Codex API + Harmony Hook）、目录职责、资源按英雄归档与自动聚合构建流。
- [Hook 事件总线与引擎桥接](architecture/hook-system.md)：`HookBootstrap`、`DotaHookBus` 全事件清单与 `PathosEngineBridge` 能力速查。
- [DOTA2 → Pathos 机制映射规范](architecture/design-guidelines.md)：蓝耗映射、先天/光环/召唤物/变身实现模式与贴图命名防冲突规范。

### 2. 开发与运维指南（`guides/`）

- [编译、部署与调试指南](guides/build-and-debug.md)：本地路径配置、`dotnet build` 打包流程与 `DOTA_HEROES_HOOK_DEBUG` 日志排查。
- [新增 DOTA2 英雄指南](guides/adding-a-hero.md)：从贴图、代码、Hook、翻译到文档的完整接入 Checklist。

### 3. 英雄机制文档（`heroes/`）

- [英雄总览清单](heroes/README.md)
- [变体精灵（Morphling / 水人）](heroes/morphling.md)
- [主宰（Juggernaut / 尤涅若）](heroes/juggernaut.md)
