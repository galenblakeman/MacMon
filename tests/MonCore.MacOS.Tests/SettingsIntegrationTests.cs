using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class SettingsIntegrationTests
{
    private sealed class Services : IDesktopSettingsServices
    {
        public int LoginStatus => 0;
        public List<string> Calls { get; } = [];
        public bool AllowNotifications;
        public string? ShortcutError;
        public string? SetLogin(bool enabled) { Calls.Add("login:" + enabled); return null; }
        public string? SetShortcut(string id) { Calls.Add("key:" + id); return ShortcutError; }
        public Task<bool> RequestNotifications() { Calls.Add("permission"); return Task.FromResult(AllowNotifications); }
    }
    [Test]
    public async Task GrantedNotificationPermissionIsPersistedOnceAndNotRequestedAgain()
    {
        var current = new MacPreferences(); var services = new Services { AllowNotifications = true }; var saves = 0;
        var draft = current with { Captures = current.Captures with { Notify = true } };
        Assert.That(await SettingsEffects.Save(current, draft, null, services,
            () => { saves++; return Task.CompletedTask; }), Is.Null);
        Assert.That(await SettingsEffects.Save(draft, draft, null, services,
            () => { saves++; return Task.CompletedTask; }), Is.Null);
        Assert.That(saves, Is.EqualTo(2));
        Assert.That(services.Calls, Is.EqualTo(new[] { "permission" }));
    }
    [Test]
    public async Task NativeSettingsRollBackIfAtomicPersistenceFails()
    {
        var current = new MacPreferences(); var services = new Services();
        var error = await SettingsEffects.Save(current, current with { ShowShortcut = "ControlAltM" }, true, services,
            () => throw new IOException("Disk full"));
        Assert.That(error, Does.Contain("Disk full"));
        Assert.That(services.Calls, Is.EqualTo(new[] { "key:ControlAltM", "login:True", "login:False", "key:Off" }));
    }
    [Test]
    public async Task DeniedNotificationPermissionAndShortcutConflictsDoNotPersistDrafts()
    {
        var current = new MacPreferences(); var services = new Services(); var saved = false;
        var draft = current with { Captures = current.Captures with { Notify = true } };
        var error = await SettingsEffects.Save(current, draft, true, services, () => { saved = true; return Task.CompletedTask; });
        Assert.That(error, Does.Contain("not allowed")); Assert.That(saved, Is.False);
        Assert.That(services.Calls, Is.EqualTo(new[] { "permission" }));
        services.Calls.Clear(); services.ShortcutError = "In use";
        error = await SettingsEffects.Save(current, current with { ShowShortcut = "ControlAltM" }, null, services, () => { saved = true; return Task.CompletedTask; });
        Assert.That(error, Is.EqualTo("In use")); Assert.That(saved, Is.False);
        Assert.That(services.Calls, Is.EqualTo(new[] { "key:ControlAltM" }));
    }
    [Test]
    public async Task OrdinarySaveDoesNotRequestPermissionOrChangeNativeRegistration()
    {
        var current = new MacPreferences(); var services = new Services(); var saved = false;
        Assert.That(await SettingsEffects.Save(current, current with { Theme = "Copper" }, false, services,
            () => { saved = true; return Task.CompletedTask; }), Is.Null);
        Assert.That(saved, Is.True); Assert.That(services.Calls, Is.Empty);
        var prefs = (current with { Language = "es", StartupView = "Compact", ShowShortcut = "ControlAltM",
            Captures = current.Captures with { Folder = "/tmp/MacMon reports", Notify = true } }).Normalized("boot");
        var merged = SettingsValues.From(prefs).ApplyTo(prefs, "boot");
        Assert.That(merged.Language, Is.EqualTo("es")); Assert.That(merged.StartupView, Is.EqualTo("Compact"));
        Assert.That(merged.Captures.ReportFolder, Is.EqualTo("/tmp/MacMon reports")); Assert.That(merged.Captures.Notify, Is.True);
        Assert.That(MacDesktopIntegration.NormalizeShortcut("unknown"), Is.EqualTo("Off"));
        Assert.That((prefs with { StartupView = "unknown", Language = "unknown" }).Normalized("boot").StartupView, Is.EqualTo("Last"));
        Assert.That((prefs.Captures with { Folder = "relative/path" }).Normalized().Folder, Is.Null);
    }
}
