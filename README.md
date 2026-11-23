# Pathos — DOTA2 Heroes Expansion (`DotaHeroesExpansion`)

面向 **Pathos: NetHack Codex**（Windows / macOS / Linux 桌面端）的可扩展 **DOTA2 英雄扩展包框架**。

采用 **「声明式外壳 + Harmony Hook 核心逻辑」混合架构**：
- **声明式（官方 Codex API）**：负责贴图（Atlas）、多语言词条（Dictionary）、种族/职业/法术/装备与预设英雄注册。
- **运行时 Hook（单 DLL 内嵌 `Lib.Harmony 2.4.3`）**：通过统一事件总线 `DotaHookBus` 忠实还原原生引擎无法声明式实现的 DOTA2 机制（属性守恒、动态力敏比缩放、先天技能实时加成、伤害/状态/击杀触发等），且打包期自动跳过 `PathosMaker`。

---

## 首批英雄：变体精灵（Morphling / 水人，按 DOTA2 7.42 机制还原）

- **专属种族**：`变体水灵 (waterkin)`
  - 水元素之躯：天生水系与寒冰抗性、湿滑（Slippery）、精通游泳、水上通行；弱点为电系（Shock）。
  - 生态交互：遇水回血回蓝、遇火蒸发少量法力；饮用水药水 / 喷泉汲取可恢复生命与法力。
  - 力 / 敏 / 体上限调整为 `26 / 30 / 26`，为属性转换留出充足空间；开局配备无限远程弹药 **潮汐水弹 (`tidal orb`)**。
- **专属职业**：`变体精灵 (morphling)`
  - **Lv 1 先天技能 — 潮涨潮落 (`ebb and flow`)**（天赋栏常驻显示，变形期间持续保留）：
    - **敏捷（DEX）**：每高于 10 点敏捷提升 `+2%` 移动速度（上限 `+50%`），每 5 点额外敏捷提供 `+1` 格远程攻击距离。
    - **力量（STR）**：每 5 点额外力量提供 `+1` 格法术施法距离（含变形后窃取的法术），并提供减速抗性（高力量 `STR ≥ 24` 直接免疫减速）。
  - **等级专长**：Lv 4 水系抗性；Lv 6 变形控制（`polymorph_control`）；Lv 8 水雾幻影（`displacement`）；Lv 12 澄澈心境（`clarity`）。
- **专属技能（开局法术书自带全套技能）**：
  - **Q — 波浪形态 (`waveform`, 6 蓝)**：闪烁突进 + 落地水波 AOE 伤害 + 短暂穿墙与幻影闪避（`phasing` + `displacement`）。
  - **W — 变体打击 (`adaptive strike`, 5 蓝)**：单技能合一——基础水伤 + 击退 + 眩晕；Hook 按实时力敏比动态结算，敏捷 ≥ 1.5×力量时达最高爆发伤害倍率，力量 ≥ 1.5×敏捷时达最长眩晕时长。
  - **E1 / E2 — 属性转换·敏捷 / 力量 (`morph agility` / `morph strength`, 2 蓝)**：Hook 全权接管，按变化系熟练度每次转换 `1 ~ 5` 点，同步修改 `CurrentForm` 与 `MaximumForm`（总量守恒、卡住上下限，复活或恢复属性药水不再白嫖）；转力同步提升最大生命与当前生命，转敏仅降低最大生命值而不额外扣血。
  - **R1 / R2 — 变形 / 解除变形 (`morph`, 10 蓝 / `morph revert`, 2 蓝)**：指向目标直接化身复制其外形、属性、技能与法术，并可随时切回本体，期间「潮涨潮落」持续保留。
- **预设英雄**：选人界面可直接选择预设英雄 **变体精灵 (Morphling)** 一键开局，也可将 `变体水灵` 种族或 `变体精灵` 职业与其他官方种族/职业自由混搭。

---

## 新增英雄：主宰（Juggernaut / 尤涅若，按 DOTA2 7.42 机制还原）

- **种族与职业**：官方 `人类 (human)` 种族 + 专属职业 `主宰 (juggernaut)`（开局配备 `+1 武士刀 (katana)`、皮甲、回复药水及全套技能书）。
- **Lv 1 先天技能 — 剑心犹在 (`bladeform`)**：
  - 每连续 2 回合未受到伤害叠加 1 层（上限 10 层）；
  - 每层提供 `+1%` 移动速度（满层 `+10%`）与 `+2.5%` 等效基础敏捷加成（攻击速度与近战/无敌斩额外伤害，满层 `+25%`）；
  - 受到伤害后保留 **2 回合粘滞期** 再消散；无敌斩期间因全程无敌可稳定吃满层数。
- **Lv 1 被动专长 — 剑舞 (`blade dance`)**：
  - 近战普攻、无敌斩斩击与剑刃风暴均有 **`20% ~ 35%`** 概率（随中刃熟练度成长）触发 **`1.8×`** 致命一击。
- **等级专长**：Lv 4 自由行动（`free_action`）；Lv 8 幻影闪避（`displacement`）；Lv 12 迅捷（`quickness`）。
- **专属主动技能（开局法术书自带）**：
  - **Q — 剑刃风暴 (`blade fury`, 6 蓝)**：立即基础驱散自身负面状态并舞起持续 4~6 回合的剑刃风暴；期间免疫负面状态、获得 80% 魔法/元素减伤，每回合行动自动对周围 3×3 敌人造成魔法伤害（可触发剑舞暴击），且普通物理普攻不造成常规伤害。
  - **W — 治疗守卫 (`healing ward`, 7 蓝)**：召唤 1 HP 可移动友方随从「治疗守卫」（同时上限 1 个，持续 8~16 回合），每回合为周围 2 格友方回复 `4% ~ 8%` 最大生命值。
  - **R — 无敌斩 (`omnislash`, 14 蓝)**：突进并在周围 3 格敌方目标间连续瞬移发动 `5 ~ 12` 次武器物理斩击，全程免疫一切伤害，每次斩击均可触发「剑舞」暴击与「剑心犹在」加成（射程内无目标时自动返还法力）。
