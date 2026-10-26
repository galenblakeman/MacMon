using static MonCore.MacOS.DarwinObjects;

namespace MonCore.MacOS;

internal sealed record MacGpuSnapshot(string Id, string Name, double? Percent, ulong? UsedBytes)
{
    internal static MacGpuSnapshot Empty { get; } = new("", "Unavailable", null, null);
}

/// <summary>Optional device-wide IOAccelerator statistics. Does not substitute renderer/tiler counters or app allocation for utilization.</summary>
internal static class DarwinGpuReader
{
    internal static MacGpuSnapshot Read()
    {
        var devices = Services("IOAccelerator", entry =>
        {
            if (IORegistryEntryGetRegistryEntryID(entry, out var id) != 0) return MacGpuSnapshot.Empty;
            var name = Property(entry, "model", Text);
            var stats = Property(entry, "PerformanceStatistics");
            try { return new MacGpuSnapshot(id.ToString(System.Globalization.CultureInfo.InvariantCulture), name.Length > 0 ? name : $"GPU {id}",
                Validate(Number(Get(stats, "Device Utilization %"))), Counter(Get(stats, "In use system memory"))); }
            finally { Release(stats); }
        });
        // Stable, explicit single-adapter semantics; do not switch to whichever GPU is busiest.
        return devices.Where(d => d.Id.Length > 0).OrderBy(d => d.Id, StringComparer.Ordinal).FirstOrDefault() ?? MacGpuSnapshot.Empty;
    }
    internal static double? Validate(double? percent) => percent is { } p && double.IsFinite(p) && p is >= 0 and <= 100 ? p : null;
}
