using System;
using System.Collections.Generic;
using Pathos.DotaHeroes.Framework.UI;

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
    public CodexStrikes Strikes => Codex.Strikes;

    private readonly Dictionary<string, Glyph> GlyphCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册并缓存一个贴图（源文件位于 Resources/Heroes/&lt;hero&gt;/&lt;tileset&gt;/&lt;Name&gt;.png，构建期自动聚合到 Atlases/）。
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

    /// <summary>
    /// 注册「长效技能 / 先天技能 / 被动专长」Property：
    /// 会通过 Class.AddFeat 展示在角色原生「天赋」栏与游戏内百科「天赋/特性」栏目中。
    /// </summary>
    public Property RegisterPassiveProperty(string Name, string Description, Glyph Icon)
    {
      return Properties.Add(P =>
      {
        P.Name = Name;
        P.Description = Description;
        P.Glyph = Icon;
      });
    }

    /// <summary>
    /// 注册「短期主动技能状态（Transient-only）」Property（如剑刃风暴、无敌斩）：
    /// 自动向 DotaPropertyVisibility 登记，避免在原生 TalentPanel（含大师模式）与百科「天赋/特性」中被误展示为永久天赋。
    /// </summary>
    public Property RegisterSkillStatusProperty(string Name, string Description, Glyph Icon)
    {
      var Prop = Properties.Add(P =>
      {
        P.Name = Name;
        P.Description = Description;
        P.Glyph = Icon;
      });
      DotaPropertyVisibility.RegisterTransientOnlyProperty(Prop);
      return Prop;
    }

    /// <summary>
    /// 注册单格自定义像素打击/斩击特效（配合 PathosEngineBridge.StoryStriking 使用）。
    /// </summary>
    public Strike RegisterStrikeVfx(string Name, string GlyphName)
    {
      var Glyph = RegisterGlyph(GlyphName);
      return Strikes.Add(S =>
      {
        S.Name = Name;
        S.Glyph = Glyph;
      });
    }

    /// <summary>
    /// 注册 4 方向自定义光束/残影特效（配合 PathosEngineBridge.StoryBeaming / DotaCombatHelper.PlayBeamTrail 使用）。
    /// </summary>
    public Beam RegisterBeamVfx(
      string Name,
      string HorizontalGlyphName,
      string VerticalGlyphName,
      string ForwardSlantGlyphName,
      string BackwardSlantGlyphName)
    {
      var H = RegisterGlyph(HorizontalGlyphName);
      var V = RegisterGlyph(VerticalGlyphName);
      var F = RegisterGlyph(ForwardSlantGlyphName);
      var B = RegisterGlyph(BackwardSlantGlyphName);

      return Beams.Add(Bm =>
      {
        Bm.Name = Name;
        Bm.HorizontalGlyph = H;
        Bm.VerticalGlyph = V;
        Bm.ForwardSlantGlyph = F;
        Bm.BackwardSlantGlyph = B;
      });
    }

    /// <summary>
    /// 注册 3×3 九宫格自定义范围爆炸特效（贴图命名约定：&lt;GlyphPrefix&gt; tl/tc/tr/ml/mc/mr/bl/bc/br）。
    /// 配合 PathosEngineBridge.StoryExploding 使用。
    /// </summary>
    public Explosion RegisterExplosion3x3Vfx(string Name, string GlyphPrefix)
    {
      var Tl = RegisterGlyph($"{GlyphPrefix} tl");
      var Tc = RegisterGlyph($"{GlyphPrefix} tc");
      var Tr = RegisterGlyph($"{GlyphPrefix} tr");
      var Ml = RegisterGlyph($"{GlyphPrefix} ml");
      var Mc = RegisterGlyph($"{GlyphPrefix} mc");
      var Mr = RegisterGlyph($"{GlyphPrefix} mr");
      var Bl = RegisterGlyph($"{GlyphPrefix} bl");
      var Bc = RegisterGlyph($"{GlyphPrefix} bc");
      var Br = RegisterGlyph($"{GlyphPrefix} br");

      return Explosions.Add(E =>
      {
        E.Name = Name;
        E.SetGlyph(ExplosionArea.TopLeft, Tl);
        E.SetGlyph(ExplosionArea.TopCenter, Tc);
        E.SetGlyph(ExplosionArea.TopRight, Tr);
        E.SetGlyph(ExplosionArea.MiddleLeft, Ml);
        E.SetGlyph(ExplosionArea.MiddleCenter, Mc);
        E.SetGlyph(ExplosionArea.MiddleRight, Mr);
        E.SetGlyph(ExplosionArea.BottomLeft, Bl);
        E.SetGlyph(ExplosionArea.BottomCenter, Bc);
        E.SetGlyph(ExplosionArea.BottomRight, Br);
      });
    }

    /// <summary>
    /// 注册预设英雄（选人界面一键开局）。
    /// </summary>
    public Hero RegisterPresetHero(string Name, Race Race, Class HeroClass, Gender Gender, Glyph CustomGlyph)
    {
      return Heroes.Add(H =>
      {
        H.Name = Name;
        H.Race = Race;
        H.Class = HeroClass;
        H.Gender = Gender;
        H.CustomGlyph = CustomGlyph;
      });
    }
  }
}
