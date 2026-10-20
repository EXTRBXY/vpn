using NothingVpn.Application.Models;
using NothingVpn.Application.Ports;
using NothingVpn.Domain.Models;

namespace NothingVpn.Application.Services;

public sealed class StorageHealthService : IStorageHealthService
{
    private readonly IStorageHealthPort _port;
    private readonly ILogPort _log;

    public StorageHealthService(IStorageHealthPort port, ILogPort log)
    {
        _port = port;
        _log = log;
    }

    /// <summary>Забирает накопленные проблемы хранилища и пишет каждую в журнал (только имя файла, без пути).</summary>
    public IReadOnlyList<StorageIssueModel> DrainIssues()
    {
        var issues = _port.DrainIssues();
        foreach (var issue in issues)
        {
            var level = issue.RecoveredFromBackup ? LogLevel.Warn : LogLevel.Error;
            try { _log.Append(level, AppLogSources.Storage, $"{issue.Message} Файл: {Path.GetFileName(issue.Path)}."); }
            catch { }
        }

        return issues;
    }
}
