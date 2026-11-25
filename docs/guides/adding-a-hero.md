# 新增 DOTA2 英雄指南

本文以新增一名新英雄（例如敌法师 `antimage`、斧王 `axe`、水晶室女 `crystal_maiden`）为例，说明如何直接接入现有 `Framework` 基建完成标准开发。

> 开发前建议速览：
> - [Framework 基建 API 速查](../architecture/framework-api.md)（首选，按需求查 API）
> - [DOTA2 → Pathos 机制与数值映射规范](../architecture/design-guidelines.md)
> - [天赋树与长/短期技能状态体系](../architecture/talents-and-skills.md)
> - [全射程选格与范围施法体系](../architecture/range-targeting.md)
> - [Hook 事件总线、引擎桥接与战斗基建](../architecture/hook-system.md)

---

## 标准接入 Checklist

### 第 1 步：准备英雄贴图与自定义像素特效

在 `Resources/Heroes/<hero_id>/` 下直接放入单份 `128x128` 透明背景 PNG（无需手动复制四份）：

```text
Resources/Heroes/antimage/
├── antimage.png            # 英雄头像/立绘（注意不要与官方内置怪物贴图重名）
├── mana break.png          # 技能图标
├── blink.png               # 技能图标
└── mana void fx strike.png # 可选：自定义像素特效贴图
```

- **命名冲突校验**：文件名（不含 `.png`）必须全局唯一且不与 Pathos 官方内置贴图冲突（参考主宰用 `yurnero.png` 避开内置怪物 `juggernaut`）。构建时 MSBuild 的 `StageAtlasesForPathosMaker` 会自动分发至四套图集并拦截重名。

---

### 第 2 步：在 `Heroes/` 下实现 `IDotaHeroDefinition`

新建 `Heroes/<HeroName>Hero.cs`，实现 [IDotaHeroDefinition](../../Framework/IDotaHeroDefinition.cs)，按以下标准结构直接接入框架基建：

#### 2.1 数值常量推导（`DotaHeroProfile` + `DotaMechanicsMapper`）
- 基础属性档案（每英雄一次，详见 [hook-system.md §10](../architecture/hook-system.md)）：
  ```csharp
  // DOTA2 7.42 <英雄>初始三维与成长：STR x (+g) / AGI y (+g) / INT z (+g)
  private static readonly DotaHeroProfile Profile = new(HeroClassName,
    BaseStr: 20, StrGain: 2.0f, BaseAgi: 32, AgiGain: 2.8f, BaseInt: 14, IntGain: 1.4f);
  ```
  自动推导 `Profile.StartingStats`（`MapStartingStats`）、`Profile.StatLimits`（`MapStatLimits`）与 1 级职业蓝量基准 `Profile.ComputeClassManaBase(raceManaBase)`。
- 职业样板：在 `Ctx.Classes.Add(C => ...)` 中调用 `Profile.ConfigureClass(Ctx, C, Description, AvatarGlyph, LifeBase, LifeDice, RaceManaBase, ManaDice)`。
- 属性读写：`DotaStats.Str(ch)` / `Dex(ch)` / `Limit(ch, DotaStat.Con)` / `SetPermanent(...)`，禁止传 `"strength"` 等裸字符串；最大生命联动用 `DotaLife.AdjustMax`（§9）。
- 技能蓝耗：`DotaMechanicsMapper.MapSkillManaCost(dotaMana)` 与 `MapSpellLevel(manaCost)`
- 技能伤害与持续时间：`MapSkillDamage` / `MapSkillDamageDice` / `MapDotDamageDicePerTurn` / `MapDurationTurns`
- 天赋减冷却比例：`ComputeCooldownReductionRatio(reducedSec, refCooldownSec)`

#### 2.2 `Register(DotaHeroContext Ctx)` — 声明式注册
1. **贴图与自定义 VFX**：
   - `Ctx.RegisterGlyph("<name>")`
   - `Ctx.RegisterStrikeVfx(...)` / `Ctx.RegisterBeamVfx(...)` / `Ctx.RegisterExplosion3x3Vfx(...)`，注册后**必须**调用 `DotaVfx.Style(...)` 配置淡入淡出/缩放样式。
