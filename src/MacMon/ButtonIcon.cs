using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace MacMon;

/// <summary>WinMon's MIT-licensed vector catalog, adapted to Avalonia. Saved under explicit semantic names.</summary>
public sealed class ButtonIcon : AvaloniaObject
{
    public static readonly AttachedProperty<string?> KindProperty = AvaloniaProperty.RegisterAttached<ButtonIcon, Control, string?>("Kind");
    public static readonly AttachedProperty<Geometry?> DataProperty = AvaloniaProperty.RegisterAttached<ButtonIcon, Control, Geometry?>("Data");
    static ButtonIcon() => KindProperty.Changed.AddClassHandler<Control>((control, _) =>
        control.SetValue(DataProperty, GetKind(control) is { } kind ? GeometryFor(kind) : null));
    public static string? GetKind(Control control) => control.GetValue(KindProperty);
    public static void SetKind(Control control, string? kind) => control.SetValue(KindProperty, kind);
    public static Geometry? GetData(Control control) => control.GetValue(DataProperty);
    public static void SetData(Control control, Geometry? data) => control.SetValue(DataProperty, data);

    // Exact paths from WinMon b5c60e43b5cdc06b89855c0010c40f35de69d896.
    // ButtonIcon.cs plus SettingsWindow.cs navigation icons; license: dependencies/winmon-ui/LICENSE.
    private static readonly IReadOnlyDictionary<string, Geometry> Catalog = new Dictionary<string, string>
    {
        ["Compact"] = "M 2,3 L 14,3 L 14,13 L 2,13 Z M 2,7 L 14,7",
        ["ChevronUp"] = "M 3,10 L 8,5 L 13,10",
        ["ChevronDown"] = "M 3,6 L 8,11 L 13,6",
        ["Expand"] = "M 2,6 L 2,2 L 6,2 M 10,2 L 14,2 L 14,6 M 14,10 L 14,14 L 10,14 M 6,14 L 2,14 L 2,10",
        ["CaptureFrame"] = "M 5,2 L 2,2 L 2,5 M 11,2 L 14,2 L 14,5 M 14,11 L 14,14 L 11,14 M 5,14 L 2,14 L 2,11",
        ["Pin"] = "M 5,2 L 11,2 M 6,2 L 6,6 L 3,9 L 3,10 L 13,10 L 13,9 L 10,6 L 10,2 M 8,10 L 8,15",
        ["AlwaysOnTop"] = "M 1,1 L 11,1 L 11,11 L 1,11 Z M 1,4 L 11,4 M 5,11 L 5,15 L 15,15 L 15,5 L 11,5",
        ["PinFilled"] = "M 5,2 L 11,2 L 11,3 L 10,3 L 10,6 L 13,9 L 13,10 L 9,10 L 8,15 L 7,10 L 3,10 L 3,9 L 6,6 L 6,3 L 5,3 Z",
        ["Live"] = "M 13,5 A 6,6 0 1 0 14,10 M 13,1 L 13,5 L 9,5",
        ["Settings"] = "M 2,4 L 5,4 M 9,4 L 14,4 M 2,12 L 9,12 M 13,12 L 14,12 M 5,2 L 9,2 L 9,6 L 5,6 Z M 9,10 L 13,10 L 13,14 L 9,14 Z",
        ["Info"] = "M 8,1 A 7,7 0 1 1 8,15 A 7,7 0 1 1 8,1 M 8,7 L 8,12 M 8,4 L 8,5",
        ["Cancel"] = "M 3,3 L 13,13 M 13,3 L 3,13",
        ["Save"] = "M 2,2 L 12,2 L 14,4 L 14,14 L 2,14 Z M 5,2 L 5,6 L 11,6 L 11,2 M 5,14 L 5,9 L 11,9 L 11,14",
        ["Download"] = "M 8,1 L 8,10 M 4,6 L 8,10 L 12,6 M 2,11 L 2,14 L 14,14 L 14,11",
        ["Copy"] = "M 5,5 L 14,5 L 14,14 L 5,14 Z M 2,11 L 2,2 L 11,2",
        ["Folder"] = "M 1,13 L 1,3 L 6,3 L 8,5 L 15,5 L 15,13 Z M 1,7 L 15,7",
        ["Start"] = "M 4,2 L 13,8 L 4,14 Z",
        ["Stop"] = "M 3,3 L 13,3 L 13,13 L 3,13 Z",
        ["Process"] = "M 1,2 L 15,2 L 15,11 L 1,11 Z M 8,11 L 8,14 M 4,14 L 12,14 M 4,8 L 4,6 M 8,8 L 8,4 M 12,8 L 12,5",
        ["More"] = "M 3,7 A 1,1 0 1 1 3,9 A 1,1 0 1 1 3,7 M 8,7 A 1,1 0 1 1 8,9 A 1,1 0 1 1 8,7 M 13,7 A 1,1 0 1 1 13,9 A 1,1 0 1 1 13,7",
        ["Appearance"] = "M 2,2 L 14,2 L 14,12 L 2,12 Z M 8,12 L 8,15 M 5,15 L 11,15 M 4,8 L 7,5 L 9,8 L 12,5",
        ["Monitoring"] = "M 1,9 L 4,9 L 6,3 L 9,13 L 11,7 L 15,7",
        ["AutomaticCapture"] = "M 2,3 L 14,3 L 8,13 Z",
    }.ToDictionary(pair => pair.Key, pair => Geometry.Parse(pair.Value));
    public static Geometry GeometryFor(string kind) => Catalog.TryGetValue(kind == "Capture" ? "CaptureFrame" : kind, out var geometry)
        ? geometry : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown button icon");

    public static Control Create(string kind, Control foreground, double stroke = 1.5)
    {
        var icon = new Grid { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = GeometryFor(kind), Width = 16, Height = 16, Stretch = Stretch.None,
            StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            [!Shape.StrokeProperty] = new Binding("Foreground") { Source = foreground }
        };
        if (kind == "PinFilled") path.Bind(Shape.FillProperty, new Binding("Foreground") { Source = foreground });
        icon.Children.Add(path);
        if (kind == "Capture") icon.Children.Add(new Ellipse
        {
            Width = 3, Height = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            [!Shape.FillProperty] = new Binding("Foreground") { Source = foreground }
        });
        return icon;
    }
}
