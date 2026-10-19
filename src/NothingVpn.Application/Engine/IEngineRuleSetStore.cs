namespace NothingVpn.Application.Engine;

/// <summary>
/// Откуда ядро берёт файлы rule-set запроса. Внутри процесса — общий каталог rulesets пользователя
/// (<see cref="ReferencedRuleSetStore"/>), в службе — её собственный каталог, куда пишется переданное содержимое.
/// </summary>
public interface IEngineRuleSetStore
{
    /// <summary>
    /// Готовит файлы и возвращает их простые имена в каталоге rule-set ядра — по одному на запись, в том же порядке.
    /// Вызывается под локом ядра, когда sing-box остановлен.
    /// </summary>
    IReadOnlyList<string> Prepare(IReadOnlyList<EngineRuleSet> ruleSets);

    /// <summary>Убирает файлы прошлой сессии. Вызывается при остановке ядра.</summary>
    void Clear();
}

/// <summary>Rule-set по ссылке: файл с именем из запроса уже лежит в общем каталоге rulesets пользователя.</summary>
public sealed class ReferencedRuleSetStore : IEngineRuleSetStore
{
    public static ReferencedRuleSetStore Instance { get; } = new();

    public IReadOnlyList<string> Prepare(IReadOnlyList<EngineRuleSet> ruleSets) =>
        ruleSets.Select(rs => rs.FileName).ToList();

    public void Clear()
    {
    }
}
