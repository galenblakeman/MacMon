namespace MacMon;

/// <summary>Binary memory and decimal I/O units, chosen independently for each reading.</summary>
public static class TableUnits
{
    private static readonly string[] MemoryUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
    private static readonly string[] IoUnits = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
    public static string Memory(double? bytes) => Format(bytes, false, 1024, MemoryUnits);
    public static string IoRate(double? bytes) => Format(bytes, true, 1000, IoUnits);
    public static string IoTotal(double? bytes) => Format(bytes, false, 1000, IoUnits);

    private static string Format(double? bytes, bool perSecond, int scale, string[] units)
    {
        if (bytes is not { } value || !double.IsFinite(value) || value < 0) return "—";
        var unit = 0;
        while (unit < units.Length - 1 && value >= scale) { value /= scale; unit++; }
        // Promote rounded values at the next unit boundary.
        if (unit < units.Length - 1 && Math.Round(value, 1) >= scale)
        { value /= scale; unit++; }
        return $"{value:F1} {units[unit]}{(perSecond ? "/s" : "")}";
    }
}
