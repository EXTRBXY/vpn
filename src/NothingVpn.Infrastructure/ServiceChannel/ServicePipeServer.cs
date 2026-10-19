using System.IO.Pipes;
using System.Threading.Channels;
using NothingVpn.Application.Engine;
using NothingVpn.Infrastructure.Diagnostics;

namespace NothingVpn.Infrastructure.ServiceChannel;

public sealed class ServicePipeServerOptions
{
    public string PipeName { get; init; } = ServiceProtocol.PipeName;

    /// <summary>ACL канала. В службе — <see cref="ServicePipeSecurity.Create"/> с владельцем SYSTEM.</summary>
    public Func<PipeSecurity> SecurityFactory { get; init; } = () => ServicePipeSecurity.Create(ServicePipeSecurity.LocalSystem);

    public int MaxConnections { get; init; } = 8;

    /// <summary>Сколько ждать, пока клиент примет кадр; дольше — соединение закрывается.</summary>
    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Сообщения о сбоях самого сервера (журнал службы).</summary>
    public Action<string>? Trace { get; init; }
}

/// <summary>
/// Сервер канала службы: принимает команды по протоколу <see cref="ServiceProtocol"/>, выполняет их на ядре
/// (<see cref="IVpnEngine"/> — в службе это <see cref="SingBoxVpnEngine"/>) и рассылает события статуса и, после
/// <c>subscribeLog</c>, строки журнала службы.
/// <para>
/// Имя канала занимается с FILE_FLAG_FIRST_PIPE_INSTANCE: если его уже создал кто-то другой, <see cref="Start"/>
/// падает, а не становится ещё одним экземпляром чужого канала. Пока работает сервер, хотя бы один его экземпляр
/// существует (следующий создаётся до обработки подключённого), и первый экземпляр без флага не создаётся.
/// </para>
/// </summary>
public sealed class ServicePipeServer : IAsyncDisposable
{
    private const int LogQueueCapacity = 2048;

    private readonly IVpnEngine _engine;
    private readonly InMemoryLogStore _logStore;
    private readonly ServicePipeServerOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();
    private readonly List<Connection> _connections = new();
    private int _liveInstances;
    private NamedPipeServerStream? _firstListener;
    private Task? _acceptLoop;

