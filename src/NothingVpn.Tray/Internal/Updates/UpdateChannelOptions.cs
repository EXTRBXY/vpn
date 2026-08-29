namespace NothingVpn.Tray.Internal.Updates;

/// <summary>Параметры канала обновлений (при форке смените owner/repo здесь).</summary>
internal static class UpdateChannelOptions
{
    public const string GitHubOwner = "EXTRBXY";
    public const string GitHubRepo = "vpn";
    public const string InstallerAssetName = "NothingVpnSetup.exe";
    public const string InstallerTempNamePrefix = "NothingVpnSetup-";

    internal static bool IsAcceptedInstallerFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? "");
        if (name.Length == 0) return false;
        if (string.Equals(name, InstallerAssetName, StringComparison.OrdinalIgnoreCase))
            return true;
        return name.StartsWith(InstallerTempNamePrefix, StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }
}
