using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MacMon;
using MonCore.MacOS;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class HardwareInventoryTests
{
    private const string Apple = """
        {"SPHardwareDataType":[{"machine_name":"Mac mini","machine_model":"Mac14,12","chip_type":"Apple M2 Pro","serial_number":"PRIVATE-SERIAL","platform_UUID":"PRIVATE-UUID"}],
         "SPMemoryDataType":[{"dimm_manufacturer":"Hynix","dimm_type":"LPDDR5","SPMemoryDataType":"16 GB"}],
         "SPDisplaysDataType":[{"sppci_model":"Apple M2 Pro","spdisplays_vendor":"sppci_vendor_Apple","sppci_bus":"spdisplays_builtin","sppci_cores":"16"}],
         "SPStorageDataType":[{"_name":"Data","bsd_name":"disk3s5","mount_point":"/System/Volumes/Data","file_system":"APFS","size_in_bytes":494384795648,"free_space_in_bytes":0,"writable":"yes","volume_uuid":"PRIVATE-VOLUME"}]}
        """;
    private const string Intel = """
        {"SPHardwareDataType":[{"machine_name":"MacBook Pro","cpu_type":"Intel Core i7"}],
         "SPMemoryDataType":[{"_name":"BANK 0","_items":[{"_name":"DIMM0","dimm_size":"8 GB","dimm_type":"DDR4","dimm_speed":"2400 MHz","dimm_manufacturer":"Vendor"},{"_name":"DIMM1","dimm_size":"Empty"}]}],
         "SPDisplaysDataType":[{"sppci_model":"Intel UHD Graphics","spdisplays_vram_shared":"1536 MB"},{"sppci_model":"AMD Radeon Pro","spdisplays_vram":"4 GB"}],
         "SPStorageDataType":[]}
        """;
    internal static MacHardwareInventory Inventory()
    {
        var parsed = MacHardwareInventoryReader.ParseProfiler(Apple);
        return new(DateTimeOffset.UnixEpoch, parsed.Model, "Mac14,12", "26.1", "25B123", parsed.Processor, 10, 10, 16UL << 30,
            parsed.Memory, parsed.Graphics, [new("disk0 · Example SSD", 512000000000)], parsed.Volumes,
            [new("en0", "Ethernet", "Ethernet", "Up", 1000000000, ["192.0.2.1", "2001:db8::1"])], []);
    }
    [Test]
    public void AppleInventoryUsesWhitelistedSpecificationsAndKeepsZeroCapacitySeparateFromUnknown()
    {
        var parsed = MacHardwareInventoryReader.ParseProfiler(Apple);
        Assert.That(parsed.Model, Is.EqualTo("Mac mini")); Assert.That(parsed.Processor, Is.EqualTo("Apple M2 Pro"));
        Assert.That(parsed.Memory.Single().Type, Is.EqualTo("LPDDR5"));
        Assert.That(parsed.Graphics.Single().Cores, Is.EqualTo(16));
        Assert.That(parsed.Graphics.Single().ReportedMemory, Is.Null, "Do not infer VRAM from system RAM");
        Assert.That(parsed.Volumes.Single().Available, Is.Zero);
        Assert.That(parsed.Volumes.Single().Writable, Is.True);
        Assert.That(JsonSerializer.Serialize(parsed), Does.Not.Contain("PRIVATE-"));
        Assert.That(parsed.UnavailableSections, Is.Empty);
    }
    [Test]
    public void IntelInventoryRetainsMemorySlotsAndEveryGpuWithoutInventingEmptySlotCapacity()
    {
        var parsed = MacHardwareInventoryReader.ParseProfiler(Intel);
        Assert.That(parsed.Processor, Is.EqualTo("Intel Core i7"));
        Assert.That(parsed.Memory, Has.Count.EqualTo(2));
        Assert.That(parsed.Memory[0].Speed, Is.EqualTo("2400 MHz"));
        Assert.That(parsed.Memory[1].Size, Is.Null);
        Assert.That(parsed.Graphics.Select(gpu => gpu.ReportedMemory), Is.EqualTo(new[] { "1536 MB", "4 GB" }));
        Assert.That(parsed.UnavailableSections, Is.EqualTo(new[] { "Mounted volumes" }));
    }
    [Test]
    public void MissingMalformedAndOversizedProfilerDataIsExplicit()
    {
        var partial = MacHardwareInventoryReader.ParseProfiler("{\"SPDisplaysDataType\":{}}");
        Assert.That(partial.UnavailableSections, Has.Count.EqualTo(4));
        Assert.That(partial.Graphics, Is.Empty);
        Assert.Catch<JsonException>(() => MacHardwareInventoryReader.ParseProfiler("{ broken"));
        Assert.Throws<InvalidDataException>(() => MacHardwareInventoryReader.ParseProfiler("[]"));
        Assert.Throws<InvalidDataException>(() => MacHardwareInventoryReader.ParseProfiler(new string(' ', MacHardwareInventoryReader.MaximumCharacters + 1)));
        var devices = string.Join(',', Enumerable.Repeat("{}", MacHardwareInventoryReader.MaximumDevices + 1));
        Assert.That(MacHardwareInventoryReader.ParseProfiler("{\"SPDisplaysDataType\":[" + devices + "]}").Graphics, Is.Empty);
    }
    [Test]
    public void ReportsContainAllCollapsedDetailsAndExplainHardwareSemanticsInBothLanguages()
    {
        try
        {
            foreach (var language in new[] { "en", "es" })
            {
                UiText.Apply(language);
                var snapshot = HardwarePresentation.Create(Inventory()); var report = HardwarePresentation.Report("MacMon", snapshot);
                Assert.That(snapshot.Sections, Has.Count.EqualTo(9));
                Assert.That(report, Does.Contain("Apple M2 Pro").And.Contain("LPDDR5").And.Contain("192.0.2.1"));
                Assert.That(report, Does.Contain("0.0 B").And.Contain("1.0 Gbps"));
                Assert.That(report, Does.Contain(UiText.Get("Whole disk devices")).And.Contain(UiText.Get("Mounted volumes")));
                Assert.That(report, Does.Contain("APFS").And.Contain(UiText.Get("Reported graphics memory")));
                Assert.That(report, Does.Not.Contain("sppci_").And.Not.Contain("spdisplays_"));
                Assert.That(report, Does.Contain(UiText.Get("Unavailable")));
            }
        }
        finally { UiText.Apply("en"); }
    }
    [Test]
    public async Task NativeInventoryHasRealCpuMemoryAndFallbackSurvivesProfilerFailure()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS only");
        var normal = await new MacHardwareInventoryReader().Read();
        Assert.That(normal.MemoryBytes, Is.GreaterThan(0)); Assert.That(normal.PhysicalCores, Is.GreaterThan(0));
        Assert.That(normal.LogicalCores, Is.GreaterThanOrEqualTo(normal.PhysicalCores ?? 0));
        var partial = await new MacHardwareInventoryReader(_ => throw new IOException("Unavailable")).Read();
        Assert.That(partial.MemoryBytes, Is.GreaterThan(0));
        Assert.That(partial.UnavailableSections, Does.Contain("Graphics adapters"));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await new MacHardwareInventoryReader().Read(canceled.Token));
    }
    [AvaloniaTest]
    public async Task HardwareRefreshCoalescesRetainsOldSnapshotOnFailureAndIgnoresLateCloseCompletion()
    {
        var pending = new TaskCompletionSource<HardwareInfoSnapshot>(); var calls = 0; CancellationToken token = default;
        var snapshot = HardwarePresentation.Create(Inventory());
        var window = new HardwareInfoWindow(ct => { calls++; token = ct; return pending.Task; });
        window.Show();
        Assert.That(window.FindControl<Button>("CopyDetails")!.IsEnabled, Is.False);
        window.FindControl<Button>("RefreshInfo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(calls, Is.EqualTo(1));
        pending.SetResult(snapshot); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.That(window.FindControl<Button>("CopyDetails")!.IsEnabled, Is.True);
        pending = new();
        window.FindControl<Button>("RefreshInfo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        pending.SetException(new IOException("Unavailable")); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.That(window.FindControl<ItemsControl>("InfoSections")!.ItemsSource, Is.SameAs(snapshot.Sections));
        Assert.That(window.FindControl<TextBlock>("InventoryStatus")!.Text, Does.Contain("previous"));
        pending = new();
        window.FindControl<Button>("RefreshInfo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.Close(); Assert.That(token.IsCancellationRequested, Is.True);
        pending.SetResult(snapshot with { Sections = [] }); await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.That(window.FindControl<ItemsControl>("InfoSections")!.ItemsSource, Is.SameAs(snapshot.Sections));
    }
    [AvaloniaTest]
    public void ExpandedHardwareDetailsFitMinimumSizeInBothLanguages()
    {
        try
        {
            foreach (var language in new[] { "en", "es" })
            {
                UiText.Apply(language);
                var snapshot = HardwarePresentation.Create(Inventory());
                var window = new HardwareInfoWindow(_ => Task.FromResult(snapshot)) { Width = 480, Height = 350, Position = new PixelPoint(0, 0) };
                try
                {
                    window.Show(); Dispatcher.UIThread.RunJobs();
                    foreach (var expander in window.GetVisualDescendants().OfType<Expander>()) expander.IsExpanded = true;
                    Dispatcher.UIThread.RunJobs();
                    var copy = window.FindControl<Button>("CopyDetails")!;
                    var title = window.FindControl<TextBlock>("ProductVersion")!;
                    Assert.That(title.TranslatePoint(default, window)!.Value.X, Is.GreaterThanOrEqualTo(0));
                    Assert.That(title.TranslatePoint(default, window)!.Value.Y, Is.GreaterThanOrEqualTo(0));
                    Assert.That(copy.TranslatePoint(default, window)!.Value.Y + copy.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height));
                    using var frame = window.CaptureRenderedFrame(); Assert.That(frame, Is.Not.Null);
                    if (Environment.GetEnvironmentVariable("MACMON_RENDER_OUTPUT") is { } output)
                    { Directory.CreateDirectory(output); frame!.Save(Path.Combine(output, "hardware-" + language + ".png"), PngBitmapEncoderOptions.Default); }
                }
                finally { window.Close(); }
            }
        }
        finally { UiText.Apply("en"); }
    }
}
