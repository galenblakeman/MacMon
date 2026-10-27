using System.Text.Json;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record UpdateRestartResult(HistoryLoadResult History, UsageLedgerState Usage);

/// <summary>A bounded, same-boot, single-use handoff for Sparkle's next launch.</summary>
public static class MacUpdateRestart
{
    private sealed record Manifest(int Version, string BootId, DateTimeOffset SavedAt, UsageLedgerState Usage);
    private const long MaximumManifestBytes = 64L << 20;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);

    public static void Save(string directory, string? bootId, DateTimeOffset now,
        IReadOnlyList<MacHistoryEntry> history, UsageLedgerState usage)
    {
        if (string.IsNullOrEmpty(bootId)) throw new InvalidDataException("Update restart requires a boot identity.");
        Validate(usage);
        var staging = directory + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            MacHistoryStore.Save(Path.Combine(staging, "history.gz"), history);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Manifest(1, bootId, now, usage));
            if (bytes.LongLength > MaximumManifestBytes) throw new InvalidDataException("Update usage exceeds its size limit.");
            using (var file = new FileStream(Path.Combine(staging, "manifest.json"), FileMode.CreateNew))
            { file.Write(bytes); file.Flush(true); }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.Move(staging, directory);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public static UpdateRestartResult? Consume(string directory, string? bootId, int retentionSeconds,
        DateTimeOffset now, long monotonicNow)
    {
        if (!Directory.Exists(directory)) return null;
        // Claim before reading, so two launches cannot both import the same accounting period.
        var claimed = directory + "." + Guid.NewGuid().ToString("N");
        try { Directory.Move(directory, claimed); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        try
        {
            var path = Path.Combine(claimed, "manifest.json");
            if (new FileInfo(path).Length > MaximumManifestBytes) return null;
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllBytes(path));
            if (manifest is null || manifest.Version != 1 || string.IsNullOrEmpty(bootId) || manifest.BootId != bootId ||
                now < manifest.SavedAt || now - manifest.SavedAt > MaximumAge) return null;
            Validate(manifest.Usage);
            var historyPath = Path.Combine(claimed, "history.gz");
            if (!File.Exists(historyPath)) return null;
            var history = MacHistoryStore.Load(historyPath, retentionSeconds, now, monotonicNow);
            if (history.Error is not null || !history.CanSave) return null;
            var usage = manifest.Usage with { Apps = manifest.Usage.Apps.Select(app => app with
            { LastActive = app.LastActive is { } key ? history.ResolvePeak(key) : null }).ToArray() };
            return new(history, usage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return null; }
        finally
        {
            try { if (Directory.Exists(claimed)) Directory.Delete(claimed, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Already claimed; cannot be imported again. */ }
        }
    }

    private static void Validate(UsageLedgerState? state)
    {
        static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
        if (state is null || state.IdentityPolicyId != MacFrameSource.IdentityPolicy.Id || state.Apps is null || state.Lifetimes is null ||
            state.Apps.Count > UsageLedger.MaxApps + 1 || state.Lifetimes.Count > MacHistoryStore.MaxProcesses ||
            !Nonnegative(state.MachineBusyTicks) || state.MachineBusyTicks > long.MaxValue ||
            !Nonnegative(state.AttributedTicks) || state.AttributedTicks > long.MaxValue ||
            state.Apps.Any(app => app is null || app.Key is null || app.Name is null || app.ImagePath is null ||
                app.AppName is null || app.Publisher is null || !Nonnegative(app.CpuTicks) || app.CpuTicks > long.MaxValue ||
                !Nonnegative(app.IoBytes) || app.GpuSeconds is { } gpu && !Nonnegative(gpu) || app.Lifetimes < 0) ||
            state.Apps.Select(app => app.Key).Distinct(StringComparer.Ordinal).Count() != state.Apps.Count ||
            state.Lifetimes.Any(lifetime => (uint)lifetime.App >= (uint)state.Apps.Count || lifetime.Absences < 0 ||
                lifetime.LifetimeDomain != ProcessLifetimeDomain.UnixMicroseconds))
            throw new InvalidDataException("Invalid update usage state.");
    }
}
