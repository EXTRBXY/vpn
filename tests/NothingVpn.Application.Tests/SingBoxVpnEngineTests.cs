using NothingVpn.Application.Engine;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Tests;

/// <summary>Ядро отдельно от оркестрации: валидация запроса, запуск, откат и выход процесса.</summary>
public sealed class SingBoxVpnEngineTests
{
    [Theory]
    [InlineData("https://hitmos.fm")]
    [InlineData("*.hitmos.fm")]
    [InlineData(null)]
    public async Task ConnectAsync_RejectsInvalidDomainExceptionBeforeWritingConfig(string? domain)
    {
        var singBox = new FakeSingBoxPort();
        var request = ValidRequest();
        request.DomainExceptions = [domain is null ? null! : new() { Domain = domain }];
        await Assert.ThrowsAsync<ArgumentException>(() => CreateEngine(singBox).ConnectAsync(request));
        Assert.Null(singBox.WrittenState);
        Assert.False(singBox.StartCalled);
    }

    [Fact]
    public async Task ConnectAsync_RejectsIncompatibleContractVersion()
    {
        var singBox = new FakeSingBoxPort();
        var engine = CreateEngine(singBox);
        var request = ValidRequest();
        request.ContractVersion = EngineContract.Version + 1;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConnectAsync(request));

