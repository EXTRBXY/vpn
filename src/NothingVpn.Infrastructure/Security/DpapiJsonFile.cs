using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NothingVpn.Infrastructure.Security;

internal static class DpapiJsonFile
{
    private static readonly byte[] Magic = "NV1"u8.ToArray();
    private static readonly ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public static T LoadOrDefault<T>(string path, Func<T> defaultFactory)
        => LoadOrDefault(path, defaultFactory, Protect, Unprotect);

    internal static T LoadOrDefault<T>(
        string path,
        Func<T> defaultFactory,
        Func<byte[], byte[]> protect,
        Func<byte[], byte[]> unprotect)
    {
        var fullPath = Path.GetFullPath(path);
        lock (GetFileLock(fullPath))
        {
            if (!File.Exists(fullPath))
                return LoadBackupOrDefault(fullPath, defaultFactory, primaryError: null, protect, unprotect);

            try
            {
                var value = Deserialize<T>(File.ReadAllBytes(fullPath), unprotect, out var wasLegacy)
                    ?? defaultFactory();
                if (wasLegacy)
                    SaveCore(fullPath, value, protect);
                return value;
            }
            catch (Exception primaryError)
            {
                return LoadBackupOrDefault(fullPath, defaultFactory, primaryError, protect, unprotect);
            }
        }
    }

    public static void Save<T>(string path, T obj)
        => Save(path, obj, Protect);

    internal static void Save<T>(string path, T obj, Func<byte[], byte[]> protect)
    {
        var fullPath = Path.GetFullPath(path);
        lock (GetFileLock(fullPath))
        {
            SaveCore(fullPath, obj, protect);
        }
    }

    private static T LoadBackupOrDefault<T>(
        string path,
        Func<T> defaultFactory,
        Exception? primaryError,
        Func<byte[], byte[]> protect,
        Func<byte[], byte[]> unprotect)
    {
        var backupPath = GetBackupPath(path);
        if (!File.Exists(backupPath))
        {
            if (primaryError is not null)
                Trace.TraceError($"Failed to read DPAPI JSON file '{path}': {primaryError}");
            return defaultFactory();
        }

        try
        {
            var value = Deserialize<T>(File.ReadAllBytes(backupPath), unprotect, out _)
                ?? defaultFactory();
            RestorePrimaryFromBackup(path, backupPath);
            if (primaryError is not null)
                Trace.TraceWarning($"Restored DPAPI JSON file '{path}' from backup after: {primaryError.Message}");
            return value;
        }
        catch (Exception backupError)
        {
            Trace.TraceError(
                $"Failed to read DPAPI JSON file '{path}' and its backup. " +
                $"Primary: {primaryError}; Backup: {backupError}");
            return defaultFactory();
        }
    }

    private static void SaveCore<T>(string path, T obj, Func<byte[], byte[]> protect)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(obj, JsonOptions());
        var plaintext = Encoding.UTF8.GetBytes(json);
        var protectedBytes = protect(plaintext);

        var output = new byte[Magic.Length + protectedBytes.Length];
        Buffer.BlockCopy(Magic, 0, output, 0, Magic.Length);
        Buffer.BlockCopy(protectedBytes, 0, output, Magic.Length, protectedBytes.Length);

        var tempPath = CreateSiblingTempPath(path, "tmp");
        try
        {
            File.WriteAllBytes(tempPath, output);
            if (File.Exists(path))
                File.Replace(tempPath, path, GetBackupPath(path), ignoreMetadataErrors: true);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static T? Deserialize<T>(byte[] data, Func<byte[], byte[]> unprotect, out bool wasLegacy)
    {
        wasLegacy = !LooksLikeEncrypted(data);
        var json = wasLegacy
            ? Encoding.UTF8.GetString(data).TrimStart('\uFEFF')
            : Encoding.UTF8.GetString(unprotect(data[Magic.Length..]));
        return JsonSerializer.Deserialize<T>(json, JsonOptions());
    }

    private static void RestorePrimaryFromBackup(string path, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = CreateSiblingTempPath(path, "restore.tmp");
        try
        {
            File.Copy(backupPath, tempPath, overwrite: true);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static string CreateSiblingTempPath(string path, string suffix) =>
        Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.{suffix}");

    private static string GetBackupPath(string path) => path + ".bak";

    private static object GetFileLock(string path) =>
        FileLocks.GetOrAdd(path, static _ => new object());

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static byte[] Protect(byte[] plaintext) => ProtectedData.Protect(
        plaintext,
        optionalEntropy: null,
        scope: DataProtectionScope.CurrentUser);

    private static byte[] Unprotect(byte[] protectedBytes) => ProtectedData.Unprotect(
        protectedBytes,
        optionalEntropy: null,
        scope: DataProtectionScope.CurrentUser);

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
