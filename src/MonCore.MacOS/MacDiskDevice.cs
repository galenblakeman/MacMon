namespace MonCore.MacOS;

public sealed record MacDiskChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record MacDiskDevice(string Id, string Name, string CounterIdentity, ulong? Read, ulong? Written, ulong? Size);
internal sealed record MacDiskCounters(string Id, string Name, ulong? Read, ulong? Written, ulong? Size)
{
    internal static MacDiskCounters Missing(string? selection) => new("", selection is null ? "" : $"{selection} · disconnected", null, null, null);
}

internal static class MacDiskSelection
{
    internal static MacDiskCounters Resolve(MacActivitySnapshot activity, string? id, bool primary)
    {
        if (id is null)
            return primary ? new(activity.StorageSet, "All block-storage drivers", activity.Read, activity.Written, null) : MacDiskCounters.Missing(null);
        var matches = activity.Disks.Where(d => d.Id == id).ToArray();
        // Duplicate persistent IDs are ambiguous (for example cloned partition tables).
        return matches.Length == 1 ? new(matches[0].CounterIdentity, matches[0].Name, matches[0].Read, matches[0].Written, matches[0].Size) : MacDiskCounters.Missing(id);
    }
    internal static double? Rate(MacDiskCounters? before, MacDiskCounters current, double seconds, bool valid, bool read) =>
        valid && before is not null && current.Id.Length > 0 && before.Id == current.Id
            ? WinMon.Monitoring.ResourceRates.PerSecond(read ? before.Read : before.Written, read ? current.Read : current.Written, seconds) : null;
}
