namespace NothingVpn.Domain.Policies;

/// <summary>
/// Минимальные требования ядра к профилю и локальным портам. Ядро проверяет их само, не полагаясь на разбор
/// ссылки в интерфейсе: на шаге 7 запрос приходит в службу по каналу.
/// </summary>
public static class VlessEndpointPolicy
{
    public static bool IsValidPort(int port) => port is > 0 and <= 65535;

    public static void EnsureConnectable(string? profileId, string? uuid, string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new InvalidOperationException("Профиль не найден.");
        if (!Guid.TryParse(uuid, out _))
            throw new InvalidOperationException("В профиле неверный UUID.");
        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("В профиле не указан адрес сервера.");
        if (!IsValidPort(port))
            throw new InvalidOperationException("В профиле неверный порт сервера.");
    }

    public static void EnsureLocalPort(int port)
    {
        if (!IsValidPort(port))
            throw new InvalidOperationException($"Неверный порт локального прокси: {port}.");
    }
}
