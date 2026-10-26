using System.Globalization;
using WinMon.Monitoring;

namespace MacMon;

public enum UsageSort { Cpu, DiskIo, LastActive, Name, BusyShare, Processes }

public sealed record UsageRow(AppUsage App, long MachineBusyTicks)
{
    public Avalonia.Media.IImage Icon => ApplicationIcons.For(App.ImagePath);
    public DateTimeOffset? LatestObservation { get; init; }
    public string Name => App.Name;
    public string Cpu => $"{(long)TimeSpan.FromTicks(App.CpuTicks).TotalHours}:{TimeSpan.FromTicks(App.CpuTicks).Minutes:00}:{TimeSpan.FromTicks(App.CpuTicks).Seconds:00}";
    public string Share => MachineBusyTicks > 0 ? $"{100d * App.CpuTicks / MachineBusyTicks:F1}%" : "—";
    public string DiskIo => TableUnits.IoTotal(App.IoBytes);
    public string Processes => $"{App.Lifetimes} ({App.Running})";
    public string LastActive
    {
        get
        {
            if (App.LastActive is not { } active) return "—";
            if (LatestObservation is { } now && (now - active.Timestamp).TotalSeconds is >= 0 and <= 5) return "now";
            var local = active.Timestamp.ToLocalTime();
            return local.ToString(local.Date == DateTimeOffset.Now.Date ? "T" : "g", CultureInfo.CurrentCulture);
        }
    }
    public string Path => App.ImagePath;
    public override string ToString() => $"{Name}, CPU {Cpu}, I/O {DiskIo}, running/seen {Processes}";
}

public static class UsagePresentation
{
    public static string Report(IEnumerable<UsageRow> rows, DateTimeOffset since)
    {
        static string Cell(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        return $"MacMon session usage since {since:O}\n" +
            "App\tCPU time\tBusy share\tI/O\tSeen (running)\tLast active\tExecutable\n" +
            string.Join("\n", rows.Select(r => string.Join('\t', new[] { r.Name, r.Cpu, r.Share, r.DiskIo, r.Processes, r.LastActive, r.Path }.Select(Cell))));
    }
    public static UsageRow[] Rows(UsageLedger ledger, string search, UsageSort sort, int limit = 0, bool appsOnly = false, bool ascending = false, DateTimeOffset? latest = null)
    {
        var matching = ledger.Apps.Where(app => (!appsOnly || TablePresentation.ApplicationBundle(app.ImagePath) is not null) &&
            (app.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || app.ImagePath.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));
        var comparer = Comparer<AppUsage>.Create((a, b) =>
        {
            var compare = sort switch
            {
                UsageSort.Name => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name),
                UsageSort.DiskIo => a.IoBytes.CompareTo(b.IoBytes),
                UsageSort.LastActive => Nullable.Compare(a.LastActive?.Timestamp, b.LastActive?.Timestamp),
                UsageSort.Processes => a.Running != b.Running ? a.Running.CompareTo(b.Running) : a.Lifetimes.CompareTo(b.Lifetimes),
                _ => a.CpuTicks.CompareTo(b.CpuTicks)
            };
            return ascending ? compare : -compare;
        });
        var apps = matching.OrderBy(app => sort == UsageSort.LastActive && app.LastActive is null)
            .ThenBy(app => app, comparer).ThenBy(app => app.Key, StringComparer.Ordinal);
        var maximum = TablePresentation.NormalizeLimit(limit);
        return (maximum == 0 ? apps.AsEnumerable() : apps.Take(maximum)).Select(app => new UsageRow(app, ledger.MachineBusyTicks) { LatestObservation = latest }).ToArray();
    }
}
