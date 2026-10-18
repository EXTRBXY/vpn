using System.Text.Json;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Application.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task UpdateState_ConcurrentFieldUpdates_AreAllPersisted()
    {
        var port = new MemoryStatePort();
        var settings = new SettingsService(new AppStateStore(port), new FakePathPolicyPort());
        var paths = Enumerable.Range(0, 50).Select(i => $@"C:\Apps\app{i}.exe").ToList();

        await Task.WhenAll(paths.Select(path => Task.Run(() =>
            settings.UpdateState(s => s.TunAppProcessPaths = s.TunAppProcessPaths.Append(path).ToList()))));

        Assert.Equal(paths.Order(), port.Snapshot().TunAppProcessPaths.Order());
    }

    [Fact]
    public void UpdateState_CannotChangeConnectionRuntimeFlags()
    {
        var port = new MemoryStatePort();
        port.Save(new AppStateModel
        {
            ProxyWasEnabledByUs = true,
            PreviousProxySettings = new ProxySettingsSnapshotModel { ProxyEnable = true, ProxyServer = "corp:3128" }
        });
        var settings = new SettingsService(new AppStateStore(port), new FakePathPolicyPort());

        settings.UpdateState(s =>
        {
            s.CloseBehavior = "exit";
            s.ProxyWasEnabledByUs = false;
            s.PreviousProxySettings!.ProxyServer = "tampered";
        });

        var saved = port.Snapshot();
        Assert.Equal("exit", saved.CloseBehavior);
        Assert.True(saved.ProxyWasEnabledByUs);
        Assert.Equal("corp:3128", saved.PreviousProxySettings?.ProxyServer);
    }

    [Fact]
    public void ScreenSave_WithStaleSnapshot_KeepsSelectionUpdateMetadataAndRuntimeFlags()
    {
        var port = new MemoryStatePort();
        var settings = new SettingsService(new AppStateStore(port), new FakePathPolicyPort());
        var stale = settings.GetState();
        port.Save(new AppStateModel
        {
            ActiveProfileId = "p2",
            ProxyWasEnabledByUs = true,
            PreviousProxySettings = new ProxySettingsSnapshotModel(),
            UpdateDismissedModalForTag = "v9.9.9",
            LastRecordedAppSemver = "9.9.9"
        });
        var controller = new ConnectionScreenController(new NoProfiles(), settings);

        stale.DohServer = "1.1.1.1";
        controller.Save(stale);

        var saved = port.Snapshot();
        Assert.Equal("1.1.1.1", saved.DohServer);
        Assert.Equal("p2", saved.ActiveProfileId);
        Assert.True(saved.ProxyWasEnabledByUs);
        Assert.NotNull(saved.PreviousProxySettings);
        Assert.Equal("v9.9.9", saved.UpdateDismissedModalForTag);
        Assert.Equal("9.9.9", saved.LastRecordedAppSemver);
    }

    [Fact]
    public void RuleSetMarkDownloaded_UpdatesOnlyThatRuleSetInLatestState()
    {
        var port = new MemoryStatePort();
        port.Save(new AppStateModel
        {
            UserRuleSets = [new() { Tag = "builtin", BuiltinId = "catalog", FileName = "b.srs", Name = "B" }]
        });
        var settings = new SettingsService(new AppStateStore(port), new FakePathPolicyPort());
        var stale = settings.GetState();
        settings.UpdateState(s => s.CloseBehavior = "exit");
        var controller = new RuleSetManagementController(settings);

        controller.MarkDownloaded(stale, stale.UserRuleSets[0], " \"etag\" ");

        var saved = port.Snapshot();
        Assert.Equal("exit", saved.CloseBehavior);
        Assert.Equal("\"etag\"", saved.UserRuleSets[0].RemoteEtag);
        Assert.NotNull(saved.UserRuleSets[0].LastDownloadedUtc);
    }

    private sealed class MemoryStatePort : IStateStorePort
    {
        private string _json = JsonSerializer.Serialize(new AppStateModel());

        public AppStateModel Load() => Snapshot();
        public void Save(AppStateModel state) => _json = JsonSerializer.Serialize(state);
        public bool SaveMissingBuiltinRuleSets() => false;
        public AppStateModel Snapshot() => JsonSerializer.Deserialize<AppStateModel>(_json)!;
    }

    private sealed class FakePathPolicyPort : IPathPolicyPort
    {
        public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths) =>
            (paths ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
        {
            normalizedPath = rawPath ?? string.Empty;
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }
    }

    private sealed class NoProfiles : IProfileService
    {
        public IReadOnlyList<VpnProfile> GetProfiles() => [];
        public IReadOnlyList<VpnProfile> ImportFromVlessLink(string link) => throw new NotSupportedException();
        public IReadOnlyList<VpnProfile> DeleteProfile(string profileId) => throw new NotSupportedException();
        public bool TryParseVlessLink(string link, out VpnProfile profile) => throw new NotSupportedException();
        public VpnProfile UpsertFromVlessLink(string link, string? nameOverride) => throw new NotSupportedException();
    }
}
