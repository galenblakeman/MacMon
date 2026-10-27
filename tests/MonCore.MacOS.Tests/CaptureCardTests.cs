using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MacMon;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class CaptureCardTests
{
    [AvaloniaTest]
    public void CardKeepsCopyOpenAndBindsActionsToItsDisplayedSnapshot()
    {
        var file = Path.GetTempFileName();
        var anchor = new Button { Content = "Capture" };
        var host = new Window { Width = 800, Height = 500, Content = anchor };
        var card = new CaptureCard();
        try
        {
            var sample = TablePresentationTests.Sample();
            var record = new MacCaptureRecord(Guid.NewGuid(), SampleKey.Of(sample), CaptureMetric.Cpu, "CPU", file, "Saved");
            string? copied = null; MacCaptureRecord? opened = null; var live = 0;
            card.CopyRequested += text => copied = text; card.ReportRequested += report => opened = report; card.LiveRequested += () => live++;
            host.Show();
            foreach (var language in new[] { "en", "es" })
            {
                UiText.Apply(language);
                card.Update(sample, "exact displayed sample", record); card.ShowAt(anchor); Dispatcher.UIThread.RunJobs();
                Assert.That(card.IsOpen, Is.True);
                Assert.That(card.Bounds.Width, Is.LessThanOrEqualTo(440));
                using var frame = TopLevel.GetTopLevel(card)!.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                if (Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT") is { } output)
                {
                    Directory.CreateDirectory(output);
                    frame!.Save(Path.Combine(output, $"capture-card-{language}.png"), PngBitmapEncoderOptions.Default);
                }
                var copy = card.FindControl<Button>("CopyReading")!;
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(copied, Is.EqualTo("exact displayed sample")); Assert.That(card.IsOpen, Is.True);
                var report = card.FindControl<Button>("ViewReport")!;
                Assert.That(report.IsVisible, Is.True); report.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(opened, Is.SameAs(record));
                foreach (var name in new[] { "CopyReading", "ResumeLive", "ViewReport" })
                    Assert.That(ButtonIcon.GetKind(card.FindControl<Button>(name)!), Is.Not.Null);
                card.Update(sample, "new displayed sample", record with { State = "Saving" });
                Assert.That(report.IsVisible, Is.False);
                card.Update(null, null, record);
                Assert.That(copy.IsEnabled, Is.False); Assert.That(report.IsVisible, Is.True);
                card.FindControl<Button>("ResumeLive")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.That(card.IsOpen, Is.False);
            }
            Assert.That(live, Is.EqualTo(2));
            File.Delete(file); card.Update(sample, "sample", record);
            Assert.That(card.FindControl<Button>("ViewReport")!.IsVisible, Is.False);
        }
        finally { card.Hide(); host.Close(); File.Delete(file); UiText.Apply("en"); }
    }
    [Test]
    public void AutomaticCaptureNavigationUsesExactRecordsAndWallTimeAcrossBoots()
    {
        var sample = TablePresentationTests.Sample();
        var past = new MacCaptureRecord(Guid.NewGuid(), new(long.MaxValue, sample.Timestamp.AddMinutes(-1)), CaptureMetric.Cpu, "past", "past", "Saved");
        var next = past with { Id = Guid.NewGuid(), Peak = new(1, sample.Timestamp.AddMinutes(1)), Label = "next" };
        Assert.That(CaptureNavigation.Next([next, past], [sample], null, false), Is.SameAs(past));
        Assert.That(CaptureNavigation.Next([next, past], [sample], sample.MonotonicTimestamp, true), Is.SameAs(next));
        Assert.That(CaptureNavigation.Next([], [sample], null, false), Is.Null);
        Assert.That(CaptureNavigation.Next([past], [], null, false), Is.Null);
    }
}
