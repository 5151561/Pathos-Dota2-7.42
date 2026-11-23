namespace Pathos.DotaHeroes.Framework
{
  /// <summary>
  /// DOTA2 英雄扩展定义接口。
  /// 每新增一名英雄，只需在 Heroes/ 目录下实现本接口并在 DotaHeroesExpansion 中注册即可。
  /// </summary>
  public interface IDotaHeroDefinition
  {
    /// <summary>
    /// 英雄唯一标识（英文小写，用于内部日志与归类，如 "morphling"）。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 将该英雄的贴图、种族、职业、法术、装备及预设英雄注册到 Pathos Campaign 中。
    /// </summary>
    void Register(DotaHeroContext Context);

    /// <summary>
    /// 可选：向 DotaHookBus 注册该英雄的运行时 Harmony Hook 逻辑（幂等按键注册）。
    /// </summary>
    void RegisterHooks(DotaHeroContext Context) { }
  }
}