    public ServicePipeServer(IVpnEngine engine, InMemoryLogStore logStore, ServicePipeServerOptions? options = null)
    {
        _engine = engine;
        _logStore = logStore;
        _options = options ?? new ServicePipeServerOptions();
        if (_options.MaxConnections < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxConnections must be positive.");
    }

    /// <summary>Создаёт первый экземпляр канала (исключение, если имя уже занято) и начинает принимать клиентов.</summary>
    public void Start()
    {
        if (_acceptLoop is not null)
            throw new InvalidOperationException("Server already started.");

        _firstListener = CreateInstance();
        _engine.StatusChanged += OnEngineStatusChanged;
        _logStore.Appended += OnLogAppended;
        _logStore.Cleared += OnLogCleared;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public async ValueTask DisposeAsync()
    {
        _engine.StatusChanged -= OnEngineStatusChanged;
        _logStore.Appended -= OnLogAppended;
        _logStore.Cleared -= OnLogCleared;
        _cts.Cancel();

        Connection[] connections;
        lock (_gate)
            connections = _connections.ToArray();
        foreach (var connection in connections)
            connection.Close();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch { }
        }

        // Экземпляры канала освобождаются при завершении соединений; после выхода из DisposeAsync имя свободно.
        try { await Task.WhenAll(connections.Select(c => c.Completion)).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch { }

        _cts.Dispose();
    }

    private NamedPipeServerStream CreateInstance()
    {
        lock (_gate)
        {
            // Нет ни одного нашего экземпляра — имя свободно, и занять его надо только как первый экземпляр.
            var options = PipeOptions.Asynchronous | (_liveInstances == 0 ? PipeOptions.FirstPipeInstance : PipeOptions.None);
            var stream = NamedPipeServerStreamAcl.Create(
                _options.PipeName,
                PipeDirection.InOut,
                _options.MaxConnections,
                PipeTransmissionMode.Byte,
                options,
                inBufferSize: 64 * 1024,
                outBufferSize: 64 * 1024,
                _options.SecurityFactory());
            _liveInstances++;
            return stream;
        }
    }

    private void ReleaseInstance(NamedPipeServerStream stream)
    {
        lock (_gate)
        {
            try { stream.Dispose(); }
            catch { }
            _liveInstances--;
        }
    }

    private async Task AcceptLoopAsync()
    {
        var token = _cts.Token;
        var listener = _firstListener;
        while (!token.IsCancellationRequested)
        {
            try
            {
                listener ??= CreateInstance();
            }
            catch (IOException) when (!token.IsCancellationRequested)
            {
                // Все экземпляры заняты (MaxConnections) — имя держат подключённые, ждём освобождения.
                await DelayAsync(TimeSpan.FromMilliseconds(200), token).ConfigureAwait(false);
                continue;
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                _options.Trace?.Invoke($"Не удалось создать экземпляр канала службы: {ex.Message}");
                await DelayAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                continue;
            }

            try
            {
                await listener.WaitForConnectionAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReleaseInstance(listener);
                listener = null;
                if (token.IsCancellationRequested)
                    break;
                _options.Trace?.Invoke($"Ошибка ожидания клиента канала службы: {ex.Message}");
                continue;
            }

            var connection = new Connection(this, listener);
            listener = null;
            lock (_gate)
                _connections.Add(connection);
            connection.Completion = Task.Run(() => connection.RunAsync(token));
        }

        if (listener is not null)
            ReleaseInstance(listener);
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private void OnEngineStatusChanged(object? sender, EngineStatus status)
    {
        var message = new ServiceMessage { Type = ServiceMessageTypes.Status, Status = status };
        foreach (var connection in Snapshot())
            connection.Post(message);
    }

    private void OnLogAppended(object? sender, LogLine line)
    {
        var message = new ServiceMessage { Type = ServiceMessageTypes.Log, Log = ToWire(line) };
        foreach (var connection in Snapshot())
        {
            if (connection.LogSubscribed)
                connection.PostLog(message);
        }
    }

    private void OnLogCleared(object? sender, EventArgs e)
    {
        var message = new ServiceMessage { Type = ServiceMessageTypes.LogCleared };
        foreach (var connection in Snapshot())
        {
            if (connection.LogSubscribed)
                connection.PostLog(message);
        }
    }

    private static ServiceLogLine ToWire(LogLine line) => new()
    {
        TimestampUtc = line.TimestampUtc,
        Level = line.Level,
        Source = line.Source,
        Message = line.Message
    };

    private Connection[] Snapshot()
    {
        lock (_gate)
            return _connections.ToArray();
    }

    private void Remove(Connection connection)
    {
        lock (_gate)
            _connections.Remove(connection);
        ReleaseInstance(connection.Stream);
    }

    private async Task<ServiceMessage> ExecuteAsync(Connection connection, ServiceMessage message, CancellationToken token)
    {
        try
        {
            switch (message.Type)
            {
                case ServiceMessageTypes.Connect:
                    if (message.Request is null)
                        return ServiceMessage.ErrorOf(message.Id, ServiceErrorCodes.Protocol, "Команда connect без запроса.");
                    return ServiceMessage.ResultOf(message.Id, await _engine.ConnectAsync(message.Request, token).ConfigureAwait(false));

                case ServiceMessageTypes.Disconnect:
                    await _engine.DisconnectAsync(token).ConfigureAwait(false);
                    return ServiceMessage.ResultOf(message.Id, _engine.GetStatus());

                case ServiceMessageTypes.GetStatus:
                    return ServiceMessage.ResultOf(message.Id, _engine.GetStatus());

                case ServiceMessageTypes.SubscribeLog:
                    connection.SubscribeLog(_logStore.SnapshotLines().Select(line =>
                        new ServiceMessage { Type = ServiceMessageTypes.Log, Log = ToWire(line) }));
                    return ServiceMessage.ResultOf(message.Id, _engine.GetStatus());

                default:
                    return ServiceMessage.ErrorOf(message.Id, ServiceErrorCodes.Protocol, $"Неизвестная команда канала службы: {message.Type}.");
            }
        }
        catch (Exception ex)
        {
            return ServiceMessage.ErrorOf(message.Id, ServiceErrorCodes.Failed, ex.Message);
        }
    }

    private sealed class Connection
    {
        private readonly ServicePipeServer _server;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Channel<ServiceMessage> _logQueue = Channel.CreateBounded<ServiceMessage>(
            new BoundedChannelOptions(LogQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
        private readonly CancellationTokenSource _closed = new();
        private int _logSubscribed;

        public Connection(ServicePipeServer server, NamedPipeServerStream stream)
        {
            _server = server;
            Stream = stream;
        }

        public NamedPipeServerStream Stream { get; }

        public Task Completion { get; set; } = Task.CompletedTask;

        public bool LogSubscribed => Volatile.Read(ref _logSubscribed) == 1;

        public async Task RunAsync(CancellationToken serverToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverToken, _closed.Token);
            var token = linked.Token;
            var logPump = Task.Run(() => PumpLogAsync(token));
            try
            {
                while (!token.IsCancellationRequested)
                {
                    ServiceMessage? message;
                    try
                    {
                        message = await ServiceFrameCodec.ReadAsync(Stream, ServiceProtocol.FrameBodyTimeout, token).ConfigureAwait(false);
                    }
                    catch (ServiceProtocolException ex)
                    {
                        await TrySendAsync(ServiceMessage.ErrorOf(null, ServiceErrorCodes.Protocol, ex.Message)).ConfigureAwait(false);
                        break;
                    }

                    if (message is null)
                        break;

                    if (message.ContractVersion != EngineContract.Version)
                    {
                        await TrySendAsync(ServiceMessage.ErrorOf(
                            message.Id,
                            ServiceErrorCodes.Version,
                            EngineContract.IncompatibleVersionMessage(message.ContractVersion))).ConfigureAwait(false);
                        break;
                    }

                    // Команды выполняются параллельно чтению: долгий connect не задерживает другие запросы
                    // соединения; порядок и взаимоисключение операций обеспечивает ядро.
                    _ = Task.Run(async () =>
                    {
                        var response = await _server.ExecuteAsync(this, message, serverToken).ConfigureAwait(false);
                        await TrySendAsync(response).ConfigureAwait(false);
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
            finally
            {
                Close();
                _logQueue.Writer.TryComplete();
                try { await logPump.ConfigureAwait(false); }
                catch { }
                _server.Remove(this);
            }
        }

        public void SubscribeLog(IEnumerable<ServiceMessage> backlog)
        {
            if (Interlocked.Exchange(ref _logSubscribed, 1) == 1)
                return;
            foreach (var message in backlog)
                _logQueue.Writer.TryWrite(message);
        }

        public void Post(ServiceMessage message) => _ = TrySendAsync(message);

        public void PostLog(ServiceMessage message) => _logQueue.Writer.TryWrite(message);

        public void Close()
        {
            try { _closed.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private async Task PumpLogAsync(CancellationToken token)
        {
            try
            {
                await foreach (var message in _logQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    if (!await TrySendAsync(message).ConfigureAwait(false))
                        break;
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task<bool> TrySendAsync(ServiceMessage message)
        {
            if (_closed.IsCancellationRequested)
                return false;

            try
            {
                await _writeLock.WaitAsync(_closed.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
                timeout.CancelAfter(_server._options.WriteTimeout);
                await ServiceFrameCodec.WriteAsync(Stream, message, timeout.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                // Клиент не читает или ушёл — соединение закрывается, клиент переподключится.
                Close();
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
