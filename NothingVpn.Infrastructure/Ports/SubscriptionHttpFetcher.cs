using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;

namespace NothingVpn.Infrastructure.Ports;

public sealed class SubscriptionHttpFetcher : ISubscriptionFetcherPort
{
    private static readonly string UserAgent = BuildUserAgent();

    public async Task<SubscriptionFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = null,
                UseProxy = false
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new SubscriptionFetchResult
            {
                Success = true,
                StatusCode = (int)response.StatusCode,
                Body = body,
                Headers = headers
            };
        }
        catch (TaskCanceledException)
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
