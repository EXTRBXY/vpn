using NothingVpn.Application.Engine;
using NothingVpn.Application.Mappers;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

/// <summary>
/// Клиентская оркестрация подключения: профиль и настройки из хранилищ, запрос к ядру (<see cref="IVpnEngine"/>),
/// проверка прав для TUN (только если ядро в процессе интерфейса), системный прокси WinINET и его runtime-флаги
/// в state.json, восстановление после остановки ядра и откат. Всё это остаётся на стороне интерфейса и с ядром
/// в службе: прокси хранится в HKCU пользователя (ADR-0001).
/// <para>
/// Ядро в службе переживает перезапуск интерфейса. Тогда сессии в памяти нет, а статус ядра «работает»: режим и
/// профиль берутся из статуса ядра, runtime-флаги прокси в state.json остаются (их восстановит отключение), а
/// <see cref="RecoverStaleRuntimeState"/> такую сессию застрявшей не считает.
/// </para>
/// </summary>
public sealed class VpnConnectionService : IVpnConnectionService
{
    public const string InProcessTunRequiresAdministratorMessage =
        "Ядро в процессе: для TUN запустите клиент от имени администратора.";

    private readonly IProfileStorePort _profileStore;
    private readonly AppStateStore _stateStore;
    private readonly IVpnEngine _engine;
    private readonly IProxyPort _proxyPort;
    private readonly IElevationPort _elevationPort;
    private readonly IRuleSetFileService _ruleSetFiles;
    private readonly IPathPolicyPort _pathPolicy;
    private readonly ILogPort _log;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private int _sessionGeneration;
    private ConnectedSession? _session;

