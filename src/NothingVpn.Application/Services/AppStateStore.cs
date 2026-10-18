using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;

namespace NothingVpn.Application.Services;

/// <summary>
/// Единственная точка доступа к state.json внутри процесса. Все записи — чтение-изменение-запись под одним локом,
/// поэтому параллельные изменения разных полей (настройки из UI, флаги подключения, метаданные обновлений)
/// не теряют друг друга. Экземпляр должен быть один на процесс (создаётся в composition root).
/// </summary>
public sealed class AppStateStore(IStateStorePort port)
{
    private readonly object _sync = new();

    public AppStateModel Load()
    {
        lock (_sync) return port.Load();
    }

    /// <summary>Загружает актуальное состояние, применяет изменение и сохраняет. Возвращает сохранённый объект.</summary>
    public AppStateModel Update(Action<AppStateModel> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_sync)
        {
            var state = port.Load();
            update(state);
            port.Save(state);
            return state;
        }
    }

    /// <summary>Явная миграция при старте: встроенные rule-set, которых нет в файле, дописываются на диск.</summary>
    public bool SaveMissingBuiltinRuleSets()
    {
        lock (_sync) return port.SaveMissingBuiltinRuleSets();
    }
}
