# Framework 基建 API 速查（按需求查）

> **写英雄 / 改机制先查这张表，不要去英雄源码里「找用法」**。每一行都给出要用的 API 和详细说明所在章节；只有表里写了「参考实现」时才需要去看英雄代码。
> 本文与代码不一致时以代码为准，并顺手修正本文。

## 1. 需求 → API 索引

| 我要做什么 | 用这个 | 详细说明 |
| :--- | :--- | :--- |
| 声明英雄三维 / 初始六维 / 属性上限 / 职业样板 | `DotaHeroProfile` + `Profile.RegisterStats()` + `Profile.ConfigureClass(...)` | [hook-system.md §10](hook-system.md) |
| 声明一个主动技能（法术外壳、射程、选格、施法回调） | `DotaAbility.Register(Ctx, new DotaAbilitySpec { ... })` | [hook-system.md §8](hook-system.md) |
| 施法时找目标 / 落点 / 空放返蓝 | `Cast.TryResolveHostileTarget` / `Cast.ResolveOpenPointTarget` / `Cast.Refund` | [hook-system.md §8.2](hook-system.md) |
| 按熟练度阶 / 技能等级取数值 | `Ability.Tier/Level/StarMultiplier/Range/RangeAt` + `DotaTable.Of(...)[n]` | [hook-system.md §8.3–8.4](hook-system.md) |
| 技能加点表（Q/W/E/R/黄点） | `HeroSkillBuild.CreateStandard` + `DotaSkillSlot.CreateActive/CreateActiveGroup/CreatePassive` + `HeroSkillRegistry.Register` | 本文 §3 |
| 被动技能当前等级 | `HeroSkillService.GetSkillLevel(ch, slotId)` | 本文 §3 |
| 天赋树 | `HeroTalentTree.CreateStandard` + `TalentNode` + `TalentRegistry.Register`；运行时 `TalentService.HasTalent(ch, id)` | [talents-and-skills.md §1](talents-and-skills.md) |
| 「+N 属性」天赋 | `TalentNode { StatBonus = PathosStatLimits.Bonus(...) }` | [hook-system.md §9.3](hook-system.md) |
| 减冷却天赋 | `TalentNode.ManaReductions` + `SpellManaReductionRule` | [talents-and-skills.md §1.3](talents-and-skills.md) |
| 造成伤害 | `DotaDamage.Strike/Harm` + `DotaDamageInfo.Physical/Magical/Pure` | [hook-system.md §7](hook-system.md) |
| 暴击 / 魔抗 | `DotaDamage.RegisterCritSource` / `RegisterMagicResist` | [hook-system.md §7.2](hook-system.md) |
| 带回合数的增益/减益（护甲、移速、攻速、DoT、伤害格挡、徽章） | `DotaModifierService.Define` + `Apply` | [hook-system.md §6](hook-system.md) |
| 每次受伤固定格挡（含小数，概率进位） | `DotaModifier.DamageBlock`；已自行结算格挡的伤害加 `DotaDamageFlags.NoBlock` | [hook-system.md §6.2](hook-system.md)；参考实现：帕吉「肉盾」 |
| 嘲讽 | `DotaTauntService.ApplyTaunt` | [hook-system.md §5](hook-system.md) |
| 无敌 / 禁魔 | `DotaHookBus.RegisterInvulnerableQuery` / `RegisterSilenceQuery` | [hook-system.md §2](hook-system.md) |
| 临时六维加成（不改永久值） | `DotaHookBus.RegisterActiveFormQuery` | [hook-system.md §2](hook-system.md)；参考实现：斧王「一人之军」 |
| 读写永久属性 / 最大生命联动 | `DotaStats` / `DotaLife.AdjustMax` | [hook-system.md §9](hook-system.md) |
| 被动 / 先天技能（零 Property，点击状态栏徽章弹出原生帮助） | `DotaSkillSlot.CreatePassive` + `DotaSpellHelpBridge.RegisterPassive` + `DotaStatusBadge.ForPassive` | 本文 §2.3、§5 |
| 主动技能施法期间的状态图标 / 带回合数增益减益 | `DotaModifierService.Define` + `Apply`（零 Property，自动徽章） | [hook-system.md §6](hook-system.md) |
| 跨存档保存的英雄数值（永久层数等） | `DotaCharacterStorage.Get/Set/AddCustomInt` | 本文 §2 |
| 角色私有运行时状态（不存档） | `ConditionalWeakTable<Character, TState>`；只需布尔标记时用 `DotaCharacterFlagSet` | 本文 §2 |
| 左上角状态栏徽章 | `DotaStatusBarBridge.RegisterProvider` + `DotaStatusBadge.ForPassive` | [hook-system.md §12](hook-system.md) |
| 技能 / 被动帮助面板 | `DotaSpellHelpBridge.RegisterSpell` / `RegisterPassive` + `DotaHelpFormat` + `DotaHelpColor` | 本文 §5、[hook-system.md §11](hook-system.md) |
| 运行时文本（日志、面板、天赋/技能描述） | `DotaLocalization.T/Format` + `Resources/Localization/ui.<lang>.json` | 本文 §4 |
| 法术/职业/Property 名称与描述 | Codex 声明 + `Assets/DotaHeroesExpansion.<lang>.Dictionary` | 本文 §4 |
| 自定义像素特效 | `Ctx.RegisterStrikeVfx/BeamVfx/Explosion3x3Vfx` + `DotaVfx.Style` + `DotaVfx.Strike/StrikeOn/Beam/BeamTrail/Explode` | [hook-system.md §13.3](hook-system.md)、[talents-and-skills.md §4.3](talents-and-skills.md) |
| 贴地范围特效（画在角色之下，如毒雾、光环） | `DotaVfxStyle { Ground = true }` + `DotaVfx.Explode` | [talents-and-skills.md §4.3](talents-and-skills.md)；参考实现：帕吉「腐烂」 |
| 多段演出（连斩、突进、弹道）、持续旋风、顿帧 | `DotaPresentation.Sequence/Loop/HitStop` | [hook-system.md §13.2](hook-system.md) |
| 原生飘字 / 波纹 / 挥砍动作 / 平滑飞行 | `PathosEngineBridge.StoryCaption/StoryDetecting/StoryAttacking/StoryFlinging` | [hook-system.md §3](hook-system.md) |
| 召唤 / 销毁召唤物 | `PathosEngineBridge.SummonAllyEntity` / `DespawnSummon` | [range-targeting.md §3.3](range-targeting.md) |
| 变形 / 模仿 | `PathosEngineBridge.PolymorphCharacter` / `UnpolymorphCharacter` / `ImpersonateCharacter` | [hook-system.md §3](hook-system.md)；参考实现：水人「变形」 |
| 敌友判定、范围搜敌、驱散、日志目标名 | `DotaCombatHelper` | [hook-system.md §4](hook-system.md) |
| 数值换算（蓝耗、伤害、时间、护甲、减冷却） | `DotaMechanicsMapper` | [design-guidelines.md](design-guidelines.md) |
| 订阅引擎事件 | `DotaHookBus.Register*`（Key = `<hero_id>.<feature>`） | [hook-system.md §2](hook-system.md) |

