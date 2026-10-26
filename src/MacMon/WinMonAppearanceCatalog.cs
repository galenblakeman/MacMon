using Avalonia.Media;

namespace MacMon;

// Solid-surface adaptation of WinMon's MIT-licensed theme/palette catalog.
// See dependencies/winmon-ui for source provenance and license.
internal static class WinMonAppearanceCatalog
{
    internal sealed record Finish(string Surface, string Card, string Chrome, string Header, string Plot,
        string Text, string Muted, string Line, string Track, bool Light);
    internal static readonly Dictionary<string, Finish> Finishes = new()
    {
        ["Candy"] = new("#FFF8F1", "#FFFFFF", "#F7A6C7", "#F7A6C7", "#FFFCF9", "#2E2134", "#655369", "#D3C6CE", "#D9D2D2", true),
        ["Copper"] = new("#574033", "#71503E", "#A67556", "#77513C", "#171D24", "#FFF1E6", "#E0BEAA", "#956E59", "#A47D68", false),
        ["Gunmetal"] = new("#1D252C", "#2A343D", "#3A454D", "#303B43", "#151D26", "#ECF3F5", "#B6C5CB", "#53626B", "#667985", false),
        ["Porcelain"] = new("#F5F2EB", "#FFFEFA", "#E8E2D8", "#EEE9E0", "#FFFEFA", "#242B31", "#59636A", "#CCC5B9", "#D5D0C7", true),
        ["MidnightInk"] = new("#101A28", "#1B2A3D", "#23364C", "#1D3045", "#0D1724", "#EAF1F7", "#B9C9D8", "#43566A", "#52677D", false),
        ["EvergreenInstrument"] = new("#172522", "#23352F", "#30463D", "#273C34", "#111E1B", "#EFF3EA", "#BFCCBE", "#52665A", "#627968", false),
        ["FrostedPorcelain"] = new("#F2F4F6", "#FFFFFF", "#EDF2F8", "#F1F5FA", "#FFFFFF", "#202B39", "#536476", "#B9C8D5", "#C4D0DA", true),
        ["SmokedSapphire"] = new("#10213C", "#1B3050", "#19395F", "#1A3658", "#0B1930", "#F1F6FF", "#C2D2E5", "#496787", "#56718E", false),
        ["Crystal"] = new("#1C2938", "#344B61", "#537399", "#435E7D", "#121B27", "#EDF5FC", "#C5D3E0", "#5A7894", "#5C7185", false),
        ["Slate"] = new("#2B3A48", "#3D5061", "#526878", "#4D6374", "#111A23", "#F1F6F9", "#CAD6DD", "#647986", "#718591", false),
        ["Prism"] = new("#1A2031", "#303A53", "#332F5C", "#303A63", "#101624", "#F1F2FF", "#C0C3E0", "#5467A0", "#636F93", false),
        ["Wildberry"] = new("#211629", "#38203F", "#492447", "#4A2549", "#1D1424", "#FFF1FA", "#D0AFC8", "#69405F", "#796878", false),
    };
    internal static IReadOnlyList<string> Palettes { get; } = [
        "Classic", "Ocean", "Colorblind",
        "High-Contrast & Inclusive", "Sophisticated Pastel", "Vibrant Monochromatic Shift",
        "Warm Earth Tones", "Cool Tech",
        "Studio", "Signal", "Botanical", "Spectrum", "Neon", "Arcade", "Tropical", "Candy", "Wildberry",
        "Copper", "Gunmetal"
    ];
    internal static string PaletteForTheme(string? theme) => theme switch
    {
        "Candy" or "Wildberry" or "Copper" or "Gunmetal" => theme,
        "Crystal" => "Cool Tech",
        "EvergreenInstrument" or "Porcelain" => "Warm Earth Tones",
        "FrostedPorcelain" => "High-Contrast & Inclusive",
        "MidnightInk" => "Neon",
        "Prism" => "Arcade",
        "SmokedSapphire" => "Tropical",
        "Slate" => "Ocean",
        _ => "Classic"
    };
    internal static string[] Colors(string? name, bool light)
    {
        return (Appearance.NormalizePalette(name), light) switch
        {
            // Fixed hues in both themes. Order is CPU, memory, primary disk, network, GPU, secondary disk.
            ("Cool Tech", _) => ["#00A8E8", "#00C9A7", "#FF6F59", "#4B6584", "#80ED99", "#845EC2"],
            ("Studio", _) => ["#59A14F", "#4E79A7", "#F28E2B", "#B07AA1", "#76B7B2", "#EDC949"],
            ("Botanical", _) => ["#66A61E", "#7570B3", "#D95F02", "#E7298A", "#1B9E77", "#E6AB02"],
            ("Tropical", _) => ["#FF3D71", "#00B8D9", "#FFAB00", "#6554FF", "#00C896", "#FF5630"],
            // Hand-tuned sets clear every plot in their mode at 3.5:1, so chart traces keep the designed color.
            ("High-Contrast & Inclusive", false) => ["#05C49A", "#5C7CFF", "#FC581E", "#A67FE0", "#87CEFA", "#ECB925"],
            ("High-Contrast & Inclusive", true) => ["#008A6A", "#0000CD", "#EF4B0E", "#663791", "#1280C3", "#744E00"],
            ("Sophisticated Pastel", false) => ["#D5E8A3", "#C5CBEF", "#DFA895", "#C99AA4", "#B4E3D3", "#CDBA96"],
            ("Sophisticated Pastel", true) => ["#6E8C3F", "#6673C2", "#BF6F55", "#A86478", "#3E8F7A", "#957A4E"],
            ("Vibrant Monochromatic Shift", false) => ["#2E9E5E", "#3A9AE0", "#4F8F84", "#4A7FE8", "#8AD6CF", "#B8C9A8"],
            ("Vibrant Monochromatic Shift", true) => ["#014421", "#0072B5", "#2F5F57", "#0047AB", "#1F8F87", "#6B8752"],
            // The dark CPU green stays clear of Evergreen's accent greens.
            ("Warm Earth Tones", false) => ["#60CA81", "#E29F3D", "#D7673B", "#B7A8E2", "#E7CCA3", "#CAAF60"],
            ("Warm Earth Tones", true) => ["#428156", "#A06D20", "#963712", "#573669", "#6F4E33", "#706F33"],
            ("Signal", false) => ["#009E73", "#0072B2", "#D55E00", "#CC79A7", "#56B4E9", "#E69F00"],
            ("Signal", true) => ["#007A57", "#0B4F8A", "#A33C00", "#B0588A", "#298DCD", "#B57A00"],
            ("Spectrum", false) => ["#22A884", "#3A8FA8", "#7AD151", "#6F75C8", "#B06CD8", "#FDE725"],
            ("Spectrum", true) => ["#1E8F70", "#2A6F8E", "#4F8F1F", "#3E4A8F", "#5A0F6E", "#8A7A00"],
            ("Neon", false) => ["#00E5FF", "#FF1493", "#B6FF00", "#FF6D00", "#A100FF", "#FFE600"],
            ("Neon", true) => ["#0090C1", "#F0127F", "#4E9700", "#E55900", "#9A00F0", "#B07D00"],
            ("Arcade", false) => ["#FF1744", "#2979FF", "#00E676", "#FFEA00", "#D500F9", "#FF9100"],
            ("Arcade", true) => ["#F0153F", "#2266F0", "#009C4E", "#B07D00", "#B800D9", "#C24E00"],
            ("Candy", false) => ["#7EE36E", "#5FA8FF", "#FF9C52", "#FF7DB8", "#45E0C6", "#FFD25E"],
            ("Candy", true) => ["#1F6A2D", "#1265C0", "#C2430A", "#910B43", "#0E7A6C", "#694100"],
            ("Wildberry", false) => ["#9BD87B", "#81A8FF", "#FD9E5A", "#FF5BA0", "#AA7BD6", "#FFD56C"],
            ("Wildberry", true) => ["#4D8A2E", "#185BCF", "#B4510C", "#C2195B", "#7436A8", "#724700"],
            // Copper's dark set clears 4.75:1 on its brown panel, and avoids its peach and gold chrome.
            ("Copper", false) => ["#77DC95", "#AFC0E5", "#F3D447", "#FD9CB6", "#97F4F0", "#C9A9FF"],
            ("Copper", true) => ["#206B31", "#2F60C6", "#8A6A00", "#B52162", "#1A7B87", "#602994"],
            ("Gunmetal", false) => ["#5CE47F", "#6BB7FA", "#FFAF21", "#F05FCE", "#9A86FD", "#FF6B4A"],
            ("Gunmetal", true) => ["#297F3B", "#2B60B7", "#B66400", "#B0257F", "#443BBA", "#B32F21"],
            ("Ocean", false) => ["#55CFFF", "#B0A0FF", "#FFAE70", "#FF89B5", "#60E0BD", "#E5D66A"],
            ("Ocean", true) => ["#006C96", "#6745A5", "#9E4810", "#A02E62", "#08765C", "#776600"],
            ("Colorblind", false) => ["#56B4E9", "#E69F00", "#F0E442", "#CC79A7", "#3DCCA2", "#FF8053"],
            ("Colorblind", true) => ["#0173B3", "#A8733B", "#908000", "#8C356B", "#0A7A59", "#973B00"],
            (_, true) => ["#36751B", "#1469B5", "#B8461B", "#9B4DCA", "#007B83", "#876300"],
            _ => ["#73FF35", "#4285FF", "#FF773D", "#9B4DCA", "#00F0C8", "#FFD044"]
        };
    }
}
