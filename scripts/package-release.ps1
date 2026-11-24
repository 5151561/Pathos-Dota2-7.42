<#
.SYNOPSIS
    将 DotaHeroesExpansion 打包为可直接上传至 GitHub Release 的免编译安装包（.zip）。

.DESCRIPTION
    1. 默认先执行一次 `dotnet build .\DotaHeroesExpansion.csproj -c Release`（可用 -SkipBuild 跳过）。
    2. 从 Pathos 安装目录的 Expansions/ 中收集最新生成的 *DotaHeroesExpansion.* 运行时文件（自动排除 .pdb）。
    3. 生成包含 files/、一键安装(Windows).bat、卸载Mod(Windows).bat 与 安装说明.txt 的标准发布压缩包到 dist/ 目录。

.EXAMPLE
    .\scripts\package-release.ps1
    .\scripts\package-release.ps1 -Version "v0.1.0"
    .\scripts\package-release.ps1 -Version "v0.1.0" -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$PathosDir = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CsprojPath = Join-Path $RepoRoot "DotaHeroesExpansion.csproj"
$LocalPropsPath = Join-Path $RepoRoot "LocalPathos.props"

# 1. 解析 Pathos 安装路径（参数 > LocalPathos.props > 环境变量 > 默认路径）
if ([string]::IsNullOrWhiteSpace($PathosDir)) {
    if (Test-Path $LocalPropsPath) {
        [xml]$propsXml = Get-Content -Path $LocalPropsPath -Encoding UTF8
        $propNode = $propsXml.Project.PropertyGroup.PathosInstallation | Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($propNode)) {
            $PathosDir = $propNode.Trim()
        }
    }
}
if ([string]::IsNullOrWhiteSpace($PathosDir) -and -not [string]::IsNullOrWhiteSpace($env:PATHOS_INSTALL_DIR)) {
    $PathosDir = $env:PATHOS_INSTALL_DIR.Trim()
}
if ([string]::IsNullOrWhiteSpace($PathosDir)) {
    $PathosDir = "C:\Games\Pathos"
}

if (-not (Test-Path $PathosDir)) {
    throw "未找到 Pathos 安装目录: $PathosDir。请通过 -PathosDir 参数或 LocalPathos.props 指定。"
}

Write-Host "[1/4] 使用 Pathos 目录: $PathosDir" -ForegroundColor Cyan

# 2. 编译并生成 Expansions 产物
if (-not $SkipBuild) {
    Write-Host "[2/4] 正在执行 Release 构建..." -ForegroundColor Cyan
    & dotnet build $CsprojPath -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build 失败（退出码: $LASTEXITCODE）。若 Pathos 正在运行，请先关闭游戏后重试。"
    }
} else {
    Write-Host "[2/4] 已跳过编译（-SkipBuild），直接读取 Expansions/ 现有产物。" -ForegroundColor Yellow
}

$ExpansionsDir = Join-Path $PathosDir "Expansions"
if (-not (Test-Path $ExpansionsDir)) {
    throw "未找到 Expansions 目录: $ExpansionsDir"
}

# 收集运行时文件（排除 .pdb）
$RuntimeFiles = Get-ChildItem -Path $ExpansionsDir -File | Where-Object {
    $_.Name -like "*DotaHeroesExpansion.*" -and $_.Extension -ne ".pdb"
}

if ($RuntimeFiles.Count -eq 0) {
    throw "在 $ExpansionsDir 中未找到任何 *DotaHeroesExpansion.* 文件，请先执行构建。"
}

$DllFile = $RuntimeFiles | Where-Object { $_.Extension -eq ".dll" } | Select-Object -First 1
if (-not $DllFile) {
    throw "未找到 DotaHeroesExpansion.dll 产物。"
}

# 提取 Pathos 版本前缀（如 s2026.1116.0952）
$PathosVersionPrefix = ""
if ($DllFile.Name -match "^(.+?)\.DotaHeroesExpansion\.dll$") {
    $PathosVersionPrefix = $Matches[1]
}
$PathosVersionDisplay = if ($PathosVersionPrefix) { $PathosVersionPrefix } else { "Unknown" }

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyyMMdd"
}

