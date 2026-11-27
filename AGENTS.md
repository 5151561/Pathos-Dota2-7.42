# AGENTS.md — Pathos DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

AI 编程助手的入口文档：架构速查、开发铁律与「要做 X 该用哪个基建」的路由。

> **先查 [docs/architecture/framework-api.md](docs/architecture/framework-api.md)（需求 → API 索引）再动手**。不要通过通读 `Heroes/*.cs` 来学框架用法：英雄代码只在索引里标了「参考实现」时才需要看。
> 文档与代码冲突时以代码为准，并在同一次改动里修正文档（含本文件）。

---

## 1. 项目概览与构建

- **性质**：桌面端 **Pathos: NetHack Codex** 的 `.NET 10` 扩展包，单 DLL 内嵌 `Lib.Harmony 2.4.3`。
- **架构**：声明式 Codex 外壳 + Harmony 引擎补丁（`DotaHookBus`）+ `Pathos.dll` 客户端 UI 桥接。
- **构建与部署**：
  ```powershell
  dotnet build .\DotaHeroesExpansion.csproj -c Release
  ```
  C# 编译 → `StageAtlasesForPathosMaker`（校验 `Resources/**/*.png` 无重名并分发到四套 `Atlases/`）→ `PathosMaker.exe` 打包图集/字典并部署到 `<PathosInstallation>\Expansions\`。游戏运行时 DLL 被占用会导致部署失败，关闭游戏重新构建即可。
- **调试日志**：启动 Pathos 前 `$env:DOTA_HEROES_HOOK_DEBUG = "1"`，日志在 `%TEMP%\DotaHeroesHook.log`。

---

## 2. 目录

```text
DotaHeroesExpansion.cs        # 总入口：HookBootstrap.EnsureInitialized + HeroRegistry
Framework/
├── IDotaHeroDefinition.cs    # 英雄接口（Id / Register / RegisterHooks）
├── DotaHeroContext.cs        # Codex 封装：贴图、Property、VFX 注册、预设英雄
├── DotaHeroProfile.cs        # DOTA2 Base/Gain 三维 → 初始六维 / 属性上限 / 职业样板
├── DotaMechanicsMapper.cs    # DOTA2 → Pathos 数值换算
├── DotaAbility.cs / DotaTable.cs   # 主动技能一次性声明 + 1 起始数值表
├── DotaDamage.cs             # 伤害类型/来源、暴击、魔抗管线
├── DotaModifierService.cs    # 带回合数的增益/减益（零 Property）
├── DotaTauntService.cs       # 嘲讽
├── DotaStats.cs / DotaLife.cs      # 强类型六维读写 / 最大生命联动
├── DotaCharacterStorage.cs   # 跨存档数据（技能等级、天赋、自定义整数）→ Character.Dialogue.Tag
├── DotaCharacterFlagSet.cs   # 弱引用角色标记集合
├── DotaCombatHelper.cs       # 敌友判定、搜敌、驱散、目标解析、返蓝
├── Hooks/                    # HookBootstrap（Harmony 引导）、DotaHookBus（事件总线）、HookEventArgs、PathosEngineBridge（引擎反射）
├── Skills/                   # HeroSkillBuild / DotaSkillSlot / HeroSkillRegistry / HeroSkillService（Q/W/E/R/黄点加点）
├── Talents/                  # HeroTalentTree / TalentNode / TalentRegistry / TalentService / TalentTreeUiBridge（「技能与天赋」面板）
├── Aghanim/                  # AghanimDefinitions / AghanimRegistry / AghanimService（神杖/福佑/魔晶物品与升级）
├── Presentation/             # DotaPresentation（演出时间轴）/ DotaVfx（VFX 样式与播放）
├── Targeting/                # DotaRangeTargetingBridge（全射程选格，DotaAbility 自动接入）
├── Localization/             # DotaLocalization（运行时 UI 文本，读 Resources/Localization/ui.<lang>.json）
└── UI/                       # DotaSpellHelpBridge（帮助面板）、DotaStatusBarBridge（状态栏徽章）、DotaPropertyVisibility、PathosClientReflection
Heroes/                       # AxeHero.cs / JuggernautHero.cs / MorphlingHero.cs / PudgeHero.cs
Resources/Heroes/<hero_id>/   # 单份 128x128 透明 PNG
Resources/Localization/       # ui.<lang>.json 运行时词表（嵌入 DLL）
Assets/                       # PathosMaker 生成的 .Atlas 与 .Dictionary（Codex 名称/描述译文）
tools/VfxGen/                 # 像素 VFX 贴图生成器（不参与扩展编译）
docs/                         # 架构、指南、英雄溯源文档
```

