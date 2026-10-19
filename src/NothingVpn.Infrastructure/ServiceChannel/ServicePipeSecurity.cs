using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace NothingVpn.Infrastructure.ServiceChannel;

/// <summary>ACL канала службы (ADR-0001).</summary>
public static class ServicePipeSecurity
{
    public static SecurityIdentifier LocalSystem { get; } = new(WellKnownSidType.LocalSystemSid, null);
    public static SecurityIdentifier Administrators { get; } = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    public static SecurityIdentifier Interactive { get; } = new(WellKnownSidType.InteractiveSid, null);
    public static SecurityIdentifier Network { get; } = new(WellKnownSidType.NetworkSid, null);

    /// <summary>
    /// SYSTEM и администраторы — полный доступ; интерактивные пользователи — чтение и запись, но не создание
    /// экземпляров канала (<see cref="PipeAccessRights.CreateNewInstance"/>), поэтому подсесть к каналу службы
    /// своим сервером они не могут; сеть — запрет. Наследование отключено. Владелец задаётся явно: клиент проверяет,
    /// что владелец канала — SYSTEM (<see cref="LocalSystemPipeServerVerifier"/>).
    /// </summary>
    public static PipeSecurity Create(SecurityIdentifier owner)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new PipeAccessRule(Network, PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(LocalSystem, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(Administrators, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(Interactive, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }
}

/// <summary>Отказ проверки подлинности сервера канала. Запрос (с UUID и ключами профиля) не отправлен.</summary>
public sealed class ServiceAuthenticationException(string reason)
    : InvalidOperationException($"Служба Nothing VPN не прошла проверку подлинности: {reason}. Данные подключения не отправлены.");

/// <summary>Проверка сервера канала до отправки первого сообщения. Подменяется в тестах.</summary>
internal interface IPipeServerVerifier
{
    /// <exception cref="ServiceAuthenticationException">Сервер — не служба Nothing VPN.</exception>
    void Verify(NamedPipeClientStream pipe);
}

/// <summary>
/// Сервер канала должен быть службой, работающей от LocalSystem, а не процессом пользователя, занявшим имя канала
/// раньше службы. Клиент без прав администратора не может открыть процесс и токен службы SYSTEM (OpenProcess
/// отказывает), поэтому проверка составная:
/// <list type="number">
/// <item>владелец объекта канала — SYSTEM. Служба задаёт его явно; процесс без привилегий SeRestore/SeTakeOwnership
/// не может назначить владельцем SID, которого нет в его токене;</item>
/// <item>сервер работает в сеансе 0 (<c>GetNamedPipeServerSessionId</c>) — там только службы;</item>
/// <item>процесс сервера (<c>GetNamedPipeServerProcessId</c>): если его токен удаётся открыть (сервер — процесс
/// того же пользователя или клиент повышен), SID пользователя токена обязан быть LocalSystem.</item>
/// </list>
/// </summary>
internal sealed class LocalSystemPipeServerVerifier : IPipeServerVerifier
{
    public static LocalSystemPipeServerVerifier Instance { get; } = new();

    public void Verify(NamedPipeClientStream pipe)
    {
        var owner = TryGetOwner(pipe);
        if (owner is null || !owner.Equals(ServicePipeSecurity.LocalSystem))
            throw new ServiceAuthenticationException($"владелец канала — {owner?.Value ?? "неизвестен"}, а не SYSTEM");

        var handle = pipe.SafePipeHandle;
        if (!NativeMethods.GetNamedPipeServerSessionId(handle, out var sessionId))
            throw new ServiceAuthenticationException($"не удалось узнать сеанс сервера ({Marshal.GetLastWin32Error()})");
        if (sessionId != 0)
            throw new ServiceAuthenticationException($"сервер канала работает в сеансе {sessionId}, а не в сеансе служб");

        if (!NativeMethods.GetNamedPipeServerProcessId(handle, out var processId))
            throw new ServiceAuthenticationException($"не удалось узнать процесс сервера ({Marshal.GetLastWin32Error()})");

        var user = TryGetProcessUser(processId);
        if (user is not null && !user.Equals(ServicePipeSecurity.LocalSystem))
            throw new ServiceAuthenticationException($"процесс сервера {processId} работает не от LocalSystem");
    }

    private static SecurityIdentifier? TryGetOwner(NamedPipeClientStream pipe)
    {
        try
        {
            return pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>SID пользователя процесса или <c>null</c>, если процесс или токен открыть нельзя.</summary>
    internal static SecurityIdentifier? TryGetProcessUser(uint processId)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        const uint TokenQuery = 0x0008;

        var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
            return null;

        try
        {
            if (!NativeMethods.OpenProcessToken(process, TokenQuery, out var token))
                return null;

            try
            {
                using var identity = new WindowsIdentity(token);
                return identity.User;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerSessionId(SafePipeHandle pipe, out uint serverSessionId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
