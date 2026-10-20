using System.Text.Json;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
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
            s.CloseBehavior = AppCloseBehavior.Exit;
            s.ProxyWasEnabledByUs = false;
            s.PreviousProxySettings!.ProxyServer = "tampered";
        });

        var saved = port.Snapshot();
        Assert.Equal("exit", saved.CloseBehavior.ToValue());
        Assert.True(saved.ProxyWasEnabledByUs);
        Assert.Equal("corp:3128", saved.PreviousProxySettings?.ProxyServer);
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
}
