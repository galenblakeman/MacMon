using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Controls.Documents;

namespace MacMon;

/// <summary>WinMon-sized readout with a chart toggle and live, on-demand resource tooltip.</summary>
public sealed class ResourceGauge : UserControl
{
    public static readonly string[] GaugeStyles = ["Bowl", "Needle", "SegmentedArc", "LeftBar", "BottomBar", "SegmentedBar", "None"];
    public static string StyleLabel(string style) => style switch
    { "SegmentedArc" => "Segmented arc", "LeftBar" => "Left bar", "BottomBar" => "Bottom bar", "SegmentedBar" => "Segmented bar", _ => style };
    public static string NormalizeStyle(string? style) => style == "Bar" ? "BottomBar" : GaugeStyles.Contains(style) ? style! : "Bowl";
    public static readonly StyledProperty<string> GaugeStyleProperty = AvaloniaProperty.Register<ResourceGauge, string>(nameof(GaugeStyle), "Bowl");
    public string GaugeStyle { get => GetValue(GaugeStyleProperty); set => SetValue(GaugeStyleProperty, value); }
    public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<ResourceGauge, IBrush>(nameof(Fill), Brushes.Teal);
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public static readonly StyledProperty<IBrush?> TextAccentProperty = AvaloniaProperty.Register<ResourceGauge, IBrush?>(nameof(TextAccent));
    public IBrush? TextAccent { get => GetValue(TextAccentProperty); set => SetValue(TextAccentProperty, value); }
    public static readonly StyledProperty<IBrush> TrackProperty = AvaloniaProperty.Register<ResourceGauge, IBrush>(nameof(Track), Brushes.Gray);
    public IBrush Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public static readonly StyledProperty<IBrush> MarkerProperty = AvaloniaProperty.Register<ResourceGauge, IBrush>(nameof(Marker), Brushes.White);
    public IBrush Marker { get => GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }
    public static readonly StyledProperty<double> ReadoutSizeProperty = AvaloniaProperty.Register<ResourceGauge, double>(nameof(ReadoutSize), 27);
    public double ReadoutSize { get => GetValue(ReadoutSizeProperty); set => SetValue(ReadoutSizeProperty, value); }
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<ResourceGauge, bool>(nameof(IsCompact));
    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ResourceGauge, string>(nameof(Title), "");
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public CheckBox SeriesToggle { get; } = new() { IsChecked = true, FontSize = 14, MinHeight = 20, Padding = new Thickness(0) };
    private readonly TextBlock _name = new() { FontSize = 14 };
    private readonly TextBlock _value = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _scale = new() { Classes = { "label" }, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _detail = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ResourceGaugeMarks _marks = new() { IsHitTestVisible = false };
    private readonly ResourceDetailsView _tip = new();
    private Func<ResourceDetailSnapshot>? _detailsProvider;
    private GaugeReading _reading = GaugeReading.Empty;
    private string _description = "";
    public event Action? DetailsOpening;
    public ResourceGauge()
    {
        Focusable = true;
        Height = 98;
        _tip.FindControl<Button>("CloseDetails")!.IsVisible = false;
        ToolTip.SetShowDelay(this, 350);
        ToolTip.SetPlacement(this, PlacementMode.Bottom);
        SizeChanged += (_, _) => Refresh();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == ToolTip.IsOpenProperty && ToolTip.GetIsOpen(this)) RefreshTooltip();
        };
        Refresh();
    }
    public void EnableDetails(Func<ResourceDetailSnapshot> provider) { _detailsProvider = provider; ToolTip.SetTip(this, _tip); }
    public void HideDetails() => ToolTip.SetIsOpen(this, false);
    private void RefreshTooltip()
    {
        if (_detailsProvider is not null) _tip.Update(_detailsProvider());
    }
    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (_detailsProvider is null) return;
        DetailsOpening?.Invoke();
        RefreshTooltip(); ToolTip.SetIsOpen(this, true);
    }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); HideDetails(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { HideDetails(); e.Handled = true; }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { HideDetails(); base.OnDetachedFromVisualTree(e); }
    public void Update(GaugeReading reading, string description) { _reading = reading; _description = description; Refresh(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_marks is null) return;
        if (change.Property == GaugeStyleProperty || change.Property == FillProperty || change.Property == TrackProperty ||
            change.Property == TextAccentProperty || change.Property == MarkerProperty || change.Property == IsCompactProperty || change.Property == ReadoutSizeProperty || change.Property == TitleProperty) Refresh();
    }
    private void Refresh()
    {
        Height = 98;
        _name.Text = Title; SeriesToggle.Content = Title; SeriesToggle.Foreground = _name.Foreground = _value.Foreground = TextAccent ?? Fill;
        AutomationProperties.SetName(SeriesToggle, $"Show {Title} on chart");
        var number = _reading.Text; var unit = "";
        var space = number.LastIndexOf(' ');
        if (space >= 0) { unit = number[(space + 1)..]; number = number[..space]; }
        else if (number.EndsWith('%')) { unit = "%"; number = number[..^1]; }
        var arc = NormalizeStyle(GaugeStyle) is "Bowl" or "Needle" or "SegmentedArc";
        _value.FontSize = IsCompact ? 20 : Math.Min(ReadoutSize, arc || Bounds.Width < 145 ? 22 : 27);
        _detail.FontSize = IsCompact ? 11 : arc ? 13 : 14;
        _value.Inlines!.Clear(); _value.Inlines.Add(new Run(number)); _value.Inlines.Add(new Run(unit.Length > 0 ? " " + unit : "") { FontSize = 13 });
        _scale.Text = _reading.Value is not null && _reading.Maximum != 100 ? _reading.Scale.Replace("0–", "") : "";
        _detail.Text = (Title is "Memory" or "CPU" ? _description.Split('\n').FirstOrDefault() : _description.Split('\n').LastOrDefault()) ?? "";
        if (Title == "CPU") _detail.Text = _detail.Text.Split(" · ").Last();
        var peak = _reading.Peak is { } p ? (_reading.Scale == "0–100%" ? $"{p:F1}%" : ResourceGaugeHistory.Rate(p)) : "—";
        var accessible = $"{Title}: {_reading.Text}. {_description}. Scale {_reading.Scale}. Peak over the last two minutes: {peak}.";
        AutomationProperties.SetName(this, accessible);
        if (_detailsProvider is null) ToolTip.SetTip(this, accessible);
        else if (ToolTip.GetIsOpen(this)) RefreshTooltip();
        _marks.Kind = NormalizeStyle(GaugeStyle); _marks.Fill = Fill; _marks.Track = Track; _marks.Marker = Marker; _marks.Reading = _reading;
        _marks.InvalidateVisual();
        LayoutReadout();
    }
    private string _layout = "";
    private void LayoutReadout()
    {
        _scale.MaxWidth = Math.Max(40, Bounds.Width * .48);
        _scale.TextTrimming = TextTrimming.CharacterEllipsis;
        var style = NormalizeStyle(GaugeStyle);
        var key = style + IsCompact;
        if (_layout == key) return;
        _layout = key;
        static void Clear(Panel panel)
        {
            foreach (var child in panel.Children.OfType<Panel>().ToArray()) Clear(child);
            panel.Children.Clear();
        }
        if (Content is Panel previous) Clear(previous);
        Content = null;
        var heading = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 4 };
        heading.Children.Add(IsCompact ? _name : SeriesToggle);
        heading.Children.Add(_scale); Grid.SetColumn(_scale, 1);
        _marks.Width = double.NaN; _marks.Height = double.NaN; _marks.Margin = default;
        _value.Margin = default; _value.HorizontalAlignment = HorizontalAlignment.Left;
        _value.VerticalAlignment = VerticalAlignment.Center;
        var root = new Grid();
        if (style is "Bowl" or "SegmentedArc" or "Needle")
        {
            root.RowDefinitions = new("20,*,Auto"); root.RowSpacing = 3;
            root.Children.Add(heading);
            var bowl = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            bowl.Children.Add(_marks); Grid.SetRow(bowl, 1); root.Children.Add(bowl);
            _value.HorizontalAlignment = HorizontalAlignment.Center; _value.VerticalAlignment = VerticalAlignment.Bottom;
            if (style == "Needle") { root.Children.Add(_value); Grid.SetRow(_value, 2); }
            else { bowl.Children.Add(_value); root.Children.Add(_detail); Grid.SetRow(_detail, 2); }
            _detail.TextAlignment = TextAlignment.Center;
        }
        else
        {
            var labels = new StackPanel { Spacing = 3, Children = { heading, _value, _detail } };
            _detail.TextAlignment = TextAlignment.Left;
            if (style == "LeftBar")
            {
                root.ColumnDefinitions = new("22,*"); root.ColumnSpacing = 8;
                _marks.Height = 76; root.Children.Add(_marks); root.Children.Add(labels); Grid.SetColumn(labels, 1);
            }
            else
            {
                root.RowDefinitions = new("*,8"); root.Children.Add(labels);
                if (style != "None") { root.Children.Add(_marks); Grid.SetRow(_marks, 1); }
            }
        }
        Content = root;
    }
}
