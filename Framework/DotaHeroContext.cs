using System;
using System.Collections.Generic;

namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// 封装 OfficialCampaign 与 Codex 常用注册入口及辅助方法，降低新增 DOTA2 英雄的样板代码量。
  /// </summary>
  public sealed class DotaHeroContext
  {
    public DotaHeroContext(OfficialCampaign Campaign)
    {
      this.Campaign = Campaign ?? throw new ArgumentNullException(nameof(Campaign));
    }

    public OfficialCampaign Campaign { get; }
    public Codex Codex => Campaign.Codex;

    public CodexAttributes Attributes => Codex.Attributes;
    public CodexAnatomies Anatomies => Codex.Anatomies;
    public CodexAttackTypes AttackTypes => Codex.AttackTypes;
    public CodexBeams Beams => Codex.Beams;
    public CodexClasses Classes => Codex.Classes;
    public CodexDiets Diets => Codex.Diets;
    public CodexElements Elements => Codex.Elements;
    public CodexEntities Entities => Codex.Entities;
    public CodexExplosions Explosions => Codex.Explosions;
    public CodexFeatures Features => Codex.Features;
    public CodexGenders Genders => Codex.Genders;
    public CodexGlyphs Glyphs => Codex.Glyphs;
    public CodexGrades Grades => Codex.Grades;
    public CodexHeroes Heroes => Codex.Heroes;
    public CodexItems Items => Codex.Items;
    public CodexKinds Kinds => Codex.Kinds;
    public CodexMaterials Materials => Codex.Materials;
    public CodexMotions Motions => Codex.Motions;
    public CodexProperties Properties => Codex.Properties;
    public CodexQualifications Qualifications => Codex.Qualifications;
    public CodexRaces Races => Codex.Races;
    public CodexSchools Schools => Codex.Schools;
    public CodexSkills Skills => Codex.Skills;
    public CodexSonics Sonics => Codex.Sonics;
    public CodexSpecials Specials => Codex.Specials;
    public CodexSpells Spells => Codex.Spells;
    public CodexStocks Stocks => Codex.Stocks;

    private readonly Dictionary<string, Glyph> GlyphCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册并缓存一个贴图（对应 Atlases/&lt;tileset&gt;/&lt;Name&gt;.png）。
    /// </summary>
    public Glyph RegisterGlyph(string Name)
    {
      if (GlyphCache.TryGetValue(Name, out var Existing))
        return Existing;

      var Created = Glyphs.Add(Name);
      GlyphCache[Name] = Created;
      return Created;
    }

    /// <summary>
    /// 为职业注册全性别头像（若只有一个通用形态，可传入同一个 Glyph）。
    /// </summary>
    public void AddClassAvatars(ClassEditor Editor, Glyph MaleGlyph, Glyph FemaleGlyph = null, Glyph NonBinaryGlyph = null)
    {
      var Fallback = MaleGlyph;
      Editor.AddAvatar(Genders.male, MaleGlyph);
      Editor.AddAvatar(Genders.female, FemaleGlyph ?? Fallback);
      Editor.AddAvatar(Genders.nonbinary, NonBinaryGlyph ?? Fallback);
    }

    /// <summary>
    /// 为种族注册全性别头像。
    /// </summary>
    public void AddRaceAvatars(RaceEditor Editor, Glyph MaleGlyph, Glyph FemaleGlyph = null, Glyph NonBinaryGlyph = null)
    {
      var Fallback = MaleGlyph;
      Editor.AddAvatar(Genders.male, MaleGlyph);
      Editor.AddAvatar(Genders.female, FemaleGlyph ?? Fallback);
      Editor.AddAvatar(Genders.nonbinary, NonBinaryGlyph ?? Fallback);
    }

    /// <summary>
    /// 快捷设置实体六维属性（STR/DEX/CON/INT/WIS/CHA）。
    /// </summary>
    public void SetFormScores(FormEditor Form, int Str, int Dex, int Con, int Int, int Wis, int Cha)
    {
      Form.Initialise(Attributes.List);
      Form.SetScore(Attributes.strength, Str);
      Form.SetScore(Attributes.dexterity, Dex);
      Form.SetScore(Attributes.constitution, Con);
      Form.SetScore(Attributes.intelligence, Int);
      Form.SetScore(Attributes.wisdom, Wis);
      Form.SetScore(Attributes.charisma, Cha);
    }
  }
}
