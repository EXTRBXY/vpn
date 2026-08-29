using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

public interface IStorageHealthPort
{
    IReadOnlyList<StorageIssueModel> DrainIssues();
}
