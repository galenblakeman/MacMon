using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace MacMon;

/// <summary>An owned, modeless inspector keeps details accessible outside the compact strip's small bounds.</summary>
public sealed class ResourceDetailsWindow : Window
{
    public ResourceDetailsView DetailsView { get; } = new();
    public ResourceDetailsWindow(ResourceDetailSnapshot snapshot)
    {
        Title = "MacMon · " + snapshot.Title;
        Width = MinWidth = MaxWidth = 388;
        Height = MaxHeight = 548; MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new Border { Padding = new Thickness(24), Child = DetailsView };
        DetailsView.Update(snapshot);
        DetailsView.CloseRequested += Close;
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; Close(); } };
        Opened += (_, _) =>
        {
            if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is { } screen)
                Height = Math.Min(Height, Math.Max(MinHeight, screen.WorkingArea.Height / screen.Scaling - 80));
        };
    }
}
