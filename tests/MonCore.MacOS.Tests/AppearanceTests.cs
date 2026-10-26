using System.Text.Json;
using Avalonia.Media;
using Avalonia.Styling;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class AppearanceTests
{
    [Test]
    public void DisplayNamesMatchWinMonWhileSavedIdentifiersStayStable()
    {
        Assert.That(Appearance.ThemeLabel("MidnightInk"), Is.EqualTo("Midnight"));
        Assert.That(Appearance.ThemeLabel("EvergreenInstrument"), Is.EqualTo("Evergreen"));
        Assert.That(Appearance.ThemeLabel("FrostedPorcelain"), Is.EqualTo("Frost"));
        Assert.That(Appearance.ThemeLabel("SmokedSapphire"), Is.EqualTo("Sapphire"));
        Assert.That(Appearance.PaletteLabel("High-Contrast & Inclusive"), Is.EqualTo("High-Contrast"));
        Assert.That(Appearance.PaletteLabel("Sophisticated Pastel"), Is.EqualTo("Sophisticated"));
        Assert.That(Appearance.PaletteLabel("Vibrant Monochromatic Shift"), Is.EqualTo("Vibrant"));
        Assert.That(Appearance.PaletteLabel("Warm Earth Tones"), Is.EqualTo("Earth Tones"));
        UiText.Apply("es");
        try
        {
            Assert.That(UiText.Get(Appearance.ThemeLabel("MidnightInk")), Is.EqualTo("Medianoche"));
            Assert.That(UiText.Get(ResourceGauge.StyleLabel("Bowl")), Is.EqualTo("Cuenco"));
            Assert.That(UiText.Get(Appearance.PaletteLabel("Sophisticated Pastel")), Is.EqualTo("Sofisticada"));
            var prefs = new MacPreferences { Theme = "MidnightInk", Palette = "Sophisticated Pastel", GaugeStyle = "Bowl" }.Normalized(null);
            Assert.That(prefs.Theme, Is.EqualTo("MidnightInk"));
            Assert.That(prefs.Palette, Is.EqualTo("Sophisticated Pastel"));
            Assert.That(prefs.GaugeStyle, Is.EqualTo("Bowl"));
        }
        finally { UiText.Apply("en"); }
    }

    [Test]
    public void EveryFinishPaletteKeepsReadoutsAndTracesLegible()
    {
        foreach (var theme in Appearance.Themes.Where(t => t is not ("System" or "Light" or "Dark")))
        foreach (var palette in Appearance.Palettes)
        {
            var dark = Appearance.Variant(theme) == ThemeVariant.Dark;
            var resources = new Avalonia.Controls.ResourceDictionary();
            Appearance.SetThemeResources(resources, theme, dark);
            Color Background(string name) => ((ISolidColorBrush)resources[name]!).Color;
            Assert.That(Appearance.Colors(palette, dark, true, theme).All.All(c =>
                Appearance.Contrast(c, Background("MacCardBrush")) >= 4.5 && Appearance.Contrast(c, Background("MacWindowBrush")) >= 4.5), Is.True, theme + "/" + palette);
            Assert.That(Appearance.Colors(palette, dark, false, theme).All.All(c =>
                Appearance.Contrast(c, Background("MacPlotBrush")) >= 3), Is.True, theme + "/" + palette);
        }
    }
    [Test]
    public void LegacyAndUnknownAppearanceChoicesNormalizeWithoutDiscardingOtherSettings()
    {
        var old = JsonSerializer.Deserialize<MacPreferences>("{\"Grouped\":true,\"HistoryRange\":2}")!.Normalized("boot");
        Assert.That(old.Theme, Is.EqualTo("Dark"));
        Assert.That(old.Palette, Is.EqualTo("Classic"));
        var unknown = (old with { Theme = "future", Palette = null! }).Normalized("boot");
        Assert.That(unknown.Grouped, Is.True);
        Assert.That(unknown.HistoryRange, Is.EqualTo(2));
        Assert.That(unknown.Theme, Is.EqualTo("Dark"));
        Assert.That(unknown.Palette, Is.EqualTo("Classic"));
        Assert.That(Appearance.Variant("System"), Is.EqualTo(ThemeVariant.Default));
    }

    [Test]
    public void PaletteIsIndependentOfThemeAndSettingsMergePreservesLiveViews()
    {
        var current = new MacPreferences { Grouped = true, PinnedOnly = true, DataTab = 2, JobSort = 2,
            Width = 1234, Theme = "Dark", Palette = "Ocean" };
        var values = SettingsValues.From(current) with { Theme = "Light", HistoryRange = 1 };
        var merged = values.ApplyTo(current, "boot");
        Assert.That(merged.Palette, Is.EqualTo("Ocean"));
        Assert.That(merged.Theme, Is.EqualTo("Light"));
        Assert.That(merged.Grouped && merged.PinnedOnly, Is.True);
        Assert.That(merged.DataTab, Is.EqualTo(2));
        Assert.That(merged.JobSort, Is.EqualTo(2));
        Assert.That(merged.Width, Is.EqualTo(1234));
        var path = Path.Combine(Path.GetTempPath(), "macmon-appearance-" + Guid.NewGuid(), "settings.json");
        try
        {
            MacPreferenceStore.Save(path, merged);
            var restored = MacPreferenceStore.Load(path, "boot");
            Assert.That(restored.Preferences.Theme, Is.EqualTo("Light"));
            Assert.That(restored.Preferences.Palette, Is.EqualTo("Ocean"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Test]
    public void EveryPaletteHasReadableResourceTextAndVisibleChartStrokesInBothThemes()
    {
        foreach (var palette in Appearance.Palettes)
        foreach (var dark in new[] { false, true })
        {
            var text = Appearance.Colors(palette, dark, true);
            var strokes = Appearance.Colors(palette, dark);
            Assert.That(text.All.All(color => Appearance.Contrast(color, Appearance.Card(dark)) >= 4.5), Is.True, palette);
            Assert.That(strokes.All.All(color => Appearance.Contrast(color, Appearance.Card(dark)) >= 3), Is.True, palette);
            Assert.That(strokes.All.Distinct().Count(), Is.EqualTo(6), palette);
        }
    }
}
