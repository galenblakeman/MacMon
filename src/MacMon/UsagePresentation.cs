using System.Globalization;
using WinMon.Monitoring;

namespace MacMon;

public enum UsageSort { Cpu, DiskIo, LastActive, Name, BusyShare, Processes }

public sealed record UsageRow(AppUsage App, long MachineBusyTicks)
{
    public Avalonia.Media.IImage Icon => ApplicationIcons.For(App.ImagePath);
    public string Name => App.Name;
    public string Cpu => $"{TimeSpan.FromTicks(App.CpuTicks).TotalSeconds:F1} s";
    public string Share => MachineBusyTicks > 0 ? $"{100d * App.CpuTicks / MachineBusyTicks:F1}%" : "—";
    public string DiskIo => $"{App.IoBytes / 1048576:F1} MiB";
    public string Processes => $"{App.Running} / {App.Lifetimes}";
    public string LastActive => App.LastActive?.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) ?? "—";
    public string Path => App.ImagePath;
    public override string ToString() => $"{Name}, CPU {Cpu}, disk I/O {DiskIo}, running/seen {Processes}";
}

public static class UsagePresentation
{
    public static UsageRow[] Rows(UsageLedger ledger, string search, UsageSort sort, int limit = 0, bool appsOnly = false, bool ascending = false)
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
        return (maximum == 0 ? apps.AsEnumerable() : apps.Take(maximum)).Select(app => new UsageRow(app, ledger.MachineBusyTicks)).ToArray();
    }
}
