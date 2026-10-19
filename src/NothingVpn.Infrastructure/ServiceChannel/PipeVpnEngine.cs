using System.Collections.Concurrent;
using System.IO.Pipes;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Infrastructure.ServiceChannel;

public sealed class PipeVpnEngineOptions
{
    public string PipeName { get; init; } = ServiceProtocol.PipeName;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Ответ на connect: sing-box check, запуск и проверка порта на стороне службы.</summary>
    public TimeSpan ConnectRequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int ReconnectAttempts { get; init; } = 3;
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromMilliseconds(300);
}

/// <summary>
/// <see cref="IVpnEngine"/> поверх канала службы (ADR-0001). Перед первым сообщением проверяет, что сервер канала —
/// служба от LocalSystem (<see cref="LocalSystemPipeServerVerifier"/>): в запросе connect UUID и ключи профиля.
/// Rule-set из общего каталога пользователя передаёт содержимым (контракт v2). Подписывается на журнал службы и
/// пишет его строки в <see cref="ILogPort"/> клиента. При обрыве переподключается; если служба недоступна,
/// считает ядро остановленным (sing-box живёт в Job Object службы и без неё не работает).
/// </summary>
public sealed class PipeVpnEngine : IVpnEngine, IDisposable
{
    private readonly ILogPort _log;
    private readonly string _ruleSetsDirectory;
    private readonly PipeVpnEngineOptions _options;
    private readonly IPipeServerVerifier _verifier;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly object _statusGate = new();
    private EngineStatus _status = EngineStatus.Stopped;
    private ClientConnection? _connection;
    private long _nextId;
    private volatile bool _disposed;

    internal const string ServiceUnavailableMessage =
        "Служба Nothing VPN не отвечает. Запустите службу «Nothing VPN» (services.msc) или переустановите приложение.";

    public PipeVpnEngine(ILogPort log, string ruleSetsDirectory, PipeVpnEngineOptions? options = null)
        : this(log, ruleSetsDirectory, options ?? new PipeVpnEngineOptions(), LocalSystemPipeServerVerifier.Instance)
    {
    }

    internal PipeVpnEngine(ILogPort log, string ruleSetsDirectory, PipeVpnEngineOptions options, IPipeServerVerifier verifier)
    {
        _log = log;
        _ruleSetsDirectory = ruleSetsDirectory;
        _options = options;
        _verifier = verifier;
    }

    public event EventHandler<EngineStatus>? StatusChanged;

    public bool TunRequiresElevatedCaller => false;

    public async Task<EngineStatus> ConnectAsync(EngineConnectRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var wireRequest = WithRuleSetContent(request);
        var response = await SendAsync(
            new ServiceMessage { Type = ServiceMessageTypes.Connect, Request = wireRequest },
            _options.ConnectRequestTimeout,
            cancellationToken).ConfigureAwait(false);
        return response.Status ?? EngineStatus.Stopped;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(
            new ServiceMessage { Type = ServiceMessageTypes.Disconnect },
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Последнее известное состояние службы, без обращения к каналу.</summary>
    public EngineStatus GetStatus()
    {
        lock (_statusGate)
            return _status;
    }

    /// <summary>Подключается к службе (если ещё нет) и запрашивает её состояние. Ошибка — исключение.</summary>
    public async Task<EngineStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            new ServiceMessage { Type = ServiceMessageTypes.GetStatus },
            _options.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        return response.Status ?? EngineStatus.Stopped;
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Exchange(ref _connection, null)?.Close();
    }

    private async Task<ServiceMessage> SendAsync(ServiceMessage message, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ServiceMessage response;
        var connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            response = await connection.RequestAsync(message, Interlocked.Increment(ref _nextId), timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestNotSentException)
        {
            // Цикл чтения ещё не заметил разрыв (например, служба перезапустилась), а запись уже упала. Служба запрос
            // не получила, поэтому повтор на новом соединении безопасен. Если разрыв случился после отправки, повтора
            // нет: команда могла выполниться.
            connection.Close();
            connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            response = await connection.RequestAsync(message, Interlocked.Increment(ref _nextId), timeout, cancellationToken)
                .ConfigureAwait(false);
        }

        if (response.Status is not null)
            UpdateStatus(response.Status);
        return response;
    }

    private async Task<ClientConnection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _connection);
        if (current is { IsAlive: true })
            return current;

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = Volatile.Read(ref _connection);
            if (current is { IsAlive: true })
                return current;

