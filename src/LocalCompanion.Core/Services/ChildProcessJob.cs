using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalCompanion.Services;

/// <summary>
/// 親プロセス終了時に子も一緒に落ちるよう、Windows Job Object に割り当てる。
/// （タスクマネージャの強制終了など、Managed 終了処理が走らない場合の安全網）
/// </summary>
internal static class ChildProcessJob
{
    private static readonly object Gate = new();
    private static IntPtr _job = IntPtr.Zero;
    private static bool _initFailed;

    public static void Assign(Process process)
    {
        if (process is null || process.HasExited)
            return;

        try
        {
            var job = EnsureJob();
            if (job == IntPtr.Zero)
                return;

            if (!AssignProcessToJobObject(job, process.Handle))
            {
                var err = Marshal.GetLastWin32Error();
                StartupLog.Write($"ChildProcessJob.Assign failed win32={err} pid={process.Id}");
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex, "ChildProcessJob.Assign");
        }
    }

    private static IntPtr EnsureJob()
    {
        lock (Gate)
        {
            if (_job != IntPtr.Zero || _initFailed)
                return _job;

            try
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero)
                {
                    _initFailed = true;
                    return IntPtr.Zero;
                }

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                    {
                        LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                    },
                };

                var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                var ptr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(_job, JobObjectInfoClass.ExtendedLimitInformation, ptr, (uint)length))
                    {
                        CloseHandle(_job);
                        _job = IntPtr.Zero;
                        _initFailed = true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            catch (Exception ex)
            {
                StartupLog.Write(ex, "ChildProcessJob.EnsureJob");
                _initFailed = true;
                _job = IntPtr.Zero;
            }

            return _job;
        }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    private enum JobObjectInfoClass
    {
        ExtendedLimitInformation = 9,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        JobObjectInfoClass jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
