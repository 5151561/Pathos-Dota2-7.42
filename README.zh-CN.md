# Pathos — DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

[English](README.md) | 简体中文

面向 **Pathos: NetHack Codex**（Windows / macOS / Linux 桌面端）的可扩展 **DOTA2 英雄扩展包**，对照 **DOTA2 7.42** 机制深度还原。

采用 **「声明式外壳 + Harmony Hook 核心逻辑」混合架构**，单 DLL 内嵌 `Lib.Harmony 2.4.3`，源码资源按英雄分目录管理并在构建期自动聚合给 `PathosMaker` 打包部署。

---

## 核心特性

- **4 位 DOTA2 7.42 英雄**：变体精灵、主宰、斧王、帕吉，完整还原三维基础/成长、先天技能与 Q/W/E/R 经典技能组。
- **技能加点与四层双修天赋树**：支持 Q/W/E/R 与黄点升级；10 / 15 / 20 / 25 级二选一天赋，27–30 级依次补点另一侧。
- **阿哈利姆神杖、福佑与魔晶**：支持各英雄专属技能强化与新增魔晶/神杖技能。
- **主动技能冷却与增益/减益体系**：统一 `1 秒 = 1 回合` 时间标尺，跨存档持久化，支持状态栏徽章，且零占用自定义 `Codex.Properties`。
- **全射程选格与自定义像素 VFX**：支持全射程自由选格、多段技能演出时间轴与 Pathos 原生风格像素特效。
- **内置 25 种语言**：UI 词表与 Codex 字典内置 25 种语言翻译。

---

## 玩家安装指南（免编译，下载即玩）

> 适用平台：**桌面端 Steam / 独立版 Pathos**（Windows / macOS / Linux）。无需安装 .NET SDK 或自行编译。

1. 前往本仓库右侧的 **[Releases](../../releases)** 页面，下载最新的 `DotaHeroesExpansion-v*.zip` 压缩包并**完整解压**（可选：对照 `SHA256SUMS.txt` 校验文件完整性）。
2. **完全关闭 Pathos 游戏**（运行中会占用文件导致无法安装或覆盖）。
3. 选择以下任意一种方式安装：
   - **Windows 一键安装（推荐）**：直接双击解压出来的 `一键安装(Windows).bat`，脚本会自动识别 Steam / 本地 Pathos 安装目录并完成部署。
   - **手动安装（Windows / macOS / Linux 通用）**：
     1. 打开 Pathos 安装目录下的 `Expansions` 文件夹（Steam 版：库中右键 **Pathos** → **管理** → **浏览本地文件** → 打开 `Expansions`）。
     2. 将解压后 `files/` 文件夹内的**全部文件**（`.dll`、`.Atlas`、`.Dictionary` 等，**不要只复制 `.dll`**）复制到 `Expansions/` 中。
4. 启动游戏，在新建角色界面即可选择 DOTA2 英雄！

> **卸载**：运行 `卸载Mod(Windows).bat`，或删除 `Expansions/` 目录中所有文件名包含 `DotaHeroesExpansion` 的文件即可。

---

## 已接入英雄（DOTA2 7.42）

- [变体精灵（Morphling）](docs/heroes/morphling.md)
- [主宰（Juggernaut）](docs/heroes/juggernaut.md)
- [斧王（Axe）](docs/heroes/axe.md)
- [帕吉（Pudge）](docs/heroes/pudge.md)

查看完整对照与资源索引：[英雄总览清单](docs/heroes/README.md)

---

## 开发者快速开始（编译与打包）

1. 复制 [LocalPathos.props.example](LocalPathos.props.example) 为 `LocalPathos.props`，将 `<PathosInstallation>` 改为本机 Pathos 安装路径（或设置环境变量 `PATHOS_INSTALL_DIR`）。
2. 在项目根目录执行构建与自动部署：

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

3. 打包可直接上传至 GitHub Releases 的免编译 `.zip` 安装包（输出至 `dist/`；推送 `v*` 标签也会通过 [.github/workflows/build-release.yml](.github/workflows/build-release.yml) 自动构建并发布 Release）：

```powershell
.\scripts\package-release.ps1 -Version "v0.1.4"
```

详细构建流程与调试日志说明请见 [编译、部署与调试指南](docs/guides/build-and-debug.md)。

---

## 文档导航

完整文档位于 [docs/](docs/README.md)：

- **架构与规范**
  - [框架基建 API 速查](docs/architecture/framework-api.md)（需求 → API 对照索引）
  - [整体架构与项目结构](docs/architecture/overview.md)（混合架构分层、目录职责与 MSBuild 资源自动聚合流）
  - [Hook 事件总线、引擎桥接与战斗基建](docs/architecture/hook-system.md)（`DotaHookBus`、`PathosEngineBridge` 与 `DotaCombatHelper`）
  - [天赋树与长/短期技能状态体系](docs/architecture/talents-and-skills.md)（4 层双修天赋树、增益/减益状态、左上角状态栏徽章与自定义 VFX）
  - [全射程选格与范围施法体系](docs/architecture/range-targeting.md)（`DotaRangeTargetingBridge`、目标解析、点地召唤与 AOE 范围）
  - [DOTA2 → Pathos 机制与数值映射规范](docs/architecture/design-guidelines.md)（属性/蓝耗/伤害/时间/治疗/护甲映射与命名规范）
- **开发指南**
  - [编译、部署与调试指南](docs/guides/build-and-debug.md)
  - [新增 DOTA2 英雄指南](docs/guides/adding-a-hero.md)
- **英雄机制**
  - [英雄总览清单](docs/heroes/README.md)
