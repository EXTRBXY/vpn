namespace NothingVpn.Application.Models;

public sealed class UserRuleSetModel
{
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Action { get; set; } = "direct";
    public string? BuiltinId { get; set; }
    public string? RemoteEtag { get; set; }
    public DateTimeOffset? LastDownloadedUtc { get; set; }
}

