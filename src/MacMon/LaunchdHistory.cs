using System.Diagnostics;
using MonCore.MacOS;

namespace MacMon;

/// <summary>Session-only inventory observations. Never extrapolate through stale or unobserved intervals.</summary>
public sealed class LaunchdHistory
{
    public const int MaximumSnapshots = 120;
    public const int MaximumJobReferences = 100_000;
    private readonly Queue<(long Moment, LaunchdSnapshot Snapshot)> _entries = new();
    private int _jobs;
    private LaunchdSnapshot? _lastObserved;
    public int Count => _entries.Count;
    public int JobReferences => _jobs;
    public void Record(LaunchdSnapshot snapshot, long moment)
    {
        if (!snapshot.Available || moment <= 0 || snapshot.Jobs.Count > MaximumJobReferences) return;
        if (_entries.LastOrDefault() is var last && last.Snapshot is not null)
        {
            if (moment <= last.Moment || last.Snapshot.Context != snapshot.Context) { _entries.Clear(); _jobs = 0; }
            else if (ReferenceEquals(_lastObserved, snapshot)) return;
        }
        _lastObserved = snapshot;
        _entries.Enqueue((moment, snapshot with { Jobs = snapshot.Jobs.ToArray() })); _jobs += snapshot.Jobs.Count;
        while (_entries.Count > MaximumSnapshots || _jobs > MaximumJobReferences)
            _jobs -= _entries.Dequeue().Snapshot.Jobs.Count;
    }
    public LaunchdSnapshot? At(long moment)
    {
        var entry = _entries.LastOrDefault(entry => entry.Moment <= moment);
        return entry.Snapshot is not null && (moment - entry.Moment) / (double)Stopwatch.Frequency <= 15
            ? entry.Snapshot : null;
    }
}
