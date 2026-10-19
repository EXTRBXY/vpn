using NothingVpn.Application.Models;

namespace NothingVpn.Application.Ports;

/// <summary>
/// Поиск приложений для режима TUN по приложениям. Пути в результате нормализованы, без повторов и без уже выбранных.
/// </summary>
public interface IAppDiscoveryPort
{
    Task<IReadOnlyList<AppCandidate>> GetInstalledCandidatesAsync(
        IEnumerable<string>? alreadySelectedPaths,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AppCandidate>> GetRunningCandidatesAsync(
        IEnumerable<string>? alreadySelectedPaths,
        CancellationToken cancellationToken);
}
