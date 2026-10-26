using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class GaugeParityTests
{
    [AvaloniaTest]
    public void GaugeTooltipContentFitsItsActualPopupAtBothWindowEdges()
    {
        var gauge = new ResourceGauge { Title = "GPU", Width = 140 };
        gauge.EnableDetails(() => new("GPU", "Live · updated 12:00:00 · " + new string('x', 80), new string('x', 500),
            [new("Registry identity", new string('x', 150)), new("Shared memory in use", "2.0 GiB")]));
        var window = new Window { Width = 800, Height = 700, Content = new Grid { Children = { gauge } } };
        try
        {
            window.Show();
            foreach (var horizontal in new[] { HorizontalAlignment.Left, HorizontalAlignment.Right })
            foreach (var vertical in new[] { VerticalAlignment.Top, VerticalAlignment.Bottom })
            {
                gauge.HideDetails(); gauge.HorizontalAlignment = horizontal; gauge.VerticalAlignment = vertical;
                Dispatcher.UIThread.RunJobs(); ToolTip.SetIsOpen(gauge, true); Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var view = (ResourceDetailsView)ToolTip.GetTip(gauge)!;
                var presenter = view.GetVisualAncestors().OfType<ContentPresenter>().First();
                Assert.That(view.Bounds.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(view.Bounds.Right, Is.LessThanOrEqualTo(presenter.Bounds.Width + 1));
                var tip = view.GetVisualAncestors().OfType<ToolTip>().Single();
                var origin = view.TranslatePoint(default, tip)!.Value;
                Assert.That(origin.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(origin.X + view.Bounds.Width, Is.LessThanOrEqualTo(tip.Bounds.Width));
                var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().Single();
                Assert.That(scroll.Extent.Width, Is.LessThanOrEqualTo(scroll.Viewport.Width + 1), "Long values must wrap inside the popup");
                Assert.That(view.FindControl<TextBlock>("DetailStatus")!.Bounds.Width, Is.LessThanOrEqualTo(view.Bounds.Width));
                gauge.HideDetails();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void ArcCornerPeaksUseNarrowFallbackAndHideWithMissingReadingsOrBarStyles()
    {
        var gauge = new ResourceGauge { Title = "Memory", Width = 200 };
        var tile = new Border { Padding = new Thickness(5), BorderThickness = new Thickness(0, 0, 1, 0), Child = gauge,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = 500, Height = 180, Content = tile };
        try
        {
            window.Show();
            foreach (var language in new[] { "en", "es" })
            foreach (var compact in new[] { false, true })
            foreach (var style in ResourceGauge.GaugeStyles)
            foreach (var width in new[] { 120, 200 })
            {
                UiText.Apply(language); gauge.IsCompact = compact; gauge.GaugeStyle = style; gauge.Width = width;
                gauge.Update(new(40, 78.3, 100, "40.0%", "0–100%"), "8.0 / 16.0 GiB"); Dispatcher.UIThread.RunJobs();
                var corner = gauge.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "GaugeCorner");
                var arc = style is "Bowl" or "Needle" or "SegmentedArc";
                Assert.That(corner.Text, Is.EqualTo(!arc ? "" : width == 120 ? "78.3%" : UiText.Format("Peak {0}", "78.3%")));
                if (arc)
                {
                    Assert.That(AutomationProperties.GetName(corner), Is.EqualTo(UiText.Format("Peak {0}", "78.3%")));
                    var name = gauge.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "GaugeName");
                    Assert.That(name.TranslatePoint(new(name.Bounds.Width, 0), gauge)!.Value.X,
                        Is.LessThanOrEqualTo(corner.TranslatePoint(default, gauge)!.Value.X + 1), "Title and peak must not overlap");
                    var right = corner.TranslatePoint(new(corner.Bounds.Width, 0), tile)!.Value.X;
                    Assert.That(tile.Bounds.Width - right, Is.EqualTo(12).Within(1));
                }
            }
            gauge.Update(GaugeReading.Empty, "Unavailable"); Dispatcher.UIThread.RunJobs();
            Assert.That(gauge.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "GaugeCorner").Text, Is.Empty);
        }
        finally { window.Close(); UiText.Apply("en"); }
    }

    [TestCase(0, 125000, "1.0M")]
    [TestCase(125001, 250000, "2.0M")]
    [TestCase(250001, 625000, "5.0M")]
    [TestCase(625001, 1250000, "10.0M")]
    [TestCase(125000000, 125000000, "1.0G")]
    public void NetworkScalesUseRoundedBitRates(double peak, double maximum, string label)
    {
        Assert.That(ResourceGaugeHistory.NetworkMaximum(peak), Is.EqualTo(maximum));
        Assert.That(ResourceGaugeHistory.NetworkScaleLabel(maximum), Is.EqualTo(label));
    }
}
