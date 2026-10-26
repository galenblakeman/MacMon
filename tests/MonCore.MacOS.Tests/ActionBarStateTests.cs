using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class ActionBarStateTests
{
    [AvaloniaTest]
    public void ChangingAnyTabUpdatesAllBarsAndTheirIcons()
    {
        var main = new MainWindow();
        var toggles = new[] { main.FindControl<ToggleButton>("ShowProcessActions")!,
            main.FindControl<LaunchdView>("JobsView")!.FindControl<ToggleButton>("ShowJobActions")!,
            main.FindControl<ToggleButton>("ShowUsageActions")! };
        foreach (var toggle in toggles)
        foreach (var expanded in new[] { true, false, true })
        {
            toggle.IsChecked = expanded;
            Assert.That(toggles.All(t => t.IsChecked == expanded), Is.True);
            Assert.That(toggles.All(t => ButtonIcon.GetKind(t) == (expanded ? "ChevronDown" : "ChevronUp")), Is.True);
        }
    }
    [Test]
    public void LegacyChoiceMigratesAndNewChoiceSurvivesRoundTripAndSettingsMerge()
    {
        var old = JsonSerializer.Deserialize<MacPreferences>("{\"ProcessActionsHidden\":true}")!.Normalized(null);
        Assert.That(old.ActionsHidden, Is.True);
        var changed = (old with { ActionsHidden = false }).Normalized(null);
        Assert.That(changed.ProcessActionsHidden, Is.False);
        var restored = JsonSerializer.Deserialize<MacPreferences>(JsonSerializer.Serialize(changed))!.Normalized(null);
        Assert.That(restored.ActionsHidden, Is.False);
        var merged = (SettingsValues.From(restored) with { Theme = "Light" }).ApplyTo(restored, null);
        Assert.That(merged.ActionsHidden, Is.False);
    }
}
