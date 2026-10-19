namespace NothingVpn.Application.Engine;

public static class EngineContract
{
    /// <summary>
    /// Версия контракта <see cref="IVpnEngine"/>. Меняется при любом несовместимом изменении
    /// <see cref="EngineConnectRequest"/> или <see cref="EngineStatus"/>: на шаге 7 интерфейс и служба обновляются
    /// не одновременно, и несовпадение версий должно давать понятную ошибку, а не неверный конфиг.
    /// </summary>
    public const int Version = 1;

    public static void EnsureCompatible(int version)
    {
        if (version != Version)
            throw new InvalidOperationException(
                $"Несовместимая версия протокола ядра ({version}, ожидается {Version}). Обновите приложение.");
    }
}
