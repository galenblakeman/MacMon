using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace MacMon;

public sealed record HardwareEntry(string Label, string Value);
public sealed record HardwareSection(string Title, IReadOnlyList<HardwareEntry> Entries);
public sealed partial class HardwareInfoWindow : Window
{
    public HardwareInfoWindow() : this(() => []) { }
    public HardwareInfoWindow(Func<IReadOnlyList<HardwareSection>> snapshot)
    {
        InitializeComponent();
        ProductVersion.Text = $"MacMon {typeof(HardwareInfoWindow).Assembly.GetName().Version?.ToString(3)}";
        IReadOnlyList<HardwareSection> sections = [];
        void Refresh() { sections = snapshot(); InfoSections.ItemsSource = sections; }
        RefreshInfo.Click += (_, _) => Refresh();
        CancelAction.Click += (_, _) => Close();
        CopyDetails.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard) await clipboard.SetTextAsync(ProductVersion.Text + "\n\n" +
                string.Join("\n\n", sections.Select(s => s.Title + "\n" + string.Join("\n", s.Entries.Select(e => e.Label + ": " + e.Value)))));
        };
        Refresh();
    }
}
