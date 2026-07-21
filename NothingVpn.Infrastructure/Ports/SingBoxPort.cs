using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Mappers;
using NothingVpn.Infrastructure.Runtime;
using NothingVpn.Tray.Internal.SingBox;

namespace NothingVpn.Infrastructure.Ports;

public sealed class SingBoxPort : ISingBoxPort
{
    private readonly LegacyRuntimeContext _context;

    internal SingBoxPort(LegacyRuntimeContext context)
    {
        _context = context;
    }

    public event EventHandler? ProcessExited
    {
        add => _context.Runner.ProcessExited += value;
        remove => _context.Runner.ProcessExited -= value;
    }

    public bool IsRunning => _context.Runner.IsRunning;

    public string WriteConfig(VpnProfile profile, AppStateModel state)
    {
        var legacyProfile = LegacyModelMapper.ToLegacy(profile);
        var legacyState = LegacyModelMapper.ToLegacy(state);
        return SingBoxConfigGenerator.WriteConfig(_context.Paths, legacyProfile, legacyState);
    }

    public void Start(string configPath) => _context.Runner.Start(configPath);

    public void Stop() => _context.Runner.Stop();

    public void TryDeleteLastConfig() => _context.Runner.TryDeleteLastConfig();
}