---

## 2. 持久化与运行时状态

### 2.1 `DotaCharacterStorage`（跨存档）

[DotaCharacterStorage.cs](../../Framework/DotaCharacterStorage.cs) 把数据以紧凑文本写进 `Character.Dialogue.Tag`（玩家角色原生为 `null`，随 `Character` 完整序列化进存档）。**技能等级、已学天赋都存在这里**，不占用任何 `Codex.Properties`。

| API | 用途 |
| :--- | :--- |
| `GetSkillLevel` / `SetSkillLevel` | 技能加点等级（由 `HeroSkillService` 调用，英雄代码一般不直接用） |
| `HasTalent` / `AddTalent` | 已学天赋（由 `TalentService` 调用，英雄代码用 `TalentService.HasTalent`） |
| `GetCustomInt(ch, key, default)` / `SetCustomInt` / `AddCustomInt` | 英雄自定义持久整数（值为 0 自动删键）。如斧王淘汰之刃永久护甲（存 1/10 精度整数） |

- Key 命名 `<hero_id>.<feature>`，**一旦发布不得改名**（存档兼容）。
- 只有整数；小数按固定精度换算成整数存储。

### 2.2 不存档的运行时状态

- 每角色的结构化状态：`ConditionalWeakTable<Character, TState>`（角色被回收自动释放）。
- 只需「是否在某集合里」：`DotaCharacterFlagSet`（`Add` / `Remove` / `Contains`），替代 `static HashSet<Character>`。参考：主宰 `SkipNextBladeFuryTick`。
- 带剩余回合的效果：直接用 `DotaModifierService`，不要自建状态类。
- 以上都**读档即清空**，需要跨存档的走 §2.1。