$PackageName = "DotaHeroesExpansion-$Version"
$DistDir = Join-Path $RepoRoot "dist"
$StagingDir = Join-Path $DistDir $PackageName
$FilesStagingDir = Join-Path $StagingDir "files"
$ZipPath = Join-Path $DistDir "$PackageName.zip"

Write-Host "[3/4] 正在整理发布包内容（共 $($RuntimeFiles.Count) 个文件，适配 Pathos $PathosVersionDisplay）..." -ForegroundColor Cyan

if (Test-Path $StagingDir) {
    Remove-Item -Path $StagingDir -Recurse -Force
}
if (Test-Path $ZipPath) {
    Remove-Item -Path $ZipPath -Force
}

New-Item -ItemType Directory -Path $FilesStagingDir -Force | Out-Null
foreach ($file in $RuntimeFiles) {
    Copy-Item -Path $file.FullName -Destination (Join-Path $FilesStagingDir $file.Name) -Force
}

# 辅助函数：以 UTF-8 无 BOM + CRLF 写入批处理文件，避免 cmd.exe 乱码或把 BOM 当命令
function Write-BatFile {
    param([string]$Path, [string]$Text)
    $crlf = ($Text -replace "`r?`n", "`r`n")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $crlf, $utf8NoBom)
}

function Write-Utf8File {
    param([string]$Path, [string]$Text)
    $crlf = ($Text -replace "`r?`n", "`r`n")
    $utf8Bom = New-Object System.Text.UTF8Encoding($true)
    [System.IO.File]::WriteAllText($Path, $crlf, $utf8Bom)
}

# 3.1 生成 一键安装(Windows).bat
$InstallBat = @'
@echo off
chcp 65001 >nul
pushd "%~dp0"
title Pathos DOTA2 Heroes Expansion - 一键安装

