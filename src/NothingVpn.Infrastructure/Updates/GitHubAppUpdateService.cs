using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NothingVpn.Application.Models;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Updates;

namespace NothingVpn.Infrastructure.Updates;

public sealed class GitHubAppUpdateService : IAppUpdateService
{
    private const string Owner = "EXTRBXY";
    private const string Repository = "vpn";
    private const string InstallerAssetName = "NothingVpnSetup.exe";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task<AppReleaseModel?> GetLatestAsync(string currentVersion, CancellationToken cancellationToken = default) =>
        GetAsync("latest", currentVersion, cancellationToken);

    public Task<AppReleaseModel?> GetByVersionAsync(string version, CancellationToken cancellationToken = default) =>
        GetAsync($"tags/{Uri.EscapeDataString(SemanticVersionPolicy.ToGitTag(version))}", version, cancellationToken);

    private static async Task<AppReleaseModel?> GetAsync(
        string releasePath,
        string currentVersion,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { Proxy = null, UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NothingVpn/{SemanticVersionPolicy.Normalize(currentVersion)}");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var url = $"https://api.github.com/repos/{Owner}/{Repository}/releases/{releasePath}";
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<ReleaseDto>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        return Map(dto);
    }

    private static AppReleaseModel? Map(ReleaseDto? dto)
    {
        var semver = SemanticVersionPolicy.Normalize(dto?.TagName);
        if (dto is null || semver.Length == 0)
            return null;
        var installer = dto.Assets?.FirstOrDefault(asset =>
            string.Equals(asset.Name?.Trim(), InstallerAssetName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(installer?.BrowserDownloadUrl))
            return null;
        return new AppReleaseModel(dto.TagName!.Trim(), semver, dto.Body, installer.BrowserDownloadUrl.Trim());
    }

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
