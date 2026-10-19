using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Application.Services;

/// <summary>Аргументы перезапуска с повышением прав — внешний контракт (docs/REFACTORING.md, раздел 1).</summary>
public static class StartupArguments
{
    public static string Takeover(ConnectionMode mode, string profileId) =>
        $"--takeover --start --mode {mode.ToValue()} --profile \"{profileId}\"";
}
