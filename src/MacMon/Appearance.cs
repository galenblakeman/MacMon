using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace MacMon;

public sealed record PaletteColors(Color Cpu, Color Memory, Color Network, Color Storage, Color Gpu)
{
    public Color[] All => [Cpu, Memory, Network, Storage, Gpu];
}

public static class Appearance
{
    public static IReadOnlyList<string> Themes { get; } = Array.AsReadOnly(new[] { "System", "Light", "Dark" });
    public static IReadOnlyList<string> Palettes { get; } = Array.AsReadOnly(new[] { "Classic", "Ocean", "Colorblind", "High contrast" });
    public static string NormalizeTheme(string? theme) => Themes.Contains(theme) ? theme! : "Dark";
    public static string NormalizePalette(string? palette) => Palettes.Contains(palette) ? palette! : "Classic";
    public static ThemeVariant Variant(string? theme) => NormalizeTheme(theme) switch
    { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };

    public static Color Card(bool dark) => Color.Parse(dark ? "#17222E" : "#FFFFFF");
    public static PaletteColors Colors(string? palette, bool dark, bool text = false)
    {
        var hues = NormalizePalette(palette) switch
        {
            "Ocean" => new[] { "#64D8EB", "#AAB4FF", "#79C7FF", "#7DE2B2", "#FFAC84" },
            "Colorblind" => new[] { "#56B4E9", "#E69F00", "#CC79A7", "#009E73", "#F0E442" },
            "High contrast" => new[] { "#00FFFF", "#FFFF00", "#FF80FF", "#FFFFFF", "#80FF80" },
            _ => new[] { "#6DE3CB", "#BD9AFF", "#73BFFF", "#F6C85F", "#FF9F87" }
        };
        // Light high-contrast needs distinct hues rather than four darkened near-black colors.
        if (!dark && palette == "High contrast") hues = ["#005A9C", "#7A2500", "#6A0080", "#006039", "#805000"];
        var colors = hues.Select(hue => EnsureContrast(Color.Parse(hue), Card(dark), text ? 4.5 : 3, dark)).ToArray();
        return new(colors[0], colors[1], colors[2], colors[3], colors[4]);
    }

    public static double Contrast(Color left, Color right)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) => value / 255d <= 0.04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + 0.055) / 1.055, 2.4);
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
        var a = Luminance(left); var b = Luminance(right);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static Color EnsureContrast(Color color, Color background, double minimum, bool dark)
    {
        for (var i = 0; i <= 100; i++)
        {
            byte Adjust(byte channel) => (byte)Math.Round(channel + ((dark ? 255 : 0) - channel) * i / 100d);
            var adjusted = Color.FromRgb(Adjust(color.R), Adjust(color.G), Adjust(color.B));
            if (Contrast(adjusted, background) >= minimum) return adjusted;
        }
        return dark ? ColorsWhite : ColorsBlack;
    }
    private static Color ColorsWhite => Color.FromRgb(255, 255, 255);
    private static Color ColorsBlack => Color.FromRgb(0, 0, 0);

    public static void SetPaletteResources(IResourceDictionary resources, string palette, bool dark)
    {
        var strokes = Colors(palette, dark).All;
        var labels = Colors(palette, dark, true).All;
        var names = new[] { "Cpu", "Memory", "Network", "Storage", "Gpu" };
        for (var i = 0; i < names.Length; i++)
        {
            resources[names[i] + "Stroke"] = new SolidColorBrush(strokes[i]);
            resources[names[i] + "Text"] = new SolidColorBrush(labels[i]);
        }
    }
}

/// <summary>Only fields edited in Settings are merged; live view, window and pin state stay current.</summary>
public sealed record SettingsValues(string Theme, string Palette, bool AlwaysOnTop, bool KeepHistory,
    int HistoryRange, string? NetworkId, MacCaptureOptions Captures, string GaugeStyle = "Bowl")
{
    public static SettingsValues From(MacPreferences preferences) => new(preferences.Theme, preferences.Palette,
        preferences.AlwaysOnTop, preferences.KeepHistory, preferences.HistoryRange, preferences.NetworkId, preferences.Captures, preferences.GaugeStyle);
    public MacPreferences ApplyTo(MacPreferences current, string? bootId) => (current with
    {
        Theme = Theme, Palette = Palette, GaugeStyle = GaugeStyle, AlwaysOnTop = AlwaysOnTop, KeepHistory = KeepHistory,
        HistoryRange = HistoryRange, NetworkId = NetworkId, Captures = Captures
    }).Normalized(bootId);
}
