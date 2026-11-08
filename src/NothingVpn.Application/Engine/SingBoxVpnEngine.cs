using NothingVpn.Application.Mappers;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Engine;

/// <summary>
/// Ядро поверх <see cref="ISingBoxPort"/>: валидация запроса, конфиг, check/run/stop, статус процесса, выход
/// процесса. Работает внутри процесса интерфейса и в службе (с её портами и <see cref="IEngineRuleSetStore"/>).
/// Если профилю нужен внешний движок (<see cref="OutboundEnginePolicy"/>, ADR-0002), запускает его первым и
/// направляет в него выход sing-box; выход любого из процессов останавливает оба.
/// Ничего не знает о state.json, системном прокси и UAC.
/// </summary>
public sealed class SingBoxVpnEngine : IVpnEngine
{
    private static readonly TimeSpan SingBoxStartupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LocalMixedListenTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OutboundStartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OutboundProbeTimeout = TimeSpan.FromMilliseconds(300);

    private readonly ISingBoxPort _singBoxPort;
    private readonly IDiagnosticsPort _diagnosticsPort;
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ILogPort _logPort;
    private readonly IEngineRuleSetStore _ruleSets;
    private readonly IReadOnlyList<IOutboundEnginePort> _outboundEngines;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private EngineStatus _session = EngineStatus.Stopped;
    private IOutboundEnginePort? _activeOutbound;

