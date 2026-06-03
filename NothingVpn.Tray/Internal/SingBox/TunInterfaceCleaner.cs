using System.Diagnostics;
using System.Text.Json;

namespace NothingVpn.Tray.Internal.SingBox;

internal static class TunInterfaceCleaner
{
    internal static void TryDisableAdaptersWithPrefix(string prefix)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -NonInteractive -Command \"Get-NetAdapter -Name '{EscapePsSingleQuoted(prefix)}*' -ErrorAction SilentlyContinue | Disable-NetAdapter -Confirm:$false -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(3000);
        }
        catch
        {
            // best-effort
        }
    }

    internal static void TryDisableAdapter(string interfaceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -NonInteractive -Command \"Disable-NetAdapter -Name '{EscapePsSingleQuoted(interfaceName)}' -Confirm:$false -ErrorAction SilentlyContinue\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(2000);
        }
        catch
        {
            // best-effort
        }
    }

    internal static string? TryReadInterfaceName(string configPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!doc.RootElement.TryGetProperty("inbounds", out var inbounds))
                return null;

            foreach (var inbound in inbounds.EnumerateArray())
            {
                if (!inbound.TryGetProperty("type", out var typeProp))
                    continue;
                if (!string.Equals(typeProp.GetString(), "tun", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (inbound.TryGetProperty("interface_name", out var nameProp))
                    return nameProp.GetString();
            }
        }
        catch
        {
            // best-effort
        }

        return null;
    }

    private static string EscapePsSingleQuoted(string value) =>
        (value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal);
}
