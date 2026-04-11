namespace NothingVpn.Tray.Internal.Updates;

/// <summary>Тексты диалогов и подписей для сценария обновления (только для пользователя).</summary>
internal static class AppUpdateUserMessages
{
    public const string DialogTitle = "Обновление";

    public const string ButtonDownloadInstall = "Скачать и установить";
    public const string ButtonInstallReady = "Установить обновление";
    public const string ButtonCheckUpdates = "Проверить обновления";

    public static string BannerLine(string remoteSemver) => $"Доступна версия {remoteSemver}.";

    public static string OfferDownloadOnStartup(string remoteSemver) =>
        $"Доступна версия {remoteSemver}.\n\nСкачать и установить обновление сейчас?";

    public static string ConfirmInstallDownloaded() =>
        "Установить загруженное обновление?\n\n" +
        "Nothing VPN закроется, обновление установится автоматически, затем программа запустится снова.";

    public static string ManualCheckVersionUnknown() =>
        "Не удалось определить версию программы.";

    public static string ManualCheckNetworkError() =>
        "Не удалось проверить обновления. Проверьте подключение к интернету и попробуйте снова.";

    public static string ManualCheckUpdateAvailable(string remoteSemver) =>
        $"Доступна версия {remoteSemver}. Скачать или установить её можно в уведомлении выше на этой вкладке.";

    public const string ManualCheckUpToDate = "У вас установлена последняя версия.";

    public const string DownloadFailedFallback = "Не удалось скачать файл обновления.";

    public const string ModalUnavailable = "Операция сейчас недоступна. Попробуйте позже.";

    public const string ModalWindowClosed = "Окно было закрыто.";

    public const string ChangelogTitleOk = "Что нового";
    public const string ChangelogTitleProblem = "Обновление";

    public static string ChangelogHeading(string version) => $"Версия {version}";

    public static string ChangelogLoadFailed(string version) =>
        $"Не удалось загрузить описание изменений для версии {version}.";

    public const string ChangelogEmpty = "Для этой версии нет текстового описания.";
}
