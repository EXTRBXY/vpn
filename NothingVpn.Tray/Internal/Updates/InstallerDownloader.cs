namespace NothingVpn.Tray.Internal.Updates;

internal static class InstallerDownloader
{
    internal sealed record Result(bool Ok, string? Error);

    internal static async Task<Result> DownloadAsync(string downloadUrl, string destPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new HttpClientHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NothingVpn-InstallerDownload/1");

            using var response = await client
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new Result(false, $"HTTP {(int)response.StatusCode}");

            var dir = Path.GetDirectoryName(destPath);
            if (string.IsNullOrEmpty(dir))
                return new Result(false, "Invalid destination path.");

            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, $".{Path.GetFileName(destPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);

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
        catch (TaskCanceledException)
        {
            return new Result(false, "Timeout.");
        }
        catch (Exception ex)
        {
            return new Result(false, ex.Message);
        }
    }
}
