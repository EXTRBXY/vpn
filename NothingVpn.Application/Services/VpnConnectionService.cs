using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class VpnConnectionService : IVpnConnectionService
{
    private static readonly TimeSpan TcpReachTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ProxySmokeTimeout = TimeSpan.FromSeconds(8);
    private const string ProxySmokeHost = "api.ipify.org";
    private const int ProxySmokePort = 443;
    private static readonly TimeSpan TunSmokeTimeout = TimeSpan.FromSeconds(12);

    private readonly IProfileStorePort _profileStore;
    private readonly IStateStorePort _stateStore;
    private readonly ISingBoxPort _singBoxPort;
    private readonly IProxyPort _proxyPort;
    private readonly IDiagnosticsPort _diagnosticsPort;
    private readonly IElevationPort _elevationPort;
    private readonly IAppPathsPort _appPathsPort;
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ILogPort _logPort;

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
        _singBoxPort.ProcessExited += (_, _) => RecoverStaleRuntimeState();
    }

    public event EventHandler<bool>? ConnectionStateChanged;

    public async Task<ConnectResult> ConnectAsync(ConnectRequest request, CancellationToken cancellationToken = default)
    {
        var profiles = _profileStore.Load();
        var profile = profiles.FirstOrDefault(p => string.Equals(p.Id, request.ProfileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Профиль не найден.");

        var state = _stateStore.Load();
        state.Mode = ConnectionPolicy.NormalizeMode(state.Mode);
        state.TunAppProcessPaths = _pathPolicy.NormalizeDistinctExePaths(state.TunAppProcessPaths).ToList();
        ConnectionPolicy.EnsureTunAppsHasTargets(state.Mode, state.TunAppProcessPaths);
        ValidateRuleSets(state, _appPathsPort.Get().RuleSetsDir);

        var reach = await _diagnosticsPort.CanReachTcpAsync(profile.Host, profile.Port, TcpReachTimeout, cancellationToken);
        if (!reach.Success)
            throw new InvalidOperationException($"Узел {profile.Host}:{profile.Port} недоступен по TCP ({reach.Error}).");

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

        var configPath = _singBoxPort.WriteConfig(profile, state);

        if (_singBoxPort.IsRunning)
            await DisconnectAsync(cancellationToken);

        try
        {
            _singBoxPort.Start(configPath);

            if (!ConnectionPolicy.IsTunMode(state.Mode))
            {
                var test = await _diagnosticsPort.ProxySmokeTestAsync(
                    "127.0.0.1",
                    state.LocalMixedPort,
                    ProxySmokeHost,
                    ProxySmokePort,
                    ProxySmokeTimeout,
                    cancellationToken);

                if (!test.Success)
                    throw new InvalidOperationException($"Проверка прокси не прошла: {test.Error}");

                var previous = _proxyPort.ReadCurrent();
                _proxyPort.Enable($"127.0.0.1:{state.LocalMixedPort}", state.ProxyOverride);
                state.PreviousProxySettings = previous;
                state.ProxyWasEnabledByUs = true;
            }
            else
            {
                await WaitForSingBoxRunningAsync(cancellationToken);
                if (!_singBoxPort.IsRunning)
                    throw new InvalidOperationException(DescribeSingBoxStartupFailure());

                if (string.Equals(state.Mode, ConnectionPolicy.TunMode, StringComparison.Ordinal))
                {
                    var tunTest = await _diagnosticsPort.TunSmokeTestAsync(TunSmokeTimeout, cancellationToken);
                    if (!tunTest.Success)
                        throw new InvalidOperationException($"Проверка TUN не прошла: {tunTest.Error}");
                }
            }
        }
        catch
        {
            RollbackFailedConnect(state);
            throw;
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

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var state = _stateStore.Load();
        try
        {
            _singBoxPort.Stop();
        }
        finally
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

        ConnectionStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
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
        var state = _stateStore.Load();
        if (_singBoxPort.IsRunning)
            return;

        var changed = false;

        if (state.ProxyWasEnabledByUs)
        {
            try
            {
                _proxyPort.Restore(state.PreviousProxySettings);
            }
            catch
            {
                // best-effort
            }

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

    private async Task WaitForSingBoxRunningAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            if (_singBoxPort.IsRunning)
                return;

            await Task.Delay(50, cancellationToken);
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
            ? "sing-box завершился при запуске TUN."
            : $"sing-box завершился при запуске TUN: {detail}";
    }

    private void RollbackFailedConnect(AppStateModel state)
    {
        try
        {
            _singBoxPort.Stop();
        }
        catch
        {
            // best-effort
        }

        if (!state.ProxyWasEnabledByUs)
            return;

        try
        {
            _proxyPort.Restore(state.PreviousProxySettings);
        }
        catch
        {
            // best-effort
        }

        state.ProxyWasEnabledByUs = false;
        state.PreviousProxySettings = null;
        _stateStore.Save(state);
    }
}