---

## 3. 开发铁律

### 3.1 复用 `Framework/`，不要在 `Heroes/` 重复造轮子

完整的「需求 → API」对照在 [framework-api.md §1](docs/architecture/framework-api.md)。最常违反的几条：

- **主动技能**：只用 `DotaAbility.Register` 声明，逻辑写 `OnCast`；禁止手写 5 阶 `Configure(Qualification)`、手动 `DotaRangeTargetingBridge.RegisterSpell`、`AfterCastSpell` + 比较法术名。
- **伤害**：只用 `DotaDamage.Strike/Harm` + `DotaDamageInfo.Physical/Magical/Pure`；暴击 `RegisterCritSource`、魔抗 `RegisterMagicResist`。禁止裸调 `PathosEngineBridge.StrikeCharacter/HarmCharacter`。
- **带回合数的增益/减益**：只用 `DotaModifierService.Define/Apply`；禁止手写状态类 + 手动递减 + 单独 Defence/Speed 查询。
- **持久化**：跨存档数据只用 `DotaCharacterStorage`；运行时状态用 `ConditionalWeakTable` / `DotaCharacterFlagSet`。
- **属性与生命**：`DotaStats`（`DotaStat` 枚举，禁止裸字符串 `"strength"`）、`DotaLife.AdjustMax`；「+N 属性」天赋只声明 `TalentNode.StatBonus`。
- **英雄档案**：三维只在 `DotaHeroProfile` 声明一次；`RegisterHooks` 里 `Profile.RegisterStats()`，职业样板 `Profile.ConfigureClass(...)`。
- **帮助面板**：数值与施法共用英雄内 `ComputeXxx(ch, tier)`；蓝耗用 `DotaHelpFormat.ActualMana`；射程用 `Ability.RangeAt(ch, tier)`；文本 `DotaHelpFormat` / `DotaHelpColor`。
- **运行时文本**：一律 `DotaLocalization.T/Format` + `ui.<lang>.json`（至少补 `en` 与 `zh-Simplified`）；不要在 C# 里硬编码中英文。
- **Codex 字典清理**：移除或重构 Codex 条目（如删除旧 Property/法术/实体）后，`PathosMaker` 会报 `has obsolete terms` 警告，**必须及时清理所有 `Assets/*.Dictionary` 中的对应旧条目**，保持构建零警告。
- **VFX**：`Ctx.RegisterStrikeVfx/BeamVfx/Explosion3x3Vfx` 注册后**必须** `DotaVfx.Style(...)`，播放用 `DotaVfx.*`（按秒）。贴图用 `tools/VfxGen` 生成，画风贴合 Pathos 原生像素特效（32×32 网格 ×4、单条色阶、无抗锯齿、有序抖动淡出），造型与配色对照刀塔百科技能演示视频。
- **多段演出**：`DotaPresentation.Sequence(...).FreezeWorld(...).At(秒, ...)` 按 DOTA2 原版时长排步骤；禁止「每步 Wait 一回合」（`QueueWaitDecision` 已无人使用，不要再引入）。
- **状态栏徽章**：`DotaStatusBarBridge.RegisterProvider(key, className, ...)`；带剩余回合的走 `DotaModifierSpec.Badge*`。
- **客户端补丁**：新增 `Pathos.dll` 反射/补丁复用 `PathosClientReflection`。

### 3.2 数值必须对照 DOTA2 7.42

所有基础属性、成长、蓝耗、伤害、持续时间、概率、天赋与机制**必须对照 DOTA2 7.42 官方 Wiki / 刀塔百科核实**，不得凭记忆或混用旧版本；不确定先查。英雄文档 `docs/heroes/<hero_id>.md` 必须列 7.42 溯源表。