2. **零自定义 `Codex.Properties`（先天/被动与技能状态）**：
   - 先天/被动：不注册 Property；被动技能用 `DotaSkillSlot.CreatePassive` + `HeroSkillService.GetSkillLevel`，说明面板用 `DotaSpellHelpBridge.RegisterPassive` + `DotaStatusBadge.ForPassive`（详见 [framework-api.md §2.3](../architecture/framework-api.md)）。
   - 主动技能短期状态与带回合数的增益/减益：**统一使用 `DotaModifierService.Define` + `Apply`**（零 Property 占用，自动管理回合递减、防御/速度汇总、DoT、驱散与左上角状态栏徽章）。
3. **主动技能**：每个技能用 `DotaAbility.Register(Ctx, new DotaAbilitySpec { ... })` 声明一次（详见 [hook-system.md §8](../architecture/hook-system.md)），自动生成 5 阶法术外壳（射程写入 `Plain`、`Apply.Nothing()` 防止射线每格重复召唤）、全射程选格与按法术名分发的 `OnCast`：
   ```csharp
   ManaVoidAbility = DotaAbility.Register(Ctx, new DotaAbilitySpec
   {
     SpellName = "mana void", DisplayTitle = "Mana Void", TitleResolver = () => DotaLocalization.T("am.skill.r.title"),
     Description = "...", Icon = ManaVoidGlyph,
     School = Ctx.Schools.evocation, Skill = Ctx.Skills.evocation, Precept = new Precept(Purpose.Blast),
     ManaCost = DotaMechanicsMapper.MapSkillManaCost(250),
     Targeting = DotaAbilityTargeting.UnitHostile, Range = DotaTable.Of(4, 5, 5, 6, 7),
     SkillSlotId = SkillManaVoidId,
     OnCast = Cast =>
     {
       if (!Cast.TryResolveHostileTarget(out var Target, "am.log.void_no_target"))
         return;   // 已按实际扣蓝返还并写日志
       int Tier = Cast.Tier; float Star = ManaVoidAbility.StarMultiplier(Cast.Caster);
       DotaDamage.Strike(Cast.Engine, Cast.Caster, Target, Cast.Direction, Damage, DotaDamageInfo.Magical("mana void"));
     }
   });
   ```
   - 加点表 `DotaSkillSlot.CreateActive(Spell: ManaVoidAbility.Spell, ...)`、帮助面板 `ResolveCurrentTier = ManaVoidAbility.Tier`、职业 `C.Startup` 等都直接引用 `Ability.Spell` / `Ability.Tier`。
4. **召唤物 / 装备 / 职业 / 预设英雄**：
   - 预设英雄一键注册：`Ctx.RegisterPresetHero("HeroName", race, heroClass, gender, glyph)`。
5. **技能加点表与 4 层天赋树**：
   - 技能加点表：使用 `HeroSkillBuild.CreateStandard(...)` + `HeroSkillRegistry.Register(Ctx, Build)` 声明 Q/W/E/R（外加自动追加的 7 级全属性黄点，详见 [framework-api.md §3](../architecture/framework-api.md)）。
   - 天赋树：使用 `HeroTalentTree.CreateStandard(...)` 定义 Lv 10/15/20/25（双修 Lv 27/28/29/30）共 8 个节点；减冷却天赋直接挂 `SpellManaReductionRule`，然后调用 `TalentRegistry.Register(Ctx, tree)`。
   - 「+N 属性」天赋直接声明 `new TalentNode(...) { StatBonus = PathosStatLimits.Bonus(Str: 3, Con: 3) }`，上限拓宽、永久提升与 `+N × 6 HP` 由框架结算，不要手写 `OnLearn` 或 `RegisterHeroStatLimitBonus`（详见 [hook-system.md §9.3](../architecture/hook-system.md)）。
6. **技能帮助面板**：`DotaSpellHelpBridge.RegisterSpell(...)` / `RegisterPassive(...)` 中用 `DotaHelpFormat.Tiles/Turns/CastRangeSuffix/ActualMana` 与 `DotaSpellHelpBridge.Tiers(...)`；面板与施法共用的公式抽成英雄内 `ComputeXxx(ch, tier)`（详见 [hook-system.md §11](../architecture/hook-system.md)）。

#### 2.3 `RegisterHooks(DotaHeroContext Ctx)` — 接入运行时基建
1. **锁定固定初始六维与职业属性上限**：
   ```csharp
   Profile.RegisterStats();
   ```
