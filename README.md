# Pathos — DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

面向 **Pathos: NetHack Codex**（Windows / macOS / Linux 桌面端）的可扩展 **DOTA2 英雄扩展包**。

采用 **「声明式外壳 + Harmony Hook 核心逻辑」混合架构**，单 DLL 内嵌 `Lib.Harmony 2.4.3`，源码资源按英雄分目录管理并在构建期自动聚合给 `PathosMaker` 打包部署。

---

## 玩家安装指南（免编译，下载即玩）

> 适用平台：**桌面端 Steam / 独立版 Pathos**（Windows / macOS / Linux）。无需安装 .NET SDK 或自行编译。

1. 前往本仓库右侧的 **[Releases](../../releases)** 页面，下载最新的 `DotaHeroesExpansion-v*.zip` 压缩包并**完整解压**。
2. **完全关闭 Pathos 游戏**（运行中会占用文件导致无法安装或覆盖）。
3. 选择以下任意一种方式安装：
   - **Windows 一键安装（推荐）**：直接双击解压出来的 `一键安装(Windows).bat`，脚本会自动识别 Steam / 本地 Pathos 安装目录并完成部署。
   - **手动安装（Windows / macOS / Linux 通用）**：
     1. 打开 Pathos 安装目录下的 `Expansions` 文件夹（Steam 版：库中右键 **Pathos** → **管理** → **浏览本地文件** → 打开 `Expansions`）。
     2. 将解压后 `files/` 文件夹内的**全部文件**（`.dll`、`.Atlas`、`.Dictionary` 等，**不要只复制 `.dll`**）复制到 `Expansions/` 中。
4. 启动游戏，在新建角色界面即可选择 DOTA2 英雄！

> **卸载**：运行 `卸载Mod(Windows).bat`，或删除 `Expansions/` 目录中所有文件名包含 `DotaHeroesExpansion` 的文件即可。

---

## 开发者快速开始（编译与打包）

1. 复制 [LocalPathos.props.example](LocalPathos.props.example) 为 `LocalPathos.props`，将 `<PathosInstallation>` 改为本机 Pathos 安装路径（或设置环境变量 `PATHOS_INSTALL_DIR`）。
2. 在项目根目录执行构建与自动部署：

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

3. 打包可直接上传至 GitHub Releases 的免编译 `.zip` 安装包（输出至 `dist/`）：

```powershell
.\scripts\package-release.ps1 -Version "v0.1.0"
```

详细构建流程与调试日志说明请见 [编译、部署与调试指南](docs/guides/build-and-debug.md)。

---

## 已接入英雄（DOTA2 7.42）

| 英雄 | 种族 / 职业 | 机制文档 |
| :--- | :--- | :--- |
| **变体精灵（Morphling / 水人）** | `变体水灵 (waterkin)` / `变体精灵 (morphling)` | [docs/heroes/morphling.md](docs/heroes/morphling.md) |
| **主宰（Juggernaut / 尤涅若）** | 人类 (`human`) / `主宰 (juggernaut)` | [docs/heroes/juggernaut.md](docs/heroes/juggernaut.md) |
| **斧王（Axe / 蒙哥可汗）** | 兽人 (`orc`) / `斧王 (axe)` | [docs/heroes/axe.md](docs/heroes/axe.md) |

查看完整对照与资源索引：[英雄总览清单](docs/heroes/README.md)

---

## 文档导航

完整文档位于 [docs/](docs/README.md)：

- **架构与规范**
  - [整体架构与项目结构](docs/architecture/overview.md)（混合架构分层、目录职责与 MSBuild 资源自动聚合流）
  - [Hook 事件总线、引擎桥接与战斗基建](docs/architecture/hook-system.md)（`DotaHookBus`、`PathosEngineBridge` 与 `DotaCombatHelper`）
  - [天赋树与长/短期技能状态体系](docs/architecture/talents-and-skills.md)（4 层双修天赋树、长短期状态隐藏治理、左上角状态栏徽章与自定义 VFX）
  - [全射程选格与范围施法体系](docs/architecture/range-targeting.md)（`DotaRangeTargetingBridge`、目标解析、点地召唤与 AOE 范围）
  - [DOTA2 → Pathos 机制与数值映射规范](docs/architecture/design-guidelines.md)（属性/蓝耗/伤害/时间/治疗/护甲映射与命名规范）
- **开发指南**
  - [编译、部署与调试指南](docs/guides/build-and-debug.md)
  - [新增 DOTA2 英雄指南](docs/guides/adding-a-hero.md)
- **英雄机制**
  - [英雄总览清单](docs/heroes/README.md)
