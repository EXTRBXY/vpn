using System.Windows;
using NothingVpn.Application.Services;
using NothingVpn.Presentation;

namespace NothingVpn.Desktop.Wpf;

/// <summary>Сообщения уровня приложения, которые не относятся к конкретному экрану.</summary>
internal static class AppDialogs
{
    private const string Caption = "Nothing VPN";

    public static void ShowStartupError(string message) =>
        System.Windows.MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void ShowUnexpectedError(Window? owner, string message)
    {
        if (owner is { IsVisible: true })
            System.Windows.MessageBox.Show(owner, message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            System.Windows.MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public static void ShowStorageIssues(Window? owner, IStorageHealthService health)
    {
        var issues = health.DrainIssues();
        if (issues.Count == 0) return;
        var recovered = issues.All(x => x.RecoveredFromBackup);
        System.Windows.MessageBox.Show(
            owner,
            recovered
                ? "Повреждённый файл данных восстановлен из резервной копии."
                : "Некоторые данные не удалось прочитать. Резервные файлы сохранены для восстановления.",
            Caption,
            MessageBoxButton.OK,
            recovered ? MessageBoxImage.Warning : MessageBoxImage.Error);
    }

    public static void ShowReleaseChangelog(Window? owner, ReleaseChangelog changelog)
    {
        var dialog = new ReleaseChangelogWindow(changelog);
        if (owner is { IsVisible: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
    }
}
