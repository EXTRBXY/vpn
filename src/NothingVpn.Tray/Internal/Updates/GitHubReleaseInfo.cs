namespace NothingVpn.Tray.Internal.Updates;

internal sealed record GitHubReleaseInfo(string TagName, string Semver, string? Body, string InstallerDownloadUrl);
