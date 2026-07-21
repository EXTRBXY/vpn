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
        var service = CreateService(profileStore, stateStore, isAdmin: true, singBoxPort: singBox);

        var result = await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });

        Assert.True(result.Started);
        Assert.True(singBox.StartCalled);
        Assert.Equal("p1", stateStore.State.ActiveProfileId);
    }

    [Fact]
    public async Task ProcessExited_CleansConfigAndRestoresProxy()
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

        await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });
        Assert.True(stateStore.State.ProxyWasEnabledByUs);

        var disconnected = false;
        service.ConnectionStateChanged += (_, connected) =>
        {
            if (!connected) disconnected = true;
        };

        singBox.RaiseProcessExited();

        Assert.True(singBox.DeleteConfigCalled);
        Assert.True(proxy.RestoreCalled);
        Assert.False(stateStore.State.ProxyWasEnabledByUs);
        Assert.True(disconnected);
    }

    private static VpnConnectionService CreateService(
        FakeProfileStorePort profileStore,
        FakeStateStorePort stateStore,
        bool isAdmin,
        FakeSingBoxPort? singBoxPort = null,
        FakeProxyPort? proxyPort = null)
    {
        return new VpnConnectionService(
            profileStore,
            stateStore,
            singBoxPort ?? new FakeSingBoxPort(),
            proxyPort ?? new FakeProxyPort(),
            new FakeDiagnosticsPort(),
            new FakeElevationPort(isAdmin),
            new FakeAppPathsPort(),
            new FakePathPolicyPort());
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
        private EventHandler? _processExited;
        public event EventHandler? ProcessExited
        {
            add => _processExited += value;
            remove => _processExited -= value;
        }

        public bool IsRunning { get; private set; }
        public bool StartCalled { get; private set; }
        public bool DeleteConfigCalled { get; private set; }
        public string WriteConfig(VpnProfile profile, AppStateModel state) => "config.json";
        public void Start(string configPath)
        {
            StartCalled = true;
            IsRunning = true;
        }
        public void Stop() => IsRunning = false;
        public void TryDeleteLastConfig() => DeleteConfigCalled = true;
        public void RaiseProcessExited()
        {
            IsRunning = false;
            _processExited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeProxyPort : IProxyPort
    {
        public bool RestoreCalled { get; private set; }
        public ProxySettingsSnapshotModel ReadCurrent() => new();
        public void Enable(string proxyServer, string proxyOverride) { }
        public void Restore(ProxySettingsSnapshotModel? previous) => RestoreCalled = true;
    }

    private sealed class FakeDiagnosticsPort : IDiagnosticsPort
    {
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
}
