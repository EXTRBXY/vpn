using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class VpnConnectionService : IVpnConnectionService
{
    private static readonly TimeSpan SingBoxStartupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LocalMixedListenTimeout = TimeSpan.FromSeconds(2);

    private readonly IProfileStorePort _profileStore;
    private readonly AppStateStore _stateStore;
    private readonly ISingBoxPort _singBoxPort;
    private readonly IProxyPort _proxyPort;
    private readonly IDiagnosticsPort _diagnosticsPort;
    private readonly IElevationPort _elevationPort;
    private readonly IAppPathsPort _appPathsPort;
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ILogPort _logPort;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private int _sessionGeneration;
    private ConnectedSession? _session;

    public VpnConnectionService(
        IProfileStorePort profileStore,
        AppStateStore stateStore,
        ISingBoxPort singBoxPort,
        IProxyPort proxyPort,
        IDiagnosticsPort diagnosticsPort,
        IElevationPort elevationPort,
        IAppPathsPort appPathsPort,
        IPathPolicyPort pathPolicy,
        ILogPort logPort)
    {
        _profileStore = profileStore;
        _stateStore = stateStore;
        _singBoxPort = singBoxPort;
        _proxyPort = proxyPort;
        _diagnosticsPort = diagnosticsPort;
        _elevationPort = elevationPort;
        _appPathsPort = appPathsPort;
        _pathPolicy = pathPolicy;
        _logPort = logPort;
        _singBoxPort.ProcessExited += (_, _) =>
        {
            try { RecoverStaleRuntimeState(); }
            catch { }
        };
    }

    public event EventHandler<bool>? ConnectionStateChanged;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var generation = Interlocked.Increment(ref _sessionGeneration);
        try
        {
            var profiles = _profileStore.Load();
            var profile = profiles.FirstOrDefault(p => string.Equals(p.Id, request.ProfileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Профиль не найден.");

            // Снимок нужен только для генерации конфига и обратно целиком не сохраняется:
            // настройки, изменённые за время подключения, не теряются.
            var state = _stateStore.Load();
            AppStateNormalizer.Normalize(state, _pathPolicy);
            ConnectionPolicy.EnsureTunAppsHasTargets(state.Mode, state.TunAppProcessPaths);
            ValidateRuleSets(state, _appPathsPort.Get().RuleSetsDir);

            if (ConnectionPolicy.IsTunMode(state.Mode) && !_elevationPort.IsAdministrator())
            {
                var args = $"--takeover --start --mode {state.Mode} --profile \"{profile.Id}\"";
                return new ConnectResult
                {
                    Started = false,
                    RequiresElevation = true,
                    ElevationArgs = args
                };
            }

            if (_singBoxPort.IsRunning)
                await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);

            var configPath = _singBoxPort.WriteConfig(profile, state);
            ProxySettingsSnapshotModel? previousProxy = null;
            var proxyEnabled = false;

            try
            {
                _singBoxPort.ValidateConfig(configPath);
                _singBoxPort.Start(configPath);
                await WaitForSingBoxRunningAsync(SingBoxStartupTimeout, cancellationToken).ConfigureAwait(false);

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

                    previousProxy = _proxyPort.ReadCurrent();
                    _proxyPort.Enable($"127.0.0.1:{state.LocalMixedPort}", state.ProxyOverride);
                    proxyEnabled = true;
                    _stateStore.Update(new ConnectionRuntimeState(ProxyWasEnabledByUs: true, previousProxy).ApplyTo);
                }
            }
            catch
            {
                RollbackFailedConnect(proxyEnabled, previousProxy);
                throw;
            }

            if (Volatile.Read(ref _sessionGeneration) != generation)
            {
                RollbackFailedConnect(proxyEnabled, previousProxy);
                throw new OperationCanceledException("Сессия подключения устарела.");
            }

            _stateStore.Update(current => current.ActiveProfileId = profile.Id);
            Volatile.Write(ref _session, new ConnectedSession(state.Mode, profile.Id));
            ConnectionStateChanged?.Invoke(this, true);

            return new ConnectResult
            {
                Started = true,
                RequiresElevation = false
            };
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _sessionGeneration);
            await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public VpnConnectionStatus GetStatus()
    {
        var isRunning = _singBoxPort.IsRunning;
        var session = Volatile.Read(ref _session);
        return session is null
            ? new VpnConnectionStatus { IsRunning = isRunning }
            : new VpnConnectionStatus
            {
                IsRunning = isRunning,
                Mode = session.Mode,
                ActiveProfileId = session.ProfileId
            };
    }

    public void RecoverStaleRuntimeState()
    {
        if (!_sessionLock.Wait(0))
            return;

        try
        {
            if (_singBoxPort.IsRunning)
                return;

            Volatile.Write(ref _session, null);
            _singBoxPort.TryDeleteLastConfig();
            var state = _stateStore.Load();
            var proxyWasEnabled = state.ProxyWasEnabledByUs;
            if (!proxyWasEnabled && string.IsNullOrWhiteSpace(state.ActiveProfileId))
                return;

            if (proxyWasEnabled)
            {
                try { _proxyPort.Restore(state.PreviousProxySettings); }
                catch { }
            }

            _stateStore.Update(current =>
            {
                if (proxyWasEnabled)
                    ClearProxyFlags(current);
                current.ActiveProfileId = null;
            });
            ConnectionStateChanged?.Invoke(this, false);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = ConnectionRuntimeState.From(_stateStore.Load());
        try
        {
            _singBoxPort.Stop();
        }
        finally
        {
            Volatile.Write(ref _session, null);
            try
            {
                if (runtime.ProxyWasEnabledByUs)
                    _proxyPort.Restore(runtime.PreviousProxySettings);

                _stateStore.Update(current =>
                {
                    if (runtime.ProxyWasEnabledByUs)
                        ClearProxyFlags(current);
                    current.ActiveProfileId = null;
                });
            }
            finally { _singBoxPort.TryDeleteLastConfig(); }
        }

        ConnectionStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    private async Task WaitForSingBoxRunningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_singBoxPort.IsRunning)
                return;

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ValidateRuleSets(AppStateModel state, string ruleSetsDir)
    {
        RuleSetPolicyValidator.ValidateEnabled(ConnectionSettingsMapper.ToRuleSetEntries(state.UserRuleSets), ruleSetsDir);
    }

    private string DescribeSingBoxStartupFailure()
    {
        var detail = _logPort.TryGetLatestMessage(5) ?? _logPort.TryGetLatestMessage(4);
        return detail is null
            ? "sing-box завершился сразу после запуска. Откройте логи в приложении."
            : $"sing-box завершился сразу после запуска: {detail}";
    }

    private void RollbackFailedConnect(bool proxyEnabled, ProxySettingsSnapshotModel? previousProxy)
    {
        try { _singBoxPort.Stop(); }
        catch { }
        try { _singBoxPort.TryDeleteLastConfig(); }
        catch { }

        if (!proxyEnabled)
            return;

        try { _proxyPort.Restore(previousProxy); }
        catch { }

        _stateStore.Update(ClearProxyFlags);
    }

    private static void ClearProxyFlags(AppStateModel state) =>
        new ConnectionRuntimeState(ProxyWasEnabledByUs: false, PreviousProxySettings: null).ApplyTo(state);

    private sealed record ConnectedSession(string Mode, string ProfileId);
}
