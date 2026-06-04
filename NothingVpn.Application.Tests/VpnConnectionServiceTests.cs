using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;

namespace NothingVpn.Application.Tests;

public sealed class VpnConnectionServiceTests
{
    [Fact]
    public async Task ConnectAsync_ReturnsElevation_WhenTunAndNotAdmin()
    {
        var profileStore = new FakeProfileStorePort();
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = "tun",
                ActiveProfileId = "p1"
            }
        };
        var service = CreateService(profileStore, stateStore, isAdmin: false);

        var result = await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });

        Assert.False(result.Started);
        Assert.True(result.RequiresElevation);
        Assert.Contains("--takeover", result.ElevationArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_StartsAndSavesState_WhenProxyMode()
    {
        var profileStore = new FakeProfileStorePort();
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = "proxy",
                LocalMixedPort = 1080
            }
        };
        var singBox = new FakeSingBoxPort();
        var proxy = new FakeProxyPort();
        var service = CreateService(profileStore, stateStore, isAdmin: true, singBoxPort: singBox, proxyPort: proxy);

        var result = await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });

        Assert.True(result.Started);
        Assert.True(singBox.StartCalled);
        Assert.True(proxy.EnableCalled);
        Assert.Equal("p1", stateStore.State.ActiveProfileId);
        Assert.True(stateStore.State.ProxyWasEnabledByUs);
    }

    [Fact]
    public async Task DisconnectAsync_ClearsActiveProfileAndProxyFlags()
    {
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = "proxy",
                ActiveProfileId = "p1",
                ProxyWasEnabledByUs = true,
                PreviousProxySettings = new ProxySettingsSnapshotModel()
            }
        };
        var proxy = new FakeProxyPort();
        var singBox = new FakeSingBoxPort { IsRunning = true };
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, singBoxPort: singBox, proxyPort: proxy);

        await service.DisconnectAsync();

        Assert.False(singBox.IsRunning);
        Assert.True(proxy.RestoreCalled);
        Assert.Null(stateStore.State.ActiveProfileId);
        Assert.False(stateStore.State.ProxyWasEnabledByUs);
    }

    [Fact]
    public async Task ConnectAsync_StopsExistingSession_BeforeStart()
    {
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = "proxy", LocalMixedPort = 1080 } };
        var singBox = new FakeSingBoxPort { IsRunning = true };
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, singBoxPort: singBox);

        await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });

        Assert.True(singBox.StopCalled);
        Assert.True(singBox.StartCalled);
    }

    [Fact]
    public void RecoverStaleRuntimeState_ClearsStaleSession()
    {
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                ActiveProfileId = "p1",
                ProxyWasEnabledByUs = true,
                PreviousProxySettings = new ProxySettingsSnapshotModel()
            }
        };
        var proxy = new FakeProxyPort();
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, proxyPort: proxy);

        service.RecoverStaleRuntimeState();

        Assert.True(proxy.RestoreCalled);
        Assert.Null(stateStore.State.ActiveProfileId);
        Assert.False(stateStore.State.ProxyWasEnabledByUs);
    }

    [Fact]
    public async Task ConnectAsync_ThrowsAndRollsBack_WhenSingBoxExitsDuringConnect()
    {
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = "proxy", LocalMixedPort = 1080 } };
        var singBox = new FakeSingBoxPort { ExitOnStart = true };
        var proxy = new FakeProxyPort();
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, singBoxPort: singBox, proxyPort: proxy);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConnectAsync(new ConnectRequest { ProfileId = "p1" }));

        Assert.Null(stateStore.State.ActiveProfileId);
        Assert.False(stateStore.State.ProxyWasEnabledByUs);
        Assert.False(proxy.EnableCalled);
    }

    [Fact]
    public async Task ConnectAsync_IgnoresProcessExitedRecoverWhileInProgress()
    {
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = "proxy",
                LocalMixedPort = 1080,
                ProxyWasEnabledByUs = true,
                PreviousProxySettings = new ProxySettingsSnapshotModel()
            }
        };
        var singBox = new FakeSingBoxPort { FireExitedOnStart = true };
        var proxy = new FakeProxyPort();
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, singBoxPort: singBox, proxyPort: proxy);

        var result = await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });

        Assert.True(result.Started);
        Assert.True(proxy.EnableCalled);
        Assert.Equal("p1", stateStore.State.ActiveProfileId);
        Assert.True(stateStore.State.ProxyWasEnabledByUs);
    }

    private static VpnConnectionService CreateService(
        FakeProfileStorePort profileStore,
        FakeStateStorePort stateStore,
        bool isAdmin,
        FakeSingBoxPort? singBoxPort = null,
        FakeProxyPort? proxyPort = null,
        FakeDiagnosticsPort? diagnosticsPort = null)
    {
        return new VpnConnectionService(
            profileStore,
            stateStore,
            singBoxPort ?? new FakeSingBoxPort(),
            proxyPort ?? new FakeProxyPort(),
            diagnosticsPort ?? new FakeDiagnosticsPort(),
            new FakeElevationPort(isAdmin),
            new FakeAppPathsPort(),
            new FakePathPolicyPort(),
            new FakeLogPort());
    }

    private sealed class FakeProfileStorePort : IProfileStorePort
    {
        private readonly List<VpnProfile> _profiles =
        [
            new() { Id = "p1", Name = "Main", Host = "example.org", Port = 443, Uuid = Guid.NewGuid().ToString() }
        ];

        public IReadOnlyList<VpnProfile> Load() => _profiles;
        public IReadOnlyList<VpnProfile> Upsert(VpnProfile profile) => _profiles;
        public IReadOnlyList<VpnProfile> Delete(string profileId)
        {
            _profiles.RemoveAll(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
            return _profiles;
        }

        public ProfileSyncResult SyncForSubscription(string subscriptionId, IReadOnlyList<VpnProfile> profilesFromSubscription)
        {
            foreach (var profile in profilesFromSubscription)
                Upsert(profile);
            return new ProfileSyncResult { Profiles = _profiles };
        }

        public IReadOnlyList<VpnProfile> DeleteBySubscription(string subscriptionId)
        {
            _profiles.RemoveAll(p => string.Equals(p.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            return _profiles;
        }
    }

    private sealed class FakeStateStorePort : IStateStorePort
    {
        public AppStateModel State { get; set; } = new();
        public AppStateModel Load() => State;
        public void Save(AppStateModel state) => State = state;
    }

    private sealed class FakeSingBoxPort : ISingBoxPort
    {
        public event EventHandler? ProcessExited;
        public bool IsRunning { get; set; }
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public bool ExitOnStart { get; init; }
        public bool FireExitedOnStart { get; init; }

        public string WriteConfig(VpnProfile profile, AppStateModel state) => "config.json";

        public void Start(string configPath)
        {
            StartCalled = true;
            if (ExitOnStart)
            {
                IsRunning = false;
                return;
            }

            IsRunning = true;
            if (FireExitedOnStart)
            {
                IsRunning = true;
                ProcessExited?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Stop()
        {
            StopCalled = true;
            IsRunning = false;
        }
    }

    private sealed class FakeProxyPort : IProxyPort
    {
        public bool RestoreCalled { get; private set; }
        public bool EnableCalled { get; private set; }

        public ProxySettingsSnapshotModel ReadCurrent() => new();
        public void Enable(string proxyServer, string proxyOverride) => EnableCalled = true;
        public void Restore(ProxySettingsSnapshotModel? previous) => RestoreCalled = true;
    }

    private sealed class FakeDiagnosticsPort : IDiagnosticsPort
    {
        public Task<(bool Success, string? Error)> CanReachTcpAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));

        public Task<(bool Success, string? Error)> ProxySmokeTestAsync(string proxyHost, int proxyPort, string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));

        public Task<(bool Success, string? Error)> TunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));
    }

    private sealed class FakeElevationPort(bool isAdmin) : IElevationPort
    {
        public bool IsAdministrator() => isAdmin;
        public bool RestartElevated(string arguments) => true;
    }

    private sealed class FakeAppPathsPort : IAppPathsPort
    {
        public AppPathsModel Get() => new() { RuleSetsDir = "rulesets" };
    }

    private sealed class FakePathPolicyPort : IPathPolicyPort
    {
        public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths)
            => (paths ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        public bool TryNormalizeExePath(string? rawPath, out string normalizedPath)
        {
            normalizedPath = rawPath ?? string.Empty;
            return !string.IsNullOrWhiteSpace(normalizedPath);
        }
    }

    private sealed class FakeLogPort : ILogPort
    {
        public string SnapshotText(int minLevel) => string.Empty;
        public string? TryGetLatestMessage(int minLevel) => "FATAL test";
    }
}
