using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NothingVpn.Infrastructure.Windows;

internal sealed class ProcessJobScope : IDisposable
{
    private readonly SafeHandle _jobHandle;

    private ProcessJobScope(SafeHandle jobHandle)
    {
        _jobHandle = jobHandle;
    }

    public static ProcessJobScope? TryAttach(Process process)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var jobHandle = CreateJobObjectW(null, null);
            if (jobHandle.IsInvalid)
                return null;

            var info = new JobjectLimitInformation
            {
                BasicLimitInformation = new JobjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            if (!SetInformationJobObject(
                    jobHandle,
                    JobObjectInfoClass.ExtendedLimitInformation,
                    ref info,
                    Marshal.SizeOf<JobjectLimitInformation>()))
            {
                jobHandle.Dispose();
                return null;
            }

            if (!AssignProcessToJobObject(jobHandle, process.Handle))
            {
                jobHandle.Dispose();
                return null;
            }

            return new ProcessJobScope(jobHandle);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _jobHandle.Dispose();
    }

    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobjectLimitInformation
    {
        public JobjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr? lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeHandle hJob,
        JobObjectInfoClass jobObjectInfoClass,
        ref JobjectLimitInformation lpJobObjectInfo,
        int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeHandle hJob, IntPtr hProcess);
}
