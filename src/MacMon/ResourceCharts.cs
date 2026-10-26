using Avalonia;
using WinMon.Monitoring;

namespace MacMon;

public enum ResourceChartMode { Overlay, SmallMultiples, Heatmap, Focus }
public sealed record ChartPanel(Rect Bounds, int[] Metrics, bool SideLabel = false, double LabelWidth = 0);

/// <summary>Shared chart semantics: rates never become utilization percentages.</summary>
public static class ResourceCharts
{
    public static readonly string[] Names = ["CPU", "Memory", "Network", "Disk", "GPU", "Disk 2"];
    public static bool IsPercent(int metric) => metric is 0 or 1 or 4;
    public static int[] Metrics(int mask) => new[] { 0, 4, 1, 3, 5, 2 }.Where(i => (mask & (1 << i)) != 0).ToArray();
    public static int Focus(int mask, int focus) => Metrics(mask).Contains(focus) ? focus : Metrics(mask).FirstOrDefault();
    public static double? Value(MonitorSample sample, int metric)
    {
        if (sample.IsGap && metric != 1) return null;
        var r = sample.Resources;
        double? value = metric switch
        {
            0 => sample.TotalCpuPercent, 1 => r.MemoryPercent,
            2 => r.ReceiveBytesPerSecond,
            3 => DiskRate(r, false), 5 => DiskRate(r, true),
            4 => r.GpuPercent, _ => null
        };
        return value is { } n && double.IsFinite(n) && n >= 0 ? Math.Min(n, IsPercent(metric) ? 100 : double.MaxValue) : null;
    }
    // Before physical-disk selection, MacMon stored aggregate activity in secondary
    // fields and the data-volume path in DiskName. Preserve those saved histories.
    public static bool LegacyDisk(ResourceSample r) => (r.DiskName.Length == 0 || r.DiskName.StartsWith('/')) &&
        r.DiskReadBytesPerSecond is null && r.DiskWriteBytesPerSecond is null;
    public static double? DiskRate(ResourceSample r, bool second) => LegacyDisk(r)
        ? second ? null : r.SecondaryDiskReadBytesPerSecond + r.SecondaryDiskWriteBytesPerSecond
        : second ? r.SecondaryDiskReadBytesPerSecond + r.SecondaryDiskWriteBytesPerSecond : r.DiskReadBytesPerSecond + r.DiskWriteBytesPerSecond;
    public static double Maximum(IReadOnlyList<MonitorSample> samples, int rangeSeconds, int metric)
    {
        if (IsPercent(metric)) return 100;
        var peak = HistoryInspection.VisibleSamples(samples, rangeSeconds).Select(s => Value(s, metric) ?? 0).DefaultIfEmpty().Max();
        if (metric != 2) return ResourceGaugeHistory.RateMaximum(peak);
        return ResourceGaugeHistory.NetworkMaximum(peak);
    }
    public static string Reading(MonitorSample sample, int metric) => Value(sample, metric) is not { } value ? "—" :
        IsPercent(metric) ? $"{value:F1}%" : metric == 2 ? ResourceGaugeHistory.NetworkRate(value) : ResourceGaugeHistory.Rate(value);
    public static string Describe(MonitorSample sample) => $"{sample.Timestamp:HH:mm:ss zzz} · " +
        string.Join(" · ", Enumerable.Range(0, Names.Length).Select(i => $"{UiText.Get(Names[i])} {Reading(sample, i)}")) + (sample.IsGap ? " · interval gap" : "");
    public static string Scale(int metric, double maximum) => IsPercent(metric) ? "0–100%" : $"0–{(metric == 2 ? ResourceGaugeHistory.NetworkRate(maximum) : ResourceGaugeHistory.Rate(maximum))}";
    public static string AxisLabel(int metric, double value)
    {
        if (IsPercent(metric)) return $"{value:F1}%";
        if (metric != 2) return ResourceGaugeHistory.Rate(value);
        var bits = value * 8;
        var (number, unit) = bits >= 1e9 ? (bits / 1e9, "Gbps") : bits >= 1e6 ? (bits / 1e6, "Mbps") : (bits / 1e3, "Kbps");
        return number.ToString("F1") + " " + unit;
    }
    public static string[] AxisLabels(int metric, double maximum, int divisions = 4) =>
        Enumerable.Range(0, divisions + 1).Select(i => AxisLabel(metric, maximum * (1 - i / (double)divisions))).ToArray();

    public static ChartPanel[] Panels(Size size, int mask, ResourceChartMode mode, int focus, double? labelWidth = null)
    {
        var metrics = Metrics(mask);
        if (metrics.Length == 0 || size.Width <= 0 || size.Height <= 0) return [];
        var gutter = labelWidth ?? (mode == ResourceChartMode.Overlay ? 39 : 92);
        if (mode == ResourceChartMode.Overlay) return [new(new(size), metrics, LabelWidth: gutter)];
        if (mode == ResourceChartMode.Focus)
        {
            var selected = Focus(mask, focus);
            var others = metrics.Where(i => i != selected).ToArray();
            if (others.Length == 0) return [new(new(size), [selected], LabelWidth: gutter)];
            var top = new ChartPanel(new(0, 0, size.Width, size.Height * .55), [selected], LabelWidth: gutter);
            var height = size.Height * .41 / others.Length;
            return [top, .. others.Select((metric, i) => new ChartPanel(new(0, size.Height * .59 + i * height, size.Width, height), [metric], true, gutter))];
        }
        var laneHeight = size.Height / metrics.Length;
        return metrics.Select((metric, i) => new ChartPanel(new(0, i * laneHeight, size.Width, laneHeight), [metric], true, gutter)).ToArray();
    }
    public static Rect Plot(ChartPanel panel)
    {
        var inset = panel.SideLabel ? 3 : 8;
        return new(panel.Bounds.X + panel.LabelWidth, panel.Bounds.Y + inset,
            Math.Max(0, panel.Bounds.Width - panel.LabelWidth - 2), Math.Max(0, panel.Bounds.Height - inset * 2));
    }
}
