using System.Runtime.InteropServices;
using System.Text;

namespace MonCore.MacOS;

/// <summary>Layouts from sys/proc_info.h. Convert libproc's Mach CPU ticks to nanoseconds at this boundary.</summary>
internal sealed unsafe partial class DarwinProcessApi : IMacProcessApi
{
    private const string LibProc = "/usr/lib/libproc.dylib";
    private int[] _pids = new int[1024];
    private readonly TimebaseInfo _timebase;

    public DarwinProcessApi()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("libproc requires macOS.");
        if (MachTimebaseInfo(out _timebase) != 0 || _timebase.Numerator == 0 || _timebase.Denominator == 0)
            throw new IOException("macOS could not report its CPU timebase.");
    }

    public int[]? EnumeratePids()
    {
        // Grow and retry if processes appeared between sizing and reading. Never silently truncate the list.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var needed = ProcListPids(1, 0, null, 0);
            if (needed <= 0) return null;
            var capacity = checked(needed / sizeof(int) + 256);
            if (_pids.Length < capacity) _pids = new int[capacity];
            int written;
            fixed (int* buffer = _pids) written = ProcListPids(1, 0, buffer, _pids.Length * sizeof(int));
            if (written <= 0 || written % sizeof(int) != 0) return null;
            if (written < _pids.Length * sizeof(int)) return _pids[..(written / sizeof(int))];
            _pids = new int[checked(_pids.Length * 2)];
        }

        return null;
    }

    public NativeProcessInfo? ReadInfo(int pid)
    {
        TaskAllInfo info;
        if (ProcPidInfo(pid, 2, 0, &info, sizeof(TaskAllInfo)) == sizeof(TaskAllInfo))
            return Convert(info.Bsd, info.Task);
        // A protected task can still have readable BSD identity/metadata. Keep unknown CPU and memory unknown.
        return ReadIdentity(pid);
    }

    public NativeProcessInfo? ReadIdentity(int pid)
    {
        BsdInfo info;
        return ProcPidInfo(pid, 3, 0, &info, sizeof(BsdInfo)) == sizeof(BsdInfo) ? Convert(info, null) : null;
    }

    public string ReadPath(int pid)
    {
        var buffer = stackalloc byte[4096];
        var length = ProcPidPath(pid, buffer, 4096);
        return length is > 0 and < 4096 ? Encoding.UTF8.GetString(new ReadOnlySpan<byte>(buffer, length)) : "";
    }

    private NativeProcessInfo Convert(BsdInfo bsd, TaskInfo? task)
    {
        var name = Text(bsd.Name, 32);
        if (name.Length == 0) name = Text(bsd.Command, 16);
        ulong? cpu = task is { } timing && timing.TotalUser <= ulong.MaxValue - timing.TotalSystem
            ? MachTimeToNanoseconds(timing.TotalUser + timing.TotalSystem, _timebase.Numerator, _timebase.Denominator)
            : null;
        return new NativeProcessInfo((int)bsd.Pid, (int)bsd.ParentPid, bsd.StartSeconds, bsd.StartMicroseconds,
            name.Length == 0 ? $"PID {bsd.Pid}" : name, cpu, task?.ResidentSize,
            task is { ThreadCount: >= 0 } threads ? threads.ThreadCount : null);
    }

    private static string Text(byte* buffer, int size)
    {
        var span = new ReadOnlySpan<byte>(buffer, size);
        var end = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? span : span[..end]);
    }

    internal static ulong? MachTimeToNanoseconds(ulong ticks, uint numerator, uint denominator)
    {
        if (denominator == 0 || numerator == 0) return null;
        var scaled = (UInt128)ticks * numerator / denominator;
        return scaled <= ulong.MaxValue ? (ulong)scaled : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimebaseInfo
    {
        public uint Numerator, Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BsdInfo
    {
        public uint Flags, Status, ExitStatus, Pid, ParentPid, Uid, Gid, RealUid, RealGid, SavedUid, SavedGid, Reserved;
        public fixed byte Command[16];
        public fixed byte Name[32];
        public uint OpenFiles, ProcessGroupId, JobControlCount, TerminalDevice, TerminalGroupId;
        public int Nice;
        public ulong StartSeconds, StartMicroseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TaskInfo
    {
        public ulong VirtualSize, ResidentSize, TotalUser, TotalSystem, ThreadsUser, ThreadsSystem;
        public int Policy, Faults, PageIns, CowFaults, MessagesSent, MessagesReceived, MachCalls, UnixCalls,
            ContextSwitches, ThreadCount, RunningThreads, Priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TaskAllInfo
    {
        public BsdInfo Bsd;
        public TaskInfo Task;
    }

    [LibraryImport(LibProc, EntryPoint = "proc_listpids")]
    private static partial int ProcListPids(uint type, uint typeInfo, void* buffer, int size);

    [LibraryImport(LibProc, EntryPoint = "proc_pidinfo")]
    private static partial int ProcPidInfo(int pid, int flavor, ulong argument, void* buffer, int size);

    [LibraryImport(LibProc, EntryPoint = "proc_pidpath")]
    private static partial int ProcPidPath(int pid, byte* buffer, uint size);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "mach_timebase_info")]
    private static partial int MachTimebaseInfo(out TimebaseInfo info);
}