echo ============================================================
echo   Pathos: NetHack Codex - DOTA2 英雄扩展包 (一键安装)
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$src = Join-Path $PWD 'files';" ^
  "if (-not (Test-Path $src)) { Write-Host '[错误] 未找到 files 文件夹，请先完整解压压缩包再运行！' -ForegroundColor Red; exit 1 };" ^
  "if (Get-Process -Name 'PathosGame' -ErrorAction SilentlyContinue) { Write-Host '[提示] 检测到 Pathos 游戏正在运行，请先关闭游戏再按回车继续...' -ForegroundColor Yellow; Read-Host | Out-Null };" ^
  "$candidates = New-Object System.Collections.Generic.List[string];" ^
  "$candidates.Add('C:\Games\Pathos');" ^
  "$candidates.Add('C:\Program Files (x86)\Steam\steamapps\common\Pathos');" ^
  "$candidates.Add('C:\Program Files\Steam\steamapps\common\Pathos');" ^
  "$steamRoots = @();" ^
  "try { $p = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath; if ($p) { $steamRoots += $p } } catch {};" ^
  "try { $p = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath; if ($p) { $steamRoots += $p } } catch {};" ^
  "foreach ($root in ($steamRoots | Select-Object -Unique)) {" ^
  "  $candidates.Add((Join-Path $root 'steamapps\common\Pathos'));" ^
  "  $vdf = Join-Path $root 'steamapps\libraryfolders.vdf';" ^
  "  if (Test-Path $vdf) {" ^
  "    Get-Content $vdf -Encoding UTF8 | ForEach-Object {" ^
  "      if ($_ -match '\"path\"\s+\"([^\"]+)\"') {" ^
  "        $lib = $Matches[1] -replace '\\\\', '\';" ^
  "        $candidates.Add((Join-Path $lib 'steamapps\common\Pathos'));" ^
  "      }" ^
  "    }" ^
  "  }" ^
  "};" ^
  "$target = $null;" ^
  "foreach ($c in ($candidates | Select-Object -Unique)) {" ^
  "  if ((Test-Path (Join-Path $c 'PathosGame.exe')) -or (Test-Path (Join-Path $c 'Pathos.dll'))) { $target = $c; break }" ^
  "};" ^
  "if (-not $target) {" ^
  "  Write-Host '未能自动探测到 Pathos 安装目录。' -ForegroundColor Yellow;" ^
  "  Write-Host '请在 Steam 右键 Pathos -> 管理 -> 浏览本地文件，把文件夹直接拖入此窗口后按回车：' -ForegroundColor Cyan;" ^
  "  $inputPath = (Read-Host '游戏目录').Trim().Trim('\"');" ^
  "  if ((Test-Path (Join-Path $inputPath 'PathosGame.exe')) -or (Test-Path (Join-Path $inputPath 'Pathos.dll'))) { $target = $inputPath } else { Write-Host '[错误] 该目录下未找到 PathosGame.exe 或 Pathos.dll！' -ForegroundColor Red; exit 1 }" ^
  "};" ^
  "Write-Host ('已定位 Pathos 目录: ' + $target) -ForegroundColor Green;" ^
  "$expDir = Join-Path $target 'Expansions';" ^
  "if (-not (Test-Path $expDir)) { New-Item -ItemType Directory -Path $expDir -Force | Out-Null };" ^
  "$old = Get-ChildItem -Path $expDir -File -Filter '*DotaHeroesExpansion.*' -ErrorAction SilentlyContinue;" ^
  "if ($old) { Write-Host ('正在清理旧版本文件 (' + $old.Count + ' 个)...') -ForegroundColor Gray; $old | Remove-Item -Force };" ^
  "$newFiles = Get-ChildItem -Path $src -File;" ^
  "foreach ($f in $newFiles) { Copy-Item -Path $f.FullName -Destination (Join-Path $expDir $f.Name) -Force };" ^
  "Write-Host '';" ^
  "Write-Host ('[成功] 已安装 ' + $newFiles.Count + ' 个文件到: ' + $expDir) -ForegroundColor Green;" ^
  "Write-Host '现在可以直接打开 Pathos 游戏，在选人界面体验 DOTA2 英雄了！' -ForegroundColor Cyan;"

popd
echo.
pause
'@
Write-BatFile -Path (Join-Path $StagingDir "一键安装(Windows).bat") -Text $InstallBat

# 3.2 生成 卸载Mod(Windows).bat
$UninstallBat = @'
@echo off
chcp 65001 >nul
title Pathos DOTA2 Heroes Expansion - 卸载