    public VpnConnectionService(
        IProfileStorePort profileStore,
        AppStateStore stateStore,
        IVpnEngine engine,
        IProxyPort proxyPort,
        IElevationPort elevationPort,
        IRuleSetFileService ruleSetFiles,
        IPathPolicyPort pathPolicy,
        ILogPort log)
    {
        _profileStore = profileStore;
        _stateStore = stateStore;
        _engine = engine;
        _proxyPort = proxyPort;
        _elevationPort = elevationPort;
        _ruleSetFiles = ruleSetFiles;
        _pathPolicy = pathPolicy;
        _log = log;
        // Во время собственных Connect/Disconnect лок занят, и обработчики ничего не делают. Здесь срабатывают
        // неожиданная остановка ядра (выход sing-box, остановка службы) и «работает» без своей сессии: ядро службы
        // уже работало (интерфейс перезапущен) или его подключил другой интерфейс.
        _engine.StatusChanged += (_, status) =>
        {
            try
            {
                if (status.IsRunning)
                    AdoptRunningEngineSession();
                else
                    RecoverStaleRuntimeState();
            }
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

            // Снимок нужен только для запроса к ядру и обратно целиком не сохраняется:
            // настройки, изменённые за время подключения, не теряются.
            var state = _stateStore.Load();
            AppStateNormalizer.Normalize(state, _pathPolicy);
            ConnectionPolicy.EnsureTunAppsHasTargets(state.Mode, state.TunAppProcessPaths);
            ValidateRuleSets(state);

            // Только ядро в процессе (разработка): интерфейс не перезапускает себя с повышением прав.
            if (ConnectionPolicy.IsTunMode(state.Mode) && _engine.TunRequiresElevatedCaller && !_elevationPort.IsAdministrator())
                throw new InvalidOperationException(InProcessTunRequiresAdministratorMessage);

            if (_engine.GetStatus().IsRunning)
                await DisconnectCoreAsync(cancellationToken).ConfigureAwait(false);

            await _engine.ConnectAsync(EngineRequestMapper.CreateRequest(profile, state), cancellationToken).ConfigureAwait(false);

            ProxySettingsSnapshotModel? previousProxy = null;
            var proxyEnabled = false;
            try
            {
                if (!ConnectionPolicy.IsTunMode(state.Mode))
                {
                    previousProxy = _proxyPort.ReadCurrent();
                    _proxyPort.Enable($"127.0.0.1:{state.LocalMixedPort}", state.ProxyOverride);
                    proxyEnabled = true;
                    _stateStore.Update(new ConnectionRuntimeState(ProxyWasEnabledByUs: true, previousProxy).ApplyTo);
                    Log(LogLevel.Info, $"Системный прокси включён: 127.0.0.1:{state.LocalMixedPort}.");
                }
            }
            catch
            {
                await RollbackFailedConnectAsync(proxyEnabled, previousProxy).ConfigureAwait(false);
                throw;
            }

            if (Volatile.Read(ref _sessionGeneration) != generation)
            {
                await RollbackFailedConnectAsync(proxyEnabled, previousProxy).ConfigureAwait(false);
                throw new OperationCanceledException("Сессия подключения устарела.");
            }

            _stateStore.Update(current => current.ActiveProfileId = profile.Id);
            Volatile.Write(ref _session, new ConnectedSession(state.Mode, profile.Id));
            // Только режим и имя профиля: адрес сервера, UUID и ключи в журнал не попадают.
            Log(LogLevel.Info, $"Подключено: режим {state.Mode.ToValue()}, профиль «{DisplayName(profile)}».");
            ConnectionStateChanged?.Invoke(this, true);

            return new ConnectResult { Started = true };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(LogLevel.Error, "Не удалось подключиться: " + ex.Message);
            throw;
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log(LogLevel.Error, "Ошибка при отключении: " + ex.Message);
            throw;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public VpnConnectionStatus GetStatus()
    {
        var engine = _engine.GetStatus();
        var session = Volatile.Read(ref _session);
        if (session is not null)
        {
            return new VpnConnectionStatus
            {
                IsRunning = engine.IsRunning,
                Mode = session.Mode,
                ActiveProfileId = session.ProfileId
            };
        }

        return engine.IsRunning
            ? new VpnConnectionStatus { IsRunning = true, Mode = engine.Mode, ActiveProfileId = engine.ProfileId }
            : new VpnConnectionStatus { IsRunning = false };
    }

    public void RecoverStaleRuntimeState()
    {
        if (!_sessionLock.Wait(0))
            return;

        try
        {
            if (_engine.GetStatus().IsRunning)
                return;

            var hadSession = Interlocked.Exchange(ref _session, null) is not null;
            var state = _stateStore.Load();
            var proxyWasEnabled = state.ProxyWasEnabledByUs;
            if (!proxyWasEnabled && string.IsNullOrWhiteSpace(state.ActiveProfileId))
                return;

            Log(
                hadSession ? LogLevel.Warn : LogLevel.Info,
                hadSession ? "Ядро остановилось: VPN отключён." : "Сброшено состояние прерванного подключения.");
            if (proxyWasEnabled)
            {
                try
                {
                    _proxyPort.Restore(state.PreviousProxySettings);
                    Log(LogLevel.Info, "Системный прокси восстановлен.");
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warn, "Не удалось восстановить системный прокси: " + ex.Message);
                }
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

    private void AdoptRunningEngineSession()
    {
        if (!_sessionLock.Wait(0))
            return;

        try
        {
            if (Volatile.Read(ref _session) is not null)
                return;
        }
        finally
        {
            _sessionLock.Release();
        }

        Log(LogLevel.Info, "Подхвачено подключение, работающее в службе.");
        ConnectionStateChanged?.Invoke(this, true);
    }

    private async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = ConnectionRuntimeState.From(_stateStore.Load());
        try
        {
            await _engine.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _session, null);
            if (runtime.ProxyWasEnabledByUs)
            {
                _proxyPort.Restore(runtime.PreviousProxySettings);
                Log(LogLevel.Info, "Системный прокси восстановлен.");
            }

            _stateStore.Update(current =>
            {
                if (runtime.ProxyWasEnabledByUs)
                    ClearProxyFlags(current);
                current.ActiveProfileId = null;
            });
        }

        Log(LogLevel.Info, "Отключено.");
        ConnectionStateChanged?.Invoke(this, false);
    }

    private void ValidateRuleSets(AppStateModel state)
    {
        RuleSetPolicyValidator.ValidateEnabled(ConnectionSettingsMapper.ToRuleSetEntries(state.UserRuleSets));
        if (state.UserRuleSets.Any(rs => rs.Enabled && !_ruleSetFiles.Exists(rs)))
            throw new InvalidOperationException("Не найдены файлы включённых rule-set (.srs).");
    }

    private async Task RollbackFailedConnectAsync(bool proxyEnabled, ProxySettingsSnapshotModel? previousProxy)
    {
        try { await _engine.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { }

        if (!proxyEnabled)
            return;

        try { _proxyPort.Restore(previousProxy); }
        catch (Exception ex) { Log(LogLevel.Warn, "Не удалось восстановить системный прокси после отката: " + ex.Message); }

        _stateStore.Update(ClearProxyFlags);
    }

    private static string DisplayName(VpnProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Name) ? "без имени" : profile.Name.Trim();

    // Журнал вспомогательный: его сбой не должен ломать подключение или откат.
    private void Log(LogLevel level, string message)
    {
        try { _log.Append(level, AppLogSources.Connection, message); }
        catch { }
    }

    private static void ClearProxyFlags(AppStateModel state) =>
        new ConnectionRuntimeState(ProxyWasEnabledByUs: false, PreviousProxySettings: null).ApplyTo(state);

    private sealed record ConnectedSession(ConnectionMode Mode, string ProfileId);
}
