namespace NothingVpn.Domain.Models;

/// <summary>
/// Стек TUN. В state.json: "" | system | mixed | gvisor. <see cref="TunStack.Default"/> (пустая строка) — выбор платформы
/// (system на Windows), он хранится отдельно от явного <see cref="TunStack.System"/>.
/// </summary>
public enum TunStack
{
    Default,
    System,
    Mixed,
    Gvisor
}