2. **全射程选格与施法分发**：由 `DotaAbility.Register` 自动完成，无需手动调用 `DotaRangeTargetingBridge.RegisterSpell` 或注册 `AfterCastSpell` + 比较法术名。
3. **接入左上角状态栏叠层徽章**（带回合数的状态直接配 `DotaModifierSpec.Badge*`；无回合数的叠层/充能走 `DotaStatusBarBridge`，详见 [hook-system.md §12](../architecture/hook-system.md)）：
   ```csharp
   DotaStatusBarBridge.RegisterProvider("<hero_id>.<stack_name>", HeroClassName, Character =>
     DotaStatusBadge.ForPassive("<hero_id>.<stack_name>", Glyph, "<hero_id>.<passive_id>", "<hero_id>.status.<x>", Count: "...", Colour: Colour.Black));
   ```
4. **订阅 `DotaHookBus` 并复用 `DotaCombatHelper` & `PathosEngineBridge`**：
   - Hook Key 统一命名 `<hero_id>.<feature>`。
   - 熟练度阶数 / 技能等级 / 升星 / 射程统一走 `Ability.Tier(ch)` / `Ability.Level(ch)` / `Ability.StarMultiplier(ch)` / `Ability.Range(ch)`；按阶或按级的数值表用 `DotaTable.Of(...)[tier]`。
   - 直接复用 `DotaCombatHelper` 中的 `IsClass`、`IsHostile`、`IsFriendly`、`DispelNegativeDebuffs`、`IsNegativeDebuff`；VFX 播放用 `DotaVfx.Strike/StrikeOn/Beam/BeamTrail/Explode`；多段演出用 `DotaPresentation.Sequence/Loop/HitStop`。
   - 召唤与销毁统一调用 `PathosEngineBridge.SummonAllyEntity` / `DespawnSummon`。
   - 跨存档自定义整数走 `DotaCharacterStorage`；角色私有运行时状态统一使用 `ConditionalWeakTable<Character, TState>` 或 `DotaCharacterFlagSet`。

---

### 第 3 步：在扩展包总入口登记英雄

打开 [DotaHeroesExpansion.cs](../../DotaHeroesExpansion.cs)，在 `HeroRegistry` 中追加新英雄实例：

```csharp
var HeroRegistry = new List<IDotaHeroDefinition>
{
  new MorphlingHero(),
  new JuggernautHero(),
  new AntiMageHero(),
};
```

---

### 第 4 步：补全两套本地化词表、编译并部署

扩展包有两套本地化词表，各管各的（详见 [framework-api.md §4](../architecture/framework-api.md)）：

1. **运行时 UI 词表（`Resources/Localization/ui.<lang>.json`）**：
   - 将英雄代码里通过 `DotaLocalization.T` / `Format` 引用的全部 Key（前缀 `<hero短前缀>.`，含技能加点标题/描述、天赋标题/描述、帮助面板文案、战斗日志、徽章标题）**至少同时补进 `ui.en.json` 与 `ui.zh-Simplified.json`**（其余语言缺失时自动回退英文）。
2. **首次编译，生成 Codex 字典占位**：
   - 确保桌面端 Pathos 已关闭，执行首次构建，让 `PathosMaker.exe` 自动扫描并生成新增法术、职业、被动 Property、召唤物的占位词条：
   ```powershell
   dotnet build .\DotaHeroesExpansion.csproj -c Release
   ```
3. **补全 Codex 字典（`Assets/DotaHeroesExpansion.zh-Simplified.Dictionary`）**：
   - 打开 `Assets/DotaHeroesExpansion.zh-Simplified.Dictionary`（按需同步其他语言字典），补全新增技能、职业、天赋、召唤物的中文名称与描述。
4. **再次编译部署**：
   - 再次运行 `dotnet build .\DotaHeroesExpansion.csproj -c Release`，将完整图集、两套词表与 DLL 自动部署到游戏目录。

---

### 第 5 步：补充英雄文档（含 DOTA2 7.42 数值溯源表）

1. 在 `docs/heroes/` 下新建 `<hero_id>.md`（可直接参考 [juggernaut.md](../heroes/juggernaut.md) 或 [morphling.md](../heroes/morphling.md)），必须包含：
   - 参考版本号（如 `DOTA2 7.42`）
   - 完整的「DOTA2 7.42 → Pathos 数值映射溯源表」（基础属性与资源 + 技能数值映射 + 换算依据）
   - 天赋树清单、全射程选格与状态栏清单、Hook Key 清单与贴图清单
2. 在 [docs/heroes/README.md](../heroes/README.md)、[docs/README.md](../README.md) 与根目录 [README.md](../../README.md) 中补充索引链接。
