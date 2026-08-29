namespace NothingVpn.Application.Models;

public sealed record AppReleaseModel(
    string TagName,
    string Semver,
    string? Body,
    string InstallerDownloadUrl);