echo ============================================================
echo   Pathos: NetHack Codex - DOTA2 英雄扩展包 (卸载)
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "if (Get-Process -Name 'PathosGame' -ErrorAction SilentlyContinue) { Write-Host '[提示] 检测到 Pathos 游戏正在运行，请先关闭游戏再按回车继续...' -ForegroundColor Yellow; Read-Host | Out-Null };" ^
  "$candidates = New-Object System.Collections.Generic.List[string];" ^
  "$candidates.Add('C:\Games\Pathos');" ^
  "$candidates.Add('C:\Program Files (x86)\Steam\steamapps\common\Pathos');" ^
  "$candidates.Add('C:\Program Files\Steam\steamapps\common\Pathos');" ^
  "$steamRoots = @();" ^
  "try { $p = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath; if ($p) { $steamRoots += $p } } catch {};" ^
  "try { $p = (Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath; if ($p) { $steamRoots += $p } } catch {};" ^
  "foreach ($root in ($steamRoots | Select-Object -Unique)) {" ^
  "  $candidates.Add((Join-Path $root 'steamapps\common\Pathos'));" ^
  "  $vdf = Join-Path $root 'steamapps\libraryfolders.vdf';" ^
  "  if (Test-Path $vdf) {" ^
  "    Get-Content $vdf -Encoding UTF8 | ForEach-Object {" ^
  "      if ($_ -match '\"path\"\s+\"([^\"]+)\"') {" ^
  "        $lib = $Matches[1] -replace '\\\\', '\';" ^
  "        $candidates.Add((Join-Path $lib 'steamapps\common\Pathos'));" ^
  "      }" ^
  "    }" ^
  "  }" ^
  "};" ^
  "$target = $null;" ^
  "foreach ($c in ($candidates | Select-Object -Unique)) {" ^
  "  if ((Test-Path (Join-Path $c 'PathosGame.exe')) -or (Test-Path (Join-Path $c 'Pathos.dll'))) { $target = $c; break }" ^
  "};" ^
  "if (-not $target) {" ^
  "  Write-Host '未能自动探测到 Pathos 安装目录，请把 Pathos 游戏根目录拖入此窗口后按回车：' -ForegroundColor Yellow;" ^
  "  $inputPath = (Read-Host '游戏目录').Trim().Trim('\"');" ^
  "  if ((Test-Path (Join-Path $inputPath 'PathosGame.exe')) -or (Test-Path (Join-Path $inputPath 'Pathos.dll'))) { $target = $inputPath } else { Write-Host '[错误] 该目录下未找到 Pathos 游戏文件！' -ForegroundColor Red; exit 1 }" ^
  "};" ^
  "$expDir = Join-Path $target 'Expansions';" ^
  "if (-not (Test-Path $expDir)) { Write-Host '未找到 Expansions 目录，无需卸载。' -ForegroundColor Yellow; exit 0 };" ^
  "$old = Get-ChildItem -Path $expDir -File -Filter '*DotaHeroesExpansion.*' -ErrorAction SilentlyContinue;" ^
  "if (-not $old) { Write-Host '未在 Expansions 目录中找到 DotaHeroesExpansion 文件。' -ForegroundColor Yellow; exit 0 };" ^
  "$old | Remove-Item -Force;" ^
  "Write-Host ('[成功] 已移除 ' + $old.Count + ' 个 Mod 文件。') -ForegroundColor Green;"

echo.
pause
'@
Write-BatFile -Path (Join-Path $StagingDir "卸载Mod(Windows).bat") -Text $UninstallBat

# 3.3 生成 安装说明.txt
$ReadmeTxt = @"
============================================================
  Pathos: NetHack Codex — DOTA2 英雄扩展包 ($Version)
  适配游戏版本参考: Pathos $PathosVersionDisplay
============================================================

【方式一：Windows 一键安装（推荐）】
1. 请先完全关闭 Pathos 游戏。
2. 将本压缩包完整解压（不要直接在压缩软件里双击运行！）。
3. 双击运行「一键安装(Windows).bat」，脚本会自动查找 Steam / 本地 Pathos 目录并完成安装。
4. 打开游戏，在选人界面即可看到 DOTA2 英雄！

【方式二：手动安装（通用，适用于 Windows / macOS / Linux）】
1. 请先完全关闭 Pathos 游戏。
2. 找到 Pathos 游戏安装目录下的 Expansions 文件夹：
   - Steam 版：在 Steam 库右键 Pathos -> 管理 -> 浏览本地文件 -> 进入 Expansions 文件夹
   - 独立版：默认在 C:\Games\Pathos\Expansions\
3. 打开本压缩包内的 files 文件夹，把里面的【全部文件】（共 $($RuntimeFiles.Count) 个，包含 .dll / .Atlas / .Dictionary 等）复制到 Expansions 文件夹中。
   * 注意：不要只复制 .dll，否则会缺少贴图与中文翻译！
4. 启动游戏即可。

【卸载方法】
- Windows 可直接运行「卸载Mod(Windows).bat」；
- 或手动删除 Pathos\Expansions\ 中所有名字包含 DotaHeroesExpansion 的文件即可。
"@
Write-Utf8File -Path (Join-Path $StagingDir "安装说明.txt") -Text $ReadmeTxt

# 4. 压缩为 .zip
Write-Host "[4/4] 正在生成压缩包: $ZipPath" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $StagingDir "*") -DestinationPath $ZipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "打包完成！可直接将以下文件上传至 GitHub Release：" -ForegroundColor Green
Write-Host "  $ZipPath" -ForegroundColor White
