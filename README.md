# Pathos — DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

面向 **Pathos: NetHack Codex**（Windows / Steam / Android）的可扩展 **DOTA2 英雄扩展包框架**。

## 首批英雄：变体精灵（Morphling / 水人）

- **专属种族**：`变体水灵 (waterkin)`
  - 水元素之躯：天生水系与寒冰抗性、湿滑（Slippery）、精通游泳、水上通行；弱点为电系（Shock）。
  - 力量与敏捷上限均提升至 `24`，为属性转换提供充足空间。
- **专属职业**：`变体精灵 (morphling)`
  - Lv 4 水系抗性；**Lv 6 自动解锁大招：变形 + 变形控制（`polymorph` + `polymorph_control`）**；Lv 8 水雾幻影（Displacement）；Lv 12 澄澈心境（Clarity）。
- **专属技能（开局法术书自带基础 4 技能）**：
  - **Q — 波浪形态 (`waveform`, 6 蓝)**：闪烁位移 + 短暂相位穿梭（Phasing）。
  - **W — 变体攻击 (`adaptive strike`, 5 蓝)**：高压水波爆炸，造成水系伤害 + 击退 + 概率眩晕。
  - **E1 — 属性转换·敏捷 (`morph agility`, 2 蓝)**：将力量转化为敏捷，并获得短暂迅捷（Quickness）。
  - **E2 — 属性转换·力量 (`morph strength`, 2 蓝)**：将敏捷转化为力量，并立即回复少量生命值。
  - **R — 变形（Lv 6 自动解锁）**：升至 6 级自动获得可控变形天赋，可精准化身为地牢生物。

## DOTA2 → Pathos 技能蓝耗映射规范

| DOTA2 技能定位 | 建议 Pathos 蓝耗 | 说明 |
| :--- | :---: | :--- |
| 开关 / 切换类（如水人 E） | **1 ~ 2 蓝** | 随时可切，通过属性代价或克制收益控平衡 |
| 常规小技能（Q / W） | **4 ~ 7 蓝** | 1 级 15~20 蓝时可流畅打出 2~3 次技能连招 |
| 高爆发 / 强控小技能 | **8 ~ 10 蓝** | 1 级可用 1~2 次，中后期作为主力输出/控制 |
| 大招（R） | **Lv 6 专长解锁 / 20~30 蓝** | 还原 DOTA2 6 级学大招的成长节奏 |
- **预设英雄**：选人界面可直接选择预设英雄 **变体精灵 (Morphling)** 一键开局，也可将 `变体水灵` 种族或 `变体精灵` 职业与其他官方种族/职业自由混搭。

---

## 目录结构

```text
e:\program\pathos-mod\
├── DotaHeroesExpansion.csproj   # .NET 10 工程文件，编译后自动调用 PathosMaker.exe 打包并部署
├── DotaHeroesExpansion.cs       # 扩展包总入口（英雄注册清单）
├── Framework/
│   ├── IDotaHeroDefinition.cs   # 英雄定义接口
│   └── DotaHeroContext.cs       # Campaign/Codex 快捷封装与通用辅助方法
├── Heroes/
│   └── MorphlingHero.cs         # 水人完整定义（贴图、技能、种族、职业、预设英雄）
├── Atlases/
│   ├── absurd/                  # 128x128 高清奇幻像素风贴图
│   ├── sonyvanda/               # 128x128 柔和像素风贴图
│   ├── classic/                 # 128x128 复古 NetHack 像素风贴图
│   └── geoduck/                 # 128x128 极简像素风贴图
└── Assets/                      # PathosMaker.exe 生成的 Atlas 与多语言 Dictionary
```

---

## 编译与运行

在项目根目录执行：

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

`PathosMaker.exe` 会自动完成：
1. 校验并加载 `DotaHeroesExpansion.dll`
2. 打包 `Atlases/` 下的四套 128x128 图集
3. 打包 `Assets/DotaHeroesExpansion.*.Dictionary` 多语言翻译
4. 自动部署成品到 `E:\Steam\steamapps\common\Pathos\Expansions\`

完成后直接从 Steam 启动 **Pathos**，进入新游戏（如 `NetHack Codex`）即可在选人界面看到 **变体精灵（Morphling）**。

---

## 如何新增下一个 DOTA2 英雄（例如敌法师、卡尔、斧王）

1. **准备贴图**：在 `Atlases/absurd/`、`sonyvanda/`、`classic/`、`geoduck/` 中放入 `128x128` 透明背景 PNG（如 `antimage.png`、`mana break.png`）。
2. **编写英雄类**：在 `Heroes/` 下新建类（如 `AntiMageHero.cs`）实现 `IDotaHeroDefinition`，参考 `Heroes/MorphlingHero.cs` 注册贴图、技能、种族/职业和预设英雄。
3. **注册英雄**：在 `DotaHeroesExpansion.cs` 的 `HeroRegistry` 列表中追加 `new AntiMageHero()`。
4. **编译与翻译**：运行一次 `dotnet build -c Release`，在 `Assets/DotaHeroesExpansion.zh-Simplified.Dictionary` 中补全自动生成的新增中文词条，再次 `dotnet build -c Release` 即可生效。