- **预设英雄**：选人界面可直接选择预设英雄 **主宰 (Juggernaut)** 一键开局。

---

## DOTA2 → Pathos 技能蓝耗映射规范

| DOTA2 技能定位 | 建议 Pathos 蓝耗 | 说明 |
| :--- | :---: | :--- |
| 开关 / 切换类（如水人 E） | **1 ~ 2 蓝** | 随时可切，通过属性代价或克制收益控平衡 |
| 常规小技能（Q / W） | **4 ~ 7 蓝** | 1 级 15~20 蓝时可流畅打出 2~3 次技能连招 |
| 高爆发 / 强控小技能 | **8 ~ 10 蓝** | 1 级可用 1~2 次，中后期作为主力输出/控制 |
| 大招（R） | **10 ~ 30 蓝 / Lv 6 专长** | 还原 DOTA2 核心大招节奏 |

---

## 目录结构

```text
pathos-mod/
├── DotaHeroesExpansion.csproj   # 跨平台 .NET 10 工程文件（内嵌 0Harmony.dll，编译后自动调用 PathosMaker）
├── LocalPathos.props.example    # 本地安装路径配置模板（复制为 LocalPathos.props 使用，不入库）
├── DotaHeroesExpansion.cs       # 扩展包总入口（初始化 Hook 底座 + 英雄注册清单）
├── Framework/
│   ├── IDotaHeroDefinition.cs   # 英雄定义接口（Register 声明式注册 + 可选 RegisterHooks 运行时钩子）
│   ├── DotaHeroContext.cs       # Campaign/Codex 快捷封装与通用辅助方法
│   └── Hooks/
│       ├── HookBootstrap.cs     # Harmony 引导器（内嵌 DLL 解析、跳过 PathosMaker、统一挂载引擎方法）
│       ├── DotaHookBus.cs       # 幂等按键事件总线（施法、伤害、击杀、复活、移速、射程、施法距离、状态）
│       ├── HookEventArgs.cs     # 强类型 Hook 事件上下文定义
│       └── PathosEngineBridge.cs# PathosEngine 内部成员反射缓存与安全操作封装
├── Heroes/
│   ├── MorphlingHero.cs         # 变体精灵完整定义（DOTA2 7.42 声明式配置 + Hook 逻辑）
│   └── JuggernautHero.cs        # 主宰完整定义（DOTA2 7.42 声明式配置 + Hook 逻辑）
├── Atlases/
│   ├── absurd/                  # 128x128 高清奇幻像素风贴图
│   ├── sonyvanda/               # 128x128 柔和像素风贴图
│   ├── classic/                 # 128x128 复古 NetHack 像素风贴图
│   └── geoduck/                 # 128x128 极简像素风贴图
└── Assets/                      # PathosMaker.exe 生成的 Atlas 与多语言 Dictionary
```

---

## 编译与运行

1. 首次克隆仓库后，复制 `LocalPathos.props.example` 为 `LocalPathos.props`（已被 `.gitignore` 忽略），将其中的 `<PathosInstallation>` 修改为你本机的 Pathos 安装目录（例如 Steam 目录下的 `steamapps\common\Pathos`）；或直接设置环境变量 `PATHOS_INSTALL_DIR`。
2. 在项目根目录执行：

```powershell
dotnet build .\DotaHeroesExpansion.csproj -c Release
```

`PathosMaker.exe` 会自动完成：
1. 校验并加载 `DotaHeroesExpansion.dll`（打包期自动跳过 Harmony 补丁）
2. 打包 `Atlases/` 下的四套 128x128 图集
3. 打包 `Assets/DotaHeroesExpansion.*.Dictionary` 多语言翻译
4. 自动部署单 DLL 成品与资源到 `<Pathos安装目录>\Expansions\`

> 调试诊断：默认静默无日志；如需排查 Hook 加载情况，可在启动游戏前设置环境变量 `DOTA_HEROES_HOOK_DEBUG=1`，日志会输出到系统临时目录的 `DotaHeroesHook.log`。

---

## 如何新增下一个 DOTA2 英雄（例如敌法师、卡尔、斧王）

1. **准备贴图**：在 `Atlases/absurd/`、`sonyvanda/`、`classic/`、`geoduck/` 中放入 `128x128` 透明背景 PNG（如 `antimage.png`、`mana break.png`）。
2. **编写英雄类**：在 `Heroes/` 下新建类（如 `AntiMageHero.cs`）实现 `IDotaHeroDefinition`：
   - 在 `Register(DotaHeroContext)` 中注册贴图、技能外壳、种族/职业与预设英雄；
   - 如需复杂机制（法球削蓝、受击反击、动态缩放等），覆写 `RegisterHooks(DotaHeroContext)` 订阅 `DotaHookBus` 事件即可。
3. **注册英雄**：在 `DotaHeroesExpansion.cs` 的 `HeroRegistry` 列表中追加 `new AntiMageHero()`。
4. **编译与翻译**：运行一次 `dotnet build -c Release`，在 `Assets/DotaHeroesExpansion.zh-Simplified.Dictionary` 中补全自动生成的新增中文词条，再次 `dotnet build -c Release` 即可生效。
