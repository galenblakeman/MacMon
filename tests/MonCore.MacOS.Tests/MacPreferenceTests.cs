using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class MacPreferenceTests
{
    [Test]
    public void PinsRestoreOnlyWithinTheSameBootWhileExecutablePinsSurviveReboot()
    {
        var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
        var process = new ProcessUsage(12, 345, "worker", 0)
        { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = "/App" };
        pins.Toggle(process, false); pins.Toggle(process, true);
        var settings = new MacPreferences { BootId = "boot-a", Pins = pins.Snapshot() };
        Assert.That(settings.Normalized("boot-a").Pins, Has.Length.EqualTo(2));
        Assert.That(settings.Normalized("boot-b").Pins.Single().Grouped, Is.True);
        Assert.That(settings.Normalized(null).Pins.Single().Grouped, Is.True);
    }

    [Test]
    public void PreferencesRoundTripAndMalformedFilesArePreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"macmon-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        try
        {
            MacPreferenceStore.Save(path, new MacPreferences { BootId = "boot", Grouped = true, PinnedOnly = true,
                Width = 1200, Height = 900, NetworkId = "en1", AlwaysOnTop = true, HistoryRange = 2,
                Captures = MacCaptureOptions.Default with { Enabled = true } });
            var loaded = MacPreferenceStore.Load(path, "boot");
            Assert.Multiple(() =>
            {
                Assert.That(loaded.Error, Is.Null);
                Assert.That(loaded.Preferences.Grouped, Is.True);
                Assert.That(loaded.Preferences.NetworkId, Is.EqualTo("en1"));
                Assert.That(loaded.Preferences.Captures.Enabled, Is.True);
                Assert.That(loaded.Preferences.Width, Is.EqualTo(1200));
                Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
            });
            File.WriteAllText(path, "{broken");
            var fallback = MacPreferenceStore.Load(path, "boot");
            Assert.That(fallback.Error, Does.Contain("preserved"));
            Assert.That(File.ReadAllText(Directory.GetFiles(directory, "*.invalid-*").Single()), Is.EqualTo("{broken"));
            File.WriteAllText(path, "{\"Version\":999}");
            var unknown = MacPreferenceStore.Load(path, "boot");
            Assert.That(unknown.CanSave, Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo("{\"Version\":999}"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void InvalidDimensionsAndIndicesNormalizeBeforeTheyReachControls()
    {
        var normalized = new MacPreferences { Width = double.NaN, Height = 1, DashboardRatio = double.PositiveInfinity,
            ProcessSort = 999, UsageSort = -1, HistoryRange = 99 }.Normalized("boot");
        Assert.Multiple(() =>
        {
            Assert.That(normalized.Width, Is.EqualTo(1000));
            Assert.That(normalized.Height, Is.EqualTo(760));
            Assert.That(normalized.DashboardRatio, Is.EqualTo(2));
            Assert.That(normalized.ProcessSort, Is.EqualTo(3));
            Assert.That(normalized.UsageSort, Is.Zero);
            Assert.That(normalized.HistoryRange, Is.EqualTo(3));
        });
    }

    [Test]
    public void NativeBootIdentityIsAUuid()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS integration test");
        Assert.That(Guid.TryParse(MacBootIdentity.Read(), out _), Is.True);
    }

    [Test]
    public void DetailsNameMeasurementSemanticsAndHistoricalState()
    {
        var process = new ProcessUsage(12, 345, "worker", 2, 1024, 4096)
        { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, MemberIds = [12, 13] };
        var text = ProcessDetails.Text(new(process, PinnedProcessState.Present),
            new(DateTimeOffset.UnixEpoch, 10, [process], 0), true, true);
        Assert.That(text, Does.Contain("Historical retained observations"));
        Assert.That(text, Does.Contain("five-second average; excludes network"));
        Assert.That(text, Does.Contain("shared pages"));
        Assert.That(text, Does.Contain("12, 13"));
    }
}
