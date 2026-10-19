using System.Text.Json;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
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
                Mode = ConnectionMode.Tun,
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
    public async Task ConnectAsync_ChecksEnabledRuleSetFilesThroughFileService()
    {
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = ConnectionMode.Proxy,
                UserRuleSets =
                [
                    new UserRuleSetModel { Tag = "geoip-ru", FileName = "geoip-ru.srs", Enabled = true },
                    new UserRuleSetModel { Tag = "off", FileName = "off.srs", Enabled = false }
                ]
            }
        };
        var files = new FakeRuleSetFileService();
        var singBox = new FakeSingBoxPort();
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, singBoxPort: singBox, ruleSetFiles: files);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConnectAsync(new ConnectRequest { ProfileId = "p1" }));
        Assert.Equal("Не найдены файлы включённых rule-set (.srs).", error.Message);
        Assert.False(singBox.StartCalled);

        files.ExistingFiles.Add("geoip-ru.srs");
        var result = await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });
        Assert.True(result.Started);
    }

    [Fact]
    public async Task ConnectAsync_ReportsCorruptRuleSetBeforeMissingFiles()
    {
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = ConnectionMode.Proxy,
                UserRuleSets = [new UserRuleSetModel { Tag = "bad", FileName = @"..\bad.srs", Enabled = true }]
            }
        };
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ConnectAsync(new ConnectRequest { ProfileId = "p1" }));
        Assert.StartsWith("Некоторые rule-set записи повреждены", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_StartsAndSavesState_WhenProxyMode()
    {
        var profileStore = new FakeProfileStorePort();
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = ConnectionMode.Proxy,
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
                Mode = ConnectionMode.Proxy,
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
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 } };
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
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 } };
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
                Mode = ConnectionMode.Proxy,
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

    [Fact]
    public async Task ProcessExited_CleansConfigAndRestoresProxy()
    {
        var profileStore = new FakeProfileStorePort();
        var stateStore = new FakeStateStorePort
        {
            State = new AppStateModel
            {
                Mode = ConnectionMode.Proxy,
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

    [Fact]
    public async Task ConnectAsync_KeepsSettingsSavedWhileConnecting()
    {
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 } };
        var appState = new AppStateStore(stateStore);
        var settings = new SettingsService(appState, new FakePathPolicyPort());
        var diagnostics = new FakeDiagnosticsPort { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = CreateService(
            new FakeProfileStorePort(), stateStore, isAdmin: true,
            diagnosticsPort: diagnostics, appStateStore: appState);

        var connect = service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });
        await diagnostics.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.UpdateState(s =>
        {
            s.DohServer = "1.1.1.1";
            s.CloseBehavior = AppCloseBehavior.Exit;
            s.UpdateDismissedModalForTag = "v9.9.9";
        });
        diagnostics.Gate!.SetResult();
        var result = await connect;

        Assert.True(result.Started);
        var saved = stateStore.State;
        Assert.Equal("1.1.1.1", saved.DohServer);
        Assert.Equal("exit", saved.CloseBehavior.ToValue());
        Assert.Equal("v9.9.9", saved.UpdateDismissedModalForTag);
        Assert.Equal("p1", saved.ActiveProfileId);
        Assert.True(saved.ProxyWasEnabledByUs);
    }

    [Fact]
    public async Task SettingsSavesFromUi_DoNotOverwriteProxyRuntimeFlags()
    {
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 } };
        var appState = new AppStateStore(stateStore);
        var settings = new SettingsService(appState, new FakePathPolicyPort());
        var screen = new NothingVpn.Presentation.ConnectionScreenController(new FakeProfileService(), settings);
        var staleSnapshot = settings.GetState();
        var userProxy = new ProxySettingsSnapshotModel { ProxyEnable = true, ProxyServer = "corp:3128", ProxyOverride = "<local>" };
        var proxy = new FakeProxyPort { Current = userProxy };
        var singBox = new FakeSingBoxPort();
        var service = CreateService(
            new FakeProfileStorePort(), stateStore, isAdmin: true,
            singBoxPort: singBox, proxyPort: proxy, appStateStore: appState);

        await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });

        // Устаревший снимок окна (до подключения) и прямая попытка сбросить флаги через настройки.
        staleSnapshot.ProxyOverride = "localhost";
        screen.Save(staleSnapshot);
        settings.UpdateState(s =>
        {
            s.ProxyWasEnabledByUs = false;
            s.PreviousProxySettings = null;
        });

        var saved = stateStore.State;
        Assert.Equal("localhost", saved.ProxyOverride);
        Assert.True(saved.ProxyWasEnabledByUs);
        Assert.Equal("corp:3128", saved.PreviousProxySettings?.ProxyServer);
        Assert.Equal("p1", saved.ActiveProfileId);

        await service.DisconnectAsync();

        Assert.Equal("corp:3128", proxy.RestoredWith?.ProxyServer);
        Assert.False(stateStore.State.ProxyWasEnabledByUs);
        Assert.Null(stateStore.State.PreviousProxySettings);
    }

    [Fact]
    public async Task GetStatus_DoesNotReadStateStore()
    {
        var stateStore = new FakeStateStorePort { State = new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 } };
        var singBox = new FakeSingBoxPort();
        var service = CreateService(new FakeProfileStorePort(), stateStore, isAdmin: true, singBoxPort: singBox);
        var controller = new NothingVpn.Presentation.ConnectionController(service, new FakeAppLifecycleService());
        var before = stateStore.LoadCalls;

        Assert.False(controller.IsRunning);
        Assert.Null(service.GetStatus().ActiveProfileId);
        Assert.Equal(before, stateStore.LoadCalls);

        await service.ConnectAsync(new ConnectRequest { ProfileId = "p1" });
        before = stateStore.LoadCalls;

        for (var i = 0; i < 10; i++)
            Assert.True(controller.IsRunning);
        var status = controller.GetStatus();

        Assert.Equal(before, stateStore.LoadCalls);
        Assert.Equal("proxy", status.Mode.ToValue());
        Assert.Equal("p1", status.ActiveProfileId);

        await service.DisconnectAsync();
        before = stateStore.LoadCalls;

        Assert.False(controller.IsRunning);
        Assert.Null(controller.GetStatus().ActiveProfileId);
        Assert.Equal(before, stateStore.LoadCalls);
    }

    private static VpnConnectionService CreateService(
        FakeProfileStorePort profileStore,
        FakeStateStorePort stateStore,
        bool isAdmin,
        FakeSingBoxPort? singBoxPort = null,
        FakeProxyPort? proxyPort = null,
        FakeDiagnosticsPort? diagnosticsPort = null,
        AppStateStore? appStateStore = null,
        FakeRuleSetFileService? ruleSetFiles = null)
    {
        return new VpnConnectionService(
            profileStore,
            appStateStore ?? new AppStateStore(stateStore),
            singBoxPort ?? new FakeSingBoxPort(),
            proxyPort ?? new FakeProxyPort(),
            diagnosticsPort ?? new FakeDiagnosticsPort(),
            new FakeElevationPort(isAdmin),
            ruleSetFiles ?? new FakeRuleSetFileService(),
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
        private AppStateModel _state = new();

        // Копии, как у настоящего стора: изменения объекта после Save не попадают «на диск».
        public AppStateModel State
        {
            get => Clone(_state);
            set => _state = Clone(value);
        }

        public int LoadCalls { get; private set; }

        public AppStateModel Load()
        {
            LoadCalls++;
            return Clone(_state);
        }

        public void Save(AppStateModel state) => _state = Clone(state);
        public bool SaveMissingBuiltinRuleSets() => false;

        private static AppStateModel Clone(AppStateModel state) =>
            JsonSerializer.Deserialize<AppStateModel>(JsonSerializer.Serialize(state))!;
    }

    private sealed class FakeSingBoxPort : ISingBoxPort
    {
        public event EventHandler? ProcessExited;
        public bool IsRunning { get; set; }
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public bool ExitOnStart { get; init; }
        public bool FireExitedOnStart { get; init; }
        public bool DeleteConfigCalled { get; private set; }
        public bool ValidateCalled { get; private set; }
        public string WriteConfig(VpnProfile profile, AppStateModel state) => "config.json";
        public void ValidateConfig(string configPath) => ValidateCalled = true;

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
        public void TryDeleteLastConfig() => DeleteConfigCalled = true;
        public void RaiseProcessExited()
        {
            IsRunning = false;
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeProxyPort : IProxyPort
    {
        public bool RestoreCalled { get; private set; }
        public bool EnableCalled { get; private set; }
        public ProxySettingsSnapshotModel Current { get; init; } = new();
        public ProxySettingsSnapshotModel? RestoredWith { get; private set; }

        public ProxySettingsSnapshotModel ReadCurrent() => Current;
        public void Enable(string proxyServer, string proxyOverride) => EnableCalled = true;

        public void Restore(ProxySettingsSnapshotModel? previous)
        {
            RestoreCalled = true;
            RestoredWith = previous;
        }
    }

    private sealed class FakeDiagnosticsPort : IDiagnosticsPort
    {
        // Позволяет «заморозить» ConnectAsync на проверке локального порта и изменить состояние параллельно.
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Gate { get; init; }

        public async Task<(bool Success, string? Error)> CanReachTcpAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (Gate is not null)
                await Gate.Task.WaitAsync(cancellationToken);
            return (true, null);
        }

        public Task<(bool Success, string? Error)> ProxySmokeTestAsync(string proxyHost, int proxyPort, string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));

        public Task<(bool Success, string? Error)> TunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));
    }

    private sealed class FakeProfileService : IProfileService
    {
        private readonly IReadOnlyList<VpnProfile> _profiles = [new() { Id = "p1", Name = "Main" }];

        public IReadOnlyList<VpnProfile> GetProfiles() => _profiles;
        public IReadOnlyList<VpnProfile> ImportFromVlessLink(string link) => throw new NotSupportedException();
        public IReadOnlyList<VpnProfile> DeleteProfile(string profileId) => throw new NotSupportedException();
        public bool TryParseVlessLink(string link, out VpnProfile profile) => throw new NotSupportedException();
        public VpnProfile UpsertFromVlessLink(string link, string? nameOverride) => throw new NotSupportedException();
    }

    private sealed class FakeAppLifecycleService : IAppLifecycleService
    {
        public bool IsAdministrator() => true;
        public bool RestartElevated(string arguments) => false;
        public string BuildTakeoverArgs(ConnectionMode mode, string profileId) => string.Empty;
    }

    private sealed class FakeElevationPort(bool isAdmin) : IElevationPort
    {
        public bool IsAdministrator() => isAdmin;
        public bool RestartElevated(string arguments) => true;
    }

    private sealed class FakeRuleSetFileService : IRuleSetFileService
    {
        public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string CatalogUrl => string.Empty;
        public bool Exists(UserRuleSetModel ruleSet) => ExistingFiles.Contains(ruleSet.FileName);
        public RuleSetImportResult Import(string sourcePath) => throw new NotSupportedException();
        public void Delete(UserRuleSetModel ruleSet) => throw new NotSupportedException();
        public Task<RuleSetDownloadResult> DownloadBuiltinAsync(
            UserRuleSetModel ruleSet,
            bool useConditionalRequest,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        public int Version => 0;
        public string SnapshotText(LogLevel minLevel) => string.Empty;
        public string? TryGetLatestMessage(LogLevel minLevel) => "FATAL test";
        public void Append(LogLevel level, string source, string message) { }
        public void Clear() { }
    }
}
