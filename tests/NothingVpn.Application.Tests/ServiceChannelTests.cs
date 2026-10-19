using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Infrastructure.Composition;
using NothingVpn.Infrastructure.Diagnostics;
using NothingVpn.Infrastructure.Ports;
using NothingVpn.Infrastructure.ServiceChannel;
using NothingVpn.Infrastructure.ServiceHost;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Infrastructure.Store;

namespace NothingVpn.Application.Tests;

/// <summary>
/// Служба и её клиент в одном процессе: настоящие <see cref="ServicePipeServer"/>, <see cref="PipeVpnEngine"/> и
/// <see cref="SingBoxVpnEngine"/> с хранилищем rule-set службы, поддельный sing-box. Канал создаётся от имени
/// текущего пользователя, поэтому проверку «сервер — LocalSystem» тесты подменяют, кроме тестов самой проверки.
/// </summary>
public sealed class ServiceChannelTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "NothingVpn.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _pipeName = "NothingVpn.Tests." + Guid.NewGuid().ToString("N");
    private readonly List<IAsyncDisposable> _servers = new();
    private readonly List<PipeVpnEngine> _clients = new();

    [Fact]
    public async Task Connect_ReturnsStatusAndRaisesEvent()
    {
        var service = StartService();
        var client = CreateClient(out _);
        var events = new List<EngineStatus>();
        client.StatusChanged += (_, s) => { lock (events) events.Add(s); };

        var status = await client.ConnectAsync(ValidRequest());

        Assert.True(status.IsRunning);
        Assert.Equal("p1", status.ProfileId);
        Assert.True(service.SingBox.IsRunning);
        Assert.Equal(ConnectionMode.Proxy, client.GetStatus().Mode);
        await WaitUntil(() => { lock (events) return events.Any(e => e.IsRunning); });
    }

    [Fact]
    public async Task Disconnect_StopsServiceEngine()
    {
        var service = StartService();
        var client = CreateClient(out _);
        await client.ConnectAsync(ValidRequest());

        await client.DisconnectAsync();

        Assert.False(service.SingBox.IsRunning);
        Assert.False(client.GetStatus().IsRunning);
    }

    [Fact]
    public async Task RestartedClient_LearnsRunningSessionFromService()
    {
        StartService();
        await CreateClient(out _).ConnectAsync(ValidRequest());

        var restarted = CreateClient(out _);
        var status = await restarted.RefreshStatusAsync();

        Assert.True(status.IsRunning);
        Assert.Equal("p1", restarted.GetStatus().ProfileId);
    }

    [Fact]
    public async Task EngineError_IsReturnedToClientAsMessage()
    {
        var service = StartService();
        var request = ValidRequest();
        request.Profile.Uuid = "not-a-uuid";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateClient(out _).ConnectAsync(request));

        Assert.Equal("В профиле неверный UUID.", error.Message);
        Assert.False(service.SingBox.StartCalled);
    }

    [Fact]
    public async Task ServiceLog_IsWrittenToClientLogPort()
    {
        var service = StartService();
        var client = CreateClient(out var log);
        await client.RefreshStatusAsync();

        service.LogStore.AppendStructured(LogLevel.Warn, "sing-box/stdout", "uuid=11111111-2222-3333-4444-555555555555 slow");

        await WaitUntil(() => log.Lines.Any(l => l.Message.Contains("slow", StringComparison.Ordinal)));
        var line = log.Lines.Single(l => l.Message.Contains("slow", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warn, line.Level);
        Assert.Equal("sing-box/stdout", line.Source);
        Assert.DoesNotContain("11111111-2222", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingBoxStart_ClearsClientLogAndForwardsOutput()
    {
        StartService();
        var client = CreateClient(out var log);
        await client.RefreshStatusAsync();
        log.Append(LogLevel.Info, "app", "old");

        await client.ConnectAsync(ValidRequest());

        await WaitUntil(() => log.Lines.Any(l => l.Message == "sing-box started"));
        Assert.DoesNotContain(log.Lines, l => l.Message == "old");
    }

    [Fact]
    public async Task SubscribeLog_SendsExistingServiceLog()
    {
        var service = StartService();
        service.LogStore.AppendStructured(LogLevel.Info, "sing-box/stdout", "before client");

        var client = CreateClient(out var log);
        await client.RefreshStatusAsync();

        await WaitUntil(() => log.Lines.Any(l => l.Message == "before client"));
    }

    [Fact]
    public async Task ProcessExit_IsReportedToClient()
    {
        var service = StartService();
        var client = CreateClient(out _);
        await client.ConnectAsync(ValidRequest());
        var stopped = new TaskCompletionSource();
        client.StatusChanged += (_, s) => { if (!s.IsRunning) stopped.TrySetResult(); };

        service.SingBox.RaiseProcessExited();

        await stopped.Task.WaitAsync(Wait);
        Assert.False(client.GetStatus().IsRunning);
    }

    [Fact]
    public async Task RuleSets_AreSentAsContentAndWrittenToServiceDirectory()
    {
        var service = StartService();
        var userRuleSets = Path.Combine(_root, "user-rulesets");
        Directory.CreateDirectory(userRuleSets);
        File.WriteAllBytes(Path.Combine(userRuleSets, "ads.srs"), [1, 2, 3]);
        var client = CreateClient(out _, userRuleSets);
        var request = ValidRequest();
        request.RuleSets = [new EngineRuleSet { Tag = "ads", FileName = "ads.srs", Action = RuleSetAction.Block }];

        await client.ConnectAsync(request);

        var written = service.SingBox.WrittenState!.UserRuleSets.Single();
        Assert.Matches("^00-[0-9a-f]{16}\\.srs$", written.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(service.RuleSetsDirectory, written.FileName)));
        Assert.Null(request.RuleSets[0].Content);

        await client.DisconnectAsync();
        Assert.Empty(Directory.GetFiles(service.RuleSetsDirectory));
    }

    [Fact]
    public async Task ServiceRejectsRuleSetWithoutContent()
    {
        var service = StartService();
        var client = CreateClient(out _);
        var request = ValidRequest();
        request.RuleSets = [new EngineRuleSet { Tag = "ads", FileName = @"..\ads.srs" }];

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(request));

        Assert.False(service.SingBox.StartCalled);
        Assert.Empty(Directory.GetFiles(service.RuleSetsDirectory));
    }

    [Fact]
    public void ServiceRuleSetStore_RequiresContent()
    {
        var store = new ServiceRuleSetStore(Path.Combine(_root, "store"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            store.Prepare([new EngineRuleSet { Tag = "ads", FileName = "ads.srs" }]));

        Assert.Contains("Обновите приложение", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OldClientVersion_GetsUpdateErrorAndConnectionIsClosed()
    {
        StartService();
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);

        await ServiceFrameCodec.WriteAsync(pipe, new ServiceMessage { ContractVersion = 1, Type = ServiceMessageTypes.GetStatus, Id = 1 }, CancellationToken.None);
        var reply = await ServiceFrameCodec.ReadAsync(pipe, Wait, CancellationToken.None);

        Assert.Equal(ServiceMessageTypes.Error, reply!.Type);
        Assert.Equal(ServiceErrorCodes.Version, reply.Error!.Code);
        Assert.Contains("Обновите приложение", reply.Error.Message, StringComparison.Ordinal);
        Assert.Null(await ServiceFrameCodec.ReadAsync(pipe, Wait, CancellationToken.None));
    }

    [Fact]
    public async Task MalformedFrame_GetsProtocolErrorAndConnectionIsClosed()
    {
        StartService();
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);

        await pipe.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
        var reply = await ServiceFrameCodec.ReadAsync(pipe, Wait, CancellationToken.None);

        Assert.Equal(ServiceErrorCodes.Protocol, reply!.Error!.Code);
        Assert.Null(await ServiceFrameCodec.ReadAsync(pipe, Wait, CancellationToken.None));
    }

    [Fact]
    public async Task UnknownCommand_GetsProtocolErrorAndConnectionStays()
    {
        StartService();
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);

        await ServiceFrameCodec.WriteAsync(pipe, new ServiceMessage { Type = "format", Id = 1 }, CancellationToken.None);
        var error = await ServiceFrameCodec.ReadAsync(pipe, Wait, CancellationToken.None);
        await ServiceFrameCodec.WriteAsync(pipe, new ServiceMessage { Type = ServiceMessageTypes.GetStatus, Id = 2 }, CancellationToken.None);
        var status = await ServiceFrameCodec.ReadAsync(pipe, Wait, CancellationToken.None);

        Assert.Equal(ServiceErrorCodes.Protocol, error!.Error!.Code);
        Assert.Equal(ServiceMessageTypes.Result, status!.Type);
        Assert.Equal(2, status.Id);
    }

    [Fact]
    public async Task ServiceGone_ClientReportsStoppedAndFailsRequests()
    {
        var service = StartService();
        var client = CreateClient(out _);
        await client.ConnectAsync(ValidRequest());
        var stopped = new TaskCompletionSource();
        client.StatusChanged += (_, s) => { if (!s.IsRunning) stopped.TrySetResult(); };

        await service.Server.DisposeAsync();

        await stopped.Task.WaitAsync(Wait);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DisconnectAsync());
        Assert.Contains("Служба Nothing VPN", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceRestart_ClientReconnects()
    {
        var first = StartService();
        var client = CreateClient(out _);
        await client.RefreshStatusAsync();

        await first.Server.DisposeAsync();
        var second = StartService();
        await client.ConnectAsync(ValidRequest());

        Assert.True(second.SingBox.IsRunning);
        Assert.True(client.GetStatus().IsRunning);
    }

    [Fact]
    public async Task ServiceNotRunning_ConnectFailsWithClearMessage()
    {
        var client = CreateClient(out _);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(ValidRequest()));

        Assert.Contains("не отвечает", error.Message, StringComparison.Ordinal);
        Assert.False(client.GetStatus().IsRunning);
    }

    [Fact]
    public async Task RejectedServer_ReceivesNoData()
    {
        var service = StartService();
        var client = CreateClient(out _, verifier: new RejectingVerifier());

        await Assert.ThrowsAsync<ServiceAuthenticationException>(() => client.ConnectAsync(ValidRequest()));

        Assert.False(service.SingBox.StartCalled);
        Assert.Null(service.SingBox.WrittenState);
    }

    [Fact]
    public async Task RealVerifier_RejectsServerNotRunningAsLocalSystem_BeforeSendingAnything()
    {
        // Сервер — процесс текущего пользователя, как у злоумышленника, занявшего имя канала раньше службы.
        await using var squatter = NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            CurrentUserSecurity());
        var accepted = squatter.WaitForConnectionAsync();
        var client = new PipeVpnEngine(new RecordingLogPort(), _root, ClientOptions());
        _clients.Add(client);

        var error = await Assert.ThrowsAsync<ServiceAuthenticationException>(() => client.ConnectAsync(ValidRequest()));
        await accepted.WaitAsync(Wait);
        var received = await squatter.ReadAsync(new byte[16]).AsTask().WaitAsync(Wait);

        Assert.Contains("не SYSTEM", error.Message, StringComparison.Ordinal);
        Assert.Contains("не отправлены", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, received);
    }

    [Fact]
    public void Verifier_ReadsOwnProcessUser()
    {
        Assert.Equal(
            WindowsIdentity.GetCurrent().User,
            LocalSystemPipeServerVerifier.TryGetProcessUser((uint)Environment.ProcessId));
    }

    [Fact]
    public void SecondServer_CannotTakeExistingPipeName()
    {
        StartService();
        var second = new ServicePipeServer(new IdleEngine(), new InMemoryLogStore(), ServerOptions());

        // FILE_FLAG_FIRST_PIPE_INSTANCE при занятом имени — ERROR_ACCESS_DENIED.
        Assert.Throws<UnauthorizedAccessException>(second.Start);
    }

    [Fact]
    public async Task Server_AcceptsSeveralClientsAtOnce()
    {
        StartService();
        var clients = Enumerable.Range(0, 4).Select(i => CreateClient(out _)).ToList();

        var statuses = await Task.WhenAll(clients.Select(c => c.RefreshStatusAsync()));

        Assert.All(statuses, s => Assert.False(s.IsRunning));
    }

    [Fact]
    public void PipeSecurity_GrantsServiceAndAdminsFullAccessUsersReadWriteAndDeniesNetwork()
    {
        var security = ServicePipeSecurity.Create(ServicePipeSecurity.LocalSystem);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(ServicePipeSecurity.LocalSystem, security.GetOwner(typeof(SecurityIdentifier)));
        Assert.Equal(4, rules.Count);
        AssertRule(ServicePipeSecurity.LocalSystem, PipeAccessRights.FullControl, AccessControlType.Allow);
        AssertRule(ServicePipeSecurity.Administrators, PipeAccessRights.FullControl, AccessControlType.Allow);
        AssertRule(ServicePipeSecurity.Network, PipeAccessRights.FullControl, AccessControlType.Deny);
        var interactive = rules.Single(r => r.IdentityReference.Equals(ServicePipeSecurity.Interactive));
        Assert.Equal(AccessControlType.Allow, interactive.AccessControlType);
        Assert.True(interactive.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite));
        Assert.False(interactive.PipeAccessRights.HasFlag(PipeAccessRights.CreateNewInstance));
        Assert.False(interactive.PipeAccessRights.HasFlag(PipeAccessRights.ChangePermissions));
        Assert.False(interactive.PipeAccessRights.HasFlag(PipeAccessRights.TakeOwnership));

        void AssertRule(SecurityIdentifier sid, PipeAccessRights rights, AccessControlType type)
        {
            var rule = rules.Single(r => r.IdentityReference.Equals(sid));
            Assert.Equal(type, rule.AccessControlType);
            Assert.Equal(rights, rule.PipeAccessRights & rights);
        }
    }

    [Fact]
    public void ServiceDataDirectory_IsOnlyForSystemAndAdministrators()
    {
        var security = ServiceDataDirectory.CreateSecurity();
        var sids = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => (SecurityIdentifier)r.IdentityReference)
            .ToList();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal([ServicePipeSecurity.LocalSystem, ServicePipeSecurity.Administrators], sids);
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ServiceDataDirectory.DefaultRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServiceRunner_LooksForSingBoxOnlyInInstallDirectory()
    {
        var install = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(install, "bin"));
        File.WriteAllText(Path.Combine(install, "bin", "sing-box.exe"), "");
        File.WriteAllText(Path.Combine(_root, "sing-box.exe"), "");

        Assert.Null(SingBoxRunner.ResolveInInstallDirectory(install));

        File.WriteAllText(Path.Combine(install, "sing-box.exe"), "");
        Assert.Equal(Path.Combine(install, "sing-box.exe"), SingBoxRunner.ResolveInInstallDirectory(install));
    }

    [Fact]
    public void ServiceRunner_FailsWithServiceMessageWhenSingBoxMissing()
    {
        var install = Path.Combine(_root, "empty-install");
        Directory.CreateDirectory(install);
        var config = Path.Combine(_root, "config.json");
        File.WriteAllText(config, "{}");
        using var runner = SingBoxRunner.ForInstallDirectory(install, new InMemoryLogStore());

        var error = Assert.Throws<FileNotFoundException>(() => runner.ValidateConfig(config));

        Assert.Contains("каталоге службы", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("service", false)]
    [InlineData("in-process", false)]
    [InlineData("inprocess", true)]
    [InlineData(" InProcess ", true)]
    public void EngineSelection_DefaultsToService(string? value, bool expected)
    {
        Assert.Equal(expected, ApplicationServicesFactory.UseInProcessEngine(value));
    }

    /// <summary>
    /// Конфиг службы отличается от конфига внутри процесса только путями rule-set: файлы — в каталоге службы под её
    /// именами, с тем же содержимым. Остальной JSON (а значит, и эталоны генератора) тот же.
    /// </summary>
    [Fact]
    public async Task ServiceConfig_DiffersFromInProcessOnlyInRuleSetPaths()
    {
        var profile = SingBoxConfigGoldenTests.RealityProfile();
        var state = SingBoxConfigGoldenTests.CreateState("tun", "doh", "direct", ruleSets: true);
        var userRuleSets = Path.Combine(_root, "user-rulesets");
        Directory.CreateDirectory(userRuleSets);
        foreach (var rs in state.UserRuleSets.Where(r => r.Enabled))
            File.WriteAllBytes(Path.Combine(userRuleSets, rs.FileName), System.Text.Encoding.UTF8.GetBytes(rs.Tag));

        var inProcess = await BuildConfigAsync(profile, state, ReferencedRuleSetStore.Instance, request => request, "inprocess");
        var serviceRuleSets = Path.Combine(_root, "service", "rulesets");
        var service = await BuildConfigAsync(
            profile,
            state,
            new ServiceRuleSetStore(serviceRuleSets),
            request => WithContent(request, userRuleSets),
            "service");

        var inProcessSets = inProcess["route"]!["rule_set"]!.AsArray();
        var serviceSets = service["route"]!["rule_set"]!.AsArray();
        Assert.Equal(inProcessSets.Count, serviceSets.Count);
        Assert.NotEmpty(inProcessSets);
        for (var i = 0; i < inProcessSets.Count; i++)
        {
            var userPath = (string)inProcessSets[i]!["path"]!;
            var servicePath = (string)serviceSets[i]!["path"]!;
            Assert.Equal(Path.Combine(@"C:\NothingVpnGolden\rulesets", Path.GetFileName(userPath)), userPath);
            Assert.Equal(serviceRuleSets, Path.GetDirectoryName(servicePath));
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(userRuleSets, Path.GetFileName(userPath))),
                File.ReadAllBytes(servicePath));
            inProcessSets[i]!["path"] = "<path>";
            serviceSets[i]!["path"] = "<path>";
        }

        Assert.Equal(inProcess.ToJsonString(), service.ToJsonString());
    }

    private async Task<JsonNode> BuildConfigAsync(
        VpnProfile profile,
        AppStateModel state,
        IEngineRuleSetStore ruleSets,
        Func<EngineConnectRequest, EngineConnectRequest> prepare,
        string name)
    {
        var capture = new FakeSingBoxPort(new InMemoryLogStore());
        var engine = new SingBoxVpnEngine(capture, new FakeDiagnosticsPort(), new PathPolicyPort(), new RecordingLogPort(), ruleSets);
        await engine.ConnectAsync(prepare(EngineRequestMapper.CreateRequest(profile, state)));

        var configs = Path.Combine(_root, name);
        Directory.CreateDirectory(configs);
        var paths = new AppPaths(
            BaseDir: @"C:\NothingVpnGolden",
            ConfigsDir: configs,
            RuleSetsDir: ruleSets is ServiceRuleSetStore store ? store.Directory : @"C:\NothingVpnGolden\rulesets",
            LogsDir: string.Empty,
            ProfilesJsonPath: string.Empty,
            SubscriptionsJsonPath: string.Empty,
            StateJsonPath: string.Empty);
        return JsonNode.Parse(File.ReadAllText(SingBoxConfigGenerator.WriteConfig(paths, capture.WrittenProfile!, capture.WrittenState!)))!;
    }

    private static EngineConnectRequest WithContent(EngineConnectRequest request, string directory)
    {
        foreach (var rs in request.RuleSets)
            rs.Content = File.ReadAllBytes(Path.Combine(directory, rs.FileName));
        return request;
    }

    private RunningService StartService()
    {
        var logStore = new InMemoryLogStore();
        var singBox = new FakeSingBoxPort(logStore);
        var ruleSets = Path.Combine(_root, "service-" + _servers.Count, "rulesets");
        var engine = new SingBoxVpnEngine(
            singBox,
            new FakeDiagnosticsPort(),
            new PathPolicyPort(),
            new LogPort(logStore),
            new ServiceRuleSetStore(ruleSets));
        Directory.CreateDirectory(ruleSets);
        var server = new ServicePipeServer(engine, logStore, ServerOptions());
        server.Start();
        _servers.Add(server);
        return new RunningService(server, singBox, logStore, ruleSets);
    }

    private ServicePipeServerOptions ServerOptions() => new()
    {
        PipeName = _pipeName,
        SecurityFactory = CurrentUserSecurity
    };

    /// <summary>
    /// ACL службы, но с владельцем — текущим пользователем (назначить SYSTEM без прав нельзя) и его полным доступом:
    /// иначе тестовый процесс не создаст следующие экземпляры, а на CI без INTERACTIVE не подключится.
    /// </summary>
    private static PipeSecurity CurrentUserSecurity()
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var security = ServicePipeSecurity.Create(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private PipeVpnEngineOptions ClientOptions() => new()
    {
        PipeName = _pipeName,
        ConnectTimeout = TimeSpan.FromMilliseconds(500),
        ConnectRequestTimeout = Wait,
        CommandTimeout = Wait,
        ReconnectAttempts = 2,
        ReconnectDelay = TimeSpan.FromMilliseconds(100)
    };

    private PipeVpnEngine CreateClient(out RecordingLogPort log, string? ruleSetsDirectory = null, IPipeServerVerifier? verifier = null)
    {
        log = new RecordingLogPort();
        var client = new PipeVpnEngine(log, ruleSetsDirectory ?? _root, ClientOptions(), verifier ?? new AcceptingVerifier());
        _clients.Add(client);
        return client;
    }

    private static EngineConnectRequest ValidRequest() => EngineRequestMapper.CreateRequest(
        new VpnProfile { Id = "p1", Uuid = "11111111-2222-3333-4444-555555555555", Host = "node.example.com", Port = 443 },
        new AppStateModel { Mode = ConnectionMode.Proxy, LocalMixedPort = 1080 });

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Условие не выполнилось вовремя.");
            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            client.Dispose();
        foreach (var server in _servers)
            await server.DisposeAsync();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed record RunningService(ServicePipeServer Server, FakeSingBoxPort SingBox, InMemoryLogStore LogStore, string RuleSetsDirectory);

    private sealed class AcceptingVerifier : IPipeServerVerifier
    {
        public void Verify(NamedPipeClientStream pipe) { }
    }

    private sealed class RejectingVerifier : IPipeServerVerifier
    {
        public void Verify(NamedPipeClientStream pipe) => throw new ServiceAuthenticationException("тест");
    }

    private sealed class FakeSingBoxPort(InMemoryLogStore logStore) : ISingBoxPort
    {
        private volatile bool _running;

        public event EventHandler? ProcessExited;
        public bool IsRunning => _running;
        public bool StartCalled { get; private set; }
        public VpnProfile? WrittenProfile { get; private set; }
        public AppStateModel? WrittenState { get; private set; }

        public string WriteConfig(VpnProfile profile, AppStateModel state)
        {
            WrittenProfile = profile;
            WrittenState = state;
            return "config.json";
        }

        public void ValidateConfig(string configPath) { }

        public void Start(string configPath)
        {
            StartCalled = true;
            logStore.Clear();
            logStore.AppendStructured(LogLevel.Info, "sing-box/stdout", "sing-box started");
            _running = true;
        }

        public void Stop() => _running = false;

        public void TryDeleteLastConfig() { }

        public void RaiseProcessExited()
        {
            _running = false;
            ProcessExited?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeDiagnosticsPort : IDiagnosticsPort
    {
        public Task<(bool Success, string? Error)> CanReachTcpAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Success, string? Error)> ProxySmokeTestAsync(string proxyHost, int proxyPort, string targetHost, int targetPort, TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(bool Success, string? Error)> TunSmokeTestAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingLogPort : ILogPort
    {
        private readonly List<(LogLevel Level, string Source, string Message)> _lines = new();

        public IReadOnlyList<(LogLevel Level, string Source, string Message)> Lines
        {
            get { lock (_lines) return _lines.ToList(); }
        }

        public int Version => 0;
        public string SnapshotText(LogLevel minLevel) => string.Empty;
        public string? TryGetLatestMessage(LogLevel minLevel) => null;

        public void Append(LogLevel level, string source, string message)
        {
            lock (_lines) _lines.Add((level, source, message));
        }

        public void Clear()
        {
            lock (_lines) _lines.Clear();
        }
    }

    private sealed class IdleEngine : IVpnEngine
    {
        public event EventHandler<EngineStatus>? StatusChanged { add { } remove { } }
        public bool TunRequiresElevatedCaller => true;
        public Task<EngineStatus> ConnectAsync(EngineConnectRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public EngineStatus GetStatus() => EngineStatus.Stopped;
    }
}
