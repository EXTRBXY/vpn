using System.Windows.Input;
using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Desktop.Wpf;

/// <summary>XMUX XHTTP по умолчанию: применяется к профилям, в extra которых нет своего xmux.</summary>
public sealed class XraySettingsViewModel : SettingsSection
{
    public XraySettingsViewModel()
    {
        ResetCommand = new RelayCommand(Reset);
    }

    public string MaxConcurrency { get; set; } = XhttpPolicy.DefaultXmuxMaxConcurrency;
    public string MaxConnections { get; set; } = XhttpPolicy.DefaultXmuxMaxConnections;
    public string CMaxReuseTimes { get; set; } = XhttpPolicy.DefaultXmuxCMaxReuseTimes;
    public string HMaxRequestTimes { get; set; } = XhttpPolicy.DefaultXmuxHMaxRequestTimes;
    public string HMaxReusableSecs { get; set; } = XhttpPolicy.DefaultXmuxHMaxReusableSecs;
    public ICommand ResetCommand { get; }

    internal void Load(AppStateModel state)
    {
        MaxConcurrency = state.XhttpXmuxMaxConcurrency;
        MaxConnections = state.XhttpXmuxMaxConnections;
        CMaxReuseTimes = state.XhttpXmuxCMaxReuseTimes;
        HMaxRequestTimes = state.XhttpXmuxHMaxRequestTimes;
        HMaxReusableSecs = state.XhttpXmuxHMaxReusableSecs;
        NotifyAll();
    }

    internal XmuxSettings ToSettings() => new()
    {
        MaxConcurrency = MaxConcurrency.Trim(),
        MaxConnections = MaxConnections.Trim(),
        CMaxReuseTimes = CMaxReuseTimes.Trim(),
        HMaxRequestTimes = HMaxRequestTimes.Trim(),
        HMaxReusableSecs = HMaxReusableSecs.Trim()
    };

    private void Reset()
    {
        var defaults = new XmuxSettings();
        MaxConcurrency = defaults.MaxConcurrency;
        MaxConnections = defaults.MaxConnections;
        CMaxReuseTimes = defaults.CMaxReuseTimes;
        HMaxRequestTimes = defaults.HMaxRequestTimes;
        HMaxReusableSecs = defaults.HMaxReusableSecs;
        NotifyAll();
    }

    private void NotifyAll() => OnPropertiesChanged(
        nameof(MaxConcurrency), nameof(MaxConnections), nameof(CMaxReuseTimes),
        nameof(HMaxRequestTimes), nameof(HMaxReusableSecs));
}
