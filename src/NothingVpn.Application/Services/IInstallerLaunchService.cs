namespace NothingVpn.Application.Services;

/// <summary>
/// Запуск скачанного установщика обновления (docs/UPDATE-HANDOFF.md). Интерфейс не повышен: установщик запускается
/// с его токеном и сам запрашивает UAC. Порядок: <see cref="Prepare"/> до выхода (ошибка не трогает VPN), затем
/// штатный выход с отключением VPN и освобождением мьютекса, и только после этого <see cref="Launch"/>.
/// </summary>
public interface IInstallerLaunchService
{
    /// <summary>Отказ, если процесс интерфейса повышен: установщик унаследовал бы его права.</summary>
    void EnsureLaunchAllowed();

    /// <summary>Проверки до выхода: права процесса и файл установщика. Возвращает полный путь для <see cref="Launch"/>.</summary>
    string Prepare(string installerPath);

    /// <summary>Повторяет проверки и запускает установщик с тихими аргументами обновления.</summary>
    void Launch(string installerPath);
}
