using System.Text.Json;
using MonCore.MacOS;
using WinMon.Monitoring;

namespace MacMon;

public sealed record MacPreferences
{
    public int Version { get; init; } = 1;
    public string? BootId { get; init; }
    public bool Grouped { get; init; }
    public bool PinnedOnly { get; init; }
    public int ProcessSort { get; init; }
    public int UsageSort { get; init; }
    public int DataTab { get; init; }
    public int JobSort { get; init; }
    public int JobFilter { get; init; }
    public int HistoryRange { get; init; }
    public string? NetworkId { get; init; }
    public bool AlwaysOnTop { get; init; }
    public double Width { get; init; } = 1000;
    public double Height { get; init; } = 1000;
    public double DashboardRatio { get; init; } = 2;
    public bool KeepHistory { get; init; }
    public SavedProcessPin[] Pins { get; init; } = [];
    public MacCaptureOptions Captures { get; init; } = MacCaptureOptions.Default;

    public MacPreferences Normalized(string? bootId) => this with
    {
        BootId = bootId,
        ProcessSort = Math.Clamp(ProcessSort, 0, 3), UsageSort = Math.Clamp(UsageSort, 0, 2),
        DataTab = Math.Clamp(DataTab, 0, 2), JobSort = Math.Clamp(JobSort, 0, 2), JobFilter = Math.Clamp(JobFilter, 0, 2), HistoryRange = Math.Clamp(HistoryRange, 0, 3),
        Width = double.IsFinite(Width) ? Math.Clamp(Width, 800, 2400) : 1000,
        Height = double.IsFinite(Height) ? Math.Clamp(Height, 760, 1800) : 1000,
        DashboardRatio = double.IsFinite(DashboardRatio) ? Math.Clamp(DashboardRatio, 0.25, 5) : 2,
        NetworkId = string.IsNullOrWhiteSpace(NetworkId) || NetworkId.Length > 256 ? null : NetworkId,
        Pins = ProcessPins.Normalize((Pins ?? []).Where(pin => pin is not null &&
            (pin.Grouped || bootId is not null && BootId == bootId)).Take(512), MacFrameSource.IdentityPolicy),
        Captures = (Captures ?? MacCaptureOptions.Default).Normalized()
    };
}

public sealed record PreferenceLoadResult(MacPreferences Preferences, string? Error = null, bool CanSave = true);

public static class MacPreferenceStore
{
    public static PreferenceLoadResult Load(string path, string? bootId)
    {
        if (!File.Exists(path)) return new(new MacPreferences().Normalized(bootId));
        try
        {
            if (new FileInfo(path).Length > 1_048_576) throw new JsonException("Settings file exceeds the size limit.");
            var settings = JsonSerializer.Deserialize<MacPreferences>(File.ReadAllText(path)) ?? throw new JsonException("Empty settings.");
            if (settings.Version != 1) return new(new MacPreferences().Normalized(bootId), "Settings use an unsupported version; the original file will be preserved.", false);
            return new(settings.Normalized(bootId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var preserved = path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
            try { File.Move(path, preserved); }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            { return new(new MacPreferences().Normalized(bootId), $"Could not read or preserve settings: {ex.Message}", false); }
            return new(new MacPreferences().Normalized(bootId), $"Using defaults; unreadable settings preserved at {preserved}");
        }
    }

    public static void Save(string path, MacPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, preferences); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
