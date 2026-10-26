using System.Diagnostics;
using WinMon.Monitoring;

namespace MonCore.MacOS;

/// <summary>macOS system and process collector. Unavailable measurements remain explicitly unavailable.</summary>
public sealed class MacFrameSource : IFrameSource
{
    private readonly IMacSystemReader _reader;
    private readonly IMacProcessReader? _processReader;
    private Dictionary<ProcessLifetimeIdentity, MacProcessObservation> _previousProcesses = [];
    private SystemObservation? _previous;
    private bool _disposed;

    public MacFrameSource() : this(new DarwinSystemReader(), new MacProcessReader()) { }

    internal MacFrameSource(IMacSystemReader reader, IMacProcessReader? processReader = null)
    {
        _reader = reader;
        _processReader = processReader;
    }

    private string? _primaryDiskId, _secondaryDiskId;
    private MacDiskCounters? _priorPrimaryDisk, _priorSecondaryDisk;
    public string? PrimaryDiskId { get => Volatile.Read(ref _primaryDiskId); set => Volatile.Write(ref _primaryDiskId, value); }
    public string? SecondaryDiskId { get => Volatile.Read(ref _secondaryDiskId); set => Volatile.Write(ref _secondaryDiskId, value); }
    public IReadOnlyList<MacDiskChoice> DiskChoices => Volatile.Read(ref _activity).Disks
        .GroupBy(d => d.Id).Where(g => g.Count() == 1).Select(g => new MacDiskChoice(g.Key, g.First().Name)).ToArray();
    private MacVolumeSnapshot _volume = new("/System/Volumes/Data", null, null);
    public MacVolumeSnapshot Volume => Volatile.Read(ref _volume);
    private string? _networkId;
    public string? NetworkInterfaceId { get => Volatile.Read(ref _networkId); set => Volatile.Write(ref _networkId, value); }
    private MacActivitySnapshot _activity = MacActivitySnapshot.Empty;
    public IReadOnlyList<MacNetworkChoice> NetworkInterfaces => Volatile.Read(ref _activity).Interfaces;

    public MacMemorySnapshot Memory => Volatile.Read(ref _memory);
    private MacMemorySnapshot _memory = new(null, null, null, "Unavailable");

    public static ProcessIdentityPolicy IdentityPolicy => ProcessIdentityPolicy.ExactPaths;

    public CollectorCapabilities Capabilities { get; } = new(
        SystemMetricCapabilities.Cpu | SystemMetricCapabilities.Memory | SystemMetricCapabilities.Disk | SystemMetricCapabilities.Network | SystemMetricCapabilities.Gpu,
        ProcessMetricCapabilities.Cpu | ProcessMetricCapabilities.ResidentMemory |
        ProcessMetricCapabilities.Threads | ProcessMetricCapabilities.ExecutablePath | ProcessMetricCapabilities.Io, ProcessActionCapabilities.Terminate);

