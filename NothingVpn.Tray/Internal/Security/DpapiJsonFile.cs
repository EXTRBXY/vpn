using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NothingVpn.Tray.Internal.Security;

internal static class DpapiJsonFile
{
    private static readonly byte[] Magic = "NV1"u8.ToArray();

    public static T LoadOrDefault<T>(string path, Func<T> defaultFactory)
    {
        try
        {
            if (!File.Exists(path)) return defaultFactory();

            var data = File.ReadAllBytes(path);
            if (LooksLikeEncrypted(data))
            {
                var unprotected = ProtectedData.Unprotect(data[Magic.Length..], optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(unprotected);
                return JsonSerializer.Deserialize<T>(json, JsonOptions()) ?? defaultFactory();
            }

            // Legacy plaintext JSON
            var legacyJson = Encoding.UTF8.GetString(data);
            var obj = JsonSerializer.Deserialize<T>(legacyJson, JsonOptions()) ?? defaultFactory();

            // Migrate to encrypted format (best-effort).
            try { Save(path, obj); } catch { }
            return obj;
        }
        catch
        {
            return defaultFactory();
        }
    }

    public static void Save<T>(string path, T obj)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(obj, JsonOptions());
        var plaintext = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plaintext, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

        var output = new byte[Magic.Length + protectedBytes.Length];
        Buffer.BlockCopy(Magic, 0, output, 0, Magic.Length);
        Buffer.BlockCopy(protectedBytes, 0, output, Magic.Length, protectedBytes.Length);
        File.WriteAllBytes(path, output);
    }

    private static bool LooksLikeEncrypted(byte[] data)
    {
        if (data.Length < Magic.Length + 16) return false;
        for (var i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) return false;
        return true;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

