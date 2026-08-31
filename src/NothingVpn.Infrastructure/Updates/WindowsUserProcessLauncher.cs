using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NothingVpn.Infrastructure.Updates;

internal interface IUserProcessLauncher
{
    void EnsureAvailable();
    Process Start(ProcessStartInfo info);
}

internal sealed class WindowsUserProcessLauncher : IUserProcessLauncher
{
    internal sealed record TokenFacts(string UserSid, int SessionId, bool Elevated, int Integrity, bool Administrator);
    private const uint TokenAccess = 0x0001 | 0x0002 | 0x0008; // AssignPrimary, Duplicate, Query
    private const uint MaximumAllowed = 0x02000000;
    private const uint QueryLimitedInformation = 0x1000;
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;

    public void EnsureAvailable()
    {
        using var token = SelectToken(out _);
    }

    public Process Start(ProcessStartInfo info)
    {
        using var token = SelectToken(out var elevatedCaller);
        var environment = ReadEnvironment(token);
        foreach (var pair in info.Environment)
            environment[pair.Key] = pair.Value ?? string.Empty;

        if (!elevatedCaller)
        {
            info.Environment.Clear();
            foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
            return Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить процесс обновления.");
        }

        // Explicit application path: never resolve an executable via PATH, COMSPEC or shell associations.
        var command = new StringBuilder($"\"{info.FileName}\" {info.Arguments}");
        if (command.Length >= 1024)
            throw new InvalidOperationException("Слишком длинная команда запуска обновления.");
        var block = string.Join('\0', environment.Select(p => $"{p.Key}={p.Value}")) + "\0\0";
        var pointer = Marshal.StringToHGlobalUni(block);
        try
        {
            var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 1, ShowWindow = 0 };
            // The desktop user's profile is already loaded by Explorer; do not reload it.
            if (!CreateProcessWithTokenW(token, 0, info.FileName, command,
                    CreateSuspended | CreateUnicodeEnvironment | CreateNoWindow,
                    pointer, info.WorkingDirectory, ref startup, out var created))
                throw NativeError("Не удалось запустить обновление без прав администратора");
            using var processHandle = new SafeProcessHandle(created.Process, true);
            using var threadHandle = new SafeWaitHandle(created.Thread, true);
            try
            {
                using var childToken = OpenToken(processHandle, 0x0002 | 0x0008);
                ValidateTarget(ReadFacts(token), ReadFacts(childToken));
                if (ResumeThread(threadHandle) == uint.MaxValue)
                    throw NativeError("Не удалось продолжить процесс обновления");
                var process = Process.GetProcessById(created.ProcessId);
                _ = process.SafeHandle;
                return process;
            }
            catch
            {
                TerminateProcess(processHandle, 1);
                throw;
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static SafeAccessTokenHandle SelectToken(out bool elevatedCaller)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentToken = OpenToken(currentProcess.SafeHandle, TokenAccess);
        var current = ReadFacts(currentToken);
        elevatedCaller = current.Elevated || current.Administrator || current.Integrity > 8192;
        if (!elevatedCaller)
        {
            ValidateTarget(current, current);
            return Duplicate(currentToken);
        }

        var shellWindow = GetShellWindow();
        if (shellWindow == IntPtr.Zero || GetWindowThreadProcessId(shellWindow, out var shellId) == 0)
            throw new InvalidOperationException("Не найден обычный рабочий стол Windows для запуска обновления.");
        using var shellProcess = OpenProcess(QueryLimitedInformation, false, shellId);
        if (shellProcess.IsInvalid) throw NativeError("Не удалось проверить рабочий стол Windows");
        using var shellToken = OpenToken(shellProcess, 0x0002 | 0x0008);
        ValidateTarget(current, ReadFacts(shellToken));
        return Duplicate(shellToken);
    }

    internal static void ValidateTarget(TokenFacts current, TokenFacts target)
    {
        if (target.UserSid != current.UserSid || target.SessionId != current.SessionId)
            throw new InvalidOperationException("Обновление нельзя передать другой учётной записи или сеансу Windows. Запустите клиент под пользователем текущего рабочего стола.");
        if (target.Elevated || target.Administrator || target.Integrity != 8192)
            throw new InvalidOperationException("Windows не предоставила процесс с обычными правами. Обновление не запущено.");
    }

    internal static TokenFacts ReadCurrentFacts()
    {
        using var process = Process.GetCurrentProcess();
        using var token = OpenToken(process.SafeHandle, 0x0002 | 0x0008);
        return ReadFacts(token);
    }

    private static SafeAccessTokenHandle Duplicate(SafeAccessTokenHandle source)
    {
        // Access to the duplicate is bounded by its DACL; this does not add privileges to the token.
        if (!DuplicateTokenEx(source, MaximumAllowed, IntPtr.Zero, 2, 1, out var token))
            throw NativeError("Не удалось подготовить права процесса обновления");
        return token;
    }

    private static SafeAccessTokenHandle OpenToken(SafeProcessHandle process, uint access)
    {
        if (!OpenProcessToken(process, access, out var token))
            throw NativeError("Не удалось проверить права процесса");
        return token;
    }

    private static TokenFacts ReadFacts(SafeAccessTokenHandle token)
    {
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        var sid = identity.User?.Value ?? throw new InvalidOperationException("Не удалось определить пользователя Windows.");
        var administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var elevation = ReadTokenInt(token, 20) != 0;
        var session = ReadTokenInt(token, 12);
        var pointer = ReadTokenInfo(token, 25);
        try
        {
            var integritySid = new SecurityIdentifier(Marshal.ReadIntPtr(pointer));
            var integrity = int.Parse(integritySid.Value.Split('-')[^1], System.Globalization.CultureInfo.InvariantCulture);
            return new TokenFacts(sid, session, elevation, integrity, administrator);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static int ReadTokenInt(SafeAccessTokenHandle token, int kind)
    {
        var pointer = ReadTokenInfo(token, kind);
        try { return Marshal.ReadInt32(pointer); }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static IntPtr ReadTokenInfo(SafeAccessTokenHandle token, int kind)
    {
        GetTokenInformation(token, kind, IntPtr.Zero, 0, out var size);
        if (size <= 0) throw NativeError("Не удалось прочитать права Windows");
        var pointer = Marshal.AllocHGlobal(size);
        if (GetTokenInformation(token, kind, pointer, size, out _)) return pointer;
        var error = NativeError("Не удалось прочитать права Windows");
        Marshal.FreeHGlobal(pointer);
        throw error;
    }

    private static SortedDictionary<string, string> ReadEnvironment(SafeAccessTokenHandle token)
    {
        if (!CreateEnvironmentBlock(out var pointer, token, false))
            throw NativeError("Не удалось подготовить окружение пользователя");
        try
        {
            var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var cursor = pointer; ;)
            {
                var entry = Marshal.PtrToStringUni(cursor)!;
                if (entry.Length == 0) return result;
                var separator = entry.IndexOf('=', 1);
                if (separator > 0) result[entry[..separator]] = entry[(separator + 1)..];
                cursor = IntPtr.Add(cursor, (entry.Length + 1) * 2);
            }
        }
        finally { DestroyEnvironmentBlock(pointer); }
    }

    internal static int GetPipeServerId(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var id)) throw NativeError("Не удалось проверить канал обновления");
        return id;
    }

    private static Win32Exception NativeError(string message)
    {
        var code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{message}: {new Win32Exception(code).Message} (Win32 {code}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public int ProcessId, ThreadId;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, IntPtr information, int length, out int returned);
    [DllImport("userenv.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(out IntPtr environment, SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool inherit);
    [DllImport("userenv.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logonFlags, string application, StringBuilder command, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeProcessHandle process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out int processId);
}
