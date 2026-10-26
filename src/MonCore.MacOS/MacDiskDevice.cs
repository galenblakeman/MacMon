namespace MonCore.MacOS;

public sealed record MacDiskChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record MacDiskDevice(string Id, string Name, string CounterIdentity, ulong? Read, ulong? Written, ulong? Size,
    ulong? ReadNanoseconds = null, ulong? WriteNanoseconds = null);
internal sealed record MacDiskCounters(string Id, string Name, ulong? Read, ulong? Written, ulong? Size,
    ulong? ReadNanoseconds = null, ulong? WriteNanoseconds = null)
{
    internal static MacDiskCounters Missing(string? selection) => new("", selection is null ? "" : $"{selection} · disconnected", null, null, null);
}

internal static class MacDiskSelection
{
    internal static MacDiskCounters Resolve(MacActivitySnapshot activity, string? id, bool primary)
    {
        if (id is null)
            return primary ? new(activity.StorageSet, "All block-storage drivers", activity.Read, activity.Written, null, SumTime(activity, true), SumTime(activity, false)) : MacDiskCounters.Missing(null);
        var matches = activity.Disks.Where(d => d.Id == id).ToArray();
        // Duplicate persistent IDs are ambiguous (for example cloned partition tables).
        return matches.Length == 1 ? new(matches[0].CounterIdentity, matches[0].Name, matches[0].Read, matches[0].Written, matches[0].Size, matches[0].ReadNanoseconds, matches[0].WriteNanoseconds) : MacDiskCounters.Missing(id);
    }
    private static ulong? SumTime(MacActivitySnapshot activity, bool read)
    {
        if (activity.Read is null || activity.Written is null || activity.Disks.Count == 0) return null;
        try
        {
            ulong sum = 0;
            foreach (var disk in activity.Disks)
            {
                if ((read ? disk.ReadNanoseconds : disk.WriteNanoseconds) is not { } time) return null;
                sum = checked(sum + time);
            }
            return sum;
        }
        catch (OverflowException) { return null; }
    }
    // Accumulated operation durations overlap under concurrent I/O. This is a
    // capped load estimate, never a measurement of non-overlapping busy time.
    internal static double? ActivityPercent(MacDiskCounters? before, MacDiskCounters current, double seconds, bool valid)
    {
        if (!valid || before is null || current.Id.Length == 0 || before.Id != current.Id ||
            current.ReadNanoseconds is null || current.WriteNanoseconds is null ||
            current.ReadNanoseconds == 0 && current.WriteNanoseconds == 0) return null;
        var read = WinMon.Monitoring.ResourceRates.PerSecond(before.ReadNanoseconds, current.ReadNanoseconds, seconds);
        var write = WinMon.Monitoring.ResourceRates.PerSecond(before.WriteNanoseconds, current.WriteNanoseconds, seconds);
        return read is { } r && write is { } w ? Math.Clamp((r + w) / 10_000_000d, 0, 100) : null;
    }
    internal static double? Rate(MacDiskCounters? before, MacDiskCounters current, double seconds, bool valid, bool read) =>
        valid && before is not null && current.Id.Length > 0 && before.Id == current.Id
            ? WinMon.Monitoring.ResourceRates.PerSecond(read ? before.Read : before.Written, read ? current.Read : current.Written, seconds) : null;
}
