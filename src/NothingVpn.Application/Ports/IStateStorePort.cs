using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IStateStorePort
{
    /// <summary>Читает состояние без записи на диск; недостающие встроенные rule-set добавляются только в модель.</summary>
    AppStateModel Load();

    void Save(AppStateModel state);

    /// <summary>Дописывает в файл недостающие встроенные rule-set. Возвращает true, если файл был перезаписан.</summary>
    bool SaveMissingBuiltinRuleSets();
}
