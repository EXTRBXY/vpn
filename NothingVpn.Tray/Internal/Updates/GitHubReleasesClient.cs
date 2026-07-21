using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NothingVpn.Tray.Internal.Updates;

internal sealed class GitHubReleasesClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public GitHubReleasesClient(string userAgent)
    {
        var handler = new HttpClientHandler
        {
            Proxy = null,
            UseProxy = false
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public void Dispose() => _http.Dispose();

    public async Task<GitHubReleaseInfo?> GetLatestAsync(string owner, string repo, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest";
        return await GetReleaseCoreAsync(url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitHubReleaseInfo?> GetByTagAsync(string owner, string repo, string tag, CancellationToken cancellationToken = default)
    {
        var enc = Uri.EscapeDataString(tag);
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/tags/{enc}";
        return await GetReleaseCoreAsync(url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GitHubReleaseInfo?> GetReleaseCoreAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return Map(dto);
    }

    private static GitHubReleaseInfo? Map(GitHubReleaseDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.TagName))
            return null;
        if (!SemVerComparer.TryParse(dto.TagName, out _, out _, out _))
            return null;
        var semver = SemVerComparer.NormalizeToString(dto.TagName);
        var url = FindInstallerUrl(dto.Assets);
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return new GitHubReleaseInfo(dto.TagName.Trim(), semver, dto.Body, url);
    }

    private static string? FindInstallerUrl(List<GitHubAssetDto>? assets)
    {
        if (assets is null) return null;
        foreach (var a in assets)
        {
            if (a is null || string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.BrowserDownloadUrl))
                continue;
            if (string.Equals(a.Name.Trim(), UpdateChannelOptions.InstallerAssetName, StringComparison.OrdinalIgnoreCase))
                return a.BrowserDownloadUrl.Trim();
        }

        return null;
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
