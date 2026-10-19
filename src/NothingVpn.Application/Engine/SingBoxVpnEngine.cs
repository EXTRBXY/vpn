using NothingVpn.Application.Mappers;
using NothingVpn.Application.Ports;
using NothingVpn.Application.Services;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Engine;

/// <summary>
/// Ядро поверх <see cref="ISingBoxPort"/>: валидация запроса, конфиг, check/run/stop, статус процесса, выход
/// процесса. Работает внутри процесса интерфейса и в службе (с её портами и <see cref="IEngineRuleSetStore"/>).
/// Ничего не знает о state.json, системном прокси и UAC.
/// </summary>
public sealed class SingBoxVpnEngine : IVpnEngine
{
    private static readonly TimeSpan SingBoxStartupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LocalMixedListenTimeout = TimeSpan.FromSeconds(2);

    private readonly ISingBoxPort _singBoxPort;
    private readonly IDiagnosticsPort _diagnosticsPort;
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ILogPort _logPort;
    private readonly IEngineRuleSetStore _ruleSets;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private EngineStatus _session = EngineStatus.Stopped;

    public SingBoxVpnEngine(
        ISingBoxPort singBoxPort,
        IDiagnosticsPort diagnosticsPort,
        IPathPolicyPort pathPolicy,
        ILogPort logPort,
        IEngineRuleSetStore? ruleSets = null)
    {
        _singBoxPort = singBoxPort;
        _ruleSets = ruleSets ?? ReferencedRuleSetStore.Instance;
        _diagnosticsPort = diagnosticsPort;
        _pathPolicy = pathPolicy;
        _logPort = logPort;
        _singBoxPort.ProcessExited += (_, _) =>
        {
            try { OnProcessExited(); }
            catch { }
        };
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    public bool TunRequiresElevatedCaller => true;

    public async Task<EngineStatus> ConnectAsync(EngineConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (profile, state) = Validate(request);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_singBoxPort.IsRunning)
                StopCore();

            try
            {
                ApplyRuleSetFiles(request, state);
                var configPath = _singBoxPort.WriteConfig(profile, state);
                _singBoxPort.ValidateConfig(configPath);
                _singBoxPort.Start(configPath);
                await WaitForSingBoxRunningAsync(cancellationToken).ConfigureAwait(false);

                if (!_singBoxPort.IsRunning)
                    throw new InvalidOperationException(DescribeSingBoxStartupFailure());

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
    private (Models.VpnProfile Profile, Models.AppStateModel State) Validate(EngineConnectRequest request)
    {
        EngineContract.EnsureCompatible(request.ContractVersion);

        var profile = EngineRequestMapper.ToProfile(request);
        VlessEndpointPolicy.EnsureConnectable(profile.Id, profile.Uuid, profile.Host, profile.Port);

        var state = EngineRequestMapper.ToState(request);
        AppStateNormalizer.Normalize(state, _pathPolicy);
        if (!ConnectionPolicy.IsTunMode(state.Mode))
            VlessEndpointPolicy.EnsureLocalPort(state.LocalMixedPort);
        ConnectionPolicy.EnsureTunAppsHasTargets(state.Mode, state.TunAppProcessPaths);
        RuleSetPolicyValidator.ValidateEnabled(ConnectionSettingsMapper.ToRuleSetEntries(state.UserRuleSets));
        RuleSetPolicyValidator.EnsureContentWithinLimits(
            (request.RuleSets ?? []).Where(rs => rs.Content is not null).Select(rs => rs.Content!.Length));
        return (profile, state);
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

    private void StopCore()
    {
        try
        {
            _singBoxPort.Stop();
        }
        finally
        {
            Volatile.Write(ref _session, EngineStatus.Stopped);
            _singBoxPort.TryDeleteLastConfig();
            ClearRuleSets();
        }
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

    private string DescribeSingBoxStartupFailure()
    {
        var detail = _logPort.TryGetLatestMessage(LogLevel.Fatal) ?? _logPort.TryGetLatestMessage(LogLevel.Error);
        return detail is null
            ? "sing-box завершился сразу после запуска. Откройте логи в приложении."
            : $"sing-box завершился сразу после запуска: {detail}";
    }
}
