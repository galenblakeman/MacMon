using System.Globalization;
using System.Text;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record LaunchdRow(LaunchdJob Job, string Label, string State, string Pid, string ExitStatus, string ProcessName)
{
    public ProcessUsage? Process { get; init; }
    public ProcessTrail Trail { get; init; } = new([]);
    public string Name => Label.StartsWith("application.", StringComparison.Ordinal) && ProcessName != "—" ? ProcessName : Label;
    public string Cpu => Process?.ObservedCpuPercent is { } value ? $"{value:F1}%" : "—";
    public string Resident => Process?.WorkingSetBytes is { } value ? TableUnits.Memory(value) : "—";
    public string Io => Process?.IoBytesPerSecond is { } value ? TableUnits.IoRate(value) : "—";
    public string ProcessDescription => $"{(Name == Label ? ProcessName : Label)} · PID {Pid}";
    public string Description => UiText.Interpolate($"{Label}\n{State} · PID {Pid} · last {ExitStatus}\nResource readings describe only the verified direct process at this observation; child workers are excluded from totals.");
    public override string ToString() => UiText.Interpolate($"{Label}, {State}, PID {Pid}, last {ExitStatus}, observed process {ProcessName}");
}
public sealed class LaunchdInventory(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private long _receivedAt;
    public TimeSpan Age => LastSuccess is null ? TimeSpan.MaxValue : _clock.GetElapsedTime(_receivedAt);
    public bool IsStale => LastSuccess is not null && (Error is not null || Age.TotalSeconds >= 15);
    public LaunchdSnapshot? LastSuccess { get; private set; }
    public string? Error { get; private set; }
    public void Apply(LaunchdSnapshot snapshot)
    {
        Error = snapshot.Error;
        if (snapshot.Available) { LastSuccess = snapshot; _receivedAt = _clock.GetTimestamp(); }
    }
}

public static class LaunchdPresentation
{
    public static string Context(LaunchdContext context) => UiText.Interpolate($"Current bootstrap context · UID {context.UserId} · {context.Manager}");
    public static string ExitText(int status) => status < 0 ? UiText.Interpolate($"Signal {-(long)status}") : UiText.Interpolate($"Exit {status}");
    public static ProcessUsage? Match(LaunchdJob job, MonitorSample? live, bool fresh)
    {
        if (!fresh || job.ObservedIdentity is not { } identity) return null;
        return live?.Processes.FirstOrDefault(process => process.Identity == identity);
    }
    public static IReadOnlyList<LaunchdRow> Rows(LaunchdSnapshot? snapshot, MonitorSample? live, bool fresh,
        string? search, int filter, int sort, int limit = 0, IReadOnlyList<MonitorSample>? history = null, bool? ascending = null)
    {
        var query = search?.Trim() ?? "";
        var current = fresh && live is not null ? live.Processes.ToDictionary(p => p.Identity) : [];
        var rows = (snapshot?.Jobs ?? []).Where(job => filter switch
        { 1 => job.ReportedPid is not null, 2 => job.ReportedPid is null, _ => true })
            .Select(job => new LaunchdRow(job, job.Id.Label, UiText.Get(job.ReportedPid is null ? "Not running" : "Running"),
                job.ReportedPid?.ToString(CultureInfo.InvariantCulture) ?? "—", ExitText(job.LastExitStatus),
                job.ObservedIdentity is { } identity && current.TryGetValue(identity, out var process) ? process.Name : "—")
            { Process = job.ObservedIdentity is { } key && current.TryGetValue(key, out var matched) ? matched : null })
            .Where(row => query.Length == 0 || row.Label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                row.Pid.Contains(query, StringComparison.Ordinal) || row.ProcessName.Contains(query, StringComparison.OrdinalIgnoreCase));
        var direction = ascending ?? sort < 3;
        double? Value(LaunchdRow row) => sort switch
        { 1 => row.Job.ReportedPid is not null ? 0 : 1, 2 => row.Job.ReportedPid, 3 => row.Process?.ObservedCpuPercent,
          4 => row.Process?.WorkingSetBytes, 5 => row.Process?.IoBytesPerSecond, _ => 0 };
        var sorted = rows.OrderBy(row => Value(row) is null).ThenBy(row => row,
            Comparer<LaunchdRow>.Create((a, b) => direction
                ? sort == 0 ? StringComparer.Ordinal.Compare(a.Label, b.Label) : Nullable.Compare(Value(a), Value(b))
                : sort == 0 ? StringComparer.Ordinal.Compare(b.Label, a.Label) : Nullable.Compare(Value(b), Value(a))))
            .ThenBy(row => row.Label, StringComparer.Ordinal);
        var maximum = TablePresentation.NormalizeLimit(limit);
        var shown = (maximum == 0 ? sorted.AsEnumerable() : sorted.Take(maximum)).ToArray();
        if (history is null || history.Count == 0 || shown.Length == 0) return shown;
        // Only index visible, verified lifetimes. Several jobs may refer to the same process.
        var trails = new Dictionary<ProcessLifetimeIdentity, double?[]>();
        var instants = new List<MonitorSample?>();
        long previous = 0;
        foreach (var sample in history)
        {
            if (previous != 0 && (sample.MonotonicTimestamp - previous) / (double)System.Diagnostics.Stopwatch.Frequency > 2.5)
                instants.Add(null);
            instants.Add(sample);
            previous = sample.MonotonicTimestamp;
        }
        foreach (var row in shown)
            if (row.Process is { } process && !trails.ContainsKey(process.Identity))
                trails.Add(process.Identity, new double?[instants.Count]);
        // A small row limit should not hash full lifetime keys for every unrelated process.
        // PID is only a prefilter: association still requires the complete lifetime below.
        var visiblePids = live is not null && trails.Count < live.Processes.Count / 4
            ? trails.Keys.Select(identity => identity.ProcessId).ToHashSet() : null;
        if (trails.Count > 0)
            for (var instant = 0; instant < instants.Count; instant++)
            {
                if (instants[instant] is not { } sample || (sample.IsGap && sort != 4)) continue;
                foreach (var process in sample.Processes)
                    if ((visiblePids is null || visiblePids.Contains(process.ProcessId)) &&
                        trails.TryGetValue(process.Identity, out var values))
                        values[instant] = sort switch
                        { 4 => process.WorkingSetBytes, 5 => process.IoBytesPerSecond, _ => process.ObservedCpuPercent };
            }
        var maximumTrail = sort is 4 or 5 ? Math.Max(1, trails.Values.SelectMany(values => values).Max() ?? 1) : 100;
        for (var i = 0; i < shown.Length; i++)
            shown[i] = shown[i] with { Trail = new(shown[i].Process is { } process ? trails[process.Identity] : [], maximumTrail) };
        return shown;
    }

