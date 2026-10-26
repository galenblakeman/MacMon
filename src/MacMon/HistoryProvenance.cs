using WinMon.Monitoring;

namespace MacMon;

/// <summary>Boot provenance is application metadata; it must survive repeated saves across reboots.</summary>
public sealed class HistoryProvenance(string? currentBoot)
{
    private readonly Dictionary<SampleKey, string?> _origins = [];
    public void Restore(IEnumerable<MacHistoryEntry> entries)
    {
        _origins.Clear();
        foreach (var entry in entries) _origins[SampleKey.Of(entry.Sample)] = entry.BootId;
    }
    public string? Origin(MonitorSample? sample) => sample is not null && _origins.TryGetValue(SampleKey.Of(sample), out var boot)
        ? boot : currentBoot;
    public bool SameBoot(MonitorSample? before, MonitorSample? after) => Origin(before) is { } boot && boot == Origin(after);
    public bool CanPin(MonitorSample? sample, bool grouped, bool historical) => grouped || !historical ||
        currentBoot is not null && Origin(sample) == currentBoot;
    public MacHistoryEntry[] Snapshot(IReadOnlyList<MonitorSample> samples) =>
        samples.Select(sample => new MacHistoryEntry(sample, Origin(sample))).ToArray();
}
