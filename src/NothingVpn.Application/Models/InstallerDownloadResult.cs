namespace NothingVpn.Application.Models;

public sealed record InstallerDownloadResult(bool Success, string? Error, string? InstallerPath = null);
