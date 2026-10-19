using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using NothingVpn.Application.Engine;
using NothingVpn.Application.Serialization;
using NothingVpn.Domain.Models;

namespace NothingVpn.Infrastructure.ServiceChannel;

/// <summary>
/// Протокол канала службы (docs/adr/0001-privileged-service.md, «Протокол канала»). Кадр — 4 байта длины
/// (uint32 little-endian) и UTF-8 JSON <see cref="ServiceMessage"/>. Поля конверта <c>contractVersion</c>,
/// <c>type</c>, <c>id</c>, <c>error</c> не меняются между версиями: по ним любая версия понимает, что собеседник
/// другой версии, и показывает «Обновите приложение».
/// </summary>
public static class ServiceProtocol
{
    public const string PipeName = "NothingVpn.Service";

    /// <summary>Предел кадра. Самое большое сообщение — Connect с rule-set в base64 (не больше 16 МиБ данных).</summary>
    public const int MaxFrameBytes = 24 * 1024 * 1024;

    /// <summary>Сколько ждать тело кадра после заголовка: медленный или зависший собеседник не держит соединение вечно.</summary>
    public static readonly TimeSpan FrameBodyTimeout = TimeSpan.FromSeconds(30);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public static class ServiceMessageTypes
{
    // Клиент → служба.
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string GetStatus = "getStatus";
    public const string SubscribeLog = "subscribeLog";

    // Служба → клиент.
    public const string Result = "result";
    public const string Error = "error";
    public const string Status = "status";
    public const string Log = "log";
    public const string LogCleared = "logCleared";
}

public static class ServiceErrorCodes
{
    /// <summary>Версия контракта собеседника не совпадает. Соединение закрывается.</summary>
    public const string Version = "version";

    /// <summary>Неверный кадр или сообщение. Для неверного кадра соединение закрывается.</summary>
    public const string Protocol = "protocol";

    /// <summary>Команда выполнена с ошибкой; текст — для пользователя.</summary>
    public const string Failed = "failed";
}

public sealed class ServiceMessage
{
    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; set; } = EngineContract.Version;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Номер запроса; ответ (<c>result</c>/<c>error</c>) несёт тот же номер. У событий не задан.</summary>
    [JsonPropertyName("id")]
    public long? Id { get; set; }

    [JsonPropertyName("request")]
    public EngineConnectRequest? Request { get; set; }

    [JsonPropertyName("status")]
    public EngineStatus? Status { get; set; }

    [JsonPropertyName("error")]
    public ServiceError? Error { get; set; }

    [JsonPropertyName("log")]
    public ServiceLogLine? Log { get; set; }

    public static ServiceMessage ResultOf(long? id, EngineStatus status) =>
        new() { Type = ServiceMessageTypes.Result, Id = id, Status = status };

    public static ServiceMessage ErrorOf(long? id, string code, string message) =>
        new() { Type = ServiceMessageTypes.Error, Id = id, Error = new ServiceError { Code = code, Message = message } };
}

public sealed class ServiceError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = ServiceErrorCodes.Failed;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Строка журнала службы. Уже замаскирована (<c>LogRedactor</c>) на стороне службы.</summary>
public sealed class ServiceLogLine
{
    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset TimestampUtc { get; set; }

    [JsonPropertyName("level")]
    [JsonConverter(typeof(LogLevelJsonConverter))]
    public LogLevel Level { get; set; } = LogLevel.Info;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Нарушение протокола: неверная длина, обрезанный кадр, некорректный JSON. Соединение закрывается.</summary>
public sealed class ServiceProtocolException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>Чтение и запись кадров. Версию контракта проверяет получатель, а не кодек.</summary>
public static class ServiceFrameCodec
{
    private const int HeaderBytes = 4;

    public static byte[] Encode(ServiceMessage message)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, ServiceProtocol.JsonOptions);
        if (body.Length > ServiceProtocol.MaxFrameBytes)
            throw new ServiceProtocolException(
                $"Сообщение канала службы слишком большое ({body.Length} байт, предел {ServiceProtocol.MaxFrameBytes}).");

        var frame = new byte[HeaderBytes + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length);
        body.CopyTo(frame, HeaderBytes);
        return frame;
    }

    public static ServiceMessage Decode(ReadOnlySpan<byte> body)
    {
        ServiceMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ServiceMessage>(body, ServiceProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ServiceProtocolException("Некорректное сообщение канала службы.", ex);
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Type))
            throw new ServiceProtocolException("Сообщение канала службы без типа.");
        return message;
    }

    public static async Task WriteAsync(Stream stream, ServiceMessage message, CancellationToken cancellationToken)
    {
        var frame = Encode(message);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Читает один кадр. <c>null</c> — собеседник закрыл соединение между кадрами. Ожидание заголовка не ограничено
    /// (соединение живёт, пока клиент подписан на события), тело должно прийти за <paramref name="bodyTimeout"/>.
    /// </summary>
    public static async Task<ServiceMessage?> ReadAsync(
        Stream stream,
        TimeSpan bodyTimeout,
        CancellationToken cancellationToken)
    {
        var header = new byte[HeaderBytes];
        var read = await ReadAtLeastAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (read == 0)
            return null;
        if (read < HeaderBytes)
            throw new ServiceProtocolException("Кадр канала службы обрезан.");

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length == 0 || length > ServiceProtocol.MaxFrameBytes)
            throw new ServiceProtocolException($"Недопустимая длина кадра канала службы: {length}.");

        var body = new byte[length];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(bodyTimeout);
        try
        {
            if (await ReadAtLeastAsync(stream, body, timeout.Token).ConfigureAwait(false) < body.Length)
                throw new ServiceProtocolException("Кадр канала службы обрезан.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceProtocolException("Тело кадра канала службы не пришло вовремя.");
        }

        return Decode(body);
    }

    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                break;
            total += n;
        }

        return total;
    }
}
