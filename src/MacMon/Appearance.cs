using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace MacMon;

public sealed record PaletteColors(Color Cpu, Color Memory, Color Network, Color Storage, Color Gpu, Color SecondDisk)
{
    public Color[] All => [Cpu, Memory, Network, Storage, Gpu, SecondDisk];
}

public static class Appearance
{
    public static IReadOnlyList<string> Themes { get; } = Array.AsReadOnly(new[] { "System", "Light", "Dark" }.Concat(WinMonAppearanceCatalog.Finishes.Keys.OrderBy(ThemeLabel, StringComparer.OrdinalIgnoreCase)).ToArray());
    public static IReadOnlyList<string> Palettes { get; } = WinMonAppearanceCatalog.Palettes;
    public static string NormalizeTheme(string? theme) => Themes.Contains(theme) ? theme! : "Dark";
    public static string NormalizePalette(string? palette) => palette == "High contrast" ? "High-Contrast & Inclusive" : Palettes.Contains(palette) ? palette! : "Classic";
    public static ThemeVariant Variant(string? theme) => NormalizeTheme(theme) switch
    { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, var name when WinMonAppearanceCatalog.Finishes.TryGetValue(name, out var finish) => finish.Light ? ThemeVariant.Light : ThemeVariant.Dark, _ => ThemeVariant.Default };

    public static Color Card(bool dark) => Color.Parse(dark ? "#17222E" : "#FFFFFF");
    public static PaletteColors Colors(string? palette, bool dark, bool text = false, string? theme = null)
    {
        var source = WinMonAppearanceCatalog.Colors(palette, !dark);
        var hues = new[] { source[0], source[1], source[3], source[2], source[4], source[5] };
        var finish = theme is not null ? WinMonAppearanceCatalog.Finishes.GetValueOrDefault(theme) : null;
        var surfaces = text ? new[] { Color.Parse(finish?.Surface ?? (dark ? "#0E1721" : "#EDF2F7")), Color.Parse(finish?.Card ?? (dark ? "#17222E" : "#FFFFFF")) }
            : new[] { Color.Parse(finish?.Plot ?? (dark ? "#0E1721" : "#EDF2F7")), Color.Parse(finish?.Card ?? (dark ? "#17222E" : "#FFFFFF")) };
        var colors = hues.Select(hue => EnsureContrast(Color.Parse(hue), surfaces, text ? 4.5 : 3, dark)).ToArray();
        return new(colors[0], colors[1], colors[2], colors[3], colors[4], colors[5]);
    }

    public static string DefaultPalette(string? theme) => WinMonAppearanceCatalog.PaletteForTheme(theme);
    public static string ThemeLabel(string theme) => theme switch
    {
        "MidnightInk" => "Midnight", "EvergreenInstrument" => "Evergreen",
        "FrostedPorcelain" => "Frost", "SmokedSapphire" => "Sapphire", _ => theme
    };
    public static string PaletteLabel(string palette) => palette switch
    {
        "High-Contrast & Inclusive" => "High-Contrast", "Sophisticated Pastel" => "Sophisticated",
        "Vibrant Monochromatic Shift" => "Vibrant", "Warm Earth Tones" => "Earth Tones", _ => palette
    };
    public static void SetThemeResources(IResourceDictionary resources, string theme, bool dark)
    {
        var f = WinMonAppearanceCatalog.Finishes.GetValueOrDefault(theme);
        (string Key, string Value)[] values = [
            ("MacWindowBrush", f?.Surface ?? (dark ? "#0E1721" : "#EDF2F7")), ("MacCardBrush", f?.Card ?? (dark ? "#17222E" : "#FFFFFF")),
            ("MacChromeBrush", f?.Chrome ?? (dark ? "#0E1721" : "#EDF2F7")), ("MacHeaderBrush", f?.Header ?? (dark ? "#17222E" : "#FFFFFF")),
            ("MacPlotBrush", f?.Plot ?? (dark ? "#0E1721" : "#EDF2F7")), ("MacTextBrush", f?.Text ?? (dark ? "#F2F5F8" : "#17222E")),
            ("MacMutedBrush", f?.Muted ?? (dark ? "#9BAAB9" : "#4D5E70")), ("MacGridBrush", f?.Line ?? (dark ? "#394959" : "#CDD7E2")),
            ("MacGaugeTrackBrush", f?.Track ?? (dark ? "#394959" : "#CDD7E2")), ("MacMarkerBrush", f?.Text ?? (dark ? "#FFFFFF" : "#17222E"))];
        foreach (var (key, value) in values) resources[key] = new SolidColorBrush(Color.Parse(value));
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

    private static Color EnsureContrast(Color color, Color[] backgrounds, double minimum, bool dark)
    {
        for (var i = 0; i <= 100; i++)
        {
            byte Adjust(byte channel) => (byte)Math.Round(channel + ((dark ? 255 : 0) - channel) * i / 100d);
            var adjusted = Color.FromRgb(Adjust(color.R), Adjust(color.G), Adjust(color.B));
            if (backgrounds.All(background => Contrast(adjusted, background) >= minimum)) return adjusted;
        }
        return dark ? ColorsWhite : ColorsBlack;
    }
    private static Color ColorsWhite => Color.FromRgb(255, 255, 255);
    private static Color ColorsBlack => Color.FromRgb(0, 0, 0);

    public static void SetPaletteResources(IResourceDictionary resources, string palette, bool dark, string? theme = null)
    {
        var strokes = Colors(palette, dark, false, theme).All;
        var labels = Colors(palette, dark, true, theme).All;
        var names = new[] { "Cpu", "Memory", "Network", "Storage", "Gpu", "SecondDisk" };
        for (var i = 0; i < names.Length; i++)
        {
            resources[names[i] + "Stroke"] = new SolidColorBrush(strokes[i]);
            resources[names[i] + "Text"] = new SolidColorBrush(labels[i]);
        }
    }
}

/// <summary>Only fields edited in Settings are merged; live view, window and pin state stay current.</summary>
public sealed record SettingsValues(string Theme, string Palette, bool AlwaysOnTop, bool KeepHistory,
    int HistoryRange, string? NetworkId, MacCaptureOptions Captures, string GaugeStyle = "Bowl", string? PrimaryDiskId = null, string? SecondaryDiskId = null,
    string Language = "System", string StartupView = "Last", string ShowShortcut = "Off", bool? OpenAtLogin = null)
{
    public static SettingsValues From(MacPreferences preferences) => new(preferences.Theme, preferences.Palette,
        preferences.AlwaysOnTop, preferences.KeepHistory, preferences.HistoryRange, preferences.NetworkId, preferences.Captures, preferences.GaugeStyle, preferences.PrimaryDiskId, preferences.SecondaryDiskId,
        preferences.Language, preferences.StartupView, preferences.ShowShortcut);
    public MacPreferences ApplyTo(MacPreferences current, string? bootId) => (current with
    {
        Theme = Theme, Palette = Palette, GaugeStyle = GaugeStyle, AlwaysOnTop = AlwaysOnTop, KeepHistory = KeepHistory,
        HistoryRange = HistoryRange, NetworkId = NetworkId, Captures = Captures, PrimaryDiskId = PrimaryDiskId, SecondaryDiskId = SecondaryDiskId,
        Language = Language, StartupView = StartupView, ShowShortcut = ShowShortcut
    }).Normalized(bootId);
}
