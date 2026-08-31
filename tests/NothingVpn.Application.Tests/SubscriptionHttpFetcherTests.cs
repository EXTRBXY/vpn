using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NothingVpn.Infrastructure.Ports;

namespace NothingVpn.Application.Tests;

public sealed class SubscriptionHttpFetcherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1L)]
    [InlineData(4194305L)]
    public async Task FetchAsync_RejectsOversizedBodyRegardlessOfDeclaredLength(long? length)
    {
        using var stream = new CountedBodyStream(new byte[SubscriptionHttpFetcher.MaximumBodyBytes + 1]);
        var content = new StreamContent(stream);
        content.Headers.ContentLength = length;
        var fetcher = Create(content);
        var result = await fetcher.FetchAsync("https://subscription.example/list");
        Assert.False(result.Success);
        Assert.Contains("4 МиБ", result.Error);
        Assert.True(string.IsNullOrEmpty(result.Body));
        if (length > SubscriptionHttpFetcher.MaximumBodyBytes)
            Assert.Equal(0, stream.BytesRead);
        else
            Assert.True(stream.BytesRead > SubscriptionHttpFetcher.MaximumBodyBytes);
    }

    [Fact]
    public async Task FetchAsync_StopsStalledBodyAtWholeOperationDeadline()
    {
        using var stream = new StalledStream();
        var result = await Create(new StreamContent(stream), TimeSpan.FromMilliseconds(100))
            .FetchAsync("https://subscription.example/list").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Equal("Timeout.", result.Error);
        Assert.True(stream.ReadStarted);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task FetchAsync_HonorsCancellationDuringBodyRead()
    {
        using var stream = new StalledStream();
        using var cancellation = new CancellationTokenSource();
        var pending = Create(new StreamContent(stream)).FetchAsync("https://subscription.example/list", cancellation.Token);
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.Success);
        Assert.Equal("Загрузка отменена.", result.Error);
        Assert.True(stream.Disposed);
    }

    [Theory]
    [InlineData("vless://node#Имя", "utf-8")]
    [InlineData("dmxlc3M6Ly9ub2Rl", "utf-8")]
    [InlineData("Имя узла", "utf-16")]
    public async Task FetchAsync_PreservesBodyCharsetAndHeaders(string body, string charset)
    {
        var content = new ByteArrayContent(Encoding.GetEncoding(charset).GetBytes(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = charset };
        content.Headers.Add("subscription-userinfo", "upload=1; download=2");
        var result = await Create(content).FetchAsync("https://subscription.example/list");
        Assert.True(result.Success, result.Error);
        Assert.Equal(body, result.Body);
        Assert.Equal("upload=1; download=2", result.Headers["subscription-userinfo"]);
    }

    [Fact]
    public async Task FetchAsync_PreservesBomDecodingWithoutCharset()
    {
        var encoding = Encoding.Unicode;
        var content = new ByteArrayContent(encoding.GetPreamble().Concat(encoding.GetBytes("Имя узла")).ToArray());
        var result = await Create(content).FetchAsync("https://subscription.example/list");
        Assert.True(result.Success, result.Error);
        Assert.Equal("Имя узла", result.Body);
    }

    [Fact]
    public async Task FetchAsync_AcceptsBodyAtLimit()
    {
        var result = await Create(new ByteArrayContent(new byte[SubscriptionHttpFetcher.MaximumBodyBytes]))
            .FetchAsync("https://subscription.example/list");
        Assert.True(result.Success, result.Error);
        Assert.Equal(SubscriptionHttpFetcher.MaximumBodyBytes, result.Body.Length);
    }

    private static SubscriptionHttpFetcher Create(HttpContent content, TimeSpan? timeout = null) =>
        new(() => new ResponseHandler(content), timeout ?? TimeSpan.FromSeconds(10));

    private sealed class ResponseHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private sealed class CountedBodyStream(byte[] bytes) : MemoryStream(bytes)
    {
        // Simulate a network body with unknown/chunked length, not a seekable file.
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }
    }

    private sealed class StalledStream : Stream
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ReadStarted { get; private set; }
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted = true;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
