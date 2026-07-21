using System.Net;
using System.Text.RegularExpressions;
using NothingVpn.Domain.Models;

namespace NothingVpn.Domain.Policies;

public static class TunSettingsPolicy
{
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
        if (normalized.Equals("auto", StringComparison.OrdinalIgnoreCase))
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
            return "NothingVpn";

        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    public static string NormalizeAddressCidr(string? cidr)
    {
        var trimmed = (cidr ?? string.Empty).Trim();
        if (trimmed.Length == 0
            || trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("172.19.0.1/30", StringComparison.OrdinalIgnoreCase))
            return "auto";

        return trimmed;
    }

    public static int NormalizeMtu(int mtu)
    {
        if (mtu <= 0 || mtu == LegacyMtuSentinel)
            return DefaultMtu;

        return Math.Clamp(mtu, MinMtu, MaxMtu);
    }

    public static string NormalizeStack(string? stack)
    {
        var s = (stack ?? string.Empty).Trim().ToLowerInvariant();
        return s switch
        {
            "system" => "system",
            "mixed" => "mixed",
            "gvisor" => "gvisor",
            _ => ""
        };
    }

    public static int StackToComboIndex(string? stack) =>
        NormalizeStack(stack) switch
        {
            "mixed" => 1,
            "gvisor" => 2,
            _ => 0
        };

    public static string ComboIndexToStack(int index) => index switch
    {
        1 => "mixed",
        2 => "gvisor",
        _ => ""
    };

    public static string StackToDisplayLabel(string? stack) =>
        NormalizeStack(stack) switch
        {
            "mixed" => "Смешанный",
            "gvisor" => "gVisor",
            _ => "Системный"
        };
}
