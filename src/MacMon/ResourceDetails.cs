using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public enum ResourceMetric { Cpu, Memory, Network, Storage }
public sealed record ResourceDetailRow(string Label, string Value);
public sealed record ResourceDetailSnapshot(string Title, string Status, string Definition, IReadOnlyList<ResourceDetailRow> Rows);

/// <summary>Supported live Mac measurements only. A null sample suppresses stale/restored values and metadata.</summary>
public static class ResourceDetails
{
    public static ResourceDetailSnapshot Create(ResourceMetric metric, MonitorSample? sample, MacMemorySnapshot? memory,
        GaugeReading reading, int logicalCores)
    {
        if (sample is null) { memory = null; reading = GaugeReading.Empty; }
        var r = sample?.Resources;
        var rows = new List<ResourceDetailRow> { new("Current", reading.Value is null ? "—" : reading.Text) };
        string definition;
        switch (metric)
        {
            case ResourceMetric.Cpu:
                definition = "CPU is a share of total machine capacity over the latest measured interval. Enumerated processes include processes whose readings could not be obtained.";
                var total = PercentValue(sample?.TotalCpuPercent);
                var system = PercentValue(r?.CpuKernelPercent);
                rows.AddRange([
                    new("User CPU", total is { } t && system is { } k && k <= t ? Percent(t - k) : "—"),
                    new("System CPU", Percent(system)),
                    new("Logical cores", sample is not null && logicalCores > 0 ? logicalCores.ToString() : "—"),
                    new("Enumerated processes", r?.ProcessCount?.ToString() ?? "—"),
                    new("Measured interval", sample?.IntervalSeconds is > 0 and <= 5 ? $"{sample.IntervalSeconds:F2} s" : "—")]);
                break;
            case ResourceMetric.Memory:
                definition = "Physical usage is a macOS estimate. Available is its complement, not guaranteed allocatable RAM. Cached pages may be reclaimable. Pressure and compressed storage are live adapter metadata.";
                rows.AddRange([
                    new("Used physical", Bytes(r?.UsedMemoryBytes)), new("Installed", Bytes(r?.TotalMemoryBytes)),
                    new("Available estimate", Bytes(r?.AvailableMemoryBytes)), new("Cached", Bytes(r?.CachedBytes)),
                    new("Compressed storage", Bytes(memory?.CompressedBytes)),
                    new("Pressure", memory?.Pressure is "Normal" or "Warning" or "Critical" ? memory.Pressure : "—")]);
                break;
            case ResourceMetric.Network:
                definition = "Throughput is receive + send on the selected interface, in bytes per second. The adaptive gauge scale is not link speed or percent utilization. A source change starts a new peak window.";
                rows.AddRange([
                    new("Interface", Text(r?.NetworkName)), new("Interface ID", Text(r?.NetworkId)),
                    new("Receive", Rate(r?.ReceiveBytesPerSecond)), new("Send", Rate(r?.SendBytesPerSecond))]);
                break;
            case ResourceMetric.Storage:
                definition = "Activity is read + write across all readable block-storage drivers, in bytes per second. Data-volume capacity is a separate filesystem reading; it is not the activity gauge's utilization.";
                rows.AddRange([
                    new("Activity scope", "All readable block-storage drivers"),
                    new("Read", Rate(r?.SecondaryDiskReadBytesPerSecond)), new("Write", Rate(r?.SecondaryDiskWriteBytesPerSecond)),
                    new("Capacity volume", Text(r?.DiskName)), new("Available capacity", Bytes(r?.DiskFreeBytes)),
                    new("Total capacity", Bytes(r?.DiskTotalBytes))]);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(metric));
        }
        rows.Add(new("Peak · last 2 minutes", reading.Value is null ? "—" : metric is ResourceMetric.Cpu or ResourceMetric.Memory
            ? Percent(reading.Peak) : Rate(reading.Peak)));
        rows.Add(new("Gauge scale", reading.Value is null ? "—" : reading.Scale));
        return new(metric == ResourceMetric.Cpu ? "CPU details" : $"{metric} details",
            sample is null ? "Waiting for live readings…" : $"Live · updated {sample.Timestamp:HH:mm:ss zzz}", definition, rows);
    }
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static double? PercentValue(double? value) => value is { } number && double.IsFinite(number) && number is >= 0 and <= 100 ? number : null;
    private static string Percent(double? value) => PercentValue(value) is { } number ? $"{number:F1}%" : "—";
    private static string Rate(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? ResourceGaugeHistory.Rate(number) : "—";
    public static string Bytes(ulong? value) => value is not { } bytes ? "—" : bytes >= 1073741824 ? $"{bytes / 1073741824d:F1} GiB" :
        bytes >= 1048576 ? $"{bytes / 1048576d:F1} MiB" : bytes >= 1024 ? $"{bytes / 1024d:F1} KiB" : $"{bytes} B";
}
