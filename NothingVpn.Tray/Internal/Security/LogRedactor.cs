using System.Text.RegularExpressions;

namespace NothingVpn.Tray.Internal.Security;

internal static class LogRedactor
{
    // Keep these patterns conservative to avoid over-redacting normal text.
    private static readonly Regex GuidRegex = new(@"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex VlessUriRegex = new(@"vless:\/\/[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SubscriptionUrlRegex = new(
        @"(https?://[^\s/]+/sub/)[^\s?#]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Reality public key (pbk) often base64-like; short id (sid) hex.
    private static readonly Regex PbkRegex = new(@"(\bpbk=)([^&#\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SidRegex = new(@"(\bsid=)([^&#\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TokenRegex = new(@"(\b(token|access_token|refresh_token|password|passwd|secret|apikey|api_key)=)([^&#\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AuthHeaderRegex = new(@"(authorization:\s*bearer\s+)([^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UuidKvRegex = new(@"(\buuid=)([^&#\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        var s = line;
        s = VlessUriRegex.Replace(s, "vless://***");
        s = SubscriptionUrlRegex.Replace(s, "$1***");
        s = GuidRegex.Replace(s, "********-****-****-****-************");
        s = PbkRegex.Replace(s, "$1***");
        s = SidRegex.Replace(s, "$1***");
        s = UuidKvRegex.Replace(s, "$1***");
        s = TokenRegex.Replace(s, "$1***");
        s = AuthHeaderRegex.Replace(s, "$1***");
        return s;
    }
}

