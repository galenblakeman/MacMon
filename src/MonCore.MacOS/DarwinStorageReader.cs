using static MonCore.MacOS.DarwinObjects;

namespace MonCore.MacOS;

/// <summary>One snapshot of block-driver counters; whole media only, never APFS volumes or partitions.</summary>
internal static class DarwinStorageReader
{
    private static readonly string Boot = MacBootIdentity.Read() ?? "session:" + Guid.NewGuid().ToString("N");
    internal static (string Key, ulong? Read, ulong? Written, MacDiskDevice[] Devices) Read()
    {
        var observed = Services("IOBlockStorageDriver", driver =>
        {
            if (IORegistryEntryGetRegistryEntryID(driver, out var registryId) != 0) return null;
            var counters = Property(driver, "Statistics");
            try
            {
                var read = Counter(Get(counters, "Bytes (Read)"));
                var write = Counter(Get(counters, "Bytes (Write)"));
                var readTime = Counter(Get(counters, "Total Time (Read)"));
                var writeTime = Counter(Get(counters, "Total Time (Write)"));
                var media = Children(driver, child =>
                {
                    var bsd = Property(child, "BSD Name", Text);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(bsd, @"^disk[0-9]+$")) return null;
                    var uuid = Children(child, part => Property(part, "UUID", Text)).FirstOrDefault(v => Guid.TryParse(v, out _));
                    // A GPT UUID survives reconnection and BSD renumbering. Unidentified media
                    // deliberately use a boot-scoped identity rather than silently following diskN.
                    var id = uuid is not null ? "gpt:" + uuid.ToUpperInvariant() : $"boot:{Boot}:registry:{registryId}";
                    return new MacDiskDevice(id, $"{bsd} · {Name(child)}", $"{id}:{registryId}", read, write, Property(child, "Size", Counter), readTime, writeTime);
                }).Where(d => d is not null).ToArray();
                // An unreadable/unknown driver still participates in aggregate availability.
                return media.Length == 1 ? media[0] : new MacDiskDevice($"boot:{Boot}:registry:{registryId}",
                    $"Block driver {registryId}", registryId.ToString(), read, write, null, readTime, writeTime);
            }
            finally { Release(counters); }
        });
        var devices = observed.Where(d => d is not null).Cast<MacDiskDevice>().OrderBy(d => d.CounterIdentity, StringComparer.Ordinal).ToArray();
        try
        {
            var valid = devices.Length > 0 && observed.All(d => d is not null) && devices.All(d => d.Read is not null && d.Written is not null);
            return (string.Join(',', devices.Select(d => d.CounterIdentity)),
                valid ? devices.Aggregate(0UL, (sum, d) => checked(sum + d.Read!.Value)) : null,
                valid ? devices.Aggregate(0UL, (sum, d) => checked(sum + d.Written!.Value)) : null, devices);
        }
        catch (OverflowException) { return ("", null, null, devices); }
    }
}
