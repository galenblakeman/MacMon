using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;

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
    private readonly TextBlock _name = new() { Name = "GaugeName", FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _checkMark = new() { Text = "✓", FontSize = 12, FontWeight = FontWeight.Bold,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _checkBox = new() { Width = 15, Height = 15, CornerRadius = new(3), BorderThickness = new(1),
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _value = new() { Name = "GaugeValue", FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _scale = new() { Name = "GaugeCorner", Classes = { "label" }, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
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
        _value.Classes.Add("gaugeValue");
        SeriesToggle.Width = 20; SeriesToggle.Height = 20;
        _checkBox.Child = _checkMark;
        SeriesToggle.Template = new FuncControlTemplate<CheckBox>((_, _) => _checkBox);
        SeriesToggle.IsCheckedChanged += (_, _) => RefreshCheck();
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
        _name.Text = UiText.Get(Title); SeriesToggle.Foreground = _name.Foreground = _value.Foreground = TextAccent ?? Fill;
        RefreshCheck();
        AutomationProperties.SetName(SeriesToggle, UiText.Format("Show {0} on chart", UiText.Get(Title)));
        var number = _reading.Text; var unit = "";
        var space = number.LastIndexOf(' ');
        if (space >= 0) { unit = number[(space + 1)..]; number = number[..space]; }
        else if (number.EndsWith('%')) { unit = "%"; number = number[..^1]; }
        var arc = NormalizeStyle(GaugeStyle) is "Bowl" or "Needle" or "SegmentedArc";
        var fontSize = IsCompact ? 20 : Math.Min(ReadoutSize, arc || Bounds.Width < 145 ? 22 : 27);
        var unitSize = 13d;
        var available = Bounds.Width - (NormalizeStyle(GaugeStyle) == "LeftBar" ? 30 : 0) - 2;
        if (available > 0)
        {
            var face = new Typeface(_value.FontFamily, _value.FontStyle, _value.FontWeight);
            double Width(string text, double size) => new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, face, size, _value.Foreground!).Width;
            var desired = Width(number, fontSize) + Width(unit.Length > 0 ? " " + unit : "", unitSize);
            var fit = desired > 0 ? Math.Min(1, available / desired) : 1;
            fontSize *= fit; unitSize *= fit;
        }
        _value.FontSize = fontSize;
        _detail.FontSize = IsCompact ? 11 : arc ? 13 : 14;
        _value.Inlines!.Clear(); _value.Inlines.Add(new Run(number)); _value.Inlines.Add(new Run(unit.Length > 0 ? " " + unit : "") { FontSize = unitSize });
        var showCorner = _reading.Value is not null && GaugeStyle is "Bowl" or "Needle" or "SegmentedArc";
        var peakLabel = _reading.Peak is { } peakValue ? $"{peakValue:F1}%" : "—";
        var corner = _reading.Maximum == 100 ? UiText.Format("Peak {0}", peakLabel) : _reading.Scale.Replace("0–", "");
        _scale.Text = !showCorner ? "" : _reading.Maximum != 100 ? ResourceGaugeHistory.NetworkScaleLabel(_reading.Maximum)
            : Bounds.Width is > 0 and < 155 ? peakLabel : corner;
        AutomationProperties.SetName(_scale, corner);
        ToolTip.SetTip(_scale, corner);
        var outerInset = Parent is Border border ? border.Padding.Right + border.BorderThickness.Right : 0;
        _scale.Margin = new Thickness(4, 0, Math.Max(0, 12 - outerInset), 0);
        _detail.Text = (Title is "Memory" or "CPU" ? _description.Split('\n').FirstOrDefault() : _description.Split('\n').LastOrDefault()) ?? "";
        if (Title == "CPU") _detail.Text = _detail.Text.Split(" · ").Last();
        var peak = _reading.Peak is { } p ? (_reading.Scale == "0–100%" ? $"{p:F1}%" : Title == "Network" ? ResourceGaugeHistory.NetworkRate(p) : ResourceGaugeHistory.Rate(p)) : "—";
        var accessible = $"{Title}: {_reading.Text}. {_description}. Scale {_reading.Scale}. Peak over the last two minutes: {peak}.";
        AutomationProperties.SetName(this, accessible);
        if (_detailsProvider is null) ToolTip.SetTip(this, accessible);
        else if (ToolTip.GetIsOpen(this)) RefreshTooltip();
        _marks.Kind = NormalizeStyle(GaugeStyle); _marks.Fill = Fill; _marks.Track = Track; _marks.Marker = Marker; _marks.Reading = _reading;
        _marks.InvalidateVisual();
        LayoutReadout();
    }
    private string _layout = "";
    private void RefreshCheck()
    {
        _checkBox.BorderBrush = TextAccent ?? Fill;
        _checkBox.Background = SeriesToggle.IsChecked == true ? TextAccent ?? Fill : Brushes.Transparent;
        _checkMark.IsVisible = SeriesToggle.IsChecked == true;
        var color = (TextAccent ?? Fill) is ISolidColorBrush solid ? solid.Color : Colors.Teal;
        _checkMark.Foreground = .2126 * color.R + .7152 * color.G + .0722 * color.B > 145 ? Brushes.Black : Brushes.White;
    }
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
        _name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_name, 0);
        if (IsCompact) heading.Children.Add(_name);
        else
        {
            var label = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 2 };
            label.Children.Add(SeriesToggle); label.Children.Add(_name); Grid.SetColumn(_name, 1);
            heading.Children.Add(label);
        }
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
