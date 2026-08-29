using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Security;

namespace NothingVpn.Infrastructure.Ports;

public sealed class StorageHealthPort : IStorageHealthPort
{
    public IReadOnlyList<StorageIssueModel> DrainIssues() => StorageIssueRegistry.Drain();
}
