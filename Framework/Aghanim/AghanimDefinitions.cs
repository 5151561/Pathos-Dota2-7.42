using System;
using System.Collections.Generic;

namespace Pathos.DotaHeroes.Framework.Aghanim
{
  /// <summary>
  /// 单个阿哈利姆升级（神杖或魔晶）的描述与回调定义。
  /// </summary>
  public sealed class AghanimUpgradeSpec
  {
    /// <summary>受该升级强化的主动法术名（可选，用于在技能帮助面板自动追加神杖/魔晶区块）。</summary>
    public string AffectedSpellName { get; init; }

    /// <summary>受该升级强化的被动/先天帮助 ID（可选，用于在被动帮助面板自动追加神杖/魔晶区块）。</summary>
    public string AffectedPassiveId { get; init; }

    /// <summary>受影响技能的图标（为空时回退神杖/魔晶自身图标）。</summary>
    public Glyph IconGlyph { get; init; }

    /// <summary>升级标题（如「反击螺旋 · 神杖升级」）。</summary>
    public Func<string> TitleResolver { get; init; }

    /// <summary>简短摘要（显示在 F2 面板悬停卡片正文顶部）。</summary>
    public Func<string> SummaryResolver { get; init; }

    /// <summary>详细数值与机制条目（参数 Character 在百科中可能为 null）。</summary>
    public Func<Character, IReadOnlyList<string>> BuildDetailBullets { get; init; }

    /// <summary>角色首次获得或永久吸收该升级时的回调（可选）。</summary>
    public Action<object, Character> OnGranted { get; init; }

    public string Title => TitleResolver?.Invoke() ?? string.Empty;
    public string Summary => SummaryResolver?.Invoke() ?? string.Empty;
  }

  /// <summary>
  /// 单个 DOTA2 英雄的阿哈利姆神杖与魔晶完整定义。
  /// </summary>
  public sealed class HeroAghanimSpec
  {
    public string ClassName { get; init; }
    public AghanimUpgradeSpec Scepter { get; init; }
    public AghanimUpgradeSpec Shard { get; init; }
  }
}
