using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Tests;

public sealed class BuiltinRuleSetUpdateServiceTests
{
    [Fact]
    public async Task RefreshDueAsync_SkippedWhenDisabledOrNotDue()
    {
        var files = new FakeRuleSetFiles();
        var settings = new FakeSettings(new AppStateModel
        {
            BuiltinRuleSetsAutoUpdate = false,
            UserRuleSets =
            [
                new()
                {
                    Tag = "geosite-category-ru",
                    Name = "RU",
                    FileName = "geosite-category-ru.srs",
                    BuiltinId = "sing-geosite:category-ru",
                    Enabled = true
                }
            ]
        });
        var service = new BuiltinRuleSetUpdateService(settings, files, new FakeLog());

        await service.RefreshDueAsync();
        Assert.Empty(files.Downloads);

        settings.State.BuiltinRuleSetsAutoUpdate = true;
        settings.State.BuiltinRuleSetsLastAutoUpdateUtc = DateTimeOffset.UtcNow;
        await service.RefreshDueAsync();
        Assert.Empty(files.Downloads);
    }

    [Fact]
    public async Task RefreshDueAsync_DownloadsEnabledOrExistingAndUpdatesState()
    {
        var nowBefore = DateTimeOffset.UtcNow;
        var files = new FakeRuleSetFiles();
        files.Existing.Add("geoip-ru.srs");
        var settings = new FakeSettings(new AppStateModel
        {
            BuiltinRuleSetsAutoUpdate = true,
            BuiltinRuleSetsUpdateIntervalHours = 24,
            BuiltinRuleSetsLastAutoUpdateUtc = null,
            UserRuleSets =
            [
                new()
                {
                    Tag = "geosite-category-ru",
                    Name = "RU sites",
                    FileName = "geosite-category-ru.srs",
                    BuiltinId = "sing-geosite:category-ru",
                    Enabled = true
                },
                new()
                {
                    Tag = "geoip-ru",
                    Name = "RU IP",
                    FileName = "geoip-ru.srs",
                    BuiltinId = "sing-geoip:ru",
                    Enabled = false,
                    RemoteEtag = "\"old\""
                },
                new()
                {
                    Tag = "geosite-category-ru-ads",
                    Name = "Ads",
                    FileName = "geosite-category-ru@ads.srs",
                    BuiltinId = "sing-geosite:category-ru-ads",
                    Enabled = false
                },
                new()
                {
                    Tag = "user-list",
                    Name = "User",
                    FileName = "user.srs",
                    Enabled = true
                }
            ]
        });
        files.Results["geosite-category-ru.srs"] = new RuleSetDownloadResult(true, false, "\"new-1\"", null);
        files.Results["geoip-ru.srs"] = new RuleSetDownloadResult(true, true, "\"old\"", null);

        var service = new BuiltinRuleSetUpdateService(settings, files, new FakeLog());
        await service.RefreshDueAsync();

        Assert.Equal(["geosite-category-ru.srs", "geoip-ru.srs"], files.Downloads);
        Assert.DoesNotContain("geosite-category-ru@ads.srs", files.Downloads);
        Assert.DoesNotContain("user.srs", files.Downloads);

        var ru = Assert.Single(settings.State.UserRuleSets, x => x.BuiltinId == "sing-geosite:category-ru");
        Assert.Equal("\"new-1\"", ru.RemoteEtag);
        Assert.NotNull(ru.LastDownloadedUtc);
        Assert.True(ru.LastDownloadedUtc >= nowBefore);

        var geoip = Assert.Single(settings.State.UserRuleSets, x => x.BuiltinId == "sing-geoip:ru");
        Assert.Equal("\"old\"", geoip.RemoteEtag);
        Assert.NotNull(settings.State.BuiltinRuleSetsLastAutoUpdateUtc);
        Assert.True(settings.State.BuiltinRuleSetsLastAutoUpdateUtc >= nowBefore);
    }

    [Fact]
    public async Task RefreshDueAsync_WithNoTargets_StillMarksLastCheck()
    {
        var settings = new FakeSettings(new AppStateModel
        {
            BuiltinRuleSetsAutoUpdate = true,
            UserRuleSets = []
        });
        var service = new BuiltinRuleSetUpdateService(settings, new FakeRuleSetFiles(), new FakeLog());
        await service.RefreshDueAsync();
        Assert.NotNull(settings.State.BuiltinRuleSetsLastAutoUpdateUtc);
    }

    [Fact]
    public void AppStateNormalizer_ClampsBuiltinInterval()
    {
        var state = new AppStateModel { BuiltinRuleSetsUpdateIntervalHours = 0 };
        AppStateNormalizer.Normalize(state, new FakePathPolicy());
        Assert.Equal(BuiltinRuleSetSyncPolicy.DefaultUpdateIntervalHours, state.BuiltinRuleSetsUpdateIntervalHours);
    }

    private sealed class FakeSettings(AppStateModel state) : ISettingsService
    {
        public AppStateModel State { get; } = state;
        public event EventHandler<AppStateModel>? StateChanged;
        public AppStateModel GetState() => State;
        public void UpdateState(Action<AppStateModel> mutate)
        {
            mutate(State);
            AppStateNormalizer.Normalize(State, new FakePathPolicy());
            StateChanged?.Invoke(this, State);
        }
        public void UpdateMode(ConnectionMode mode) => throw new NotSupportedException();
        public void UpdateDns(DnsMode mode, string dohServer, string dohPath, string dohSni, DnsDetour detour) =>
            throw new NotSupportedException();
        public void UpdateTunSettings(TunSettings settings) => throw new NotSupportedException();
        public void UpdateProxySettings(ProxyConnectionSettings settings) => throw new NotSupportedException();
        public void UpdateRuleSets(IReadOnlyCollection<UserRuleSetModel> ruleSets) => throw new NotSupportedException();
        public void UpdateTunApps(IReadOnlyCollection<string> paths) => throw new NotSupportedException();
    }

    private sealed class FakeRuleSetFiles : IRuleSetFileService
    {
        public string CatalogUrl => "https://example.test";
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Downloads { get; } = [];
        public Dictionary<string, RuleSetDownloadResult> Results { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Exists(UserRuleSetModel ruleSet) => Existing.Contains(ruleSet.FileName);
        public RuleSetImportResult Import(string sourcePath) => throw new NotSupportedException();
        public void Delete(UserRuleSetModel ruleSet) { }

        public Task<RuleSetDownloadResult> DownloadBuiltinAsync(
            UserRuleSetModel ruleSet,
            bool useConditionalRequest,
            CancellationToken cancellationToken = default)
        {
            Downloads.Add(ruleSet.FileName);
            if (!Results.TryGetValue(ruleSet.FileName, out var result))
                result = new RuleSetDownloadResult(true, false, "\"etag\"", null);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeLog : ILogPort
    {
        public int Version => 0;
        public string SnapshotText(LogLevel minLevel) => string.Empty;
        public string? TryGetLatestMessage(LogLevel minLevel) => null;
        public void Append(LogLevel level, string source, string message) { }
        public void Clear() { }
    }

    private sealed class FakePathPolicy : IPathPolicyPort
    {
        public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths) =>
            (paths ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();

        public bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
        {
            normalizedPath = rawPath ?? string.Empty;
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }
    }
}