            var pipe = new NamedPipeClientStream(".", _options.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync((int)_options.ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                pipe.Dispose();
                throw new InvalidOperationException(ServiceUnavailableMessage, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                // ACL канала пускает только интерактивных пользователей (не сетевых, не службы).
                pipe.Dispose();
                throw new InvalidOperationException("Нет доступа к службе Nothing VPN: подключаться может только пользователь, вошедший в Windows на этом компьютере.", ex);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            try
            {
                _verifier.Verify(pipe);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            var connection = new ClientConnection(this, pipe);
            Volatile.Write(ref _connection, connection);
            connection.Start();

            var subscribed = await connection.RequestAsync(
                new ServiceMessage { Type = ServiceMessageTypes.SubscribeLog },
                Interlocked.Increment(ref _nextId),
                _options.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
            if (subscribed.Status is not null)
                UpdateStatus(subscribed.Status);
            return connection;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void OnConnectionLost(ClientConnection connection)
    {
        if (Interlocked.CompareExchange(ref _connection, null, connection) != connection || _disposed)
            return;

        _ = Task.Run(ReconnectAsync);
    }

    private async Task ReconnectAsync()
    {
        for (var attempt = 0; attempt < _options.ReconnectAttempts && !_disposed; attempt++)
        {
            try
            {
                await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch
            {
                await Task.Delay(_options.ReconnectDelay).ConfigureAwait(false);
            }
        }

        UpdateStatus(EngineStatus.Stopped);
    }

    private void UpdateStatus(EngineStatus status)
    {
        lock (_statusGate)
        {
            if (_status.IsRunning == status.IsRunning
                && _status.Mode == status.Mode
                && string.Equals(_status.ProfileId, status.ProfileId, StringComparison.Ordinal))
                return;
            _status = status;
        }

        try { StatusChanged?.Invoke(this, status); }
        catch { }
    }

    private void OnEvent(ServiceMessage message)
    {
        switch (message.Type)
        {
            case ServiceMessageTypes.Status when message.Status is not null:
                UpdateStatus(message.Status);
                break;
            case ServiceMessageTypes.Log when message.Log is not null:
                var line = message.Log;
                _log.Append(line.Level, line.Source, line.Message);
                break;
            case ServiceMessageTypes.LogCleared:
                _log.Clear();
                break;
        }
    }

    /// <summary>
    /// Копия запроса с содержимым rule-set из общего каталога пользователя. Читаются только файлы с безопасным простым
    /// именем и в пределах размера; иначе содержимое не заполняется, и запрос отклонит политика службы.
    /// </summary>
    private EngineConnectRequest WithRuleSetContent(EngineConnectRequest request) => new()
    {
        ContractVersion = request.ContractVersion,
        Profile = request.Profile,
        Mode = request.Mode,
        LogLevel = request.LogLevel,
        LocalMixedPort = request.LocalMixedPort,
        Dns = request.Dns,
        Tun = request.Tun,
        TunAppProcessPaths = request.TunAppProcessPaths,
        RuleSets = (request.RuleSets ?? []).Select(rs => new EngineRuleSet
        {
            Tag = rs.Tag,
            FileName = rs.FileName,
            Action = rs.Action,
            Content = rs.Content ?? ReadRuleSet(rs.FileName)
        }).ToList()
    };

    private byte[]? ReadRuleSet(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !RuleSetPolicyValidator.IsSafeRuleSetFileName(fileName))
            return null;

        var path = Path.Combine(_ruleSetsDirectory, fileName.Trim());
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new InvalidOperationException("Не найдены файлы включённых rule-set (.srs).");
        RuleSetPolicyValidator.EnsureContentWithinLimits([(int)Math.Min(info.Length, int.MaxValue)]);
        return File.ReadAllBytes(path);
    }

    private sealed class ClientConnection
    {
        private readonly PipeVpnEngine _owner;
        private readonly NamedPipeClientStream _pipe;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<ServiceMessage>> _pending = new();
        private readonly CancellationTokenSource _closed = new();
        private Exception? _failure;

        public ClientConnection(PipeVpnEngine owner, NamedPipeClientStream pipe)
        {
            _owner = owner;
            _pipe = pipe;
        }

        public bool IsAlive => !_closed.IsCancellationRequested && _pipe.IsConnected;

        public void Start() => _ = Task.Run(ReadLoopAsync);

        public void Close()
        {
            try { _closed.Cancel(); }
            catch (ObjectDisposedException) { }
            try { _pipe.Dispose(); }
            catch { }
        }

        public async Task<ServiceMessage> RequestAsync(
            ServiceMessage message,
            long id,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            message.Id = id;
            var completion = new TaskCompletionSource<ServiceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = completion;
            try
            {
                if (_closed.IsCancellationRequested)
                    throw new RequestNotSentException(ConnectionLost(_failure));

                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ServiceFrameCodec.WriteAsync(_pipe, message, cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    Close();
                    throw new RequestNotSentException(ConnectionLost(ex));
                }
                catch (ObjectDisposedException ex)
                {
                    throw new RequestNotSentException(ConnectionLost(ex));
                }
                finally
                {
                    _writeLock.Release();
                }

                ServiceMessage response;
                try
                {
                    response = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException ex)
                {
                    throw new InvalidOperationException("Служба Nothing VPN не ответила вовремя.", ex);
                }

                if (response.Type == ServiceMessageTypes.Error)
                    throw new InvalidOperationException(response.Error?.Message is { Length: > 0 } text
                        ? text
                        : "Служба Nothing VPN вернула ошибку.");
                return response;
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_closed.IsCancellationRequested)
                {
                    var message = await ServiceFrameCodec.ReadAsync(_pipe, ServiceProtocol.FrameBodyTimeout, _closed.Token)
                        .ConfigureAwait(false);
                    if (message is null)
                        break;

                    if (message.ContractVersion != EngineContract.Version && message.Type != ServiceMessageTypes.Error)
                    {
                        _failure = new InvalidOperationException(EngineContract.IncompatibleVersionMessage(message.ContractVersion));
                        break;
                    }

                    if (message.Id is { } id && message.Type is ServiceMessageTypes.Result or ServiceMessageTypes.Error)
                    {
                        if (_pending.TryGetValue(id, out var completion))
                            completion.TrySetResult(message);
                        continue;
                    }

                    if (message.Type == ServiceMessageTypes.Error)
                    {
                        // Ошибка без номера — служба закрывает соединение (неверный кадр, чужая версия).
                        _failure = new InvalidOperationException(message.Error?.Message ?? "Ошибка протокола службы.");
                        continue;
                    }

                    _owner.OnEvent(message);
                }
            }
            catch (Exception ex)
            {
                _failure ??= ex;
            }
            finally
            {
                Close();
                foreach (var completion in _pending.Values)
                    completion.TrySetException(ConnectionLost(_failure));
                _owner.OnConnectionLost(this);
            }
        }

        private Exception ConnectionLost(Exception? inner = null) =>
            inner is InvalidOperationException { InnerException: null } userFacing && inner is not ObjectDisposedException
                ? userFacing
                : new InvalidOperationException("Связь со службой Nothing VPN потеряна.", inner);
    }
}

/// <summary>
/// Запрос не был записан в канал: соединение уже разорвано. Текст тот же, что у обычной потери связи, — если повтор
/// тоже не удался, пользователь видит прежнее сообщение.
/// </summary>
internal sealed class RequestNotSentException(Exception lost)
    : InvalidOperationException(lost.Message, lost.InnerException ?? lost);
