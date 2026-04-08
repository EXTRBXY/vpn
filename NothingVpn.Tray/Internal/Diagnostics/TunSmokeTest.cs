using System.Net.Http;

namespace NothingVpn.Tray.Internal.Diagnostics;

/// <summary>
/// Проверка HTTP из текущего процесса. В режиме sing-box «TUN (выбранные приложения)» результат
/// отражает маршрут этого процесса, а не обязательно выбранных в списке .exe.
/// </summary>
internal static class TunSmokeTest
{
    public sealed record Result(bool Success, string? Error);

    public static async Task<Result> IpifyAsync(TimeSpan timeout)
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler
            {
                Proxy = null,
                UseProxy = false
            })
            {
                Timeout = timeout
            };

            using var resp = await http.GetAsync("https://api.ipify.org");
            var text = (await resp.Content.ReadAsStringAsync()).Trim();
            if (!resp.IsSuccessStatusCode)
                return new Result(false, $"HTTP {(int)resp.StatusCode}: {text}");
            if (text.Length < 7)
                return new Result(false, "Unexpected response.");
            return new Result(true, null);
        }
        catch (TaskCanceledException)
        {
            return new Result(false, "Timeout.");
        }
        catch (Exception ex)
        {
            return new Result(false, ex.Message);
        }
    }
}

