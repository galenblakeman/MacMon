using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MacMon;

/// <summary>Decorates string button captions without replacing their content, bindings or accessible names.</summary>
public sealed class ButtonLabelTemplate : IDataTemplate
{
    public bool Match(object? data) => data is string or Control;
    public Control? Build(object? data) => data is Control control ? control : data is string text ? new ButtonLabel(text) : null;

    private sealed class ButtonLabel : Grid
    {
        private readonly Avalonia.Controls.Shapes.Path _icon;
        private readonly TextBlock _caption;
        private readonly bool _pinned;
        public ButtonLabel(string text)
        {
            _pinned = text == "★";
            ColumnDefinitions = new("Auto,*"); ColumnSpacing = 5;
            VerticalAlignment = VerticalAlignment.Center;
            _caption = new() { Text = text.TrimStart('●', '◎', 'ⓘ', '↻', '☆', ' '),
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            _icon = new() { Width = 14, Height = 14, StrokeThickness = 1.5, Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
                [!Avalonia.Controls.Shapes.Shape.StrokeProperty] = new Binding("Foreground") { Source = _caption } };
            SetColumn(_caption, 1); Children.Add(_icon); Children.Add(_caption);
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            var button = this.FindAncestorOfType<Button>();
            var kind = Kind(button?.Name);
            if (button?.Classes.Contains("tableHeader") == true) { kind = "Sort"; _icon.Width = _icon.Height = 10; ColumnSpacing = 2; }
            if (button?.Classes.Contains("rowPin") == true)
            {
                kind = "Pin"; _caption.IsVisible = false; ColumnSpacing = 0;
                if (_pinned) _icon.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, new Binding("Foreground") { Source = _caption });
            }
            _icon.Data = Geometry.Parse(TitleButtonContentExtension.IconPath(kind));
        }
    }

    internal static string Kind(string? name) => name switch
    {
        "LiveMode" => "Live",
        "CaptureMode" or "AutomaticCaptures" => "Capture",
        "IndividualProcesses" => "Process", "GroupProcesses" => "Group",
        "ShowProcessActions" => "Actions", "PinSelection" => "Pin",
        "ShowProperties" or "UsageProperties" or "JobDetails" => "Info",
        "EndProcess" or "ConfirmAction" => "Stop",
        "LoginItems" => "Startup", "SaveSettings" => "Save",
        "ChangeFolder" or "OpenFolder" or "RevealExecutable" or "RevealUsageApp" => "Folder",
        "ResetUsage" or "DefaultShortcut" or "DefaultPalette" or "DefaultFolder" => "Reset",
        "RefreshJobs" or "RefreshInfo" => "Reset", "ConfigureCaptures" => "Settings", "OpenCaptureReport" => "Document",
        "CloseDetails" or "CancelSettings" or "CancelAction" => "Close",
        not null when name.StartsWith("Copy", StringComparison.Ordinal) => "Copy",
        not null when name.StartsWith("Export", StringComparison.Ordinal) || name.EndsWith("Csv", StringComparison.Ordinal) => "Export",
        not null when name.StartsWith("Range", StringComparison.Ordinal) => "Clock",
        _ => "Actions"
    };
}
