namespace NothingVpn.Tray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        const string appId = "NothingVpn.Tray";
        args ??= Array.Empty<string>();
        var takeover = HasFlag(args, "--takeover");

        Internal.Windows.SingleInstance? primary = null;
        if (takeover)
        {
            // Elevated takeover: wait until the previous instance exits and releases the mutex.
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                primary = Internal.Windows.SingleInstance.TryCreatePrimary(appId, out var alreadyRunning);
                if (!alreadyRunning && primary is not null) break;
                try { Thread.Sleep(200); } catch { }
            }
        }
        else
        {
            primary = Internal.Windows.SingleInstance.TryCreatePrimary(appId, out var alreadyRunning);
            if (alreadyRunning)
            {
                // Forward to the already running instance and exit immediately.
                Internal.Windows.SingleInstance.ForwardToPrimary(appId, args, TimeSpan.FromMilliseconds(650));
                return;
            }
        }

        if (primary is null) return;

        ApplicationConfiguration.Initialize();
        var startup = StartupArgs.Parse(args);
        using (primary)
        {
            Application.Run(new MainAppContext(startup, primary));
        }
    }    

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (var a0 in args)
        {
            var a = (a0 ?? "").Trim();
            if (a.Equals(flag, StringComparison.OrdinalIgnoreCase)) return true;
            if (a.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

internal sealed record StartupArgs(
    bool AutoStart,
    string? Mode,
    string? ProfileId)
{
    public static StartupArgs Parse(string[]? args)
    {
        if (args is null || args.Length == 0) return new StartupArgs(false, null, null);

        bool autoStart = false;
        string? mode = null;
        string? profileId = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = (args[i] ?? "").Trim();
            if (a.Length == 0) continue;

            if (a.Equals("--start", StringComparison.OrdinalIgnoreCase))
            {
                autoStart = true;
                continue;
            }

            if (TryReadValue(args, ref i, "--mode", out var vMode))
            {
                mode = vMode;
                continue;
            }

            if (TryReadValue(args, ref i, "--profile", out var vProfile))
            {
                profileId = vProfile;
                continue;
            }
        }

        return new StartupArgs(autoStart, mode, profileId);
    }

    private static bool TryReadValue(string[] args, ref int idx, string key, out string? value)
    {
        value = null;
        var a = (args[idx] ?? "").Trim();

        if (a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = a[(key.Length + 1)..].Trim().Trim('"');
            return true;
        }

        if (a.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            if (idx + 1 >= args.Length) return true;
            idx++;
            value = (args[idx] ?? "").Trim().Trim('"');
            return true;
        }

        return false;
    }
}