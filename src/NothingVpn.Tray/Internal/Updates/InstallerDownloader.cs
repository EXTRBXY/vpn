using NothingVpn.Domain.Updates;

namespace NothingVpn.Tray.Internal.Updates;

internal readonly record struct InstallerDownloadProgress(long BytesReceived, long? TotalBytes);

internal static class InstallerDownloader
{
    internal sealed record Result(bool Ok, string? Error);

    internal static Task<Result> DownloadAsync(
        string downloadUrl,
        string destPath,
        CancellationToken cancellationToken = default,
        IProgress<InstallerDownloadProgress>? progress = null) =>
        DownloadCoreAsync(downloadUrl, destPath, cancellationToken, progress);

    private static async Task<Result> DownloadCoreAsync(
        string downloadUrl,
        string destPath,
        CancellationToken cancellationToken,
        IProgress<InstallerDownloadProgress>? progress)
    {
        try
        {
            InstallerDownloadUrlValidator.EnsureValid(downloadUrl, UpdateChannelOptions.InstallerAssetName);

            using var handler = new HttpClientHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NothingVpn-InstallerDownload/1");

            using var response = await client
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new Result(false, $"HTTP {(int)response.StatusCode}");

            if (response.RequestMessage?.RequestUri is { } finalUri)
            {
                try
                {
                    // After CDN redirect the path may be opaque; still require https + allowlisted host.
                    InstallerDownloadUrlValidator.EnsureValid(
                        finalUri.AbsoluteUri,
                        UpdateChannelOptions.InstallerAssetName,
                        requireAssetFileName: false);
                }
                catch (ArgumentException ex)
                {
                    return new Result(false, ex.Message);
                }
            }

            long? totalBytes = null;
            if (response.Content.Headers.ContentLength is { } len && len >= 0)
                totalBytes = len;

            var dir = Path.GetDirectoryName(destPath);
            if (string.IsNullOrEmpty(dir))
                return new Result(false, "Invalid destination path.");

            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, $".{Path.GetFileName(destPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    progress?.Report(new InstallerDownloadProgress(0, totalBytes));
                    var buffer = new byte[81920];
                    long readTotal = 0;
                    while (true)
                    {
                        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                            break;
                        await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        readTotal += read;
                        progress?.Report(new InstallerDownloadProgress(readTotal, totalBytes));
                    }
                }

                File.Move(temp, destPath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch
                {
                    // ignore
                }
            }

            return new Result(true, null);
        }
        catch (ArgumentException ex)
        {
            return new Result(false, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new Result(false, "Загрузка отменена.");
        }
        catch (OperationCanceledException)
        {
            return new Result(false, "Превышено время ожидания.");
        }
        catch (Exception ex)
        {
            return new Result(false, ex.Message);
        }
    }
}
