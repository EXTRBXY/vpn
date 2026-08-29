using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace NothingVpn.Desktop.Wpf;

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
        var mutex = new Mutex(true, $@"Global\{appId}", out var createdNew);
        alreadyRunning = !createdNew;
        if (createdNew) return new SingleInstance(mutex, $"{appId}.pipe");
        mutex.Dispose();
        return null;
    }

    public static void ForwardToPrimary(string appId, string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", $"{appId}.pipe", PipeDirection.Out);
            client.Connect(650);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            foreach (var arg in args) writer.WriteLine(arg ?? string.Empty);
        }
        catch { }
    }

    public void StartServer(Action<string[]> onArgs)
    {
        _serverTask ??= Task.Run(() => ServerLoopAsync(onArgs, _cts.Token));
    }

    private async Task ServerLoopAsync(Action<string[]> onArgs, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = CreateServer();
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, false);
                var lines = new List<string>();
                while (await reader.ReadLineAsync(cancellationToken) is { } line) lines.Add(line);
                onArgs(lines.ToArray());
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { await Task.Delay(150, cancellationToken); } catch { }
            }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        var sid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Не удалось определить пользователя Windows.");
        security.AddAccessRule(new PipeAccessRule(
            sid,
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        try { _serverTask?.Wait(400); } catch { }
        _cts.Dispose();
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
