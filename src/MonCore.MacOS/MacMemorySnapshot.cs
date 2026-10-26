namespace MonCore.MacOS;

/// <summary>Live macOS memory details. Usage is anonymous non-purgeable + wired + compressor storage.</summary>
public sealed record MacMemorySnapshot(ulong? UsedBytes, ulong? CachedBytes, ulong? CompressedBytes, string Pressure)
{
    internal static string PressureName(uint level) => level switch
    {
        1 => "Normal",
        2 => "Warning",
        4 => "Critical",
        _ => "Unavailable"
    };

    internal static MacMemorySnapshot FromPages(ulong? total, uint pageSize, uint internalPages,
        uint purgeable, uint wired, uint compressor, uint external, string pressure)
    {
        if (total is null or 0 || pageSize == 0 || purgeable > internalPages)
            return new(null, null, null, pressure);
        var used = ((ulong)internalPages - purgeable + wired + compressor) * pageSize;
        if (used > total) return new(null, null, null, pressure);
        return new(used, ((ulong)external + purgeable) * pageSize, (ulong)compressor * pageSize, pressure);
    }
}
