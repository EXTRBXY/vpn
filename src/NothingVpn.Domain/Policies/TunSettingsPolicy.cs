using System.Net;
using System.Text.RegularExpressions;
using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class TunSettingsPolicy
{
    public const string DefaultInterfaceName = "NothingVpn";
    public const string AutoAddressCidr = "auto";
    public const TunStack DefaultStack = TunStack.Default;
    public const int DefaultMtu = 1500;
    public const int LegacyMtuSentinel = 9000;
    public const int MinMtu = 576;
    public const int MaxMtu = 9000;

    private static readonly Regex InvalidInterfaceChars = new(@"[\\/:*?""<>|]", RegexOptions.Compiled);

    public static void Normalize(TunSettings settings)
    {
        settings.InterfaceName = NormalizeInterfaceName(settings.InterfaceName);
        settings.AddressCidr = NormalizeAddressCidr(settings.AddressCidr);
        settings.Mtu = NormalizeMtu(settings.Mtu);
        settings.Stack = NormalizeStack(settings.Stack);
    }

    public static void Validate(TunSettings settings)
    {
        Normalize(settings);

        if (!IsValidAddressCidr(settings.AddressCidr))
            throw new ArgumentException($"Некорректный TUN CIDR: {settings.AddressCidr}");
    }

    public static bool IsValidAddressCidr(string? cidr)
    {
        var normalized = NormalizeAddressCidr(cidr);
        if (normalized == AutoAddressCidr)
            return true;

        var parts = normalized.Split('/', 2);
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;

        if (!int.TryParse(parts[1], out var prefix) || prefix is < 1 or > 32)
            return false;

        return true;
    }

    public static string NormalizeInterfaceName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0 || InvalidInterfaceChars.IsMatch(trimmed))
            return DefaultInterfaceName;

        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    public static string NormalizeAddressCidr(string? cidr)
    {
        var trimmed = (cidr ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("172.19.0.1/30", StringComparison.OrdinalIgnoreCase))
            return AutoAddressCidr;

        return trimmed;
    }

    public static int NormalizeMtu(int mtu)
    {
        if (mtu <= 0 || mtu == LegacyMtuSentinel)
            return DefaultMtu;

        return Math.Clamp(mtu, MinMtu, MaxMtu);
    }

    public static readonly SettingValueMap<TunStack> Stacks = new(
        DefaultStack,
        new Dictionary<TunStack, string>
        {
            [TunStack.Default] = "",
            [TunStack.System] = "system",
            [TunStack.Mixed] = "mixed",
            [TunStack.Gvisor] = "gvisor"
        });

    public static TunStack NormalizeStack(TunStack stack) => Stacks.Normalize(stack);
}
