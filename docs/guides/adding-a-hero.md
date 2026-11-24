# 新增 DOTA2 英雄指南

本文以新增一名新英雄（例如敌法师 `antimage`、卡尔 `invoker`、斧王 `axe`）为例，说明完整的标准接入流程。

## 接入 Checklist

### 第 1 步：准备英雄贴图资源

在 `Resources/Heroes/<hero_id>/` 下直接放入单份 `128x128` 透明背景 PNG：

```text
Resources/Heroes/antimage/
├── antimage.png
└── mana break.png
```

> **注意**：文件名（不含 `.png`）必须全局唯一且不与 Pathos 官方内置贴图冲突。构建时 MSBuild 会自动将 `Resources/**/*.png` 分发到 `Atlases/{absurd,classic,geoduck,sonyvanda}/` 并进行重名校验。

---

### 第 2 步：编写英雄定义类

在 `Heroes/` 下新建类（如 `Heroes/AntiMageHero.cs`），实现 [IDotaHeroDefinition](../../Framework/IDotaHeroDefinition.cs)：

1. `Id`：返回英文小写唯一标识（如 `"antimage"`），与 `Resources/Heroes/<hero_id>/` 和 `docs/heroes/<hero_id>.md` 保持一致。
2. `Register(DotaHeroContext Ctx)`：
   - 调用 `Ctx.RegisterGlyph("<name>")` 注册所有贴图；
   - 注册先天技能 / 被动专长、专属装备或召唤物、主动技能外壳；
   - 注册专属种族（如需要）、专属职业与预设英雄（`Ctx.Heroes.Add(...)`）。
3. `RegisterHooks(DotaHeroContext Ctx)`（可选）：
   - 订阅 [DotaHookBus](../architecture/hook-system.md) 事件实现复杂机制（如法球削蓝、受击反击、动态缩放）；
   - Hook Key 统一命名为 `<hero_id>.<feature>`；
   - 如需访问 PathosEngine 内部成员，优先复用或扩展 [PathosEngineBridge.cs](../../Framework/Hooks/PathosEngineBridge.cs)。

机制设计与蓝耗参考见 [DOTA2 → Pathos 机制映射规范](../architecture/design-guidelines.md)。

---

### 第 3 步：在扩展包入口注册英雄

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

### 第 4 步：编译并补全中文词条

1. 运行首次构建，让 `PathosMaker.exe` 自动扫描并生成新增词条占位：
   ```powershell
   dotnet build .\DotaHeroesExpansion.csproj -c Release
   ```
2. 打开 `Assets/DotaHeroesExpansion.zh-Simplified.Dictionary`（以及按需编辑其他语言字典），补全新增技能/职业/天赋的中文名称与描述。
3. 再次运行 `dotnet build .\DotaHeroesExpansion.csproj -c Release`，将翻译打包并部署到游戏目录。

---

### 第 5 步：补充英雄文档（含 7.42 数值映射溯源表）

1. 在 `docs/heroes/` 下新建 `<hero_id>.md`（可参考 [morphling.md](../heroes/morphling.md) 或 [juggernaut.md](../heroes/juggernaut.md)），**必须标明参考版本（如 `DOTA2 7.42`）并附上完整的「DOTA2 7.42 → Pathos 数值映射溯源表」**（包含初始属性、蓝量、蓝耗、持续时间、护甲等原版数值与换算依据），同时记录种族/职业、Hook Key 清单与贴图清单，方便后续版本更新时溯源调参。
2. 在 [docs/heroes/README.md](../heroes/README.md) 与 [docs/README.md](../README.md) 中补充索引链接。
