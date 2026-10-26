using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class CaptureToolbarTests
{
    [AvaloniaTest]
    public void CountsStatusAndToolbarPlacementRemainReadableInBothLanguages()
    {
        var main = new MainWindow();
        main.FindControl<Grid>("MainLayout")!.IsVisible = true;
        main.FindControl<CompactView>("CompactPanel")!.IsVisible = false;
        var shell = main.Content; main.Content = null;
        var window = new Window { Width = 800, Height = 760, Content = shell };
        try
        {
            window.Show();
            var auto = main.FindControl<Button>("AutomaticCaptures")!;
            var choice = main.FindControl<ComboBox>("ChartModeBox")!;
            var live = main.FindControl<Button>("LiveMode")!;
            foreach (var language in new[] { "en", "es" })
            foreach (var width in new[] { 800, 1000 })
            foreach (var enabled in new[] { false, true })
            foreach (var count in new[] { 0, 2, 200 })
            {
                UiText.Apply(language); window.Width = width;
                CaptureToolbar.Update(auto, enabled, count); Dispatcher.UIThread.RunJobs();
                Assert.That(auto.Content, Is.EqualTo(count == 0 ? "Auto" : $"Auto {count}"));
                var description = AutomationProperties.GetName(auto);
                Assert.That(description, Is.EqualTo(ToolTip.GetTip(auto)));
                Assert.That(description, Does.Contain(count.ToString()));
                Assert.That(description, Does.Contain(language == "en" ? enabled ? "enabled" : "disabled" : enabled ? "activadas" : "desactivadas"));
                var selectorRight = choice.TranslatePoint(new(choice.Bounds.Width, 0), window)!.Value;
                var autoLeft = auto.TranslatePoint(default, window)!.Value;
                Assert.That(choice.Width, Is.EqualTo(120));
                Assert.That(autoLeft.X - selectorRight.X, Is.EqualTo(6).Within(1));
                Assert.That(auto.TranslatePoint(new(auto.Bounds.Width, 0), window)!.Value.X,
                    Is.LessThanOrEqualTo(live.TranslatePoint(default, window)!.Value.X));
            }
        }
        finally { window.Close(); UiText.Apply("en"); }
    }
}
