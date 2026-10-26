using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public enum ResourceMetric { Cpu, Memory, Network, Storage, Gpu, SecondDisk }
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
                definition = "CPU is a share of total machine capacity over the latest measured interval. Clock speed is an active-residency-weighted requested-state estimate from optional IOReport channels, not a hardware clock measurement. Enumerated processes include processes whose readings could not be obtained.";
                var total = PercentValue(sample?.TotalCpuPercent);
                var system = PercentValue(r?.CpuKernelPercent);
                rows.AddRange([
                    new("User CPU", total is { } t && system is { } k && k <= t ? Percent(t - k) : "—"),
                    new("System CPU", Percent(system)),
                    new("Clock estimate", r?.CpuClockMhz is > 0 ? $"{r.CpuClockMhz / 1000:F2} GHz" : "—"),
                    new("Logical cores", sample is not null && logicalCores > 0 ? logicalCores.ToString() : "—"),
                    new("Enumerated processes", r?.ProcessCount?.ToString() ?? "—"),
                    new("Measured interval", sample?.IntervalSeconds is > 0 and <= 5 ? $"{sample.IntervalSeconds:F2} s" : "—")]);
                break;
            case ResourceMetric.Memory:
                definition = "Physical usage is a macOS estimate. Available is its complement, not guaranteed allocatable RAM. Cached pages may be reclaimable. Pressure and compressed storage are live adapter metadata.";
                rows.AddRange([
                    new("Used", Bytes(r?.UsedMemoryBytes)), new("Installed", Bytes(r?.TotalMemoryBytes)),
                    new("Available estimate", Bytes(r?.AvailableMemoryBytes)), new("Cached", Bytes(r?.CachedBytes)),
                    new("Compressed storage", Bytes(memory?.CompressedBytes)),
                    new("Pressure", memory?.Pressure is "Normal" or "Warning" or "Critical" ? memory.Pressure : "—")]);
                break;
            case ResourceMetric.Network:
                definition = "The main reading is download on the selected interface, in bits per second. Upload is shown separately. The adaptive gauge scale is not link speed or percent utilization. A source change starts a new peak window.";
                rows.AddRange([
                    new("Interface", Text(r?.NetworkName)), new("Interface ID", Text(r?.NetworkId)),
                    new("Receive", Rate(r?.ReceiveBytesPerSecond)), new("Send", Rate(r?.SendBytesPerSecond))]);
                break;
            case ResourceMetric.Gpu:
                definition = "Device-wide utilization reported by the selected IOAccelerator. The first adapter by registry identity is monitored; renderer/tiler and per-process GPU utilization are not substituted. Shared memory is driver-reported and has no dedicated VRAM capacity on unified-memory hardware.";
                rows.AddRange([new("GPU", Text(r?.GpuName)), new("Registry identity", Text(r?.GpuId)),
                    new("Shared memory in use", Bytes(r?.GpuSharedMemoryUsedBytes))]);
                break;
            case ResourceMetric.Storage:
            case ResourceMetric.SecondDisk:
                var second = metric == ResourceMetric.SecondDisk;
                if (second && r is not null && ResourceCharts.LegacyDisk(r)) r = null;
                var legacy = r is not null && ResourceCharts.LegacyDisk(r);
                definition = "The main percentage estimates I/O activity from accumulated read and write operation times divided by elapsed time, capped at 100%. Concurrent operations and multiple drivers overlap, so it is not Windows active-time utilization. All-driver mode sums operation times across drivers. Throughput is shown separately. Missing timing counters, source changes, resets and wake gaps require fresh readings.";
                rows.AddRange([
                    new("Estimated I/O activity", Percent(second ? r?.SecondaryDiskActivePercent : r?.DiskActivePercent)),
                    new("Activity scope", r is null ? "—" : legacy ? "All readable block-storage drivers" : Text(second ? r.SecondaryDiskName : r.DiskName)),
                    new("Read", Rate(second || legacy ? r?.SecondaryDiskReadBytesPerSecond : r?.DiskReadBytesPerSecond)),
                    new("Write", Rate(second || legacy ? r?.SecondaryDiskWriteBytesPerSecond : r?.DiskWriteBytesPerSecond))]);
                if (legacy)
                {
                    definition += " Data-volume capacity is a separate filesystem reading.";
                    rows.AddRange([new("Capacity volume", Text(r?.DiskName)), new("Available capacity", Bytes(r?.DiskFreeBytes)), new("Total capacity", Bytes(r?.DiskTotalBytes))]);
                }
                else rows.Add(new("Device capacity", Bytes(second ? r?.SecondaryDiskTotalBytes : r?.DiskTotalBytes)));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(metric));
        }
        rows.Add(new("Peak · last 2 minutes", reading.Value is null ? "—" : metric is ResourceMetric.Cpu or ResourceMetric.Memory or ResourceMetric.Gpu or ResourceMetric.Storage or ResourceMetric.SecondDisk
            ? Percent(reading.Peak) : metric == ResourceMetric.Network ? ResourceGaugeHistory.NetworkRate(reading.Peak) : Rate(reading.Peak)));
        rows.Add(new("Gauge scale", reading.Value is null ? "—" : reading.Scale));
        return new(metric == ResourceMetric.Cpu ? "CPU details" : metric == ResourceMetric.Gpu ? "GPU details" : metric == ResourceMetric.SecondDisk ? "Disk 2 details" : metric == ResourceMetric.Storage ? "Disk details" : $"{metric} details",
            sample is null ? "Waiting for live readings…" : $"Live · updated {sample.Timestamp:HH:mm:ss zzz}", definition, rows);
    }
    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private static double? PercentValue(double? value) => value is { } number && double.IsFinite(number) && number is >= 0 and <= 100 ? number : null;
    private static string Percent(double? value) => PercentValue(value) is { } number ? $"{number:F1}%" : "—";
    private static string Rate(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? ResourceGaugeHistory.Rate(number) : "—";
    public static string Bytes(ulong? value) => value is not { } bytes ? "—" : bytes >= 1073741824 ? $"{bytes / 1073741824d:F1} GiB" :
        bytes >= 1048576 ? $"{bytes / 1048576d:F1} MiB" : bytes >= 1024 ? $"{bytes / 1024d:F1} KiB" : $"{bytes} B";
}
