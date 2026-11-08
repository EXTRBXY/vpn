using System.Text.Json.Serialization;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Models;

public sealed class DomainExceptionModel
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = string.Empty;

    [JsonPropertyName("includeSubdomains")]
    public bool IncludeSubdomains { get; set; } = DomainExceptionPolicy.DefaultIncludeSubdomains;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    public DomainExceptionModel Copy() => new()
    {
        Domain = Domain, IncludeSubdomains = IncludeSubdomains, Enabled = Enabled
    };
}
