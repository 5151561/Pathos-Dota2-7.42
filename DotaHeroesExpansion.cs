using System.Collections.Generic;
using Pathos.DotaHeroes.Framework;
using Pathos.DotaHeroes.Framework.Hooks;
using Pathos.DotaHeroes.Heroes;

namespace Pathos
{
  /// <summary>
  /// DOTA2 英雄扩展包总入口。
  /// Pathos 引擎启动时会自动扫描 Expansions/*Expansion.dll 并实例化本类。
  /// </summary>
  public sealed class DotaHeroesExpansion : Expansion
  {
    public DotaHeroesExpansion(OfficialCampaign Campaign)
      : base(Campaign, Handle: "dotaheroes", Name: "DOTA2 Heroes")
    {
      // 初始化 Harmony Hook 运行时底座（在 PathosMaker 打包期会自动跳过）
      HookBootstrap.EnsureInitialized();

      var Context = new DotaHeroContext(Campaign);

      var HeroRegistry = new List<IDotaHeroDefinition>
      {
        new MorphlingHero(),
        new JuggernautHero(),
        new AxeHero(),
        new PudgeHero(),
        // 后续新增 DOTA2 英雄只需在此追加，例如：
        // new AntiMageHero(),
        // new InvokerHero(),
      };

      foreach (var Hero in HeroRegistry)
      {
        Hero.Register(Context);
        Hero.RegisterHooks(Context);
      }
    }
  }
}
