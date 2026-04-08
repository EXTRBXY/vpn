using System.IO.Pipes;
using System.Text;

namespace NothingVpn.Tray.Internal.Windows;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    public static SingleInstance? TryCreatePrimary(string appId, out bool alreadyRunning)
    {
        var mutexName = $@"Global\{appId}";
        var pipeName = $"{appId}.pipe";

        var createdNew = false;
        var mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out createdNew);
        if (!createdNew)
        {
            alreadyRunning = true;
            try { mutex.Dispose(); } catch { }
            return null;
        }

        alreadyRunning = false;
        return new SingleInstance(mutex, pipeName);
    }

    public static bool ForwardToPrimary(string appId, string[] args, TimeSpan timeout)
    {
        var pipeName = $"{appId}.pipe";
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: pipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            client.Connect((int)timeout.TotalMilliseconds);

            using var sw = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            // One arg per line; args are expected to not contain newlines.
            foreach (var a in args ?? Array.Empty<string>())
                sw.WriteLine(a ?? "");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartServer(Action<string[]> onArgs)
    {
        if (_serverTask is not null) return;
        _serverTask = Task.Run(() => ServerLoopAsync(onArgs, _cts.Token));
    }

    private async Task ServerLoopAsync(Action<string[]> onArgs, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName: _pipeName,
                    direction: PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var sr = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
                var lines = new List<string>();
                while (true)
                {
                    var line = await sr.ReadLineAsync(ct);
                    if (line is null) break;
                    lines.Add(line);
                }

                onArgs(lines.ToArray());
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // best-effort; keep server alive
                try { await Task.Delay(150, ct); } catch { }
            }
        }
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _serverTask?.Wait(400); } catch { }
        try { _cts.Dispose(); } catch { }
        try { _mutex.ReleaseMutex(); } catch { }
        try { _mutex.Dispose(); } catch { }
    }
}

