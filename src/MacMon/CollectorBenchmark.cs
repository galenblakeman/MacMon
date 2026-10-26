using System.Diagnostics;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

internal static class CollectorBenchmark
{
    public static int Run()
    {
        using var source = new MacFrameSource();
        var options = new SamplingOptions();
        for (var i = 0; i < 10; i++) source.Capture(options);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var timings = new List<double>();
        var count = 0;
        for (var i = 0; i < 50; i++)
        {
            var started = Stopwatch.GetTimestamp();
            count = source.Capture(options).Full.Processes.Count;
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        var allocation = (GC.GetAllocatedBytesForCurrentThread() - allocationBefore) / 50;
        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds / 50;
        timings.Sort();
        Console.WriteLine($"{count} observed processes; capture median {timings[25]:F2} ms; CPU {cpu:F2} ms/capture; allocation {allocation:N0} bytes/capture");
        return 0;
    }
}