换算统一走 `DotaMechanicsMapper`（详见 [design-guidelines.md](docs/architecture/design-guidelines.md)）：
- 蓝量/蓝耗 $K_{\text{mana}} = 20$（0 蓝耗保留 0），伤害 $K_{\text{dmg}} = 15$，`1 秒 = 2 回合`，护甲 `MapArmorToDefence`（±8 封顶），减冷却 → 减蓝耗（`SpellManaReductionRule`）。
- 技能加点：1 级自带 1 点；小技能 Lv 1/3/5/7、大招 Lv 6/12/18、7 级黄点；主动技能 0→1 学会、之后原生升星；被动由独立技能等级 0→4 驱动。
- 天赋树：`Lv 10/15/20/25` 四层二选一，`Lv 27/28/29/30` 依次补点另一侧。

### 3.3 引擎避坑

1. **零自定义 `Codex.Properties`**：Pathos `PropertySet` 是单个 `Int64`（硬上限 64，官方已占 57），超出会按位回绕撞上官方状态（如幻觉）。本扩展**不注册任何自定义 Property（0 占用，总计 57/64）**：天赋/技能等级/计数器走 `DotaCharacterStorage`，带回合数技能状态与增益/减益走 `DotaModifierService`，先天/被动技能走 `HeroSkillService` + `DotaSpellHelpBridge.RegisterPassive` + `DotaStatusBarBridge`（详见 [framework-api.md §2.3](docs/architecture/framework-api.md)）。禁止调用 `Ctx.Properties.Add`。
2. **学法/升星**用 `PathosEngineBridge.LearnSpell`（`Knowledge.LearnSpell`），禁止 `Engine.LearnSpell`（低智力会触发读书失败惩罚）。`HeroSkillService` 已封装。
3. **点地召唤**：法术外壳 `Plain(Range)` 会对射线每格执行 `Apply`，所以外壳保持 `Apply.Nothing()`（`DotaAbility` 默认如此），在 `OnCast` 里 `PathosEngineBridge.SummonAllyEntity`。
4. **召唤物到期/替换**用 `PathosEngineBridge.DespawnSummon`，禁止 `DismissAlly`（会留下野生单位）。
5. **贴图**：单份 `128x128` 透明 PNG 放 `Resources/Heroes/<hero_id>/`，不要动 `Atlases/`（构建期重建、已忽略）。文件名全局唯一且避开官方 Glyph（主宰用 `yurnero.png` 避开内置 `juggernaut`）。英雄形象、技能/天赋图标按官方 Wiki 原图制作。
6. **标识符**：Hook Key 用 `<hero_id>.<feature>`（只在内存）。会写入存档或字典的标识**禁止改名**：技能槽 ID（`axe.q.*` / `jugg.q.*` / `morph.q.*` / `pudge.q.*`）、天赋 ID、`DotaCharacterStorage` 键、法术/实体/物品/职业/种族/贴图名、本地化 Key。
7. **Hook 结算伤害的技能**必须在法术 `Description` 写明伤害骰与倍率。

---

## 4. 新增英雄 Checklist

完整步骤见 [docs/guides/adding-a-hero.md](docs/guides/adding-a-hero.md)。

1. 贴图放入 `Resources/Heroes/<hero_id>/`。
2. `Heroes/<Hero>Hero.cs` 实现 `IDotaHeroDefinition`：`DotaHeroProfile` → `DotaAbility` → `HeroSkillBuild` → `HeroTalentTree` → 帮助面板 → `RegisterHooks`。
3. 在 `DotaHeroesExpansion.cs` 的 `HeroRegistry` 登记。
4. 运行时文本 Key 补进 `Resources/Localization/ui.en.json` / `ui.zh-Simplified.json`；`dotnet build` 生成 `.Dictionary` 占位后补中文，再构建部署。
5. 新增 `docs/heroes/<hero_id>.md`（含 7.42 溯源表），更新 `docs/heroes/README.md`、`docs/README.md`、`README.md`。
6. 若框架有新增/改动，同步更新 `docs/architecture/framework-api.md` 与本文件。

---

## 5. 文档

- **基建 API 速查（首选）**：[docs/architecture/framework-api.md](docs/architecture/framework-api.md)
- 架构总览：[overview.md](docs/architecture/overview.md)
- Hook 总线与战斗基建详解：[hook-system.md](docs/architecture/hook-system.md)
- 技能加点、天赋树与状态：[talents-and-skills.md](docs/architecture/talents-and-skills.md)
- 全射程选格：[range-targeting.md](docs/architecture/range-targeting.md)
- 数值换算规范：[design-guidelines.md](docs/architecture/design-guidelines.md)
- 新增英雄指南：[adding-a-hero.md](docs/guides/adding-a-hero.md)
