namespace NothingVpn.Desktop.Wpf;

/// <summary>
/// Аргументы запуска, которые применяются к окну: <c>--start</c>, <c>--mode</c>, <c>--profile</c>
/// (также в форме <c>--key=value</c>). Режим остаётся строкой и разбирается при применении.
/// </summary>
internal sealed record StartupOptions(bool Start, string? Mode, string? ProfileId)
{
    public const string TakeoverSwitch = "--takeover";
    public const string SmokeTestSwitch = "--smoke-test";

    public static bool HasSwitch(string[] args, string name) =>
        args.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    public static StartupOptions Parse(string[] args)
    {
        var start = false;
        string? mode = null;
        string? profile = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Trim().Equals("--start", StringComparison.OrdinalIgnoreCase))
            {
                start = true;
                continue;
            }
            if (Read(args, ref i, "--mode", out var value))
                mode = value;
            else if (Read(args, ref i, "--profile", out value))
                profile = value;
        }
        return new(start, mode, profile);
    }

    private static bool Read(string[] args, ref int i, string key, out string? value)
    {
        value = null;
        var arg = args[i];
        if (arg.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
        {
            value = arg[(key.Length + 1)..].Trim('"');
            return true;
        }
        if (!arg.Equals(key, StringComparison.OrdinalIgnoreCase))
            return false;
        if (i + 1 < args.Length)
            value = args[++i].Trim('"');
        return true;
    }
}
