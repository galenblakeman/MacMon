using System.Diagnostics;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

internal static class PresentationBenchmark
{
    public static int Run()
    {
        var history = Enumerable.Range(0, 120).Select(second => new MonitorSample(
            DateTimeOffset.UnixEpoch.AddSeconds(second), 35,
            Enumerable.Range(1, 850).Select(pid => new ProcessUsage(pid, pid * 100, $"process{pid}", pid % 9, (ulong)pid * 1048576)
            {
                ImagePath = $"/Applications/app{pid / 4}", LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds,
                ThreadCount = pid % 20
            }).ToArray(), 0, MonotonicTimestamp: (second + 1) * Stopwatch.Frequency)
            { IndependentProcessReadings = true }).ToArray();
        var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
        Measure("Previous individual rows (ranking + filtering)", () =>
            ProcessSearch.Filter(ProcessRanking.Rank(history, includeInactive: true,
                identityPolicy: MacFrameSource.IdentityPolicy).Processes, "").Select(process => new ProcessRow(process)).ToArray());
        Measure("Individual rows with 30s trails", () => ProcessPresentation.Rows(history, history[^1], false,
            ProcessSort.Cpu, "", pins, false));
        Measure("Grouped rows with 30s trails", () => ProcessPresentation.Rows(history, history[^1], true,
            ProcessSort.Cpu, "", pins, false));
        Measure("Individual top 20 with 30s trails", () => ProcessPresentation.Rows(history, history[^1], false,
            ProcessSort.Cpu, "", pins, false, limit: 20));
        var context = new LaunchdContext(Guid.Empty, 501, "Aqua");
        var jobs = history[^1].Processes.Take(600).Select(p => new LaunchdJob(new(context, $"example.job{p.ProcessId}"),
            p.ProcessId, 0, p.Identity)).ToArray();
        var snapshot = new LaunchdSnapshot(DateTimeOffset.UnixEpoch, context, jobs);
        var recent = history.TakeLast(31).ToArray();
        Measure("600 jobs with verified process trails", () => LaunchdPresentation.Rows(snapshot, history[^1], true, "", 0, 3, history: recent).ToArray());
        Measure("Top 20 jobs with verified process trails", () => LaunchdPresentation.Rows(snapshot, history[^1], true, "", 0, 3, 20, history: recent).ToArray());
        Measure("Jobs with no search matches", () => LaunchdPresentation.Rows(snapshot, history[^1], true, "no matching job", 0, 4, history: recent).ToArray());
        return 0;
    }

    private static void Measure<T>(string label, Func<T[]> operation)
    {
        for (var i = 0; i < 20; i++) operation();
        var times = new List<double>();
        var allocations = new List<long>();
        for (var batch = 0; batch < 7; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < 20; i++) operation();
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds / 20);
            allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / 20);
        }
        times.Sort(); allocations.Sort();
        Console.WriteLine($"{label}: median {times[3]:F3} ms / refresh, {allocations[3]:N0} bytes / refresh");
    }
}
