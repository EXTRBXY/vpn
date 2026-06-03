using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace NothingVpn.Tray.Internal.Diagnostics;

internal static class ProxySmokeTest
{
    public static async Task<ProxySmokeTestResult> TcpConnectAsync(
        string host,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return ProxySmokeTestResult.Fail("Host is empty.");
        if (port <= 0 || port > 65535)
            return ProxySmokeTestResult.Fail("Port is invalid.");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cts.Token);
            return ProxySmokeTestResult.Ok();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProxySmokeTestResult.Fail("Timeout.");
        }
        catch (Exception ex)
        {
            return ProxySmokeTestResult.Fail(ex.Message);
        }
    }

    public static async Task<ProxySmokeTestResult> HttpConnectAsync(
        string proxyHost,
        int proxyPort,
        string targetHost,
        int targetPort,
        TimeSpan timeout)
    {
        var url = string.Equals(targetHost, "api.ipify.org", StringComparison.OrdinalIgnoreCase)
            ? "https://api.ipify.org"
            : $"https://{targetHost}:{targetPort}/";

        return await HttpGetViaProxyAsync(proxyHost, proxyPort, url, timeout);
    }

    public static async Task<ProxySmokeTestResult> HttpGetViaProxyAsync(
        string proxyHost,
        int proxyPort,
        string url,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"http://{proxyHost}:{proxyPort}"),
                UseProxy = true
            };
            using var http = new HttpClient(handler, disposeHandler: true);
            using var resp = await http.GetAsync(url, cts.Token);
            var body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            if (!resp.IsSuccessStatusCode)
                return ProxySmokeTestResult.Fail($"HTTP {(int)resp.StatusCode}: {body}");

            if (body.Length < 7 || body.Contains('\n', StringComparison.Ordinal))
                return ProxySmokeTestResult.Fail("Unexpected tunnel response.");

            return ProxySmokeTestResult.Ok();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProxySmokeTestResult.Fail("Timeout.");
        }
        catch (Exception ex)
        {
            return ProxySmokeTestResult.Fail(ex.Message);
        }
    }
}

internal readonly record struct ProxySmokeTestResult(bool Success, string? Error)
{
    public static ProxySmokeTestResult Ok() => new(true, null);
    public static ProxySmokeTestResult Fail(string error) => new(false, error);
}

