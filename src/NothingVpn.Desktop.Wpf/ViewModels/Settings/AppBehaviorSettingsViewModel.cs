using NothingVpn.Application.Models;
using NothingVpn.Domain.Models;
using NothingVpn.Domain.Policies;

namespace NothingVpn.Desktop.Wpf;

/// <summary>Разделы «Приложение» и «Журнал»: поведение окна при закрытии и уровень журнала sing-box.</summary>
public sealed class AppBehaviorSettingsViewModel : SettingsSection
{
    public LogLevel LogLevel { get; set; } = SingBoxLogLevelPolicy.DefaultLevel;
    public AppCloseBehavior CloseBehavior { get; set; } = AppCloseBehaviors.Default;

    internal void Load(AppStateModel state)
    {
        LogLevel = state.SingBoxLogLevel;
        CloseBehavior = AppCloseBehaviors.Values.Normalize(state.CloseBehavior);
        OnPropertiesChanged(nameof(LogLevel), nameof(CloseBehavior));
    }
}
