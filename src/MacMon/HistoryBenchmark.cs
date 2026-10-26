using System.Diagnostics;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

internal static class HistoryBenchmark
{
    public static int Run()
    {
        var directory = Directory.CreateTempSubdirectory("macmon-history-benchmark-").FullName;
        var path = Path.Combine(directory, "history.mmh.gz");
        try
        {
            var processes = Enumerable.Range(1, 850).Select(pid => new ProcessUsage(pid, pid * 1000, $"process{pid}", pid % 5 * 0.01, (ulong)pid * 1048576, pid % 3 * 1024)
            {
                LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = $"/Applications/App{pid / 4}", ThreadCount = pid % 20
            }).ToArray();
            var now = DateTimeOffset.UtcNow;
            var moment = Stopwatch.GetTimestamp();
            var entries = Enumerable.Range(0, 588).Select(index => new MacHistoryEntry(
                new MonitorSample(now.AddSeconds(index - 587), 35, processes, 10,
                    MonotonicTimestamp: moment + (index - 587L) * Stopwatch.Frequency)
                {
                    IndependentProcessReadings = true, IntervalSeconds = 1, IntervalCpuTicks = 10 * TimeSpan.TicksPerSecond,
                    Resources = new() { TotalMemoryBytes = 16UL << 30, UsedMemoryBytes = 10UL << 30,
                        NetworkId = "en1", NetworkName = "Wi-Fi", ReceiveBytesPerSecond = 1048576, SendBytesPerSecond = 32768 }
                }, "benchmark-boot")).ToArray();
            // Use a virtual positive clock even if this benchmark is run immediately after boot.
            if (entries[0].Sample.MonotonicTimestamp <= 0)
            {
                var offset = Stopwatch.Frequency - entries[0].Sample.MonotonicTimestamp;
                entries = entries.Select(entry => entry with { Sample = entry.Sample with { MonotonicTimestamp = entry.Sample.MonotonicTimestamp + offset } }).ToArray();
            }
            var started = Stopwatch.GetTimestamp();
            MacHistoryStore.Save(path, entries);
            var saved = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            var restored = MacHistoryStore.Load(path, 3600, DateTimeOffset.UtcNow, Stopwatch.GetTimestamp());
            var loaded = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Console.WriteLine($"{entries.Length} samples / {entries.Length * processes.Length:N0} process entries: save {saved:F1} ms, load {loaded:F1} ms, file {new FileInfo(path).Length:N0} bytes, restored {restored.Entries.Count} samples, error={restored.Error ?? "none"}");
            return restored.Entries.Count == entries.Length && restored.Error is null ? 0 : 1;
        }
        finally { Directory.Delete(directory, true); }
    }

    public static int MeasureProcess(int pid, int seconds)
    {
        using var source = new MacFrameSource();
        ProcessLifetimeIdentity? identity = null;
        var values = new List<double>();
        ulong peakResident = 0;
        for (var i = 0; i <= seconds; i++)
        {
            var sample = source.Capture(new SamplingOptions()).Full;
            var process = sample.Processes.FirstOrDefault(process => process.ProcessId == pid);
            if (process is null || (identity is { } observed && process.Identity != observed))
            { Console.Error.WriteLine("The target process exited, changed lifetime, or became unreadable."); return 1; }
            identity ??= process.Identity;
            if (process.ObservedCpuPercent is { } cpu) values.Add(cpu);
            peakResident = Math.Max(peakResident, process.WorkingSetBytes ?? 0);
            if (i < seconds) Thread.Sleep(1000);
        }
        if (values.Count == 0) return 1;
        Console.WriteLine($"PID {pid}: {values.Count} measured intervals; mean {values.Average():F3}% of machine CPU; peak {values.Max():F3}%; peak resident {peakResident / 1048576d:F1} MiB");
        return 0;
    }
}