### 2.3 全局 Property 约束（本扩展零占用）

Pathos 的 `PropertySet` 底层是 `Inv.BitmixSet`（单个 `Int64`），全局最多 64 个 `Property`，超出会按位回绕撞上官方状态（如幻觉）。官方 `CodexProperties` 已占 **57** 个。

**本扩展已全部迁移为零自定义 Property（0 占用，总计 57/64）**：
- 天赋、技能等级、计数器 → `DotaCharacterStorage`（§2.1）。
- 主动技能短期状态与带回合数增益/减益 → `DotaModifierService`（自动生成左上角徽章）。
- 先天技能与被动技能 → `HeroSkillService`（独立等级）+ `DotaSpellHelpBridge.RegisterPassive`（原生 `HelpQuery` 弹窗）+ `DotaStatusBadge.ForPassive`（左上角徽章）。
- **禁止调用 `Ctx.Properties.Add`**，确保后续新增任意数量英雄都不受 64 位上限约束。

---

## 3. 技能加点表：`HeroSkillBuild` / `DotaSkillSlot`

在英雄 `Register` 中、`DotaAbility.Register` 之后声明一次：

```csharp
var Build = HeroSkillBuild.CreateStandard(
  ClassName, "Axe", HeroId: "axe", IconGlyph: AxeGlyph,
  SlotQ: DotaSkillSlot.CreateActive(Id: SkillCallId, Hotkey: "Q", Title: "Berserker's Call",
    IconGlyph: CallGlyph, Spell: CallAbility.Spell,
    DescribeLevel: Lv => ...,                                   // 加点面板单行描述
    TitleResolver: () => DotaLocalization.T("axe.skill.q.title"),
    Stats: new[] { DotaSkillStat.Of("ui.stat.duration", Lv => Lv > 0 ? $"{...}" : null) }),
  SlotW: ..., SlotR: DotaSkillSlot.CreateActive(..., IsUltimate: true),
  SlotE: DotaSkillSlot.CreatePassive(Id: SkillHelixId, Hotkey: "E", ..., IconGlyph: CounterHelixGlyph),
  HeroDisplayNameResolver: () => DotaLocalization.T("axe.hero_display"),
  PrimaryAttribute: DotaPrimaryAttribute.Strength);   // 决定第 7 级黄点额外 +1 的属性
HeroSkillRegistry.Register(Ctx, Build);
```

| 工厂 | 行为 |
| :--- | :--- |
| `CreateActive(..., Spell)` | 0→1 学会法术，1→N 原生升星（★~★★★）。小技能 4 级（Lv 1/3/5/7 解锁），`IsUltimate` 3 级（Lv 6/12/18） |
| `CreateActiveGroup(..., Spells)` | 一次加点同时学会/升星多个法术（水人 E 两个方向、R 变形/解除） |
| `CreatePassive(...)` | 独立被动技能等级（0→4），由 `HeroSkillService.GetSkillLevel` 驱动（零 Property） |
| `CreateStandardAttributeBonus` | 7 级黄点（`CreateStandard` 自动追加，无需手写） |

- 悬停卡片第一段是技能说明 `Slot.Description`：主动技能默认取法术 Codex 描述的当前语言译文（`PathosEngineBridge.GetLocalizedDescription`，多法术槽位逐条换行）；被动技能没有法术，必须传 `DescriptionResolver`（通常复用被动帮助面板的 `<hero>.help.e.desc`）。第二段才是 `DescribeLevel(当前等级)` 的一句话数值（金色）。
- `Stats`：悬停卡片里的逐级数值行（`DotaSkillStat.Of(labelKey, Lv => text)`，返回 `null` 表示该级无此项）。必须与 `DescribeLevel`、施法共用同一套按等级函数。
- `OnUpgrade(engine, ch, newLevel)`：加点瞬间的一次性效果。
- 主动技能的等级/升星在英雄代码里用 `Ability.Level(ch)` / `Ability.StarMultiplier(ch)`（需在 `DotaAbilitySpec.SkillSlotId` 填槽位 ID）。
- 槽位 ID（`axe.q.*` 等）写入存档，**不得改名**。

