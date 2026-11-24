# 新增 DOTA2 英雄指南

本文以新增一名新英雄（例如敌法师 `antimage`、斧王 `axe`、水晶室女 `crystal_maiden`）为例，说明如何直接接入现有 `Framework` 基建完成标准开发。

> 开发前建议速览：
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

#### 2.1 数值常量推导（`DotaMechanicsMapper`）
- 初始六维：`DotaMechanicsMapper.MapStartingStats(BaseStr, BaseAgi, BaseInt, Charisma: 10)`
- 1 级职业蓝量基准：`DotaMechanicsMapper.ComputeClassManaBase(DotaBaseInt, RaceManaBase, StartingStats.Wis)`
- 技能蓝耗：`DotaMechanicsMapper.MapSkillManaCost(dotaMana)` 与 `MapSpellLevel(manaCost)`
- 技能伤害与持续时间：`MapSkillDamage` / `MapSkillDamageDice` / `MapDotDamageDicePerTurn` / `MapDurationTurns`
- 天赋减冷却比例：`ComputeCooldownReductionRatio(reducedSec, refCooldownSec)`

#### 2.2 `Register(DotaHeroContext Ctx)` — 声明式注册
1. **贴图与自定义 VFX**：
   - `Ctx.RegisterGlyph("<name>")`
   - `Ctx.RegisterStrikeVfx(...)` / `Ctx.RegisterBeamVfx(...)` / `Ctx.RegisterExplosion3x3Vfx(...)`
2. **长效天赋 vs 短期技能状态**（严格区分，避免污染天赋栏）：
   - 先天/被动（需展示在原生天赋栏与百科）：`Ctx.RegisterPassiveProperty(name, desc, glyph)`，并在职业中 `C.AddFeat(1, prop)`。
   - 主动技能短期状态（仅施法期间作为 `Transient` 生效）：**必须**使用 `Ctx.RegisterSkillStatusProperty(name, desc, glyph)`，自动从原生天赋栏与百科中隐藏。
3. **召唤物 / 装备 / 主动技能外壳 / 职业 / 预设英雄**：
   - 点地召唤技能外壳请用 `Adept.SetCast().Plain(Dice.Fixed(Range))` + `Adept.Apply.Nothing()`，避免射线每格重复召唤。
   - 预设英雄一键注册：`Ctx.RegisterPresetHero("HeroName", race, heroClass, gender, glyph)`。
4. **4 层天赋树**：
   - 使用 `HeroTalentTree.CreateStandard(...)` 定义 Lv 5/10/15/20（双修 Lv 22/23/24/25）共 8 个节点；减冷却天赋直接挂 `SpellManaReductionRule`，然后调用 `TalentRegistry.Register(Ctx, tree)`。

#### 2.3 `RegisterHooks(DotaHeroContext Ctx)` — 接入运行时基建
1. **锁定固定初始六维**：
   ```csharp
   DotaMechanicsMapper.RegisterFixedStartingStats(HeroClassName, StartingStats);
   ```
2. **接入全射程选格**（所有指向敌方 / 点地技能）：
   ```csharp
   DotaRangeTargetingBridge.RegisterSpell(new DotaRangeSpellSpec(
     SpellName: "mana void",
     DisplayTitle: "法力虚空",
     Mode: DotaTargetMode.UnitHostile,
     ResolveRange: Caster => GetManaVoidRange(Ctx, Caster),
     AllowSelf: false));
   ```
3. **接入左上角状态栏叠层徽章**（若有叠层/充能/粘滞状态）：
   ```csharp
   DotaStatusBarBridge.RegisterProvider("<hero_id>.<stack_name>", Character => ...);
   ```
4. **订阅 `DotaHookBus` 并复用 `DotaCombatHelper` & `PathosEngineBridge`**：
   - Hook Key 统一命名 `<hero_id>.<feature>`。
   - 直接复用 `DotaCombatHelper` 中的 `IsClass`、`GetSkillTier`、`IsHostile`、`IsFriendly`、`DispelNegativeDebuffs`、`IsNegativeDebuff`、`ResolveHostileTarget`、`ResolveOpenPointTarget`、`RefundMana`、`PlayBeamTrail`。
   - 召唤与销毁统一调用 `PathosEngineBridge.SummonAllyEntity` / `DespawnSummon`。
   - 角色私有运行时状态统一使用 `ConditionalWeakTable<Character, TState>`。

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

### 第 4 步：编译、补全中文词条并部署

1. 确保桌面端 Pathos 已关闭，执行首次构建，让 `PathosMaker.exe` 自动扫描并生成新增词条占位：
   ```powershell
   dotnet build .\DotaHeroesExpansion.csproj -c Release
   ```
2. 打开 `Assets/DotaHeroesExpansion.zh-Simplified.Dictionary`（按需同步其他语言字典），补全新增技能、职业、天赋、召唤物的中文名称与描述。
3. 再次运行 `dotnet build .\DotaHeroesExpansion.csproj -c Release`，将完整图集、翻译与 DLL 自动部署到游戏目录。

---

### 第 5 步：补充英雄文档（含 DOTA2 7.42 数值溯源表）

1. 在 `docs/heroes/` 下新建 `<hero_id>.md`（可直接参考 [juggernaut.md](../heroes/juggernaut.md) 或 [morphling.md](../heroes/morphling.md)），必须包含：
   - 参考版本号（如 `DOTA2 7.42`）
   - 完整的「DOTA2 7.42 → Pathos 数值映射溯源表」（基础属性与资源 + 技能数值映射 + 换算依据）
   - 天赋树清单、全射程选格与状态栏清单、Hook Key 清单与贴图清单
2. 在 [docs/heroes/README.md](../heroes/README.md)、[docs/README.md](../README.md) 与根目录 [README.md](../../README.md) 中补充索引链接。
