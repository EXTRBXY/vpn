using System.Net;
using System.Net.Http.Headers;

namespace NothingVpn.Tray.Internal.RuleSets;

internal static class RuleSetRemoteDownloader
{
    internal sealed record Result(bool Ok, bool NotModified, string? NewEtag, string? Error);

    internal static async Task<Result> DownloadAsync(
        string url,
        string destPath,
        string? ifNoneMatch,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = null,
                UseProxy = false
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(3)
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(ifNoneMatch) &&
                EntityTagHeaderValue.TryParse(ifNoneMatch.Trim(), out var etag))
                request.Headers.IfNoneMatch.Add(etag);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return new Result(true, true, ifNoneMatch, null);

            if (!response.IsSuccessStatusCode)
            {
                var err = $"HTTP {(int)response.StatusCode}";
                return new Result(false, false, null, err);
            }

            var dir = Path.GetDirectoryName(destPath);
            if (string.IsNullOrEmpty(dir))
                return new Result(false, false, null, "Invalid destination path.");

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

            var newEtag = response.Headers.ETag?.ToString();
            return new Result(true, false, newEtag, null);
        }
        catch (TaskCanceledException)
        {
            return new Result(false, false, null, "Timeout.");
        }
        catch (Exception ex)
        {
            return new Result(false, false, null, ex.Message);
        }
    }
}
