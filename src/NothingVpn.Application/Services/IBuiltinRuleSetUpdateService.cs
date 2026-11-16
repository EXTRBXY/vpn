namespace NothingVpn.Application.Services;

public interface IBuiltinRuleSetUpdateService
{
    /// <summary>
    /// Если автообновление включено и интервал истёк — условно скачивает встроенные списки
    /// (включённые или уже лежащие на диске) и обновляет метаданные в state.json.
    /// </summary>
    Task RefreshDueAsync(CancellationToken cancellationToken = default);
}
