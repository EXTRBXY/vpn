using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NothingVpn.Desktop.Wpf;

/// <summary>Сообщения под страницей настроек; их показывает <see cref="SettingsViewModel"/>.</summary>
internal interface ISettingsFeedback
{
    /// <summary>Сообщение, которое скрывается через несколько секунд.</summary>
    void ShowMessage(string message);

    /// <summary>Сообщение, которое остаётся до следующего (ошибки, ход загрузки).</summary>
    void ShowError(string message);
}

/// <summary>
/// Раздел страницы настроек. Раздел держит черновик своих полей; сохраняет их только <see cref="SettingsViewModel"/>
/// одной операцией для всех разделов.
/// </summary>
public abstract class SettingsSection : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void OnPropertiesChanged(params string[] names)
    {
        foreach (var name in names)
            OnPropertyChanged(name);
    }
}
