using Avalonia;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--presentation-benchmark")) return PresentationBenchmark.Run();
        if (args.Contains("--smoke")) return Smoke();
        return AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    private static int Smoke()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("Native smoke requires macOS.");
            return 1;
        }

        using var source = new MacFrameSource();
        var history = new MonitorHistory();
        for (var i = 0; i < 4; i++)
        {
            var frame = source.Capture(new SamplingOptions());
            history.Add(frame.Full);
            Console.WriteLine($"{frame.Full.Timestamp:O} CPU={frame.Full.TotalCpuPercent?.ToString("F1") ?? "unavailable"} " +
                              $"RAM={frame.Full.Resources.TotalMemoryBytes} Used={frame.Full.Resources.UsedMemoryBytes} Pressure={source.Memory.Pressure} " +
                              $"VolumeFree={frame.Full.Resources.DiskFreeBytes} " +
                              $"Processes={frame.Full.Processes.Count} Unavailable={frame.Full.UnavailableProcesses} " +
                              $"SelfCpu={frame.Full.Processes.FirstOrDefault(p => p.ProcessId == Environment.ProcessId)?.ObservedCpuPercent} " +
                              $"Error={frame.Error}");
            if (i < 3) Thread.Sleep(1000);
        }

        var last = history.Latest!;
        return last.TotalCpuPercent is >= 0 and <= 100 && last.Resources.TotalMemoryBytes is > 0
            && last.Resources.MemoryPercent is >= 0 and <= 100 && last.Resources.DiskFreeBytes is > 0 && last.Processes.Any(process =>
                process.ProcessId == Environment.ProcessId && process.ObservedCpuPercent is >= 0 and <= 100 &&
                process.WorkingSetBytes is > 0 && process.ThreadCount is > 0) ? 0 : 1;
    }
}
