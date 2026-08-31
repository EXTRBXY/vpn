using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;

namespace NothingVpn.Infrastructure.Ports;

public sealed class SubscriptionHttpFetcher : ISubscriptionFetcherPort
{
    private static readonly string UserAgent = BuildUserAgent();
    internal const int MaximumBodyBytes = 4 * 1024 * 1024;
    private readonly Func<HttpMessageHandler> _handlerFactory;
    private readonly TimeSpan _timeout;

    public SubscriptionHttpFetcher() : this(
        () => new HttpClientHandler { Proxy = null, UseProxy = false }, TimeSpan.FromSeconds(90)) { }

    internal SubscriptionHttpFetcher(Func<HttpMessageHandler> handlerFactory, TimeSpan timeout)
    {
        _handlerFactory = handlerFactory;
        _timeout = timeout;
    }

    public async Task<SubscriptionFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_timeout);
            var token = deadline.Token;
            using var client = new HttpClient(_handlerFactory()) { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            var headers = ExtractHeaders(response.Headers);
            foreach (var h in response.Content.Headers)
                headers[h.Key] = string.Join(", ", h.Value);

            if (!response.IsSuccessStatusCode)
            {
                return new SubscriptionFetchResult
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    Headers = headers,
                    Error = $"HTTP {(int)response.StatusCode}"
                };
            }

            if (response.Content.Headers.ContentLength > MaximumBodyBytes)
                throw new InvalidDataException("Размер подписки превышает допустимые 4 МиБ.");

            // Enforce the limit on actual bytes too: Content-Length may be absent or incorrect.
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > MaximumBodyBytes)
                    throw new InvalidDataException("Размер подписки превышает допустимые 4 МиБ.");
                buffer.Write(chunk, 0, read);
            }

            // Let HttpContent retain the existing charset/BOM decoding behavior.
            using var boundedContent = new ByteArrayContent(buffer.ToArray());
            boundedContent.Headers.ContentType = response.Content.Headers.ContentType;
            var body = await boundedContent.ReadAsStringAsync(token).ConfigureAwait(false);
            return new SubscriptionFetchResult
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                Body = body,
                Headers = headers
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SubscriptionFetchResult { Success = false, Error = "Загрузка отменена." };
        }
        catch (OperationCanceledException)
        {
            return new SubscriptionFetchResult { Success = false, Error = "Timeout." };
        }
        catch (Exception ex)
        {
            return new SubscriptionFetchResult { Success = false, Error = ex.Message };
        }
    }

    private static Dictionary<string, string> ExtractHeaders(HttpResponseHeaders headers)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
            dict[header.Key] = string.Join(", ", header.Value);
        return dict;
    }

    private static string BuildUserAgent()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
        return $"NothingVpn/{version}";
    }
}
