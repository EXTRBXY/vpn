using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class VpnConnectionService : IVpnConnectionService
{
    private static readonly TimeSpan SingBoxStartupTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LocalMixedListenTimeout = TimeSpan.FromSeconds(2);

    private readonly IProfileStorePort _profileStore;
    private readonly IStateStorePort _stateStore;
    private readonly ISingBoxPort _singBoxPort;
    private readonly IProxyPort _proxyPort;
    private readonly IDiagnosticsPort _diagnosticsPort;
    private readonly IElevationPort _elevationPort;
    private readonly IAppPathsPort _appPathsPort;
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ILogPort _logPort;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private int _sessionGeneration;

    public VpnConnectionService(
        IProfileStorePort profileStore,
        IStateStorePort stateStore,
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

            var state = _stateStore.Load();
            state.Mode = ConnectionPolicy.NormalizeMode(state.Mode);
            state.TunAppProcessPaths = _pathPolicy.NormalizeDistinctExePaths(state.TunAppProcessPaths).ToList();
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

                    var previous = _proxyPort.ReadCurrent();
                    _proxyPort.Enable($"127.0.0.1:{state.LocalMixedPort}", state.ProxyOverride);
                    state.PreviousProxySettings = previous;
                    state.ProxyWasEnabledByUs = true;
                }
            }
            catch
            {
                RollbackFailedConnect(state);
                throw;
            }

            if (Volatile.Read(ref _sessionGeneration) != generation)
            {
                RollbackFailedConnect(state);
                throw new OperationCanceledException("Сессия подключения устарела.");
            }

            state.ActiveProfileId = profile.Id;
            _stateStore.Save(state);
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
        var state = _stateStore.Load();
        return new VpnConnectionStatus
        {
            IsRunning = _singBoxPort.IsRunning,
            Mode = ConnectionPolicy.NormalizeMode(state.Mode),
            ActiveProfileId = state.ActiveProfileId
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

            _singBoxPort.TryDeleteLastConfig();
            var state = _stateStore.Load();
            var changed = false;

            if (state.ProxyWasEnabledByUs)
            {
                try { _proxyPort.Restore(state.PreviousProxySettings); }
                catch { }

                state.ProxyWasEnabledByUs = false;
                state.PreviousProxySettings = null;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(state.ActiveProfileId))
            {
                state.ActiveProfileId = null;
                changed = true;
            }

            if (!changed)
                return;

            _stateStore.Save(state);
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
        var state = _stateStore.Load();
        try
        {
            _singBoxPort.Stop();
        }
        finally
        {
            try
            {
                if (state.ProxyWasEnabledByUs)
                {
                    _proxyPort.Restore(state.PreviousProxySettings);
                    state.ProxyWasEnabledByUs = false;
                    state.PreviousProxySettings = null;
                }

                state.ActiveProfileId = null;
                _stateStore.Save(state);
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
        var entries = state.UserRuleSets.Select(x => new UserRuleSetEntry
        {
            Tag = x.Tag,
            Name = x.Name,
            FileName = x.FileName,
            Enabled = x.Enabled,
            Action = x.Action
        });
        RuleSetPolicyValidator.ValidateEnabled(entries, ruleSetsDir);
    }

    private string DescribeSingBoxStartupFailure()
    {
        var detail = _logPort.TryGetLatestMessage(5) ?? _logPort.TryGetLatestMessage(4);
        return detail is null
            ? "sing-box завершился сразу после запуска. Откройте логи в приложении."
            : $"sing-box завершился сразу после запуска: {detail}";
    }

    private void RollbackFailedConnect(AppStateModel state)
    {
        try { _singBoxPort.Stop(); }
        catch { }
        try { _singBoxPort.TryDeleteLastConfig(); }
        catch { }

        if (!state.ProxyWasEnabledByUs)
            return;

        try { _proxyPort.Restore(state.PreviousProxySettings); }
        catch { }

        state.ProxyWasEnabledByUs = false;
        state.PreviousProxySettings = null;
        _stateStore.Save(state);
    }
}
