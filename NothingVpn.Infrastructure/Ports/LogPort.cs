using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Runtime;

namespace NothingVpn.Infrastructure.Ports;

public sealed class LogPort : ILogPort
{
    private readonly LegacyRuntimeContext _context;

    internal LogPort(LegacyRuntimeContext context)
    {
        _context = context;
    }

    public string SnapshotText(int minLevel)
    {
        var text = _context.LogStore.SnapshotText(minLevel, out _);
        return text;
    }
}

