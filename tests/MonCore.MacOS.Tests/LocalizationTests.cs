using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class LocalizationTests
{
    [Test]
    public void EmbeddedCatalogsHaveMatchingKeysAndPreserveEveryFormatSpecifier()
    {
        Dictionary<string, string> Catalog(string language)
        {
            using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"MacMon.Localization.{language}.json")!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }
        var english = Catalog("en"); var spanish = Catalog("es");
        Assert.That(spanish.Keys, Is.EquivalentTo(english.Keys));
        Assert.That(english.Count, Is.GreaterThan(590));
        foreach (var pair in spanish)
        {
            Assert.That(pair.Value, Is.Not.Empty);
            Assert.That(System.Text.CompositeFormat.Parse(pair.Value).MinimumArgumentCount,
                Is.EqualTo(System.Text.CompositeFormat.Parse(pair.Key).MinimumArgumentCount), pair.Key);
        }
    }
    [Test]
    public void SpanishDetailsUsageJobsAndCaptureReportsLocalizeWithoutChangingIdentitiesOrCsv()
    {
        var sample = TablePresentationTests.Sample(); var process = sample.Processes[0];
        var context = new LaunchdContext(Guid.NewGuid(), 501, "Aqua");
        var job = new LaunchdJob(new(context, "example.job"), process.ProcessId, 0, process.Identity);
        var snapshot = new LaunchdSnapshot(sample.Timestamp, context, [job]);
        var folder = Path.Combine(Path.GetTempPath(), "macmon-localization-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            UiText.Apply("en");
            var english = Path.Combine(folder, "en.csv"); var spanish = Path.Combine(folder, "es.csv");
            CsvExport.Write(english, CsvExportKind.ProcessSamples, [sample], [], sample.Timestamp, 0, 0);
            UiText.Apply("es");
            var text = ProcessDetails.Text(new(process, PinnedProcessState.Present), sample, false, true);
            Assert.That(text, Does.Contain("Observado:").And.Contain("Vista:").And.Contain("Hilos:"));
            Assert.That(text, Does.Not.Contain("View:").And.Not.Contain("Threads:"));
            Assert.That(text, Does.Contain(process.ImagePath).And.Contain("UnixMicroseconds"));
            Assert.That(new ProcessRow(process, State: PinnedProcessState.NotRecorded).Name, Does.Contain("No registrado"));
            var details = LaunchdPresentation.Details(job, sample.Timestamp, sample, true, false);
            Assert.That(details, Does.Contain("Última terminación:").And.Contain("example.job").And.Contain("UID 501"));
            Assert.That(LaunchdPresentation.Rows(snapshot, sample, true, "", 0, 0).Single().State, Is.EqualTo(UiText.Get("Running")));
            Assert.That(LaunchdWorkers.Build(job, snapshot, sample, true).Text(), Does.Contain("Árbol de procesos observado"));
            var ledger = new UsageLedger(sample.Timestamp, MacFrameSource.IdentityPolicy); ledger.Add(sample);
            var usage = UsagePresentation.Report(UsagePresentation.Rows(ledger, "", UsageSort.Cpu), ledger.Since);
            Assert.That(usage, Does.Contain("Uso de la sesión").And.Contain("Tiempo de CPU").And.Contain(process.ImagePath));
            var fired = new CaptureFired(new(CaptureMetric.Cpu, true, 90, 1), sample, sample, 95, [95]);
            var report = MacCaptureReports.AutomaticText(fired);
            Assert.That(report, Does.StartWith(MacCaptureReports.OwnershipMarker + "\n"));
            Assert.That(report, Does.Contain("Regla:").And.Contain("Valor máximo:").And.Contain("Frecuencia estimada de CPU:"));
            var record = MacCaptureReports.Create(fired, folder); MacCaptureReports.Write(record, report);
            Assert.That(MacCaptureReports.LoadRecords(folder).Single().Id, Is.EqualTo(record.Id));
            Assert.That(File.ReadAllText(record.ReportPath), Does.Contain("MacMon metadata: "));
            CsvExport.Write(spanish, CsvExportKind.ProcessSamples, [sample], [], sample.Timestamp, 0, 0);
            Assert.That(File.ReadAllBytes(spanish), Is.EqualTo(File.ReadAllBytes(english)), "CSV headers, timestamps and numeric values remain invariant");
        }
        finally { UiText.Apply("en"); Directory.Delete(folder, true); }
    }
    [AvaloniaTest]
    public void ExistingRankingPlaceholdersAndGaugeAccessibilityFollowLanguageChanges()
    {
        var box = new ComboBox(); var menu = new RankingMenu(box, [(0, "CPU")], 0, headerChoices: [(1, "Name")]);
        menu.Sort = 1;
        var gauge = new ResourceGauge { Title = "Memory" }; var root = new StackPanel { Children = { box, gauge } };
        var window = new Window { Content = root };
        var chart = new CpuChart();
        try
        {
            window.Show();
            UiText.Apply("es"); Dispatcher.UIThread.RunJobs();
            Assert.That(box.PlaceholderText, Is.EqualTo("Nombre"));
            Assert.That(Avalonia.Automation.AutomationProperties.GetHelpText(chart), Does.StartWith("Izquierda y Derecha"));
            gauge.Update(new(30, 70, 100, "30.0%", "0–100%"), "");
            Assert.That(Avalonia.Automation.AutomationProperties.GetName(gauge), Does.Contain("Memoria").And.Contain("Escala"));
            var panel = new ResourceDetailsView();
            panel.Update(ResourceDetails.Create(ResourceMetric.Memory, sample: null, memory: null, GaugeReading.Empty, 10));
            Assert.That(panel.FindControl<TextBlock>("DetailStatus")!.Text, Is.EqualTo(UiText.Get("Waiting for live readings…")));
            UiText.Apply("en"); Dispatcher.UIThread.RunJobs();
            Assert.That(box.PlaceholderText, Is.EqualTo("Name"));
            Assert.That(Avalonia.Automation.AutomationProperties.GetHelpText(chart), Does.StartWith("Left and Right"));
        }
        finally { window.Close(); UiText.Apply("en"); }
    }
    [AvaloniaTest]
    public void SpanishDetailAndConfirmationActionsFitAtMinimumSize()
    {
        UiText.Apply("es"); var sample = TablePresentationTests.Sample();
        try
        {
            foreach (var confirmation in new[] { false, true })
            {
                var dialog = new ProcessDialog("Process properties", ProcessDetails.Text(new(sample.Processes[0], PinnedProcessState.Present), sample, false, false), confirmation)
                    { Width = 480, Height = 320, Position = new PixelPoint(0, 0) };
                try
                {
                    dialog.Show(); Dispatcher.UIThread.RunJobs();
                    foreach (var name in new[] { "CopyDetails", "CancelAction", "ConfirmAction" })
                    {
                        var button = dialog.FindControl<Button>(name)!; if (!button.IsVisible) continue;
                        var point = button.TranslatePoint(default, dialog)!.Value;
                        Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                        Assert.That(point.X + button.Bounds.Width, Is.LessThanOrEqualTo(dialog.ClientSize.Width));
                        Assert.That(point.Y + button.Bounds.Height, Is.LessThanOrEqualTo(dialog.ClientSize.Height));
                    }
                    using var frame = dialog.CaptureRenderedFrame(); Assert.That(frame, Is.Not.Null);
                    if (Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT") is { } output)
                    { Directory.CreateDirectory(output); frame!.Save(Path.Combine(output, $"details-es-{confirmation}.png"), PngBitmapEncoderOptions.Default); }
                }
                finally { dialog.Close(); }
            }
        }
        finally { UiText.Apply("en"); }
    }
}
