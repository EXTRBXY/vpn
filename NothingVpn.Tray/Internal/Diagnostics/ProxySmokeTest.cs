using System.Net.Sockets;
using System.Text;

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
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(proxyHost, proxyPort, cts.Token);
            await using var stream = client.GetStream();

            var req = $"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\nHost: {targetHost}:{targetPort}\r\nProxy-Connection: Keep-Alive\r\n\r\n";
            var bytes = Encoding.ASCII.GetBytes(req);
            await stream.WriteAsync(bytes, cts.Token);

            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer, cts.Token);
            if (read <= 0) return ProxySmokeTestResult.Fail("No response from proxy.");

            var head = Encoding.ASCII.GetString(buffer, 0, read);
            if (head.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) && head.Contains(" 200 "))
                return ProxySmokeTestResult.Ok();

            return ProxySmokeTestResult.Fail($"Unexpected response: {FirstLine(head)}");
        }
        catch (Exception ex)
        {
            return ProxySmokeTestResult.Fail(ex.Message);
        }
    }

    private static string FirstLine(string s)
    {
        var idx = s.IndexOf("\r\n", StringComparison.Ordinal);
        return idx >= 0 ? s[..idx] : s;
    }
}

internal readonly record struct ProxySmokeTestResult(bool Success, string? Error)
{
    public static ProxySmokeTestResult Ok() => new(true, null);
    public static ProxySmokeTestResult Fail(string error) => new(false, error);
}

