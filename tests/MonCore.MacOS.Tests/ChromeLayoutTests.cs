using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class ChromeLayoutTests
{
    [AvaloniaTest]
    public void HeadersCoverTheWindowAndCollapsedBarsKeepOnlyTheIdentityRow()
    {
        var main = new MainWindow();
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        var shell = (Control)main.Content!; main.Content = null;
        var window = new Window { Width = 1000, Height = 900, Content = shell };
        try
        {
            window.Show();
            foreach (var width in new[] { 800, 1200 })
            foreach (var tab in new[] { 0, 1, 2 })
            {
                window.Width = width;
                main.FindControl<TabControl>("DataTabs")!.SelectedIndex = tab;
                var toggle = shell.GetVisualDescendants().OfType<ToggleButton>().Single(t =>
                    t.Name == new[] { "ShowProcessActions", "ShowJobActions", "ShowUsageActions" }[tab]);
                var details = shell.GetVisualDescendants().OfType<TextBlock>().Single(t =>
                    t.Name == new[] { "SelectionLabel", "JobScope", "UsageSelection" }[tab]);
                toggle.IsChecked = false;
                // Exercise a retained multi-line identity and selected icon while collapsed.
                details.Text = "PID 42\n/Applications/Example.app/Contents/MacOS/Example";
                if (tab != 1) main.FindControl<Image>(tab == 0 ? "SelectionIcon" : "UsageSelectionIcon")!.IsVisible = true;
                Dispatcher.UIThread.RunJobs();
                foreach (var header in shell.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("sectionHeader")))
                {
                    var left = header.TranslatePoint(default, window)!.Value.X;
                    Assert.That(left, Is.EqualTo(0).Within(1), $"tab {tab}, width {width}");
                    Assert.That(left + header.Bounds.Width, Is.EqualTo(width).Within(1));
                }
                var bar = shell.GetVisualDescendants().OfType<Border>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("actionBar"));
                Assert.That(details.IsEffectivelyVisible, Is.False);
                Assert.That(bar.Bounds.Height, Is.LessThanOrEqualTo(34), $"tab {tab}, width {width}");
                toggle.IsChecked = true; Dispatcher.UIThread.RunJobs();
                Assert.That(details.IsEffectivelyVisible, Is.True);
                Assert.That(bar.Bounds.Height, Is.GreaterThanOrEqualTo(54));
                var headers = shell.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("sectionHeader")).ToArray();
                foreach (var header in headers) header.Background = Brushes.Magenta;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                using var stream = new MemoryStream(); frame!.Save(stream, PngBitmapEncoderOptions.Default); stream.Position = 0;
                using var pixels = SKBitmap.Decode(stream);
                foreach (var header in headers)
                {
                    var y = (int)header.TranslatePoint(new Point(0, header.Bounds.Height / 2), window)!.Value.Y;
                    Assert.That(pixels.GetPixel(1, y), Is.EqualTo(new SKColor(255, 0, 255)), $"Left edge: tab {tab}, header y {y}");
                    Assert.That(pixels.GetPixel(width - 2, y), Is.EqualTo(new SKColor(255, 0, 255)), $"Right edge: tab {tab}, header y {y}");
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void SettingsDropdownsStayWithinHalfThePanelWhenResizedAndLocalized()
    {
        var window = new SettingsWindow(new MacPreferences(), [new("", "Automatic")], 0, true,
            _ => Task.FromResult<string?>(null));
        try
        {
            window.Show();
            foreach (var language in new[] { "en", "es" })
            foreach (var width in new[] { 800, 1100 })
            foreach (var section in new[] { 0, 1, 2 })
            {
                UiText.Apply(language); window.Width = width;
                window.FindControl<ListBox>("Sections")!.SelectedIndex = section;
                Dispatcher.UIThread.RunJobs();
                var panel = window.FindControl<Grid>("SettingsPanel")!;
                var inputs = panel.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsEffectivelyVisible).ToArray();
                Assert.That(inputs, Is.Not.Empty);
                foreach (var input in inputs)
                {
                    Assert.That(input.Bounds.Width, Is.GreaterThan(70), input.Name);
                    Assert.That(input.Bounds.Width, Is.LessThanOrEqualTo(panel.Bounds.Width / 2 + 1), input.Name);
                    var right = input.TranslatePoint(new Point(input.Bounds.Width, 0), panel)!.Value.X;
                    Assert.That(right, Is.LessThanOrEqualTo(panel.Bounds.Width + 1));
                }
            }
        }
        finally { window.Close(); UiText.Apply("en"); }
    }
}
