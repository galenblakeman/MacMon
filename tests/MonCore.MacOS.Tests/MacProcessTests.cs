using System.Diagnostics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

[TestFixture]
public sealed class MacProcessTests
{
    private static readonly SamplingOptions Options = new();

    [Test]
    public void MachCpuTicksUseTheReportedTimebaseWithoutOverflow()
    {
        Assert.That(DarwinProcessApi.MachTimeToNanoseconds(24_000_000, 125, 3), Is.EqualTo(1_000_000_000));
        Assert.That(DarwinProcessApi.MachTimeToNanoseconds(1_000_000_000, 1, 1), Is.EqualTo(1_000_000_000));
        Assert.That(DarwinProcessApi.MachTimeToNanoseconds(ulong.MaxValue, 125, 3), Is.Null);
        Assert.That(DarwinProcessApi.MachTimeToNanoseconds(1, 1, 0), Is.Null);
    }

    [Test]
    public void NativeCpuAccountingMatchesManagedProcessCpuDuringRealWork()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("Native macOS CPU accounting integration.");
        var api = new DarwinProcessApi();
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        var nativeBefore = api.ReadInfo(Environment.ProcessId)!.CpuNanoseconds!.Value;
        var managedBefore = current.TotalProcessorTime;
        var end = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 4;
        while (Stopwatch.GetTimestamp() < end) Thread.SpinWait(1000);
        current.Refresh();
        var managedDeltaNanoseconds = (current.TotalProcessorTime - managedBefore).Ticks * 100d;
        var nativeDeltaNanoseconds = api.ReadInfo(Environment.ProcessId)!.CpuNanoseconds!.Value - nativeBefore;
        Assert.That(managedDeltaNanoseconds, Is.GreaterThan(0));
        Assert.That(nativeDeltaNanoseconds / managedDeltaNanoseconds, Is.InRange(0.75, 1.25));
    }

    [Test]
    public void NativeLayoutsMatchSdkSizes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<DarwinProcessApi.RUsageV2>(), Is.EqualTo(160));
            Assert.That(Marshal.SizeOf<DarwinProcessApi.BsdInfo>(), Is.EqualTo(136));
            Assert.That(Marshal.SizeOf<DarwinProcessApi.TaskInfo>(), Is.EqualTo(96));
            Assert.That(Marshal.SizeOf<DarwinProcessApi.TaskAllInfo>(), Is.EqualTo(232));
        });
    }

    [Test]
    public void ReaderRejectsPidReuseBetweenTaskAndPathReads()
    {
        using var reader = new MacProcessReader(new FakeApi { FinalStartSeconds = 101 });
        var batch = reader.Read();
        Assert.That(batch.Processes, Is.Empty);
        Assert.That(batch.UnavailableProcesses, Is.EqualTo(1));
    }

    [Test]
    public void ProtectedAndExitedProcessesCannotBecomeFalseIdleRows()
    {
        using var protectedReader = new MacProcessReader(new FakeApi { Cpu = null, Resident = null });
        var protectedProcess = protectedReader.Read().Processes.Single();
        Assert.That(protectedProcess.CpuNanoseconds, Is.Null);
        Assert.That(protectedProcess.ResidentBytes, Is.Null);
        using var exitedReader = new MacProcessReader(new FakeApi { Exited = true });
        Assert.That(exitedReader.Read().UnavailableProcesses, Is.EqualTo(1));
    }

    [Test]
    public void StartTokenUsesUnixMicrosecondsAndDisplayTimeIsSeparate()
    {
        using var reader = new MacProcessReader(new FakeApi());
        var process = reader.Read().Processes.Single();
        Assert.That(process.Identity, Is.EqualTo(new ProcessLifetimeIdentity(123, 100_000_456,
            ProcessLifetimeIdentity.UnixMicroseconds)));
        Assert.That(process.StartedAtUtc, Is.EqualTo(DateTimeOffset.FromUnixTimeSeconds(100).AddTicks(4560)));
    }

    [Test]
    public void ProcessCpuUsesActualIntervalAndMachineCapacity()
    {
        using var source = Source([Observation(0), Observation(1)],
            [Batch(Process(0, cpu: 1_000_000_000)), Batch(Process(1, cpu: 3_000_000_000))]);
        Assert.That(source.Capture(Options).Full.Processes.Single().ObservedCpuPercent, Is.Null);
        var process = source.Capture(Options).Full.Processes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(process.ObservedCpuPercent, Is.EqualTo(25)); // two CPU seconds / eight logical cores.
            Assert.That(process.WorkingSetBytes, Is.EqualTo(1000));
            Assert.That(process.PrivateBytes, Is.Null);
            Assert.That(process.ThreadCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void ReusedPidAndCounterResetNeedFreshBaselines()
    {
        using var source = Source([Observation(0), Observation(1), Observation(2)],
            [Batch(Process(0, cpu: 10)), Batch(Process(1, token: 2, cpu: 100)),
                Batch(Process(2, token: 2, cpu: 50))]);
        source.Capture(Options);
        Assert.That(source.Capture(Options).Full.Processes.Single().ObservedCpuPercent, Is.Null);
        Assert.That(source.Capture(Options).Full.Processes.Single().ObservedCpuPercent, Is.Null);
    }

    [Test]
    public void DeniedOrMissingReadDoesNotReuseStaleCpuBaseline()
    {
        using var source = Source([Observation(0), Observation(1), Observation(2)],
            [Batch(Process(0)), new MacProcessBatch([], 1), Batch(Process(2))]);
        source.Capture(Options);
        Assert.That(source.Capture(Options).Full.UnavailableProcesses, Is.EqualTo(1));
        Assert.That(source.Capture(Options).Full.Processes.Single().ObservedCpuPercent, Is.Null);
    }

    [Test]
    public void SystemCpuFailureStillAllowsProcessCpuMemoryAndRanking()
    {
        using var source = Source([Observation(0), Observation(1) with { Cpu = null }],
            [Batch(Process(0, cpu: 0)), Batch(Process(1, cpu: 1_000_000_000))]);
        source.Capture(Options);
        var frame = source.Capture(Options);
        Assert.That(frame.Error, Is.EqualTo(SamplingError.CpuUnavailable));
        Assert.That(frame.Full.IsGap, Is.False);
        Assert.That(ProcessRanking.Rank([frame.Full], sort: ProcessSort.Memory).Processes.Single().ObservedCpuPercent,
            Is.EqualTo(12.5));
    }

    [Test]
    public void EnumerationFailurePreservesSystemResourcesAndReportsPartialFailure()
    {
        using var source = Source([Observation(0), Observation(1)],
            [Batch(Process(0)), new MacProcessBatch([], 0, false)]);
        source.Capture(Options);
        var frame = source.Capture(Options);
        Assert.That(frame.Error, Is.EqualTo(SamplingError.ProcessesUnavailable));
        Assert.That(frame.Full.TotalCpuPercent, Is.EqualTo(20));
        Assert.That(frame.Full.Resources.ProcessCount, Is.Null);
    }

    [Test]
    public void NativeReaderFindsOwnProcessAndTreatsExitedPidAsUnavailable()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("Native macOS integration.");
        using var reader = new MacProcessReader();
        var process = reader.Read().Processes.Single(p => p.Identity.ProcessId == Environment.ProcessId);
        Assert.That(process.CpuNanoseconds, Is.Not.Null);
        Assert.That(process.ResidentBytes, Is.GreaterThan(0));
        Assert.That(process.ThreadCount, Is.GreaterThan(0));
        Assert.That(process.ImagePath, Does.StartWith("/"));
        Assert.That(new DarwinProcessApi().ReadInfo(int.MaxValue), Is.Null);
    }

    [Test]
    public void ProcessDiskIoResetsOnReuseRollbackAndMissingCounter()
    {
        var p0 = Process(0) with { DiskReadBytes = 100, DiskWriteBytes = 100 };
        var p1 = Process(1) with { DiskReadBytes = 300, DiskWriteBytes = 200 };
        var p2 = Process(2) with { DiskReadBytes = 50, DiskWriteBytes = 1000 };
        var p3 = Process(3) with { DiskReadBytes = null, DiskWriteBytes = 1100 };
        var p4 = Process(4, token: 2) with { DiskReadBytes = 400, DiskWriteBytes = 1200 };
        using var source = Source(Enumerable.Range(0, 5).Select(Observation).ToArray(),
            [Batch(p0), Batch(p1), Batch(p2), Batch(p3), Batch(p4)]);
        Assert.That(source.Capture(Options).Full.Processes.Single().IoBytesPerSecond, Is.Null);
        Assert.That(source.Capture(Options).Full.Processes.Single().IoBytesPerSecond, Is.EqualTo(300));
        Assert.That(source.Capture(Options).Full.Processes.Single().IoBytesPerSecond, Is.Null);
        Assert.That(source.Capture(Options).Full.Processes.Single().IoBytesPerSecond, Is.Null);
        Assert.That(source.Capture(Options).Full.Processes.Single().IoBytesPerSecond, Is.Null);
    }

    [Test]
    public void NativeSelfDiskIoCountersAreReadable()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS integration test");
        var info = new DarwinProcessApi().ReadInfo(Environment.ProcessId)!;
        Assert.That(info.DiskReadBytes, Is.Not.Null);
        Assert.That(info.DiskWriteBytes, Is.Not.Null);
    }

    private static MacFrameSource Source(SystemObservation[] system, MacProcessBatch[] processes) =>
        new(new FakeSystem(system), new FakeProcesses(processes));
    private static SystemObservation Observation(int second) => new(DateTimeOffset.UnixEpoch.AddSeconds(second),
        (second + 1) * Stopwatch.Frequency, new((uint)(second * 10), (uint)(second * 10), (uint)(second * 80), 0),
        8, 16UL << 30, "/", 100, 1000);
    private static MacProcessBatch Batch(params MacProcessObservation[] processes) => new(processes, 0);
    private static MacProcessObservation Process(int second, long token = 1, ulong? cpu = 0) =>
        new(new(123, token, ProcessLifetimeIdentity.UnixMicroseconds), DateTimeOffset.UnixEpoch,
            "app", "/Apps/App", 1, cpu, 1000, 4, (second + 1) * Stopwatch.Frequency);

    private sealed class FakeSystem(IEnumerable<SystemObservation> observations) : IMacSystemReader
    {
        private readonly Queue<SystemObservation> _queue = new(observations);
        public SystemObservation Read(string path, string? networkId = null) => _queue.Dequeue();
        public void Dispose() { }
    }
    private sealed class FakeProcesses(IEnumerable<MacProcessBatch> batches) : IMacProcessReader
    {
        private readonly Queue<MacProcessBatch> _queue = new(batches);
        public MacProcessBatch Read() => _queue.Dequeue();
        public void Dispose() { }
    }
    private sealed class FakeApi : IMacProcessApi
    {
        public ulong FinalStartSeconds { get; init; } = 100;
        public ulong? Cpu { get; init; } = 1000;
        public ulong? Resident { get; init; } = 2000;
        public bool Exited { get; init; }
        public int[] EnumeratePids() => [123];
        public NativeProcessInfo ReadInfo(int pid) => new(pid, 1, 100, 456, "app", Cpu, Resident, 4);
        public string ReadPath(int pid) => "/Apps/App";
        public NativeProcessInfo? ReadIdentity(int pid) => Exited ? null :
            new(pid, 1, FinalStartSeconds, 456, "app", null, null, null);
    }
}
