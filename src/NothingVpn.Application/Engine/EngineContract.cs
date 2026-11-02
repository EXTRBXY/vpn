namespace NothingVpn.Application.Engine;

public static class EngineContract
{
    /// <summary>
    /// Версия контракта <see cref="IVpnEngine"/> и протокола канала службы. Меняется при любом несовместимом
    /// изменении <see cref="EngineConnectRequest"/>, <see cref="EngineStatus"/> или сообщений канала: интерфейс и
    /// служба обновляются не одновременно, и несовпадение версий должно давать понятную ошибку, а не неверный конфиг.
    /// v2 (шаг 7а): rule-set передаются содержимым (<see cref="EngineRuleSet.Content"/>).
    /// v3 (ADR-0002): движок профиля, поля XHTTP и XMUX по умолчанию.
    /// </summary>
    public const int Version = 3;

    public static void EnsureCompatible(int version)
    {
        if (version != Version)
            throw new InvalidOperationException(IncompatibleVersionMessage(version));
    }

    public static string IncompatibleVersionMessage(int version) =>
        $"Несовместимая версия протокола ядра ({version}, ожидается {Version}). Обновите приложение.";
}
