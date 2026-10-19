namespace NothingVpn.Tray;

static class Program
{
    /// <summary>Запуск установщика обновления после выхода и освобождения мьютекса (см. IInstallerLaunchService).</summary>
    internal static Action? LaunchAfterExit;

    [STAThread]
    static void Main(string[] args)
    {
        const string appId = "NothingVpn.Tray";
        args ??= Array.Empty<string>();

        var primary = Internal.Windows.SingleInstance.TryCreatePrimary(appId, out var alreadyRunning);
        if (alreadyRunning)
        {
            // Forward to the already running instance and exit immediately.
            Internal.Windows.SingleInstance.ForwardToPrimary(appId, args, TimeSpan.FromMilliseconds(650));
            return;
        }

        if (primary is null) return;

        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var startup = StartupArgs.Parse(args);
        using (primary)
        {
            System.Windows.Forms.Application.Run(new MainAppContext(startup, primary));
        }

        try { LaunchAfterExit?.Invoke(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Nothing VPN", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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