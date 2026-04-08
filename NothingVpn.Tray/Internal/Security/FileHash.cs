using System.Security.Cryptography;

namespace NothingVpn.Tray.Internal.Security;

internal static class FileHash
{
    public static string Sha256Hex(string path)
    {
        using var fs = File.OpenRead(path);
        var hash = SHA256.HashData(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

