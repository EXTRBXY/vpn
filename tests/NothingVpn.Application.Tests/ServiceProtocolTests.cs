using System.Buffers.Binary;
using System.Text;
using NothingVpn.Application.Engine;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;
using NothingVpn.Infrastructure.ServiceChannel;

namespace NothingVpn.Application.Tests;

/// <summary>Кадры и сообщения канала службы: формат, версия, пределы, некорректный ввод.</summary>
public sealed class ServiceProtocolTests
{
    [Fact]
    public async Task Frame_RoundtripsMessage()
    {
        var message = new ServiceMessage
        {
            Type = ServiceMessageTypes.Connect,
            Id = 7,
            Request = new EngineConnectRequest
            {
                Mode = ConnectionMode.Tun,
                RuleSets = [new EngineRuleSet { Tag = "ads", FileName = "ads.srs", Content = [9, 8, 7] }]
            }
        };
        using var stream = new MemoryStream();

        await ServiceFrameCodec.WriteAsync(stream, message, CancellationToken.None);
        stream.Position = 0;
        var restored = await ServiceFrameCodec.ReadAsync(stream, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(EngineContract.Version, restored.ContractVersion);
        Assert.Equal(ServiceMessageTypes.Connect, restored.Type);
        Assert.Equal(7, restored.Id);
        Assert.Equal(ConnectionMode.Tun, restored.Request!.Mode);
        Assert.Equal(new byte[] { 9, 8, 7 }, restored.Request.RuleSets[0].Content);
        Assert.Null(await ServiceFrameCodec.ReadAsync(stream, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public void Frame_HasLittleEndianLengthHeaderAndCompactJson()
    {
        var frame = ServiceFrameCodec.Encode(ServiceMessage.ErrorOf(3, ServiceErrorCodes.Version, "Обновите приложение."));

        var length = BinaryPrimitives.ReadUInt32LittleEndian(frame);
        var json = Encoding.UTF8.GetString(frame, 4, frame.Length - 4);
        Assert.Equal(frame.Length - 4, (int)length);
        Assert.StartsWith(
            "{\"contractVersion\":2,\"type\":\"error\",\"id\":3,\"error\":{\"code\":\"version\",\"message\":",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("request", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LogLine_UsesStateLevelStrings()
    {
        var frame = ServiceFrameCodec.Encode(new ServiceMessage
        {
            Type = ServiceMessageTypes.Log,
            Log = new ServiceLogLine { Level = LogLevel.Warn, Source = "sing-box/stdout", Message = "m" }
        });

        Assert.Contains("\"level\":\"warn\"", Encoding.UTF8.GetString(frame, 4, frame.Length - 4), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData((uint)ServiceProtocol.MaxFrameBytes + 1)]
    [InlineData(uint.MaxValue)]
    public async Task Read_RejectsInvalidLength(uint length)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, length);

        var error = await Assert.ThrowsAsync<ServiceProtocolException>(() => Read(header));

        Assert.Contains("длина кадра", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_RejectsTruncatedHeaderAndBody()
    {
        await Assert.ThrowsAsync<ServiceProtocolException>(() => Read([5, 0]));

        var frame = ServiceFrameCodec.Encode(new ServiceMessage { Type = ServiceMessageTypes.GetStatus });
        await Assert.ThrowsAsync<ServiceProtocolException>(() => Read(frame[..^3]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"contractVersion\":2}")]
    [InlineData("{\"type\":\"\"}")]
    [InlineData("{\"type\":\"connect\",\"id\":\"x\"}")]
    public async Task Read_RejectsMalformedMessage(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)body.Length);
        body.CopyTo(frame, 4);

        await Assert.ThrowsAsync<ServiceProtocolException>(() => Read(frame));
    }

    [Fact]
    public async Task Read_TimesOutWhenBodyDoesNotArrive()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 100);
        using var stream = new StallingStream(header);

        var error = await Assert.ThrowsAsync<ServiceProtocolException>(() =>
            ServiceFrameCodec.ReadAsync(stream, TimeSpan.FromMilliseconds(100), CancellationToken.None));

        Assert.Contains("вовремя", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Encode_RejectsMessageOverLimit()
    {
        var message = new ServiceMessage
        {
            Type = ServiceMessageTypes.Connect,
            Request = new EngineConnectRequest
            {
                RuleSets = [new EngineRuleSet { Tag = "big", FileName = "big.srs", Content = new byte[ServiceProtocol.MaxFrameBytes] }]
            }
        };

        Assert.Throws<ServiceProtocolException>(() => ServiceFrameCodec.Encode(message));
    }

    [Fact]
    public void MaxFrame_FitsLargestAllowedRuleSets()
    {
        // base64 даёт 4/3 от данных; запас остаётся на профиль и остальные поля запроса.
        Assert.True(ServiceProtocol.MaxFrameBytes > RuleSetPolicyValidator.MaxTotalContentBytes / 3 * 4 + 1024 * 1024);
    }

    [Fact]
    public void IncompatibleVersion_AsksToUpdate()
    {
        var error = Assert.Throws<InvalidOperationException>(() => EngineContract.EnsureCompatible(1));

        Assert.Contains("Обновите приложение", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleSetContentLimits_AreEnforced()
    {
        RuleSetPolicyValidator.EnsureContentWithinLimits([1, RuleSetPolicyValidator.MaxContentBytes]);

        Assert.Throws<InvalidOperationException>(() => RuleSetPolicyValidator.EnsureContentWithinLimits([0]));
        Assert.Throws<InvalidOperationException>(() =>
            RuleSetPolicyValidator.EnsureContentWithinLimits([RuleSetPolicyValidator.MaxContentBytes + 1]));
        Assert.Throws<InvalidOperationException>(() => RuleSetPolicyValidator.EnsureContentWithinLimits(
            [RuleSetPolicyValidator.MaxContentBytes, RuleSetPolicyValidator.MaxContentBytes, 1]));
    }

    private static Task<ServiceMessage?> Read(byte[] bytes) =>
        ServiceFrameCodec.ReadAsync(new MemoryStream(bytes), TimeSpan.FromSeconds(1), CancellationToken.None);

    /// <summary>Отдаёт заголовок и дальше «висит», как собеседник, не дославший тело.</summary>
    private sealed class StallingStream(byte[] prefix) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _position);
                prefix.AsMemory(_position, n).CopyTo(buffer);
                _position += n;
                return n;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
