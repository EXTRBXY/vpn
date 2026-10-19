namespace NothingVpn.Application.Engine;

/// <summary>
/// Граница «интерфейс ↔ ядро» (docs/adr/0001-privileged-service.md): единственный путь запуска sing-box.
/// Реализации: клиент именованного канала службы (<c>Infrastructure/ServiceChannel/PipeVpnEngine</c>, по умолчанию
/// с шага 7б) и <see cref="SingBoxVpnEngine"/> внутри процесса (разработка, тесты и само ядро службы). Поэтому вход и выход — сериализуемые типы контракта,
/// а не модели хранения.
/// <para>
/// Журнал ядра попадает в <see cref="Ports.ILogPort"/> интерфейса: внутри процесса — общим хранилищем, через канал —
/// клиент записывает полученные строки (служба маскирует их сама). Отдельной подписки на журнал в порте нет.
/// </para>
/// <para>
/// Системный прокси WinINET, state.json и UAC — не забота ядра: это делает оркестрация на стороне интерфейса
/// (<see cref="Services.VpnConnectionService"/>).
/// </para>
/// </summary>
public interface IVpnEngine
{
    /// <summary>
    /// Ядро остановилось или запустилось. Поднимается и при неожиданном выходе sing-box; во время собственных
    /// <see cref="ConnectAsync"/>/<see cref="DisconnectAsync"/> выход процесса отдельным событием не сообщается.
    /// </summary>
    event EventHandler<EngineStatus>? StatusChanged;

    /// <summary>
    /// TUN требует прав администратора у процесса, который вызывает ядро: sing-box запускается в нём самом. У ядра
    /// в службе — нет, TUN поднимает служба (ADR-0001), и интерфейсу UAC-перезапуск не нужен.
    /// </summary>
    bool TunRequiresElevatedCaller { get; }

    /// <summary>
    /// Валидирует запрос Domain-политиками, строит конфиг, проверяет и запускает sing-box. Уже работающая сессия
    /// останавливается. При ошибке ядро остановлено, временный конфиг удалён, исключение несёт текст для
    /// пользователя.
    /// </summary>
    Task<EngineStatus> ConnectAsync(EngineConnectRequest request, CancellationToken cancellationToken = default);

    /// <summary>Останавливает sing-box и удаляет временный конфиг. Без сессии ничего не делает.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Дешёвый статус без обращения к диску.</summary>
    EngineStatus GetStatus();
}