    public CapturedFrame Capture(SamplingOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _reader.Read(options.PrimaryDrive ?? "/System/Volumes/Data", NetworkInterfaceId);
        Volatile.Write(ref _memory, current.Memory);
        Volatile.Write(ref _activity, current.Activity);
        Volatile.Write(ref _volume, new(current.VolumePath, current.VolumeFreeBytes, current.VolumeTotalBytes));
        var primaryDisk = MacDiskSelection.Resolve(current.Activity, PrimaryDiskId, true);
        var secondaryDisk = MacDiskSelection.Resolve(current.Activity, SecondaryDiskId, false);
        var batch = _processReader?.Read() ?? new MacProcessBatch([], 0);
        var before = _previous;
        _previous = current;
        var seconds = before is null ? 0 :
            (current.MonotonicTimestamp - before.MonotonicTimestamp) / (double)Stopwatch.Frequency;
        // Some macOS monotonic clocks stop during sleep. Wall time is only a discontinuity check,
        // never the rate denominator; a clock change simply starts another baseline.
        var wallSeconds = before is null ? 0 : (current.Timestamp - before.Timestamp).TotalSeconds;
        var intervalValid = before is not null && seconds is > 0 and <= 2.5 && wallSeconds is > 0 and <= 2.5;
        double? cpu = null;
        double? kernel = null;
        if (intervalValid && before!.Cpu is { } prior && current.Cpu is { } after)
        {
            // Darwin exposes 32-bit cumulative counters. Unsigned subtraction handles one rollover.
            var user = unchecked(after.User - prior.User);
            var system = unchecked(after.System - prior.System);
            var idle = unchecked(after.Idle - prior.Idle);
            var nice = unchecked(after.Nice - prior.Nice);
            var total = (ulong)user + system + idle + nice;
            if (total > 0)
            {
                cpu = 100d * ((ulong)user + system + nice) / total;
                kernel = 100d * system / total;
            }
        }

        var processes = new List<ProcessUsage>(batch.Processes.Count);
        foreach (var process in batch.Processes)
        {
            double? processCpu = null;
            if (intervalValid && process.CpuNanoseconds is { } total &&
                _previousProcesses.TryGetValue(process.Identity, out var previous) &&
                previous.CpuNanoseconds is { } priorCpu && total >= priorCpu)
            {
                var elapsed = (process.MonotonicTimestamp - previous.MonotonicTimestamp) /
                              (double)Stopwatch.Frequency;
                if (elapsed is > 0 and <= 2.5)
                    processCpu = Math.Clamp((total - priorCpu) / (elapsed * 1_000_000_000d *
                        current.LogicalProcessors) * 100, 0, 100);
            }

            double? io = null;
            if (intervalValid && _previousProcesses.TryGetValue(process.Identity, out var previousIo))
            {
                var elapsed = (process.MonotonicTimestamp - previousIo.MonotonicTimestamp) / (double)Stopwatch.Frequency;
                if (elapsed is > 0 and <= 2.5)
                    io = ResourceRates.PerSecond(previousIo.DiskReadBytes, process.DiskReadBytes, elapsed) +
                         ResourceRates.PerSecond(previousIo.DiskWriteBytes, process.DiskWriteBytes, elapsed);
            }
            processes.Add(new ProcessUsage(process.Identity.ProcessId, process.Identity.StartToken,
                process.Name, processCpu ?? 0, process.ResidentBytes)
            {
                LifetimeDomain = process.Identity.Domain,
                StartedAtUtc = process.StartedAtUtc,
                CpuAvailable = processCpu is not null,
                ImagePath = process.ImagePath,
                ParentProcessId = process.ParentProcessId,
                ThreadCount = process.ThreadCount,
                IoBytesPerSecond = io
            });
        }

        // Replacing the map drops exited, denied and reused lifetimes; failures require a fresh baseline.
        _previousProcesses = batch.Processes.ToDictionary(process => process.Identity);
        var sample = new MonitorSample(current.Timestamp, cpu, processes,
            batch.UnavailableProcesses + processes.Count(process => !process.CpuAvailable),
            IsGap: !intervalValid, MonotonicTimestamp: current.MonotonicTimestamp)
        {
            IndependentProcessReadings = true,
            IntervalSeconds = intervalValid ? seconds : 0,
            IntervalCpuTicks = !intervalValid ? 0 : checked((long)Math.Round(
                seconds * TimeSpan.TicksPerSecond * current.LogicalProcessors)),
            Resources = new ResourceSample
            {
                GpuPercent = current.Gpu.Percent, GpuId = current.Gpu.Id, GpuName = current.Gpu.Name,
                GpuEngine = "Device", GpuSharedMemoryUsedBytes = current.Gpu.UsedBytes,
                CpuKernelPercent = kernel,
                CpuClockMhz = intervalValid ? current.CpuClockMhz : null,
                TotalMemoryBytes = current.TotalMemoryBytes,
                UsedMemoryBytes = current.Memory.UsedBytes,
                AvailableMemoryBytes = current.Memory.UsedBytes is { } used && current.TotalMemoryBytes is { } totalMemory
                    ? totalMemory - used : null,
                CachedBytes = current.Memory.CachedBytes,
                ProcessCount = batch.EnumerationAvailable ? checked((uint)(processes.Count + batch.UnavailableProcesses)) : null,
                DiskName = primaryDisk.Name,
                DiskTotalBytes = primaryDisk.Size,
                DiskReadBytesPerSecond = MacDiskSelection.Rate(_priorPrimaryDisk, primaryDisk, seconds, intervalValid, true),
                DiskWriteBytesPerSecond = MacDiskSelection.Rate(_priorPrimaryDisk, primaryDisk, seconds, intervalValid, false),
                NetworkId = current.Activity.NetworkId,
                NetworkName = current.Activity.NetworkName,
                ReceiveBytesPerSecond = NetworkRate(before, current, seconds, intervalValid, true),
                SendBytesPerSecond = NetworkRate(before, current, seconds, intervalValid, false),
                SecondaryDiskName = secondaryDisk.Name,
                SecondaryDiskTotalBytes = secondaryDisk.Size,
                SecondaryDiskReadBytesPerSecond = MacDiskSelection.Rate(_priorSecondaryDisk, secondaryDisk, seconds, intervalValid, true),
                SecondaryDiskWriteBytesPerSecond = MacDiskSelection.Rate(_priorSecondaryDisk, secondaryDisk, seconds, intervalValid, false)
            }
        };
        _priorPrimaryDisk = primaryDisk; _priorSecondaryDisk = secondaryDisk;
        // Baseline and wake gaps are expected. A failed native CPU read is reported separately.
        return new CapturedFrame(sample, Error: current.Cpu is null ? SamplingError.CpuUnavailable :
            !batch.EnumerationAvailable ? SamplingError.ProcessesUnavailable : null);
    }

    private static double? NetworkRate(SystemObservation? before, SystemObservation current, double seconds, bool valid, bool receive) =>
        valid && before is not null && current.Activity.NetworkId.Length > 0 && before.Activity.NetworkId == current.Activity.NetworkId
            ? ResourceRates.PerSecond(receive ? before.Activity.Received : before.Activity.Sent,
                receive ? current.Activity.Received : current.Activity.Sent, seconds) : null;

    public void Reset()
    {
        _previous = null;
        _priorPrimaryDisk = _priorSecondaryDisk = null;
        _previousProcesses.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
        _processReader?.Dispose();
    }
}

internal readonly record struct CpuCounters(uint User, uint System, uint Idle, uint Nice);

internal sealed record SystemObservation(
    DateTimeOffset Timestamp, long MonotonicTimestamp, CpuCounters? Cpu, int LogicalProcessors,
    ulong? TotalMemoryBytes, string VolumePath, ulong? VolumeFreeBytes, ulong? VolumeTotalBytes)
{
    public MacGpuSnapshot Gpu { get; init; } = MacGpuSnapshot.Empty;
    public double? CpuClockMhz { get; init; }
    public MacActivitySnapshot Activity { get; init; } = MacActivitySnapshot.Empty;
    public MacMemorySnapshot Memory { get; init; } = new(null, null, null, "Unavailable");
}

internal interface IMacSystemReader : IDisposable
{
    SystemObservation Read(string volumePath, string? networkId = null);
}

public sealed record MacVolumeSnapshot(string Path, ulong? FreeBytes, ulong? TotalBytes);
