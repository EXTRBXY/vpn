using NothingVpn.Application.Models;

namespace NothingVpn.Application.Services;

public interface IStorageHealthService
{
    IReadOnlyList<StorageIssueModel> DrainIssues();
}
