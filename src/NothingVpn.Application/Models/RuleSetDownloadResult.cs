namespace NothingVpn.Application.Models;

public sealed record RuleSetDownloadResult(
    bool Success,
    bool NotModified,
    string? NewEtag,
    string? Error);