    public static string Details(LaunchdJob job, DateTimeOffset observedAt, MonitorSample? live, bool fresh, bool stale)
    {
        var text = new StringBuilder();
        text.AppendLine(job.Id.Label).AppendLine(Context(job.Id.Context))
            .AppendLine(UiText.Interpolate($"Observed: {observedAt:yyyy-MM-dd HH:mm:ss zzz}{(stale ? UiText.Get(" · STALE inventory") : "")}"))
            .AppendLine(job.ReportedPid is null ? UiText.Get("State: Not running (no PID reported)") : UiText.Interpolate($"State: Running at observation · PID {job.ReportedPid}"))
            .AppendLine(UiText.Interpolate($"Last termination: {ExitText(job.LastExitStatus)}"))
            .AppendLine(UiText.Get("The last exit status is not current health. Not running does not mean disabled."))
            .AppendLine(UiText.Get("Loaded jobs in this context only; disabled state and other domains are not inventoried. Worker trees show observed descendants only."));
        var process = Match(job, live, fresh && !stale);
        if (process is null) text.AppendLine(UiText.Get("\nProcess details unavailable: no fresh matching lifetime. No PID-only association is used."));
        else
        {
            text.AppendLine(UiText.Get("\nMatched observed process (the job may change after observation):"))
                .AppendLine(UiText.Interpolate($"{process.Name} · PID {process.ProcessId}"))
                .AppendLine(UiText.Interpolate($"Started: {process.StartedAtUtc?.ToString("O") ?? UiText.Get("unavailable")}"))
                .AppendLine(UiText.Interpolate($"Identity: {process.LifetimeDomain} · {process.CreationTicks}"))
                .AppendLine(UiText.Interpolate($"Executable: {(process.ImagePath.Length == 0 ? UiText.Get("unavailable") : process.ImagePath)}"));
        }
        return text.ToString();
    }
}
