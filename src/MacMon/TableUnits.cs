namespace MacMon;

/// <summary>Binary units for table values, chosen independently for each reading.</summary>
public static class TableUnits
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
    public static string Memory(double? bytes) => Format(bytes, false);
    public static string IoRate(double? bytes) => Format(bytes, true);
    public static string IoTotal(double? bytes) => Format(bytes, false);

    private static string Format(double? bytes, bool perSecond)
    {
        if (bytes is not { } value || !double.IsFinite(value) || value < 0) return "—";
        var unit = 0;
        while (unit < Units.Length - 1 && value >= 1024) { value /= 1024; unit++; }
        // Promote values which round to the next unit rather than displaying 1024 KiB.
        if (unit < Units.Length - 1 && Math.Round(value, 1) >= 1024)
        { value /= 1024; unit++; }
        return $"{value:F1} {Units[unit]}{(perSecond ? "/s" : "")}";
    }
}
