using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonCore.MacOS;

internal sealed unsafe partial class DarwinSystemReader : IMacSystemReader
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private readonly uint _host;
    private readonly int _logicalProcessors;
    private bool _disposed;
    private readonly MacActivityReader _activity = new();
    private readonly DarwinCpuFrequency _frequency = new();

    public DarwinSystemReader()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("The macOS collector requires macOS.");
        _host = MachHostSelf();
        nuint length = sizeof(uint);
        _logicalProcessors = SysctlInt("hw.logicalcpu", out var processors, ref length, IntPtr.Zero, 0) == 0 &&
            processors is > 0 and <= int.MaxValue ? (int)processors : Environment.ProcessorCount;
    }

    public SystemObservation Read(string volumePath, string? networkId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var instant = Stopwatch.GetTimestamp();
        var timestamp = DateTimeOffset.Now;
        uint count = 4; // HOST_CPU_LOAD_INFO_COUNT: four natural_t CPU states.
        CpuCounters? cpu = HostStatistics(_host, 3, out var native, ref count) == 0 && count == 4
            ? new CpuCounters(native.User, native.System, native.Idle, native.Nice)
            : null;
        nuint length = sizeof(ulong);
        ulong? memory = SysctlByName("hw.memsize", out var bytes, ref length, IntPtr.Zero, 0) == 0
                        && length == sizeof(ulong) ? bytes : null;

        ulong? free = null;
        ulong? total = null;
        try
        {
            var drive = new DriveInfo(volumePath);
            free = checked((ulong)drive.AvailableFreeSpace);
            total = checked((ulong)drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Capacity failure must not discard an otherwise useful CPU sample.
        }

        return new SystemObservation(timestamp, instant, cpu, _logicalProcessors,
            memory, volumePath, free, total) { Memory = ReadMemory(memory), Activity = _activity.Read(networkId), CpuClockMhz = _frequency.Read(true, _logicalProcessors), Gpu = DarwinGpuReader.Read() };
    }

    private MacMemorySnapshot ReadMemory(ulong? total)
    {
        // Request the stable rev1 prefix of vm_statistics64 (38 natural_t words).
        uint count = 38;
        VmStatistics64 statistics;
        uint pageSize = 0;
        var valid = HostStatistics64(_host, 4, out statistics, ref count) == 0 && count >= 38 &&
                    HostPageSize(_host, out pageSize) == 0 && pageSize > 0;
        nuint size = sizeof(uint);
        var pressure = SysctlInt("kern.memorystatus_vm_pressure_level", out var level, ref size,
            IntPtr.Zero, 0) == 0 && size == sizeof(uint) ? MacMemorySnapshot.PressureName(level) : "Unavailable";
        if (!valid) return new(null, null, null, pressure);
        return MacMemorySnapshot.FromPages(total, pageSize, statistics.Internal, statistics.Purgeable,
            statistics.Wired, statistics.Compressor, statistics.External, pressure);
    }

    // mach/vm_statistics.h, rev1. Later SDK additions are intentionally not requested.
    [StructLayout(LayoutKind.Sequential, Size = 152)]
    private struct VmStatistics64
    {
        public uint Free, Active, Inactive, Wired;
        public fixed ulong Counters[9];
        public uint Purgeable, Speculative;
        public fixed ulong CompressionCounters[4];
        public uint Compressor, Throttled, External, Internal;
        public ulong Uncompressed;
    }

    [LibraryImport(LibSystem, EntryPoint = "host_statistics64")]
    private static partial int HostStatistics64(uint host, int flavor, out VmStatistics64 info, ref uint count);

    [LibraryImport(LibSystem, EntryPoint = "host_page_size")]
    private static partial int HostPageSize(uint host, out uint pageSize);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frequency.Dispose();
        if (_host != 0) _ = MachPortDeallocate(TaskSelfTrap(), _host);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HostCpuLoadInfo
    {
        public uint User;
        public uint System;
        public uint Idle;
        public uint Nice;
    }

    [LibraryImport(LibSystem, EntryPoint = "mach_host_self")]
    private static partial uint MachHostSelf();

    [LibraryImport(LibSystem, EntryPoint = "task_self_trap")]
    private static partial uint TaskSelfTrap();

    [LibraryImport(LibSystem, EntryPoint = "mach_port_deallocate")]
    private static partial int MachPortDeallocate(uint task, uint name);

    [LibraryImport(LibSystem, EntryPoint = "host_statistics")]
    private static partial int HostStatistics(uint host, int flavor, out HostCpuLoadInfo info, ref uint count);

    [LibraryImport(LibSystem, EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SysctlByName(string name, out ulong value, ref nuint length, IntPtr newValue,
        nuint newLength);

    [LibraryImport(LibSystem, EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SysctlInt(string name, out uint value, ref nuint length, IntPtr newValue, nuint newLength);
}