---

## 4. 本地化：两套词表，各管各的

| | 运行时 UI 词表 | Codex 字典 |
| :--- | :--- | :--- |
| 文件 | `Resources/Localization/ui.<lang>.json`（25 种语言，作为 `EmbeddedResource` 打进 DLL） | `Assets/DotaHeroesExpansion.<lang>.Dictionary` |
| 管什么 | C# 运行时拼出来的文本：战斗日志、帮助面板、天赋/技能面板、徽章标题、飘字 | Codex 声明的名称与描述：法术、职业、种族、Property、实体、物品 |
| 怎么用 | `DotaLocalization.T(key)` / `DotaLocalization.Format(key, args...)` | 声明时写英文 `Name` / `Description`，`dotnet build` 时 `PathosMaker` 生成占位，再补译文 |
| 回退 | 当前语言 → （繁中先回退简中）→ `en` → 原 Key | 原生机制 |
| 实时切换 | 支持（每次调用读取当前语言） | 原生机制 |

- 新增 UI Key：**至少同时加到 `ui.en.json` 与 `ui.zh-Simplified.json`**，其余语言缺失时回退英文；Key 前缀 `<hero短前缀>.`（`axe.` / `jugg.` / `morph.`），通用文本用 `ui.` / `dota.`。
- 带参数用 `{0}` 占位并走 `Format`（使用 InvariantCulture；译文格式串出错时自动回退英文模板）。
- `TitleResolver` / `DescriptionResolver` / `HeroDisplayNameResolver` 都接受 `() => DotaLocalization.T(...)`，用于面板标题随语言切换。
- 判断是否中日韩排版：`DotaLocalization.IsCjkActive()`。

---

## 5. 技能帮助面板：`DotaSpellHelpBridge.RegisterSpell`

```csharp
DotaSpellHelpBridge.RegisterSpell(new DotaSpellHelpSpec
{
  SpellName = CallAbility.Name,
  ResolveCurrentTier = CallAbility.Tier,                    // 高亮当前熟练度阶
  BuildLiveStats = HelpCtx =>                               // 「当前实时面板」键值行
  {
    var Call = ComputeCall(HelpCtx.Character, CallAbility.Tier(HelpCtx.Character));
    return new (string Key, string Value)[]
    {
      (DotaLocalization.T("axe.help.q.key_duration"), DotaHelpColor.Control(DotaHelpFormat.Turns(Call.DurationTurns))),
    };
  },
  BuildMechanicsNotes = _ => new[] { DotaLocalization.T("axe.help.q.mech_1") },
  BuildTiers = HelpCtx => DotaSpellHelpBridge.Tiers(        // 5 阶熟练度明细
    Tier => DotaHelpFormat.CastRangeSuffix(CallAbility.RangeAt(HelpCtx.Character, Tier)),
    Tier => ... ComputeCall(HelpCtx.Character, Tier) ...)
});
```

- `HelpCtx.Character` 在百科中为 `null`，`ComputeXxx` 与 `Ability.*` 需容忍 `null`（按 1 级基础值）。
- 被动 / 先天技能用 `RegisterPassive(new DotaPassiveHelpSpec { Id, IconGlyph, TitleResolver, DescriptionResolver, ResolveCurrentLevel, BuildLiveStats, BuildMechanicsNotes, BuildLevels })`（零 Property，点击状态栏徽章弹出原生 `HelpQuery`），按等级明细用 `DotaSpellHelpBridge.Levels(maxLevel, header, bullet)`。
- 语义着色 `DotaHelpColor`：`Physical` / `Magic` / `Stat` / `Skill` / `Talent` / `Passive` / `Heal` / `Mana` / `Control`。
- 骰子显示：`DotaSpellHelpBridge.FormatDiceWithRange(dice)` / `FormatDiceNotation(dice)`。
- 其余规则（真实蓝耗、施法与面板同源）见 [hook-system.md §11](hook-system.md)。
