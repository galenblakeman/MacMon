using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace MacMon;

/// <summary>Resolution-independent title actions, with readable labels and consistent icon sizing.</summary>
public sealed class TitleButtonContentExtension(string icon, string label) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var path = IconPath(icon);
        var caption = UiText.Label(label); caption.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Stroke = Brushes.Gray,
                    StrokeThickness = 1.5, Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
                    [!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new Avalonia.Data.Binding("Foreground") { Source = caption } },
                caption
            }
        };
    }

    internal static string IconPath(string icon) => icon switch
        {
            "Info" => "M8,1 A7,7 0 1 1 7.99,1 M8,7 L8,12 M8,4 L8,5",
            "Settings" => "M2,4 L14,4 M2,8 L14,8 M2,12 L14,12 M5,2 L5,6 M11,6 L11,10 M6,10 L6,14",
            "Appearance" => "M8,1 A7,7 0 1 0 15,8 L10,8 L10,5 L8,5 Z M5,4 L5,5 M3,8 L4,8 M6,12 L7,12",
            "Monitoring" => "M1,9 L4,9 L6,3 L9,13 L11,7 L15,7",
            "Capture" => "M1,4 L5,4 L6,2 L10,2 L11,4 L15,4 L15,14 L1,14 Z M11,9 A3,3 0 1 1 5,9 A3,3 0 1 1 11,9",
            "Export" => "M8,11 L8,1 M4,5 L8,1 L12,5 M2,10 L2,14 L14,14 L14,10",
            "Pin" => "M5,1 L11,1 L10,6 L13,9 L9,9 L8,15 L7,9 L3,9 L6,6 Z",
            "Full" => "M1,6 L1,1 L6,1 M10,1 L15,1 L15,6 M1,10 L1,15 L6,15 M10,15 L15,15 L15,10",
            "Save" => "M2,1 L12,1 L15,4 L15,15 L1,15 L1,1 Z M5,1 L5,6 L11,6 L11,1 M4,15 L4,10 L12,10 L12,15",
            "Close" => "M3,3 L13,13 M13,3 L3,13",
            "Copy" => "M5,5 L14,5 L14,15 L5,15 Z M11,5 L11,1 L1,1 L1,11 L5,11",
            "Folder" => "M1,4 L1,2 L6,2 L8,4 L15,4 L15,14 L1,14 Z",
            "Reset" => "M2,7 A6,6 0 1 1 3,12 M2,2 L2,7 L7,7",
            "Live" => "M5,2 L14,8 L5,14 Z",
            "Clock" => "M8,1 A7,7 0 1 1 7.99,1 M8,4 L8,8 L11,10",
            "Group" => "M5,1 A2,2 0 1 1 4.99,1 M11,3 A2,2 0 1 1 10.99,3 M1,14 L1,11 Q5,7 9,11 L9,14 M10,10 Q14,8 15,12 L15,14",
            "Process" => "M1,2 L15,2 L15,14 L1,14 Z M1,5 L15,5 M4,8 L7,10 L4,12 M9,12 L12,12",
            "Sort" => "M4,1 L4,15 M1,4 L4,1 L7,4 M12,1 L12,15 M9,12 L12,15 L15,12",
            "Stop" => "M4,1 L12,1 L15,4 L15,12 L12,15 L4,15 L1,12 L1,4 Z M5,5 L11,11 M11,5 L5,11",
            "Startup" => "M8,1 A7,7 0 1 1 7.99,1 M8,1 L8,8 M5,5 L8,8 L11,5",
            "Document" => "M3,1 L10,1 L14,5 L14,15 L3,15 Z M10,1 L10,5 L14,5 M6,8 L11,8 M6,11 L11,11",
            "Actions" => "M2,4 L14,4 M2,8 L14,8 M2,12 L14,12",
            _ => "M1,1 L15,1 L15,15 L1,15 Z M6,6 L14,6 L14,14 L6,14 Z"
        };
}
