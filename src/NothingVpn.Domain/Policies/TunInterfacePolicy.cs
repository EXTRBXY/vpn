using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

/// <summary>
/// Значения TUN-интерфейса, производные от Id профиля. Формулы — внешний контракт: адреса и имя интерфейса
/// должны оставаться стабильными между версиями (docs/REFACTORING.md, раздел 1).
/// </summary>
public static class TunInterfacePolicy
{
    public static string InterfaceName(string normalizedBaseName, string profileId) =>
        $"{normalizedBaseName}-{profileId[..Math.Min(6, profileId.Length)]}";

    public static string Ipv4Address(string? addressCidr, string profileId)
    {
        var normalized = TunSettingsPolicy.NormalizeAddressCidr(addressCidr);
        if (normalized != TunSettingsPolicy.AutoAddressCidr)
            return normalized;

        var block = ProfileHash(profileId) % 32768;
        var third = (block >> 6) & 0xFF;
        var fourth = (block & 0x3F) * 4 + 1;
        return $"198.18.{third}.{fourth}/30";
    }

    public static string Ipv6Address(string profileId)
    {
        var hextet = (ProfileHash(profileId) % 0xFFFF).ToString("x4");
        return $"fdfe:dcba:9876:{hextet}::1/126";
    }

    /// <summary>Значение <c>stack</c> для sing-box: <see cref="TunStack.Default"/> означает «по умолчанию для платформы».</summary>
    public static string SingBoxStack(TunStack stack) =>
        TunSettingsPolicy.NormalizeStack(stack) != TunStack.Default
            ? stack.ToValue()
            : OperatingSystem.IsWindows() ? "system" : "mixed";

    private static int ProfileHash(string profileId)
    {
        var n = 0;
        for (var i = 0; i < profileId.Length; i++)
            n = unchecked(n * 31 + profileId[i]);
        return Math.Abs(n);
    }
}