    public SingBoxVpnEngine(
        ISingBoxPort singBoxPort,
        IDiagnosticsPort diagnosticsPort,
        IPathPolicyPort pathPolicy,
        ILogPort logPort,
        IEngineRuleSetStore? ruleSets = null,
        IEnumerable<IOutboundEnginePort>? outboundEngines = null)
    {
        _singBoxPort = singBoxPort;
        _ruleSets = ruleSets ?? ReferencedRuleSetStore.Instance;
        _diagnosticsPort = diagnosticsPort;
        _pathPolicy = pathPolicy;
        _logPort = logPort;
        _outboundEngines = [.. outboundEngines ?? []];
        _singBoxPort.ProcessExited += (_, _) =>
        {
            try { OnProcessExited(); }
            catch { }
        };
        foreach (var outbound in _outboundEngines)
        {
            outbound.ProcessExited += (_, _) =>
            {
                try { OnOutboundExited(outbound); }
                catch { }
            };
        }
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    public bool TunRequiresElevatedCaller => true;

    public async Task<EngineStatus> ConnectAsync(EngineConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (profile, state, engine) = Validate(request);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_singBoxPort.IsRunning || _activeOutbound is not null)
                StopCore();

            try
            {
                ApplyRuleSetFiles(request, state);
                _logPort.Clear();
                var upstream = await StartOutboundAsync(engine, profile, state, cancellationToken).ConfigureAwait(false);
                var configPath = _singBoxPort.WriteConfig(profile, state, upstream);
                _singBoxPort.ValidateConfig(configPath);
                _singBoxPort.Start(configPath);
                await WaitForSingBoxRunningAsync(cancellationToken).ConfigureAwait(false);

                if (!_singBoxPort.IsRunning)
                    throw new InvalidOperationException(DescribeStartupFailure("sing-box"));
                if (_activeOutbound is { IsRunning: false } outbound)
                    throw new InvalidOperationException(DescribeStartupFailure(OutboundEnginePolicy.DisplayName(outbound.Engine)));

                if (!ConnectionPolicy.IsTunMode(state.Mode))
                {
                    var listen = await _diagnosticsPort.CanReachTcpAsync(
                        "127.0.0.1",
                        state.LocalMixedPort,
                        LocalMixedListenTimeout,
                        cancellationToken).ConfigureAwait(false);
                    if (!listen.Success)
                        throw new InvalidOperationException($"Локальный прокси не слушает порт {state.LocalMixedPort}: {listen.Error}");
                }
            }
            catch
            {
                try { StopCore(); }
                catch { }
                throw;
            }

            var status = new EngineStatus { IsRunning = true, Mode = state.Mode, ProfileId = profile.Id };
            Volatile.Write(ref _session, status);
            return status;
        }
        finally
        {
            _lock.Release();
            StatusChanged?.Invoke(this, GetStatus());
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopCore();
        }
        finally
        {
            _lock.Release();
            StatusChanged?.Invoke(this, GetStatus());
        }
    }

    public EngineStatus GetStatus()
    {
        if (!_singBoxPort.IsRunning)
            return EngineStatus.Stopped;

        var session = Volatile.Read(ref _session);
        return session.IsRunning ? session : new EngineStatus { IsRunning = true };
    }

    /// <summary>
    /// Не доверяет нормализации интерфейса: прогоняет запрос через те же Domain-политики, что и сохранение
    /// настроек. Нормализация идемпотентна, поэтому для уже нормализованного запроса конфиг тот же.
    /// </summary>
    private (Models.VpnProfile Profile, Models.AppStateModel State, OutboundEngine Engine) Validate(EngineConnectRequest request)
    {
        EngineContract.EnsureCompatible(request.ContractVersion);

        var profile = EngineRequestMapper.ToProfile(request);
        VlessEndpointPolicy.EnsureConnectable(profile.Id, profile.Uuid, profile.Host, profile.Port);
        XhttpProfileValidator.Validate(profile);
        var engine = OutboundEnginePolicy.Resolve(profile.Engine, profile.Type, profile.Security);

        DomainExceptionPolicy.Validate((request.DomainExceptions ?? []).Select(x => x?.Domain));
        var state = EngineRequestMapper.ToState(request);
        AppStateNormalizer.Normalize(state, _pathPolicy);
        if (!ConnectionPolicy.IsTunMode(state.Mode))
            VlessEndpointPolicy.EnsureLocalPort(state.LocalMixedPort);
        ConnectionPolicy.EnsureTunAppsHasTargets(state.Mode, state.TunAppProcessPaths);
        RuleSetPolicyValidator.ValidateEnabled(ConnectionSettingsMapper.ToRuleSetEntries(state.UserRuleSets));
        RuleSetPolicyValidator.EnsureContentWithinLimits(
            (request.RuleSets ?? []).Where(rs => rs.Content is not null).Select(rs => rs.Content!.Length));
        return (profile, state, engine);
    }

    /// <summary>
    /// Имена файлов rule-set в конфиге — из хранилища ядра: внутри процесса те же, что в запросе, в службе —
    /// файлы её каталога с переданным содержимым. Записи состояния соответствуют запросу один к одному
    /// (<see cref="EngineRequestMapper.ToState"/>, нормализация rule-set не трогает, дубли тегов отклонены).
    /// </summary>
    private void ApplyRuleSetFiles(EngineConnectRequest request, Models.AppStateModel state)
    {
        var fileNames = _ruleSets.Prepare(request.RuleSets ?? []);
        if (fileNames.Count != state.UserRuleSets.Count)
            throw new InvalidOperationException("Хранилище rule-set ядра вернуло неверное число файлов.");
        for (var i = 0; i < fileNames.Count; i++)
            state.UserRuleSets[i].FileName = fileNames[i];
    }

    private async Task<LocalSocksUpstream?> StartOutboundAsync(
        OutboundEngine engine,
        Models.VpnProfile profile,
        Models.AppStateModel state,
        CancellationToken cancellationToken)
    {
        if (engine == OutboundEngine.SingBox)
            return null;

        var name = OutboundEnginePolicy.DisplayName(engine);
        var outbound = _outboundEngines.FirstOrDefault(e => e.Engine == engine)
            ?? throw new InvalidOperationException($"Движок {name} недоступен в этой сборке.");
        _activeOutbound = outbound;
        var upstream = outbound.Start(profile, state);

        var deadline = Environment.TickCount64 + (long)OutboundStartupTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (!outbound.IsRunning)
                throw new InvalidOperationException(DescribeStartupFailure(name));

            var listen = await _diagnosticsPort.CanReachTcpAsync(
                "127.0.0.1",
                upstream.Port,
                OutboundProbeTimeout,
                cancellationToken).ConfigureAwait(false);
            if (listen.Success)
                return upstream;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"{name} не начал принимать соединения за {OutboundStartupTimeout.TotalSeconds:0} с.");
    }

    private void StopCore()
    {
        try
        {
            _singBoxPort.Stop();
        }
        finally
        {
            StopOutbound();
            Volatile.Write(ref _session, EngineStatus.Stopped);
            _singBoxPort.TryDeleteLastConfig();
            ClearRuleSets();
        }
    }

    private void StopOutbound()
    {
        var outbound = _activeOutbound;
        _activeOutbound = null;
        try { outbound?.Stop(); }
        catch { }
    }

    private void ClearRuleSets()
    {
        try { _ruleSets.Clear(); }
        catch { }
    }

    private void OnProcessExited()
    {
        // Во время своих Connect/Disconnect ядро само разбирается с процессом.
        if (!_lock.Wait(0))
            return;

        try
        {
            if (_singBoxPort.IsRunning)
                return;

            StopOutbound();
            Volatile.Write(ref _session, EngineStatus.Stopped);
            _singBoxPort.TryDeleteLastConfig();
            ClearRuleSets();
        }
        finally
        {
            _lock.Release();
        }

        StatusChanged?.Invoke(this, GetStatus());
    }

    private void OnOutboundExited(IOutboundEnginePort outbound)
    {
        if (!_lock.Wait(0))
            return;

        try
        {
            if (!ReferenceEquals(_activeOutbound, outbound))
                return;

            _logPort.Append(
                LogLevel.Error,
                AppLogSources.Connection,
                $"{OutboundEnginePolicy.DisplayName(outbound.Engine)} завершился, подключение остановлено.");
            StopCore();
        }
        finally
        {
            _lock.Release();
        }

        StatusChanged?.Invoke(this, GetStatus());
    }

    private async Task WaitForSingBoxRunningAsync(CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)SingBoxStartupTimeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_singBoxPort.IsRunning)
                return;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private string DescribeStartupFailure(string engineName)
    {
        var detail = _logPort.TryGetLatestMessage(LogLevel.Fatal) ?? _logPort.TryGetLatestMessage(LogLevel.Error);
        return detail is null
            ? $"{engineName} завершился сразу после запуска. Откройте логи в приложении."
            : $"{engineName} завершился сразу после запуска: {detail}";
    }
}
