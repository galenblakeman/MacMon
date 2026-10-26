using WinMon.Monitoring;

namespace MacMon;

public enum ProcessTableOrder { Metric, Name, Pid }
public static class TablePresentation
{
    public static readonly int[] Limits = [0, 5, 10, 20, 50, 100];
    public static int NormalizeLimit(int limit) => Limits.Contains(limit) ? limit : 0;
    // Path membership only: helpers and background executables inside bundles qualify too.
    public static string? ApplicationBundle(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || executable.Length > 4096 || !executable.StartsWith('/') ||
            executable.Contains('\0') || executable.Split('/').Any(part => part is "." or "..")) return null;
        const string marker = ".app/Contents/MacOS/";
        var index = executable.LastIndexOf(marker, StringComparison.Ordinal);
        return index >= 0 && index + marker.Length < executable.Length &&
            !executable[(index + marker.Length)..].Contains('/') ? executable[..(index + 4)] : null;
    }
    public static IEnumerable<ProcessRow> Sort(IEnumerable<ProcessRow> rows, ProcessSort metric,
        ProcessTableOrder order, bool ascending)
    {
        double? Value(ProcessRow row) => row.State != PinnedProcessState.Present ? null : order == ProcessTableOrder.Pid
            ? row.Grouped ? row.Process.MemberCount : row.Process.ProcessId : metric switch
            { ProcessSort.Memory => row.Process.WorkingSetBytes, ProcessSort.Threads => row.Process.ThreadCount,
                ProcessSort.Io => row.Process.IoBytesPerSecond, _ => row.Process.ObservedCpuPercent };
        var pinned = rows.OrderByDescending(row => row.Pinned);
        IOrderedEnumerable<ProcessRow> sorted;
        if (order == ProcessTableOrder.Name)
            sorted = ascending ? pinned.ThenBy(row => row.Process.Name, StringComparer.OrdinalIgnoreCase)
                : pinned.ThenByDescending(row => row.Process.Name, StringComparer.OrdinalIgnoreCase);
        else
        {
            var available = pinned.ThenBy(row => Value(row) is null);
            sorted = ascending ? available.ThenBy(Value) : available.ThenByDescending(Value);
        }
        return sorted.ThenBy(row => row.Process.Name, StringComparer.Ordinal).ThenBy(row => row.Process.ProcessId);
    }
}
