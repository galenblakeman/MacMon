using System.Diagnostics;
using WinMon.Monitoring;

namespace MonCore.MacOS;

internal sealed record MacProcessObservation(ProcessLifetimeIdentity Identity, DateTimeOffset? StartedAtUtc,
    string Name, string ImagePath, int ParentProcessId, ulong? CpuNanoseconds, ulong? ResidentBytes,
    long? ThreadCount, long MonotonicTimestamp)
{
    public ulong? DiskReadBytes { get; init; }
    public ulong? DiskWriteBytes { get; init; }
}

internal sealed record MacProcessBatch(IReadOnlyList<MacProcessObservation> Processes, int UnavailableProcesses,
    bool EnumerationAvailable = true);

internal interface IMacProcessReader : IDisposable
{
    MacProcessBatch Read();
}

internal sealed record NativeProcessInfo(int ProcessId, int ParentProcessId, ulong StartSeconds,
    ulong StartMicroseconds, string Name, ulong? CpuNanoseconds, ulong? ResidentBytes, long? ThreadCount)
{
    public uint? EffectiveUserId { get; init; }
    public ulong? DiskReadBytes { get; init; }
    public ulong? DiskWriteBytes { get; init; }
}

internal interface IMacProcessApi
{
    int[]? EnumeratePids();
    NativeProcessInfo? ReadInfo(int pid);
    string ReadPath(int pid);
    NativeProcessInfo? ReadIdentity(int pid);
}

internal sealed class MacProcessReader(IMacProcessApi api) : IMacProcessReader
{
    public MacProcessReader() : this(new DarwinProcessApi()) { }

    public MacProcessBatch Read()
    {
        var pids = api.EnumeratePids();
        if (pids is null) return new MacProcessBatch([], 0, false);
        var result = new List<MacProcessObservation>(pids.Length);
        var unavailable = 0;
        foreach (var pid in pids.Distinct())
        {
            if (pid <= 0) continue;
            var info = api.ReadInfo(pid);
            var instant = Stopwatch.GetTimestamp();
            if (info is null || info.ProcessId != pid || info.StartSeconds == 0 ||
                info.StartMicroseconds >= 1_000_000 ||
                info.StartSeconds > ((ulong)long.MaxValue - info.StartMicroseconds) / 1_000_000)
            {
                unavailable++;
                continue;
            }

            var path = api.ReadPath(pid);
            // The path is a separate PID lookup. Reject an exit/reuse between calls instead of mixing lifetimes.
            var verified = api.ReadIdentity(pid);
            if (verified is null || verified.ProcessId != pid || verified.StartSeconds != info.StartSeconds ||
                verified.StartMicroseconds != info.StartMicroseconds)
            {
                unavailable++;
                continue;
            }

            var token = checked((long)(info.StartSeconds * 1_000_000 + info.StartMicroseconds));
            DateTimeOffset? start = null;
            // Display time is optional and never participates in identity equality.
            if (info.StartSeconds <= 253402300799UL)
                start = DateTimeOffset.FromUnixTimeSeconds((long)info.StartSeconds)
                    .AddTicks((long)info.StartMicroseconds * 10);
            result.Add(new MacProcessObservation(
                new ProcessLifetimeIdentity(pid, token, ProcessLifetimeIdentity.UnixMicroseconds),
                start, info.Name, path, info.ParentProcessId, info.CpuNanoseconds, info.ResidentBytes,
                info.ThreadCount, instant) { DiskReadBytes = info.DiskReadBytes, DiskWriteBytes = info.DiskWriteBytes });
        }

        return new MacProcessBatch(result, unavailable);
    }

    public void Dispose() { }
}
