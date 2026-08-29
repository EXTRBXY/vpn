using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Infrastructure.Updates;

public sealed class InstallerUpdateService : IInstallerUpdateService
{
    private const string InstallerAssetName = "NothingVpnSetup.exe";
    private const string CachePrefix = "NothingVpnSetup-";

    public string GetCachedInstallerPath(string version)
    {
        var normalized = SemanticVersionPolicy.Normalize(version);
        if (normalized.Length == 0)
            throw new ArgumentException("Некорректная версия обновления.", nameof(version));
        var directory = Path.Combine(Path.GetTempPath(), "NothingVpn");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{CachePrefix}{normalized}.exe");
    }

    public bool IsCached(string version) => File.Exists(GetCachedInstallerPath(version));

    public void CleanupOldInstallers()
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "NothingVpn");
            if (!Directory.Exists(directory))
                return;
            foreach (var path in Directory.EnumerateFiles(directory, $"{CachePrefix}*.exe", SearchOption.TopDirectoryOnly))
            {
                try { File.Delete(path); }
                catch { }
            }
        }
        catch { }
    }

    public async Task<InstallerDownloadResult> DownloadAsync(
        AppReleaseModel release,
        IProgress<InstallerDownloadProgressModel>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = GetCachedInstallerPath(release.Semver);
        try
        {
            InstallerDownloadUrlValidator.EnsureValid(release.InstallerDownloadUrl, InstallerAssetName);
            using var handler = new HttpClientHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NothingVpn-InstallerDownload/1");
            using var response = await client.GetAsync(
                release.InstallerDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new InstallerDownloadResult(false, $"HTTP {(int)response.StatusCode}");

            if (response.RequestMessage?.RequestUri is { } finalUri)
                InstallerDownloadUrlValidator.EnsureValid(finalUri.AbsoluteUri, InstallerAssetName, requireAssetFileName: false);

            long? totalBytes = response.Content.Headers.ContentLength is { } length and >= 0 ? length : null;
            var directory = Path.GetDirectoryName(destination)!;
            var temp = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                progress?.Report(new InstallerDownloadProgressModel(0, totalBytes));
                var buffer = new byte[81920];
                long received = 0;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new InstallerDownloadProgressModel(received, totalBytes));
                }
                file.Close();
                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
            return new InstallerDownloadResult(true, null, destination);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new InstallerDownloadResult(false, "Загрузка отменена.");
        }
        catch (OperationCanceledException)
        {
            return new InstallerDownloadResult(false, "Превышено время ожидания.");
        }
        catch (Exception ex)
        {
            return new InstallerDownloadResult(false, ex.Message);
        }
    }
}
