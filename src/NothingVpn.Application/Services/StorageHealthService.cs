using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;

namespace NothingVpn.Application.Services;

public sealed class StorageHealthService : IStorageHealthService
{
    private readonly IStorageHealthPort _port;

    public StorageHealthService(IStorageHealthPort port) => _port = port;

    public IReadOnlyList<StorageIssueModel> DrainIssues() => _port.DrainIssues();
}
