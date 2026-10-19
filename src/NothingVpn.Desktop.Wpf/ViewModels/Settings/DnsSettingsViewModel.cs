using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Desktop.Wpf;

public sealed class DnsSettingsViewModel : SettingsSection
{
    private DnsPreset? _dnsPreset;
    private DnsMode _dnsMode = DnsPolicy.DefaultMode;
    private string _dohServer = "";
    private string _dohSni = "";
    private string _dohPath = "/dns-query";
    private bool _dnsDetourEditable = true;
    private ConnectionMode _connectionMode = ConnectionPolicy.DefaultMode;

    public DnsMode DnsMode
    {
        get => _dnsMode;
        set
        {
            if (_dnsMode == value) return;
            _dnsMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDohMode));
            UpdateDnsDetourAvailability();
        }
    }

    public bool IsDohMode => DnsMode == DnsMode.Doh;

    public string DohServer
    {
        get => _dohServer;
        set
        {
            _dohServer = value;
            OnPropertyChanged();
            UpdateDnsPreset();
        }
    }

    public string DohPath
    {
        get => _dohPath;
        set
        {
            _dohPath = value;
            OnPropertyChanged();
            UpdateDnsPreset();
        }
    }

    public string DohSni
    {
        get => _dohSni;
        set
        {
            _dohSni = value;
            OnPropertyChanged();
            UpdateDnsPreset();
        }
    }

    public DnsDetour DnsDetour { get; set; } = DnsPolicy.DefaultDetour;

    public bool IsDnsDetourEditable
    {
        get => _dnsDetourEditable;
        private set
        {
            if (_dnsDetourEditable == value) return;
            _dnsDetourEditable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DnsDetourHint));
        }
    }

    public string DnsDetourHint => !IsDohMode
        ? "Маршрут используется только для защищённого DNS."
        : !DnsDetourPolicy.AllowsProxyDetour(_connectionMode)
            ? "В режиме TUN для выбранных приложений DNS всегда идёт напрямую."
            : string.Empty;

    public DnsPreset? DnsPreset
    {
        get => _dnsPreset;
        set
        {
            if (_dnsPreset == value) return;
            _dnsPreset = value;
            OnPropertyChanged();
            if (value is { } preset) ApplyDnsPreset(preset);
        }
    }

    public void ApplyDnsPreset(DnsPreset preset)
    {
        if (preset == NothingVpn.Domain.Models.DnsPreset.Custom) return;
        var dns = DnsPolicy.ApplyPreset(preset, ToSettings());
        DohServer = dns.DohServer;
        DohSni = dns.DohSni;
        DohPath = dns.DohPath;
        OnPropertiesChanged(nameof(DohServer), nameof(DohSni), nameof(DohPath));
    }

    internal void Load(AppStateModel state)
    {
        _connectionMode = state.Mode;
        DnsMode = state.DnsMode;
        DohServer = state.DohServer;
        DohPath = state.DohPath;
        DohSni = state.DohSni;
        DnsDetour = state.DnsDetour;
        UpdateDnsPreset();
        OnPropertiesChanged(
            nameof(DnsMode), nameof(DohServer), nameof(DohPath), nameof(DohSni), nameof(DnsDetour), nameof(DnsPreset));
        UpdateDnsDetourAvailability();
    }

    /// <summary>Режим подключения меняется на главном экране; черновик DNS при этом сохраняется.</summary>
    internal void ApplyConnectionMode(ConnectionMode mode)
    {
        _connectionMode = mode;
        if (!DnsDetourPolicy.AllowsProxyDetour(mode))
        {
            DnsDetour = DnsDetour.Direct;
            OnPropertyChanged(nameof(DnsDetour));
        }
        UpdateDnsDetourAvailability();
    }

    internal DnsSettings ToSettings() => new()
    {
        Mode = DnsMode,
        DohServer = DohServer,
        DohPath = DohPath,
        DohSni = DohSni,
        Detour = DnsDetour
    };

    private void UpdateDnsDetourAvailability()
    {
        IsDnsDetourEditable = IsDohMode && DnsDetourPolicy.AllowsProxyDetour(_connectionMode);
        OnPropertyChanged(nameof(DnsDetourHint));
    }

    private void UpdateDnsPreset()
    {
        _dnsPreset = DnsPolicy.DetectPreset(new DnsSettings { DohServer = DohServer, DohSni = DohSni, DohPath = DohPath });
        OnPropertyChanged(nameof(DnsPreset));
    }
}
