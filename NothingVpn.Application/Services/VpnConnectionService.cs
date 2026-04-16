using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

public sealed class VpnConnectionService : IVpnConnectionService
{
    private readonly IProfileStorePort _profileStore;
    private readonly IStateStorePort _stateStore;
    private readonly ISingBoxPort _singBoxPort;
    private readonly IProxyPort _proxyPort;
    private readonly IDiagnosticsPort _diagnosticsPort;
    private readonly IElevationPort _elevationPort;
    private readonly IAppPathsPort _appPathsPort;
    private readonly IPathPolicyPort _pathPolicy;

    public VpnConnectionService(
        IProfileStorePort profileStore,
        IStateStorePort stateStore,
        ISingBoxPort singBoxPort,
        IProxyPort proxyPort,
        IDiagnosticsPort diagnosticsPort,
        IElevationPort elevationPort,
        IAppPathsPort appPathsPort,
        IPathPolicyPort pathPolicy)
    {
        _profileStore = profileStore;
        _stateStore = stateStore;
        _singBoxPort = singBoxPort;
        _proxyPort = proxyPort;
        _diagnosticsPort = diagnosticsPort;
        _elevationPort = elevationPort;
        _appPathsPort = appPathsPort;
        _pathPolicy = pathPolicy;
        _singBoxPort.ProcessExited += (_, _) => ConnectionStateChanged?.Invoke(this, false);
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
        _singBoxPort.Start(configPath);

        if (!ConnectionPolicy.IsTunMode(state.Mode))
        {
            var test = await _diagnosticsPort.ProxySmokeTestAsync(
                proxyHost: "127.0.0.1",
                proxyPort: state.LocalMixedPort,
                targetHost: profile.Host,
                targetPort: profile.Port,
                timeout: TimeSpan.FromSeconds(3),
                cancellationToken: cancellationToken);

            if (!test.Success)
                throw new InvalidOperationException($"Proxy smoke test failed: {test.Error}");

            var previous = _proxyPort.ReadCurrent();
            _proxyPort.Enable($"127.0.0.1:{state.LocalMixedPort}", state.ProxyOverride);
            state.PreviousProxySettings = previous;
            state.ProxyWasEnabledByUs = true;
        }
        else
        {
            await Task.Delay(900, cancellationToken);
            if (!_singBoxPort.IsRunning)
                throw new InvalidOperationException("sing-box завершился при запуске TUN.");
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
                _stateStore.Save(state);
            }
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
}

