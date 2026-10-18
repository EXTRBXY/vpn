using NothingVpn.Application.Models;

namespace NothingVpn.Presentation;

public sealed record ReleaseChangelog(
    string Version,
    string Title,
    string Heading,
    string Body,
    bool LoadFailed)
{
    public const string TitleOk = "Что нового";
    public const string TitleProblem = "Обновление";
    public const string EmptyBody = "Для этой версии нет текстового описания.";

    public static ReleaseChangelog FromRelease(string version, AppReleaseModel release)
    {
        var body = NormalizeBody(release.Body);
        return new ReleaseChangelog(
            version,
            TitleOk,
            $"Версия {version}",
            body.Length == 0 ? EmptyBody : body,
            LoadFailed: false);
    }

    public static ReleaseChangelog Failed(string version) =>
        new(
            version,
            TitleProblem,
            $"Не удалось загрузить описание изменений для версии {version}.",
            string.Empty,
            LoadFailed: true);

    private static string NormalizeBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        return body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim()
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
    }
}
