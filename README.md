# Pathos — DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

English | [简体中文](README.zh-CN.md)

An extensible **DOTA2 Heroes Expansion** for desktop **Pathos: NetHack Codex** (Windows / macOS / Linux), faithfully adapting **DOTA2 7.42** mechanics into Pathos.

Built with a **hybrid "Declarative Codex Shell + Harmony Hook Core" architecture**, packaged as a single DLL with embedded `Lib.Harmony 2.4.3`. Hero assets are organized per hero and automatically staged and bundled by `PathosMaker` at build time.

---

## Key Features

- **4 Playable DOTA2 7.42 Heroes**: Morphling, Juggernaut, Axe, and Pudge — each with authentic base stats, stat gains, innate abilities, and iconic Q/W/E/R kits.
- **Skill Progression & 4-Tier Talent Trees**: Level up Q/W/E/R abilities and stats; choose between two branches at Lv 10 / 15 / 20 / 25, then unlock the remaining branch at Lv 27–30.
- **Aghanim's Scepter, Blessing & Shard**: Upgrade hero abilities or unlock new skills with Aghanim items.
- **Active Ability Cooldowns & Modifiers**: Unified `1 second = 1 turn` scale, cross-save cooldown/modifier persistence, status bar badges, and zero custom `Codex.Properties` usage.
- **Full-Range Targeting & Custom Pixel VFX**: Free tile targeting across full cast range, timed multi-stage skill presentations, and Pathos-styled pixel art VFX.
- **25 Languages Built In**: Embedded UI localization and Codex dictionaries across 25 languages.

---

## Installation (Players — No Build Required)

> Supported platforms: **Desktop Steam / standalone Pathos** (Windows / macOS / Linux). No .NET SDK or manual compilation required.

1. Go to the **[Releases](../../releases)** page, download the latest `DotaHeroesExpansion-v*.zip`, and **extract the entire archive** (optional: verify integrity with `SHA256SUMS.txt`).
2. **Completely close Pathos** (file locks while the game is running will prevent installing or overwriting files).
3. Choose either installation method:
   - **Windows One-Click Install (Recommended)**: Double-click `一键安装(Windows).bat` inside the extracted folder. The script automatically detects your Steam or standalone Pathos installation and deploys all files.
   - **Manual Install (Windows / macOS / Linux)**:
     1. Open the `Expansions` folder in your Pathos installation directory (Steam: right-click **Pathos** in your Library → **Manage** → **Browse local files** → open `Expansions`).
     2. Copy **all files** from the extracted `files/` folder (`.dll`, `.Atlas`, `.Dictionary`, etc. — **do not copy only the `.dll`**) into `Expansions/`.
4. Launch Pathos and select a DOTA2 hero on the character creation screen!

> **Uninstall**: Run `卸载Mod(Windows).bat`, or manually delete all files containing `DotaHeroesExpansion` from `Expansions/`.

---

## Playable Heroes (DOTA2 7.42)

- [Morphling](docs/heroes/morphling.md)
- [Juggernaut](docs/heroes/juggernaut.md)
- [Axe](docs/heroes/axe.md)
- [Pudge](docs/heroes/pudge.md)

Full hero overview and asset index: [docs/heroes/README.md](docs/heroes/README.md)

---

## Developer Quick Start

1. Copy [LocalPathos.props.example](LocalPathos.props.example) to `LocalPathos.props` and set `<PathosInstallation>` to your local Pathos directory (or set the `PATHOS_INSTALL_DIR` environment variable).
2. Build and auto-deploy to `<PathosInstallation>\Expansions\`:

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

3. Package a release-ready `.zip` into `dist/` (or push a `v*` tag to trigger automated GitHub Actions packaging & release via [.github/workflows/build-release.yml](.github/workflows/build-release.yml)):

```powershell
.\scripts\package-release.ps1 -Version "v0.1.4"
```

See [Build, Deploy & Debug Guide](docs/guides/build-and-debug.md) for detailed instructions and hook debug logging (`DOTA_HEROES_HOOK_DEBUG=1`).

---

## Documentation

Full documentation lives in [docs/](docs/README.md) *(currently written in Chinese)*:

- **Architecture & Standards**
  - [Framework API Index](docs/architecture/framework-api.md) (requirement → API quick reference)
  - [Architecture Overview & Project Structure](docs/architecture/overview.md) (hybrid architecture, module layout & MSBuild asset staging)
  - [Hook Event Bus, Engine Bridge & Combat Helpers](docs/architecture/hook-system.md) (`DotaHookBus`, `PathosEngineBridge`, `DotaCombatHelper`)
  - [Talent Trees & Skill State System](docs/architecture/talents-and-skills.md) (4-tier talent trees, modifiers, status bar badges & custom VFX)
  - [Full-Range Targeting & AoE Casting](docs/architecture/range-targeting.md) (`DotaRangeTargetingBridge`, target resolution, point-targeted summons)
  - [DOTA2 → Pathos Mechanics & Scaling Guidelines](docs/architecture/design-guidelines.md) (stats, mana, damage, duration/cooldown, healing & armor formulas)
- **Developer Guides**
  - [Build, Deploy & Debug Guide](docs/guides/build-and-debug.md)
  - [Adding a New DOTA2 Hero](docs/guides/adding-a-hero.md)
- **Hero Mechanics**
  - [Hero Roster Overview](docs/heroes/README.md)
