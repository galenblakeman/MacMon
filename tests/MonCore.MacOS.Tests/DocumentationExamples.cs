using System.Diagnostics;
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

/// <summary>Opt-in documentation renderer; never opens the collector-owning main window.</summary>
public sealed class DocumentationExamples
{
    [AvaloniaTest, Explicit("Regenerate the documentation gallery with MACMON_RENDER_OUTPUT set.")]
    public void RenderGallery()
    {
        var output = Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT")
            ?? throw new InvalidOperationException("Set MACMON_RENDER_OUTPUT to a fresh output directory.");
        Directory.CreateDirectory(output);
        var history = Samples();
        var app = (App)Application.Current!;
        void Save(Window window, string name, Action? populate = null)
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            populate?.Invoke(); Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("No rendered frame for " + name);
            frame.Save(Path.Combine(output, "example_" + name + ".png"), PngBitmapEncoderOptions.Default);
        }
        foreach (var name in new[] { "individual", "grouped", "pinned", "pinned_only", "apps_only", "small_multiples",
                     "focus_network", "heatmap", "jobs", "usage", "light", "neon", "arcade", "tropical" })
        {
            var main = new MainWindow();
            UiText.Apply("en");
            app.ApplyAppearance(name == "light" ? "Light" : "Dark",
                name is "neon" or "arcade" or "tropical" ? char.ToUpperInvariant(name[0]) + name[1..] : "Classic", "Bowl");
            T Get<T>(string id) where T : Control => main.FindControl<T>(id)!;
            Get<Grid>("MainLayout").IsVisible = true;
            Get<Grid>("MainLayout").RowDefinitions[1].Height = new GridLength(1.15, GridUnitType.Star);
            Get<Grid>("MainLayout").RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            Get<CompactView>("CompactPanel").IsVisible = false;
            Get<Border>("SecondDiskCard").IsVisible = false;
            Get<Avalonia.Controls.Primitives.UniformGrid>("ResourceCards").Columns = 5;
            Get<TabControl>("DataTabs").SelectedIndex = name == "jobs" ? 1 : name == "usage" ? 2 : 0;
            Get<LaunchdView>("JobsView").SetActive(false);
            Get<RadioButton>("Range2").IsChecked = true;
            Get<ComboBox>("ChartModeBox").SelectedIndex = name == "small_multiples" ? 1 : name == "heatmap" ? 2 : name == "focus_network" ? 6 : 0;
            var chart = Get<CpuChart>("Chart"); chart.VisibleMetrics = 31; chart.RangeSeconds = 120; chart.Update(history);
            var peaks = new ResourceGaugeHistory(); foreach (var sample in history) peaks.Observe(sample);
            string[] details = ["3.2 GHz", "9.4 / 16.0 GiB", "Wi-Fi · ↓ download / ↑ upload", "Internal SSD · read + write", "Example GPU"];
            string[] gauges = ["CpuGauge", "MemoryGauge", "NetworkGauge", "StorageGauge", "GpuGauge"];
            for (var i = 0; i < gauges.Length; i++) Get<ResourceGauge>(gauges[i]).Update(peaks.Readings[i], details[i]);
            Get<CheckBox>("ProcessAppsOnly").IsChecked = name == "apps_only";
            Get<CheckBox>("PinnedOnly").IsChecked = name == "pinned_only";
            Get<TextBox>("ProcessSearchBox").Text = "";
            Get<RadioButton>("GroupProcesses").IsChecked = name is "grouped" or "apps_only";
            Get<RadioButton>("IndividualProcesses").IsChecked = name is not ("grouped" or "apps_only");
            Get<ComboBox>("ProcessLimitBox").SelectedIndex = 3; // Top 20.
            Get<ComboBox>("ProcessSortBox").SelectedIndex = 0;
            Get<ComboBox>("UsageLimitBox").SelectedIndex = 0; // All.
            Get<ComboBox>("UsageSortBox").SelectedIndex = 0;
            Get<CheckBox>("UsageAppsOnly").IsChecked = false;
            Get<TextBox>("UsageSearch").Text = "";
            var pins = new ProcessPins(MacFrameSource.IdentityPolicy);
            if (name is "pinned" or "pinned_only")
            { pins.Toggle(history[^1].Processes[2], false); pins.Toggle(history[^1].Processes[0], false); }
            Get<ListBox>("ProcessList").ItemsSource = ProcessPresentation.Rows(history, history[^1],
                name is "grouped" or "apps_only", ProcessSort.Cpu, "", pins, name == "pinned_only", limit: 20, appsOnly: name == "apps_only");
            Get<TextBlock>("EmptyProcessLabel").IsVisible = false;
            Get<TextBlock>("ProcessCountLabel").Text = "Processes 12 · observed threads 186";
            Get<TextBlock>("ProcessSelectionName").Text = "Select a process";
            Get<TextBlock>("SelectionLabel").Text = "CPU averaged over 5 seconds · resident memory · disk I/O";
            if (name == "jobs")
            {
                var view = Get<LaunchdView>("JobsView"); view.RowLimit = 20;
                var context = new LaunchdContext(Guid.Empty, 501, "Aqua");
                var jobs = history[^1].Processes.Take(7).Select(p => new LaunchdJob(new(context, "com.example." + p.Name.ToLowerInvariant().Replace(' ', '-')), p.ProcessId, 0, p.Identity)).ToArray();
                view.FindControl<ListBox>("JobList")!.ItemsSource = LaunchdPresentation.Rows(new(history[^1].Timestamp, context, jobs), history[^1], true, "", 0, 3, history: history.TakeLast(31).ToArray());
                view.FindControl<TextBlock>("JobEmpty")!.IsVisible = false;
                Get<TextBlock>("JobSummary").Text = "7 shown · 7 jobs · 7 running";
            }
            if (name == "usage")
            {
                var ledger = new UsageLedger(history[0].Timestamp, MacFrameSource.IdentityPolicy);
                foreach (var sample in history) ledger.Add(sample);
                Get<ListBox>("UsageList").ItemsSource = UsagePresentation.Rows(ledger, "", UsageSort.Cpu);
                Get<TextBlock>("UsageStatus").Text = "Illustrative session · 2 minutes";
            }
            var shell = main.Content; main.Content = null;
            var window = new Window { Width = 1000, Height = 900, Content = shell };
            try
            {
                Save(window, name, () =>
                {
                    // Loaded handlers may refresh an empty session; apply the fixture afterwards.
                    Get<ListBox>("ProcessList").ItemsSource = ProcessPresentation.Rows(history, history[^1],
                        name is "grouped" or "apps_only", ProcessSort.Cpu, "", pins, name == "pinned_only", limit: 20, appsOnly: name == "apps_only");
                    Get<TextBlock>("EmptyProcessLabel").IsVisible = false;
                    Get<TextBlock>("ProcessCountLabel").Text = "Processes 12 · observed threads 186";
                    Get<TextBlock>("SelectionLabel").Text = "CPU averaged over 5 seconds · resident memory · disk I/O";
                    Get<Button>("CaptureMode").IsEnabled = true;
                    if (name == "usage")
                    {
                        var ledger = new UsageLedger(history[0].Timestamp, MacFrameSource.IdentityPolicy);
                        foreach (var sample in history) ledger.Add(sample);
                        Get<ListBox>("UsageList").ItemsSource = UsagePresentation.Rows(ledger, "", UsageSort.Cpu);
                        Get<TextBlock>("UsageStatus").Text = "Illustrative session · 2 minutes";
                    }
                });
            }
            finally { window.Close(); }
        }
        foreach (var theme in new[] { "Dark", "Light" })
        {
            app.ApplyAppearance(theme, "Classic", "Bowl");
            var peaks = new ResourceGaugeHistory(); foreach (var sample in history) peaks.Observe(sample);
            var view = new CompactView { Status = "Live · peak markers show the last 2 minutes" };
            for (var i = 0; i < 5; i++) view.Gauges[i].Update(peaks.Readings[i], new[] { "3.2 GHz", "9.4 / 16.0 GiB", "Wi-Fi · ↑ 1.2 Mbps", "Internal SSD · 12.4 MB/s", "Example GPU" }[i]);
            var window = new Window { Width = 1000, Height = WindowViewState.CompactHeight, Content = view };
            try { Save(window, theme == "Dark" ? "compact" : "compact_light"); } finally { window.Close(); }
        }
        app.ApplyAppearance("Dark", "Classic", "Bowl");
        var settings = new SettingsWindow(new MacPreferences { Theme = "Dark", Palette = "Classic", Language = "en" }, [], 1, true, _ => Task.FromResult<string?>(null))
        { Width = 880, Height = 720 };
        try { Save(settings, "settings"); } finally { settings.Close(); }
    }

    private static MonitorSample[] Samples()
    {
        string[] names = ["Web Browser", "Web Browser Helper", "Code Editor", "Build Worker", "WindowServer", "Terminal", "Music", "File Sync", "Preview", "Indexer", "MacMon", "Background Agent"];
        return Enumerable.Range(0, 121).Select(second => new MonitorSample(
            new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).AddSeconds(second),
            35 + 23 * Math.Sin(second / 12d), names.Select((name, i) => new ProcessUsage(400 + i, (400 + i) * 1000, name,
                .35 * (12 - i) * (1.1 + .6 * Math.Sin(second / 9d + i)), (ulong)(120 + (12 - i) * 55) << 20)
            { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = i == 4 ? "/System/Library/WindowServer" : $"/Applications/Example{(i == 1 ? 0 : i)}.app/Contents/MacOS/Example",
                ThreadCount = 10 + i, IoBytesPerSecond = (12 - i) * 18000, CpuAvailable = true }).ToArray(), 0,
            MonotonicTimestamp: (second + 1L) * Stopwatch.Frequency)
        { IndependentProcessReadings = true, IntervalSeconds = 1, IntervalCpuTicks = 4 * TimeSpan.TicksPerSecond,
            Resources = new() { TotalMemoryBytes = 16UL << 30, UsedMemoryBytes = (ulong)((9 + second / 300d) * (1UL << 30)),
                NetworkId = "example-wifi", NetworkName = "Wi-Fi", ReceiveBytesPerSecond = 1_000_000 + 800_000 * Math.Sin(second / 8d), SendBytesPerSecond = 150_000,
                DiskName = "Internal SSD", DiskReadBytesPerSecond = 7_000_000 + 5_000_000 * Math.Sin(second / 11d), DiskWriteBytesPerSecond = 1_000_000, DiskActivePercent = 20 + 15 * Math.Sin(second / 11d),
                GpuId = "example-gpu", GpuName = "Example GPU", GpuPercent = 25 + 20 * Math.Sin(second / 15d), CpuClockMhz = 3200 } }).ToArray();
    }
}