        Assert.Contains("Обновите приложение", error.Message, StringComparison.Ordinal);
        Assert.Null(singBox.WrittenState);
        Assert.False(singBox.StartCalled);
    }

    [Theory]
    [InlineData("", "11111111-2222-3333-4444-555555555555", "node.example.com", 443, "Профиль не найден.")]
    [InlineData("p1", "not-a-uuid", "node.example.com", 443, "В профиле неверный UUID.")]
    [InlineData("p1", "11111111-2222-3333-4444-555555555555", " ", 443, "В профиле не указан адрес сервера.")]
    [InlineData("p1", "11111111-2222-3333-4444-555555555555", "node.example.com", 70000, "В профиле неверный порт сервера.")]
    public async Task ConnectAsync_RejectsInvalidProfile(string id, string uuid, string host, int port, string expected)
    {
        var singBox = new FakeSingBoxPort();
        var engine = CreateEngine(singBox);
        var request = ValidRequest();
        request.Profile.Id = id;
        request.Profile.Uuid = uuid;
        request.Profile.Host = host;
        request.Profile.Port = port;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConnectAsync(request));

        Assert.Equal(expected, error.Message);
        Assert.False(singBox.StartCalled);
    }

    [Fact]
    public async Task ConnectAsync_RejectsInvalidLocalPortInProxyMode()
    {
        var singBox = new FakeSingBoxPort();
        var request = ValidRequest();
        request.LocalMixedPort = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEngine(singBox).ConnectAsync(request));

        Assert.False(singBox.StartCalled);
    }

    [Fact]
    public async Task ConnectAsync_RejectsTunAppsWithoutTargets()
    {
        var singBox = new FakeSingBoxPort();
        var request = ValidRequest();
        request.Mode = ConnectionMode.TunApps;
        request.TunAppProcessPaths = [" "];

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEngine(singBox).ConnectAsync(request));

        Assert.StartsWith("В режиме TUN (выбранные приложения)", error.Message, StringComparison.Ordinal);
        Assert.False(singBox.StartCalled);
    }

    [Theory]
    [InlineData(@"..\escape.srs")]
    [InlineData(@"C:\Windows\System32\evil.srs")]
    [InlineData("list.json")]
    public async Task ConnectAsync_RejectsUnsafeRuleSetFileName(string fileName)
    {
        var singBox = new FakeSingBoxPort();
        var request = ValidRequest();
        request.RuleSets = [new EngineRuleSet { Tag = "bad", FileName = fileName }];

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEngine(singBox).ConnectAsync(request));

        Assert.StartsWith("Некоторые rule-set записи повреждены", error.Message, StringComparison.Ordinal);
        Assert.Null(singBox.WrittenState);
    }

    [Fact]
    public async Task ConnectAsync_NormalizesRequestItself()
    {
        var singBox = new FakeSingBoxPort();
        var request = ValidRequest();
        request.Mode = (ConnectionMode)99;
        request.LogLevel = (LogLevel)99;
        request.Dns.Detour = (DnsDetour)99;
        request.Tun.Mtu = -1;
        request.Tun.InterfaceName = "bad:name";

        await CreateEngine(singBox).ConnectAsync(request);

        var state = singBox.WrittenState!;
        Assert.Equal(ConnectionMode.Proxy, state.Mode);
        Assert.Equal(DnsDetour.Direct, state.DnsDetour);
        Assert.Equal(1500, state.TunMtu);
        Assert.Equal("NothingVpn", state.TunInterfaceName);
        Assert.True(Enum.IsDefined(state.SingBoxLogLevel));
    }

    [Fact]
    public async Task ConnectAsync_PassesTunAppsPathsAfterPathPolicy()
    {
        var singBox = new FakeSingBoxPort();
        var request = ValidRequest();
        request.Mode = ConnectionMode.TunApps;
        request.TunAppProcessPaths = [@"C:\A\app.exe", @"c:\a\APP.exe", @"C:\B\other.exe"];

        await CreateEngine(singBox).ConnectAsync(request);

        Assert.Equal([@"C:\A\app.exe", @"C:\B\other.exe"], singBox.WrittenState!.TunAppProcessPaths);
    }

    [Fact]
    public async Task ConnectAsync_ReportsRunningSession()
    {
        var singBox = new FakeSingBoxPort();
        var engine = CreateEngine(singBox);
        EngineStatus? changed = null;
        engine.StatusChanged += (_, status) => changed = status;

        var status = await engine.ConnectAsync(ValidRequest());

        Assert.True(status.IsRunning);
        Assert.Equal("p1", status.ProfileId);
        Assert.Equal(ConnectionMode.Proxy, engine.GetStatus().Mode);
        Assert.True(singBox.ValidateCalled);
        Assert.True(changed?.IsRunning);
    }

    [Fact]
    public async Task ConnectAsync_StopsRunningSessionFirst()
    {
        var singBox = new FakeSingBoxPort { IsRunning = true };

        await CreateEngine(singBox).ConnectAsync(ValidRequest());

        Assert.True(singBox.StopCalled);
        Assert.True(singBox.StartCalled);
    }

    [Fact]
    public async Task ConnectAsync_RollsBack_WhenSingBoxExitsOnStart()
    {
        var singBox = new FakeSingBoxPort { ExitOnStart = true };
        var engine = CreateEngine(singBox);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConnectAsync(ValidRequest()));

        Assert.Equal("sing-box завершился сразу после запуска: FATAL test", error.Message);
        Assert.True(singBox.StopCalled);
        Assert.True(singBox.DeleteConfigCalled);
        Assert.False(engine.GetStatus().IsRunning);
    }

    [Fact]
    public async Task ConnectAsync_RollsBack_WhenLocalProxyDoesNotListen()
    {
        var singBox = new FakeSingBoxPort();
        var engine = CreateEngine(singBox, new FakeDiagnosticsPort { Error = "refused" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConnectAsync(ValidRequest()));

        Assert.Equal("Локальный прокси не слушает порт 1080: refused", error.Message);
        Assert.False(singBox.IsRunning);
        Assert.True(singBox.DeleteConfigCalled);
        Assert.False(engine.GetStatus().IsRunning);
    }

    [Fact]
    public async Task ConnectAsync_DoesNotProbeLocalPortInTunMode()
    {
        var singBox = new FakeSingBoxPort();
        var diagnostics = new FakeDiagnosticsPort { Error = "refused" };
        var request = ValidRequest();
        request.Mode = ConnectionMode.Tun;

        var status = await CreateEngine(singBox, diagnostics).ConnectAsync(request);

        Assert.True(status.IsRunning);
        Assert.Equal(0, diagnostics.Calls);
    }

    [Fact]
    public async Task DisconnectAsync_StopsAndDeletesConfig()
    {
        var singBox = new FakeSingBoxPort();
        var engine = CreateEngine(singBox);
        await engine.ConnectAsync(ValidRequest());

        await engine.DisconnectAsync();

        Assert.False(singBox.IsRunning);
        Assert.True(singBox.DeleteConfigCalled);
        Assert.Null(engine.GetStatus().ProfileId);
    }

    [Fact]
    public async Task ProcessExited_ReportsStoppedAndDeletesConfig()
    {
        var singBox = new FakeSingBoxPort();
        var engine = CreateEngine(singBox);
        await engine.ConnectAsync(ValidRequest());
        EngineStatus? changed = null;
        engine.StatusChanged += (_, status) => changed = status;

        singBox.RaiseProcessExited();

        Assert.False(changed?.IsRunning);
        Assert.True(singBox.DeleteConfigCalled);
        Assert.Null(engine.GetStatus().ProfileId);
    }

    [Fact]
    public async Task ConnectAsync_StartsXrayBeforeSingBoxAndPassesUpstream()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort();
        var diagnostics = new FakeDiagnosticsPort();

        var status = await CreateEngine(singBox, diagnostics, xray).ConnectAsync(XhttpRequest());

        Assert.True(status.IsRunning);
        Assert.Equal("p1", xray.StartedProfile?.Id);
        Assert.Same(xray.Upstream, singBox.WrittenUpstream);
        Assert.True(xray.StartedBeforeConfig(singBox));
        Assert.Contains(41000, diagnostics.Ports);
    }

    [Fact]
    public async Task ConnectAsync_DoesNotStartXrayForSingBoxProfile()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort();

        await CreateEngine(singBox, outbound: xray).ConnectAsync(ValidRequest());

        Assert.Null(xray.StartedProfile);
        Assert.Null(singBox.WrittenUpstream);
    }

    [Fact]
    public async Task ConnectAsync_RejectsXrayProfile_WhenEngineIsMissing()
    {
        var singBox = new FakeSingBoxPort();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEngine(singBox).ConnectAsync(XhttpRequest()));

        Assert.Equal("Движок Xray недоступен в этой сборке.", error.Message);
        Assert.False(singBox.StartCalled);
    }

    [Fact]
    public async Task ConnectAsync_RejectsSingBoxPreferenceForXhttp()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort();
        var request = XhttpRequest();
        request.Profile.Engine = OutboundEngine.SingBox;

        await Assert.ThrowsAsync<ArgumentException>(() => CreateEngine(singBox, outbound: xray).ConnectAsync(request));

        Assert.Null(xray.StartedProfile);
        Assert.False(singBox.StartCalled);
    }

    [Fact]
    public async Task ConnectAsync_RollsBack_WhenXrayExitsOnStart()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort { ExitOnStart = true };
        var engine = CreateEngine(singBox, outbound: xray);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConnectAsync(XhttpRequest()));

        Assert.Equal("Xray завершился сразу после запуска: FATAL test", error.Message);
        Assert.True(xray.StopCalled);
        Assert.False(singBox.StartCalled);
        Assert.False(engine.GetStatus().IsRunning);
    }

    [Fact]
    public async Task ConnectAsync_StopsXray_WhenSingBoxFailsToStart()
    {
        var singBox = new FakeSingBoxPort { ExitOnStart = true };
        var xray = new FakeOutboundEnginePort();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateEngine(singBox, outbound: xray).ConnectAsync(XhttpRequest()));

        Assert.True(xray.StopCalled);
        Assert.False(xray.IsRunning);
    }

    [Fact]
    public async Task XrayExit_StopsSingBoxAndReportsStopped()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort();
        var log = new FakeLogPort();
        var engine = CreateEngine(singBox, outbound: xray, log: log);
        await engine.ConnectAsync(XhttpRequest());
        EngineStatus? changed = null;
        engine.StatusChanged += (_, status) => changed = status;

        xray.RaiseProcessExited();

        Assert.False(singBox.IsRunning);
        Assert.True(singBox.DeleteConfigCalled);
        Assert.False(changed?.IsRunning);
        Assert.Contains("Xray завершился, подключение остановлено.", log.Messages);
    }

    [Fact]
    public async Task SingBoxExit_StopsXray()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort();
        var engine = CreateEngine(singBox, outbound: xray);
        await engine.ConnectAsync(XhttpRequest());

        singBox.RaiseProcessExited();

        Assert.True(xray.StopCalled);
        Assert.False(engine.GetStatus().IsRunning);
    }

    [Fact]
    public async Task DisconnectAsync_StopsXray()
    {
        var singBox = new FakeSingBoxPort();
        var xray = new FakeOutboundEnginePort();
        var engine = CreateEngine(singBox, outbound: xray);
        await engine.ConnectAsync(XhttpRequest());

        await engine.DisconnectAsync();

        Assert.True(xray.StopCalled);
        Assert.False(xray.IsRunning);
    }

    private static EngineConnectRequest ValidRequest() => EngineRequestMapper.CreateRequest(
        new VpnProfile { Id = "p1", Uuid = "11111111-2222-3333-4444-555555555555", Host = "node.example.com", Port = 443 },
        new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 });

    private static EngineConnectRequest XhttpRequest()
    {
        var request = ValidRequest();
        request.Profile.Type = "xhttp";
        request.Profile.XhttpPath = "/xh";
        return request;
    }

    private static SingBoxVpnEngine CreateEngine(
        FakeSingBoxPort singBox,
        FakeDiagnosticsPort? diagnostics = null,
        FakeOutboundEnginePort? outbound = null,
        FakeLogPort? log = null) =>
        new(
            singBox,
            diagnostics ?? new FakeDiagnosticsPort(),
            new FakePathPolicyPort(),
            log ?? new FakeLogPort(),
            outboundEngines: outbound is null ? null : [outbound]);

    private sealed class FakeOutboundEnginePort : IOutboundEnginePort
    {
        private static long _clock;
        private long _startedAt;

        public event EventHandler? ProcessExited;
        public OutboundEngine Engine => OutboundEngine.Xray;
        public bool IsRunning { get; private set; }
        public bool ExitOnStart { get; init; }
        public bool StopCalled { get; private set; }
        public VpnProfile? StartedProfile { get; private set; }
        public LocalSocksUpstream Upstream { get; } = new(41000, "user", "pass", @"C:\NothingVpn\xray.exe");

        public LocalSocksUpstream Start(VpnProfile profile, AppStateModel state)
        {
            StartedProfile = profile;
            _startedAt = Interlocked.Increment(ref _clock);
            IsRunning = !ExitOnStart;
            return Upstream;
        }

        public void Stop()
        {
            StopCalled = true;
            IsRunning = false;
        }

        public bool StartedBeforeConfig(FakeSingBoxPort singBox) => _startedAt != 0 && _startedAt < singBox.WrittenAt;

        public void RaiseProcessExited()
        {
            IsRunning = false;
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }

        internal static long Tick() => Interlocked.Increment(ref _clock);
    }

    private sealed class FakeSingBoxPort : ISingBoxPort
    {
        public event EventHandler? ProcessExited;
        public bool IsRunning { get; set; }
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public bool ValidateCalled { get; private set; }
        public bool DeleteConfigCalled { get; private set; }
        public bool ExitOnStart { get; init; }
        public AppStateModel? WrittenState { get; private set; }
        public LocalSocksUpstream? WrittenUpstream { get; private set; }
        public long WrittenAt { get; private set; }

        public string WriteConfig(VpnProfile profile, AppStateModel state, LocalSocksUpstream? upstream = null)
        {
            WrittenState = state;
            WrittenUpstream = upstream;
            WrittenAt = FakeOutboundEnginePort.Tick();
            return "config.json";
        }

        public void ValidateConfig(string configPath) => ValidateCalled = true;

        public void Start(string configPath)
        {
            StartCalled = true;
            IsRunning = !ExitOnStart;
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

    private sealed class FakeDiagnosticsPort : IDiagnosticsPort
    {
        public string? Error { get; init; }
        public int Calls { get; private set; }
        public List<int> Ports { get; } = [];

        public Task<(bool Success, string? Error)> CanReachTcpAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls++;
            Ports.Add(port);
            return Task.FromResult((Error is null, Error));
        }

        public Task<(bool Success, string? Error)> ProxySmokeTestAsync(string proxyHost, int proxyPort, string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(bool Success, string? Error)> TunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePathPolicyPort : IPathPolicyPort
    {
        public IReadOnlyList<string> NormalizeDistinctExePaths(IEnumerable<string>? paths)
            => (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
        public List<string> Messages { get; } = [];
        public void Append(LogLevel level, string source, string message) => Messages.Add(message);
        public void Clear() { }
    }
}
