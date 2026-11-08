using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Infrastructure.Runtime;
using NothingVpn.Infrastructure.RuleSets;
using NothingVpn.Infrastructure.SingBox;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

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

    public string WriteConfig(VpnProfile profile, AppStateModel state, LocalSocksUpstream? upstream = null)
    {
        var ipRuleSetTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (state.DnsMode == DnsMode.Doh)
        {
            foreach (var ruleSet in state.UserRuleSets.Where(x => x.Enabled && x.Action == RuleSetAction.Direct))
            {
                if (!RuleSetPolicyValidator.IsSafeRuleSetFileName(ruleSet.FileName))
                    throw new ArgumentException("Имя файла списка должно быть простым именем .srs.");
                if (SrsRuleSetInspector.HasIpConditions(Path.Combine(_context.Paths.RuleSetsDir, ruleSet.FileName)))
                    ipRuleSetTags.Add(ruleSet.Tag);
            }
        }
        return SingBoxConfigGenerator.WriteConfig(_context.Paths, profile, state, upstream, ipRuleSetTags);
    }

    public void Start(string configPath) => _context.Runner.Start(configPath);

    public void ValidateConfig(string configPath) => _context.Runner.ValidateConfig(configPath);

    public void Stop() => _context.Runner.Stop();

    public void TryDeleteLastConfig() => _context.Runner.TryDeleteLastConfig();
}

