using Avalonia;
using WinMon.Monitoring;

namespace MacMon;

public enum ResourceChartMode { Overlay, SmallMultiples, Heatmap, Focus }
public sealed record ChartPanel(Rect Bounds, int[] Metrics);

/// <summary>Shared chart semantics: rates never become utilization percentages.</summary>
public static class ResourceCharts
{
    public static readonly string[] Names = ["CPU", "Memory", "Network", "Storage"];
    public static int[] Metrics(int mask) => Enumerable.Range(0, 4).Where(i => (mask & (1 << i)) != 0).ToArray();
    public static int Focus(int mask, int focus) => Metrics(mask).Contains(focus) ? focus : Metrics(mask).FirstOrDefault();
    public static double? Value(MonitorSample sample, int metric)
    {
        if (sample.IsGap && metric != 1) return null;
        var r = sample.Resources;
        double? value = metric switch
        {
            0 => sample.TotalCpuPercent, 1 => r.MemoryPercent,
            2 => r.ReceiveBytesPerSecond + r.SendBytesPerSecond,
            3 => r.SecondaryDiskReadBytesPerSecond + r.SecondaryDiskWriteBytesPerSecond,
            _ => null
        };
        return value is { } n && double.IsFinite(n) && n >= 0 ? Math.Min(n, metric < 2 ? 100 : double.MaxValue) : null;
    }
    public static double Maximum(IReadOnlyList<MonitorSample> samples, int rangeSeconds, int metric) => metric < 2 ? 100 :
        ResourceGaugeHistory.RateMaximum(HistoryInspection.VisibleSamples(samples, rangeSeconds).Select(s => Value(s, metric) ?? 0).DefaultIfEmpty().Max());
    public static string Reading(MonitorSample sample, int metric) => Value(sample, metric) is not { } value ? "—" :
        metric < 2 ? $"{value:F1}%" : ResourceGaugeHistory.Rate(value);
    public static string Scale(int metric, double maximum) => metric < 2 ? "0–100%" : $"0–{ResourceGaugeHistory.Rate(maximum)}";
    public static ChartPanel[] Panels(Size size, int mask, ResourceChartMode mode, int focus)
    {
        var metrics = Metrics(mask);
        if (metrics.Length == 0 || size.Width <= 0 || size.Height <= 0) return [];
        if (mode == ResourceChartMode.Overlay) return [new(new(size), metrics)];
        if (mode == ResourceChartMode.Focus) return [new(new(size), [Focus(mask, focus)])];
        var columns = mode == ResourceChartMode.SmallMultiples && metrics.Length > 1 ? 2 : 1;
        var rows = (int)Math.Ceiling(metrics.Length / (double)columns);
        var width = Math.Max(0, (size.Width - (columns - 1) * 12) / columns);
        var height = Math.Max(0, (size.Height - (rows - 1) * 8) / rows);
        return metrics.Select((metric, i) => new ChartPanel(new((i % columns) * (width + 12),
            (i / columns) * (height + 8), width, height), [metric])).ToArray();
    }
    public static Rect Plot(ChartPanel panel) => new(panel.Bounds.X, panel.Bounds.Y + 18,
        panel.Bounds.Width, Math.Max(0, panel.Bounds.Height - 20));
}
