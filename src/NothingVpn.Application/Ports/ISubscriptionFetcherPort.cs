using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface ISubscriptionFetcherPort
{
    Task<SubscriptionFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default);
}
