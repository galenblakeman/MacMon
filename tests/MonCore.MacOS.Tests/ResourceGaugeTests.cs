using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MicroGauge.Avalonia;
using NUnit.Framework;
using SkiaSharp;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class ResourceGaugeTests
{
    private static MonitorSample Sample(int seconds, double? cpu = 25, string network = "en0", double? rate = 1024) =>
        new(DateTimeOffset.UtcNow.AddDays(-seconds), cpu, [], 0, false, (seconds + 1L) * Stopwatch.Frequency)
        { Resources = new() { TotalMemoryBytes = 100, UsedMemoryBytes = 62, NetworkId = network,
            ReceiveBytesPerSecond = rate, SendBytesPerSecond = 0, SecondaryDiskName = "All drivers",
            SecondaryDiskReadBytesPerSecond = rate, SecondaryDiskWriteBytesPerSecond = 0 } };

    [Test]
    public void PeaksUseMonotonicLiveWindowAndExpireWithoutDependingOnWallClock()
    {
        var history = new ResourceGaugeHistory();
        history.Observe(Sample(0, 90));
        for (var i = 1; i <= 120; i++) history.Observe(Sample(i));
        Assert.That(history.Readings[0].Peak, Is.EqualTo(90));
        history.Observe(Sample(121));
        Assert.That(history.Readings[0].Peak, Is.EqualTo(25));
        history.Observe(Sample(122, 80));
        history.Observe(Sample(130)); // Wake/collector gap starts a fresh window.
        Assert.That(history.Readings[0].Peak, Is.EqualTo(25));
        history.Observe(Sample(2)); // Clock reset cannot retain previous peaks.
        Assert.That(history.Readings[0].Peak, Is.EqualTo(25));
    }

    [Test]
    public void SourceChangesBaselinesAndInvalidNumbersDoNotPretendToBeZero()
    {
        var history = new ResourceGaugeHistory();
        history.Observe(Sample(0, 90, rate: 8192));
        history.Observe(Sample(1, 0, "en1", 0));
        Assert.That(history.Readings[2].Peak, Is.Zero);
        Assert.That(history.Readings[2].Maximum, Is.EqualTo(1024));
        Assert.That(history.Readings[3].Peak, Is.EqualTo(8192));
        history.Observe(Sample(2, double.NaN, "en1", null));
        Assert.That(history.Readings[0].Value, Is.Null);
        Assert.That(history.Readings[2].Value, Is.Null);
        Assert.That(history.Readings[3].Value, Is.Null);
        Assert.That(history.Readings[1].Value, Is.EqualTo(62));
        history.Observe(Sample(3, 125, "en1", 2049));
        Assert.That(history.Readings[0].Value, Is.EqualTo(100));
        Assert.That(history.Readings[2].Peak, Is.EqualTo(2049));
        Assert.That(history.Readings[3].Peak, Is.EqualTo(2049));
        Assert.That(history.Readings[3].Maximum, Is.EqualTo(4096));
        history.Observe(Sample(4, -2, "en1", double.PositiveInfinity));
        Assert.That(history.Readings[0].Value, Is.Null);
        Assert.That(history.Readings[2].Value, Is.Null);
        Assert.That(ResourceGaugeHistory.RateMaximum(double.MaxValue), Is.EqualTo(double.MaxValue));
    }

    [Test]
    public void GaugePreferenceNormalizesAndMergesWithoutLosingOtherState()
    {
        var preferences = new MacPreferences { GaugeStyle = "Needle", Width = 1234, DataTab = 2 };
        Assert.That(SettingsValues.From(preferences).ApplyTo(preferences, null).GaugeStyle, Is.EqualTo("Needle"));
        Assert.That((SettingsValues.From(preferences) with { GaugeStyle = "Bar" }).ApplyTo(preferences, null).Width, Is.EqualTo(1234));
        Assert.That((preferences with { GaugeStyle = "unknown" }).Normalized(null).GaugeStyle, Is.EqualTo("Bowl"));
    }

    private static int ColoredPixels(Window window, Color color)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.That(frame, Is.Not.Null);
        using var stream = new MemoryStream();
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
        using var bitmap = SKBitmap.Decode(stream.ToArray());
        // Only the mark area: exclude the separately rendered numeric readout.
        var count = 0;
        for (var y = 25; y < 80; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.Red - color.R) < 12 && Math.Abs(pixel.Green - color.G) < 12 && Math.Abs(pixel.Blue - color.B) < 12) count++;
        }
        return count;
    }

    [AvaloniaTest]
    public void RealMicroGaugeMarksRenderAndUnavailableAndNoneReserveSpace()
    {
        foreach (var style in ResourceGauge.GaugeStyles)
        {
            var gauge = new ResourceGauge { Title = "CPU", GaugeStyle = style, Fill = Brushes.Red, Marker = Brushes.Lime, Track = Brushes.Gray };
            gauge.Update(new(40, 80, 100, "40.0%", "0–100%"), "Machine capacity");
            var window = new Window { Width = 220, Height = 190, Content = gauge };
            window.Show();
            Assert.That(ColoredPixels(window, Colors.Red), style == "None" ? Is.Zero : Is.GreaterThan(10), style);
            Assert.That(ColoredPixels(window, Colors.Lime), style == "None" ? Is.Zero : Is.GreaterThan(2), style + " peak");
            var height = gauge.Bounds.Height;
            gauge.Update(new(0, 80, 100, "0.0%", "0–100%"), "Measured zero");
            Assert.That(ColoredPixels(window, Colors.Lime), style == "None" ? Is.Zero : Is.GreaterThan(2), style + " zero retains peak");
            if (style is "Bowl" or "Bar") Assert.That(ColoredPixels(window, Colors.Red), Is.Zero, style + " measured zero");
            gauge.Update(GaugeReading.Empty, "Unavailable");
            Assert.That(ColoredPixels(window, Colors.Red), Is.Zero);
            Assert.That(ColoredPixels(window, Colors.Lime), Is.Zero);
            Assert.That(gauge.Bounds.Height, Is.EqualTo(height));
            window.Close();
        }
    }

    [AvaloniaTest]
    public void AttachedGaugeUpdatesStylePaletteAndCanReattach()
    {
        var app = (App)Application.Current!;
        var gauge = new ResourceGauge { Title = "CPU" };
        gauge.Bind(ResourceGauge.FillProperty, gauge.GetResourceObservable("CpuStroke"));
        gauge.Update(new(50, 75, 100, "50%", "0–100%"), "Machine capacity");
        var window = new Window { Content = gauge, Width = 220, Height = 190 };
        window.Show();
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var palette in Appearance.Palettes)
        foreach (var style in ResourceGauge.GaugeStyles)
        {
            app.ApplyAppearance(theme, palette, style);
            Dispatcher.UIThread.RunJobs();
            Assert.That(gauge.GaugeStyle, Is.EqualTo(style));
            var color = Appearance.Colors(palette, theme == "Dark").Cpu;
            Assert.That(ColoredPixels(window, color), style == "None" ? Is.Zero : Is.GreaterThan(10), $"{theme}/{palette}/{style}");
        }
        app.ApplyAppearance("Dark", "Classic", "Bowl");
        window.Content = null;
        window.Content = gauge;
        Assert.That(ColoredPixels(window, Appearance.Colors("Classic", true).Cpu), Is.GreaterThan(10));
        window.Close();
    }

    [AvaloniaTest]
    public void SettingsGaugePreviewIsLocalUntilSaved()
    {
        var app = (App)Application.Current!;
        app.ApplyAppearance("Dark", "Classic", "Bowl");
        var window = new SettingsWindow();
        window.Show();
        window.FindControl<ListBox>("Sections")!.SelectedIndex = 1;
        window.FindControl<ComboBox>("GaugeChoice")!.SelectedItem = "Bar";
        window.FindControl<ComboBox>("ThemeChoice")!.SelectedItem = "Light";
        Dispatcher.UIThread.RunJobs();
        Assert.That(window.FindControl<ResourceGauge>("PreviewCpu")!.GaugeStyle, Is.EqualTo("Bar"));
        Assert.That(app.Resources["ResourceGaugeStyle"], Is.EqualTo("Bowl"));
        var preview = window.FindControl<ResourceGauge>("PreviewCpu")!;
        var title = preview.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "CPU");
        Assert.That(((ISolidColorBrush)title.Foreground!).Color, Is.EqualTo(Color.Parse("#17222E")), "Preview title follows its local light theme");
        var scale = preview.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text?.Contains("peak 2m") == true);
        Assert.That(((ISolidColorBrush)scale.Foreground!).Color, Is.EqualTo(Color.Parse("#4D5E70")));
        window.Close();
    }
}
