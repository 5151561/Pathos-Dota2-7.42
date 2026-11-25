# 英雄总览清单

当前已按 **DOTA2 7.42** 机制接入的英雄列表：

| 英雄 | ID | 种族 / 职业 | 先天技能 | 核心特色 | 源码 | 贴图目录 | 详细文档 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **变体精灵（Morphling / 水人）** | `morphling` | `变体水灵 (waterkin)` / `变体精灵 (morphling)` | 潮涨潮落 (`ebb and flow`) | 力敏属性守恒转换、动态血量联动、实时力敏比缩放、变形复制目标 | [MorphlingHero.cs](../../Heroes/MorphlingHero.cs) | `Resources/Heroes/morphling/` | [查看详情](morphling.md) |
| **主宰（Juggernaut / 尤涅若）** | `juggernaut` | 官方 `人类 (human)` / `主宰 (juggernaut)` | 剑心犹在 (`bladeform`) | 脱战叠层移速/攻速、剑舞暴击、剑刃风暴驱散与魔抗、治疗守卫光环、无敌斩瞬移多段斩击 | [JuggernautHero.cs](../../Heroes/JuggernautHero.cs) | `Resources/Heroes/juggernaut/` | [查看详情](juggernaut.md) |
| **斧王（Axe / 蒙哥可汗）** | `axe` | 官方 `兽人 (orc)` / `斧王 (axe)` | 一人之军 (`one man army`) | 独行护甲转力量、狂战士之吼嘲讽与高甲、战斗饥渴纯伤减速、受击（含 Miss）反击螺旋、淘汰之刃处决返蓝与永久叠甲 | [AxeHero.cs](../../Heroes/AxeHero.cs) | `Resources/Heroes/axe/` | [查看详情](axe.md) |
| **帕吉（Pudge / 屠夫）** | `pudge` | 官方 `兽人 (orc)` / `帕吉 (pudge)` | 腐肉堆积 (`flesh heap`) | 肉钩秒杀普通怪物并拖回、腐烂开关毒雾（自伤保底 1 HP）、肉盾逐次格挡（小数概率进位）、肢解引导吸血、英雄级击杀永久叠力量 | [PudgeHero.cs](../../Heroes/PudgeHero.cs) | `Resources/Heroes/pudge/` | [查看详情](pudge.md) |

> 新增英雄请参考 [新增 DOTA2 英雄指南](../guides/adding-a-hero.md)。
