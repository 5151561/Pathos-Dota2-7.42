# Pathos — DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

面向 **Pathos: NetHack Codex**（Windows / macOS / Linux 桌面端）的可扩展 **DOTA2 英雄扩展包**。

采用 **「声明式外壳 + Harmony Hook 核心逻辑」混合架构**，单 DLL 内嵌 `Lib.Harmony 2.4.3`，源码资源按英雄分目录管理并在构建期自动聚合给 `PathosMaker` 打包部署。

---

## 快速开始

1. 复制 [LocalPathos.props.example](LocalPathos.props.example) 为 `LocalPathos.props`，将 `<PathosInstallation>` 改为本机 Pathos 安装路径（或设置环境变量 `PATHOS_INSTALL_DIR`）。
2. 在项目根目录执行构建与自动部署：

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

详细构建流程与调试日志说明请见 [编译、部署与调试指南](docs/guides/build-and-debug.md)。

---

## 已接入英雄（DOTA2 7.42）

| 英雄 | 种族 / 职业 | 机制文档 |
| :--- | :--- | :--- |
| **变体精灵（Morphling / 水人）** | `变体水灵 (waterkin)` / `变体精灵 (morphling)` | [docs/heroes/morphling.md](docs/heroes/morphling.md) |
| **主宰（Juggernaut / 尤涅若）** | 人类 (`human`) / `主宰 (juggernaut)` | [docs/heroes/juggernaut.md](docs/heroes/juggernaut.md) |

查看完整对照与资源索引：[英雄总览清单](docs/heroes/README.md)

---

## 文档导航

完整文档位于 [docs/](docs/README.md)：

- **架构与规范**
  - [整体架构与项目结构](docs/architecture/overview.md)（含目录职责、按英雄管理贴图与 MSBuild 自动聚合流）
  - [Hook 事件总线与引擎桥接](docs/architecture/hook-system.md)（`DotaHookBus` 全事件速查与 `PathosEngineBridge`）
  - [DOTA2 → Pathos 机制映射规范](docs/architecture/design-guidelines.md)（蓝耗映射、机制实现模式、命名防冲突规范）
- **开发指南**
  - [编译、部署与调试指南](docs/guides/build-and-debug.md)
  - [新增 DOTA2 英雄指南](docs/guides/adding-a-hero.md)
- **英雄机制**
  - [英雄总览清单](docs/heroes/README.md)
