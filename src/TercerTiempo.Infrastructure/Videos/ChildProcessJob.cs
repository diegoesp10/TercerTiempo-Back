using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TercerTiempo.Infrastructure.Videos;

// En Windows, los procesos que lanza el servicio (uv y el Python que arranca uv) van a un Job Object con
// KILL_ON_JOB_CLOSE: si el servicio muere (se cierra la ventana de Start-Analyzer.ps1, un fallo), Windows cierra el
// job y mata con él a todo el árbol, en vez de dejar un Python huérfano ocupando la GPU. Los nietos entran solos en el
// job al crearse. Fuera de Windows no hace nada (allí el analizador no se lanza desde .NET).
internal static class ChildProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private static readonly Lazy<SafeFileHandle?> Job = new(Create);

    // false si no se pudo (fuera de Windows o sin permiso): el árbol se sigue matando a mano al parar o cancelar.
    public static bool TryAssign(Process process)
    {
        if (!OperatingSystem.IsWindows() || Job.Value is not { IsInvalid: false } job) return false;
        try
        {
            return AssignProcessToJobObject(job, process.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static SafeFileHandle? Create()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) return null;

        var info = new JobObjectExtendedLimit { BasicLimitInformation = new JobObjectBasicLimit { LimitFlags = JobObjectLimitKillOnJobClose } };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<JobObjectExtendedLimit>()))
        {
            job.Dispose();
            return null;
        }
        // El handle vive lo que viva el proceso: al terminar (como sea), Windows lo cierra y mata a los del job.
        return job;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobObjectExtendedLimit info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimit
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
    private struct JobObjectExtendedLimit
    {
        public JobObjectBasicLimit BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
