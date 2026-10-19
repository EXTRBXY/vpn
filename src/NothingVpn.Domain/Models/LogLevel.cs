namespace NothingVpn.Domain.Models;

/// <summary>
/// Уровень журнала в порядке возрастания важности: уровень sing-box в state.json и конфиге
/// (trace | debug | info | warn | error | fatal | panic) и уровень записей журнала приложения.
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
    Panic = 6
}
