using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using MicroGauge;
using MicroGauge.Avalonia;
using MicroGauge.Constant;

namespace MacMon;

/// <summary>MicroGauge renders the marks; Avalonia provides readable, accessible values and units.</summary>
public sealed class ResourceGauge : UserControl
{
    public static readonly string[] GaugeStyles = ["Bowl", "Needle", "Bar", "None"];
    public static string NormalizeStyle(string? style) => GaugeStyles.Contains(style) ? style! : "Bowl";
    public static readonly StyledProperty<string> GaugeStyleProperty = AvaloniaProperty.Register<ResourceGauge, string>(nameof(GaugeStyle), "Bowl");
    public string GaugeStyle { get => GetValue(GaugeStyleProperty); set => SetValue(GaugeStyleProperty, value); }
    public static readonly StyledProperty<IBrush> FillProperty = AvaloniaProperty.Register<ResourceGauge, IBrush>(nameof(Fill), Brushes.Teal);
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public static readonly StyledProperty<IBrush> TrackProperty = AvaloniaProperty.Register<ResourceGauge, IBrush>(nameof(Track), Brushes.Gray);
    public IBrush Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public static readonly StyledProperty<IBrush> MarkerProperty = AvaloniaProperty.Register<ResourceGauge, IBrush>(nameof(Marker), Brushes.White);
    public IBrush Marker { get => GetValue(MarkerProperty); set => SetValue(MarkerProperty, value); }
    public static readonly StyledProperty<double> ReadoutSizeProperty = AvaloniaProperty.Register<ResourceGauge, double>(nameof(ReadoutSize), 25);
    public double ReadoutSize { get => GetValue(ReadoutSizeProperty); set => SetValue(ReadoutSizeProperty, value); }
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ResourceGauge, string>(nameof(Title), "");
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _value = new() { FontSize = 25, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
    private readonly TextBlock _scale = new() { Classes = { "label" }, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
    private readonly TextBlock _details = new() { Classes = { "label" }, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 32 };
    private readonly Grid _marks = new() { Height = 62 };
    private AvaloniaGaugeBase? _gauge;
    private GaugeReading _reading = GaugeReading.Empty;
    private string _description = "";
    public ResourceGauge()
    {
        Height = 174;
        var panel = new StackPanel { Spacing = 4, Children = { _title, _marks, _value, _scale, _details } };
        Content = panel;
        Refresh();
    }
    public void Update(GaugeReading reading, string description)
    {
        _reading = reading; _description = description; Refresh();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_marks is null) return;
        if (change.Property == GaugeStyleProperty) { _marks.Children.Clear(); _gauge = null; }
        if (change.Property == GaugeStyleProperty || change.Property == FillProperty || change.Property == TrackProperty ||
            change.Property == ReadoutSizeProperty || change.Property == MarkerProperty || change.Property == TitleProperty) Refresh();
    }
    private void Refresh()
    {
        _value.FontSize = ReadoutSize;
        _title.Text = Title; _value.Text = _reading.Text; _value.Foreground = Fill;
        _scale.Text = _reading.Value is null ? "Unavailable" : $"{_reading.Scale} · peak 2m";
        _details.Text = _description;
        var peak = _reading.Peak is { } p ? (_reading.Maximum == 100 && _reading.Scale == "0–100%" ? $"{p:F1}%" : ResourceGaugeHistory.Rate(p)) : "—";
        var accessible = $"{Title}: {_reading.Text}. {_description}. Scale {_reading.Scale}. Peak over the last two minutes: {peak}.";
        AutomationProperties.SetName(this, accessible); ToolTip.SetTip(this, accessible);
        var style = NormalizeStyle(GaugeStyle);
        _marks.IsVisible = true; // Reserve the same space for every style and unavailable reading.
        if (style == "None" || _reading.Value is null)
        {
            if (_gauge is not null) _gauge.IsVisible = false;
            return;
        }
        if (_gauge is null)
        {
            _gauge = style == "Bar" ? new AvaloniaGaugeLinear { Height = 12, ValueWidthExtent = 1, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                : new AvaloniaGaugeRadial { RadialStyle = GaugeRadialStyle.Half, ScaleStartAngle = 180, ScaleEndAngle = 0,
                    NeedlePivotBrush = Brushes.Transparent, NeedlePivotOutlineBrush = Brushes.Transparent };
            _gauge.BackingBrush = _gauge.BackingOutlineBrush = Brushes.Transparent;
            _gauge.BackingStrokeWidth = 0;
            _gauge.TickBrush = _gauge.MinorTickBrush = _gauge.LabelFontBrush = _gauge.ValueFontBrush = Brushes.Transparent;
            _gauge.TickInterval = _gauge.MinorTickInterval = _gauge.LabelInterval = 0;
            _gauge.NeedleAnimationDuration = 0;
            _gauge.NeedleStartWidth = 3; _gauge.NeedleEndWidth = 2;
            _gauge.SetNeedleStartWidth = _gauge.SetNeedleEndWidth = 2;
            _gauge.SetNeedleStartExtent = style == "Bar" ? -.5f : .72f;
            _gauge.SetNeedleEndExtent = style == "Bar" ? .5f : 1;
            _gauge.NeedleEndExtent = .88f;
            _marks.Children.Add(_gauge);
        }
        _gauge.IsVisible = true;
        var current = (float)Math.Clamp(_reading.Value.Value / _reading.Maximum * 100, 0, 100);
        _gauge.Value = current;
        _gauge.NeedleBrush = style == "Needle" ? Fill : Brushes.Transparent;
        _gauge.SetNeedleBrush = Marker;
        _gauge.SetNeedleValue = (float)Math.Clamp((_reading.Peak ?? _reading.Value.Value) / _reading.Maximum * 100, 0, 100);
        if (_gauge is AvaloniaGaugeLinear bar) { bar.BackingBrush = Track; bar.ValueBarBrush = Fill; }
        if (_gauge is AvaloniaGaugeRadial radial)
        {
            GaugeRadialRange Range(float end, IBrush brush) => new() { StartValue = 0, EndValue = end,
                InnerStartExtent = .78f, InnerEndExtent = .78f, OuterStartExtent = .94f, OuterEndExtent = .94f,
                Brush = AvaloniaGaugeHelper.GetGaugeBrush(brush) };
            radial.Ranges = style == "Bowl" ? [Range(100, Track), Range(current, Fill)] : [Range(100, Track)];
        }
    }
}
