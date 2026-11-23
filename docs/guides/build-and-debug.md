# 编译、部署与调试指南

## 1. 环境准备

- .NET 10 SDK
- 已安装桌面端 **Pathos: NetHack Codex**（包含 `PathosMaker.exe` 及引擎 DLL）

首次克隆仓库后，配置本地 Pathos 安装目录（二选一）：

1. **推荐（本地配置文件）**：复制 [LocalPathos.props.example](../../LocalPathos.props.example) 为 `LocalPathos.props`（已被 `.gitignore` 忽略），修改 `<PathosInstallation>` 为本机安装路径，例如：
   ```xml
   <Project>
     <PropertyGroup>
       <PathosInstallation>E:\Steam\steamapps\common\Pathos</PathosInstallation>
     </PropertyGroup>
   </Project>
   ```
2. **环境变量**：设置 `PATHOS_INSTALL_DIR` 指向 Pathos 安装目录。

---

## 2. 编译与自动部署

在项目根目录执行：

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

构建过程会自动完成以下流水线：

1. **编译 C# 扩展**：将 `0Harmony.dll` 内嵌进 `DotaHeroesExpansion.dll`（单 DLL 成品）。
2. **聚合贴图资源（`StageAtlasesForPathosMaker`）**：校验 `Resources/**/<tileset>/*.png` 无跨英雄同名冲突，并自动复制到临时目录 `Atlases/<tileset>/`。
3. **运行 `PathosMaker.exe`**：
   - 校验并加载 `DotaHeroesExpansion.dll`（打包期 `HookBootstrap` 自动跳过 Harmony 补丁）
   - 打包 `Atlases/` 下的四套 `128x128` 图集到 `Assets/*.Atlas`
   - 同步并打包 `Assets/DotaHeroesExpansion.*.Dictionary` 多语言翻译
   - 自动将成品 DLL 与资源部署到 `<Pathos安装目录>\Expansions\`

---

## 3. 调试与日志诊断

- 默认情况下 `HookBootstrap` 与 `DotaHookBus` **静默运行**，不产生日志文件。
- 如需排查 Harmony 补丁挂载或 Hook 异常，在启动 Pathos 前设置环境变量：

```powershell
$env:DOTA_HEROES_HOOK_DEBUG = "1"
```

开启后，诊断日志会写入系统临时目录的 `DotaHeroesHook.log`（Windows 下可通过 `%TEMP%\DotaHeroesHook.log` 查看）。
