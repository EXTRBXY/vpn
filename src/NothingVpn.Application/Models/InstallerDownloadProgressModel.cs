namespace NothingVpn.Application.Models;

public sealed record InstallerDownloadProgressModel(long BytesReceived, long? TotalBytes);
