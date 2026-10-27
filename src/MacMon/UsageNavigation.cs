using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record UsageProcessTarget(MonitorSample Sample, ProcessUsage Process, bool Historical);

/// <summary>Resolves the exact retained moment and executable, never a neighbouring sample or reused PID.</summary>
public static class UsageNavigation
{
    public static UsageProcessTarget? Resolve(AppUsage app, IReadOnlyList<MonitorSample> retained, MonitorSample? live)
    {
        if (string.IsNullOrEmpty(app.ImagePath)) return null;
        var historical = app.Running == 0;
        var sample = historical ? app.LastActive is { } key ? retained.LastOrDefault(key.Matches) : null : live;
        var policy = MacFrameSource.IdentityPolicy;
        var process = sample?.Processes.FirstOrDefault(p => p.ImagePath == app.ImagePath &&
            policy.Comparer.Equals(policy.ExecutableKey(p), app.Key));
        return sample is null || process is null ? null : new(sample, process, historical);
    }
}
