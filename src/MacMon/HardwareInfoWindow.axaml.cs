using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace MacMon;

public sealed record HardwareEntry(string Label, string Value);
public sealed record HardwareSection(string Title, IReadOnlyList<HardwareEntry> Entries, bool Expanded = true);
public sealed partial class HardwareInfoWindow : Window
{
    private readonly CancellationTokenSource _closed = new();
    private readonly Func<CancellationToken, Task<HardwareInfoSnapshot>> _read;
    private HardwareInfoSnapshot? _snapshot;
    private bool _refreshing;
    private bool _isClosed;
    public HardwareInfoWindow() : this(() => []) { }
    public HardwareInfoWindow(Func<IReadOnlyList<HardwareSection>> snapshot)
        : this(_ => Task.FromResult(new HardwareInfoSnapshot(DateTimeOffset.Now, snapshot()))) { }
    public HardwareInfoWindow(Func<CancellationToken, Task<HardwareInfoSnapshot>> read)
    {
        _read = read;
        InitializeComponent();
        ProductVersion.Text = $"MacMon {typeof(HardwareInfoWindow).Assembly.GetName().Version?.ToString(3)}";
        RefreshInfo.Click += async (_, _) => await Refresh();
        CancelAction.Click += (_, _) => Close();
        Closed += (_, _) => { _isClosed = true; _closed.Cancel(); _closed.Dispose(); };
        CopyDetails.Click += async (_, _) =>
        {
            if (Clipboard is not { } clipboard || _snapshot is null) return;
            try { await clipboard.SetTextAsync(HardwarePresentation.Report(ProductVersion.Text!, _snapshot)); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            { InventoryStatus.Text = UiText.Get("Could not copy hardware details."); }
        };
        _ = Refresh();
    }
    private async Task Refresh()
    {
        if (_refreshing || _isClosed) return;
        _refreshing = true; RefreshInfo.IsEnabled = CopyDetails.IsEnabled = false;
        InventoryStatus.Text = UiText.Get("Reading hardware details…");
        try
        {
            var snapshot = await _read(_closed.Token);
            if (_isClosed) return;
            _snapshot = snapshot; InfoSections.ItemsSource = snapshot.Sections;
            InventoryStatus.Text = UiText.Format("Observed: {0}", snapshot.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")) +
                (snapshot.Partial ? " · " + UiText.Get("Some hardware details are unavailable.") : "");
        }
        catch (OperationCanceledException) when (_isClosed) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or OperationCanceledException)
        { if (!_isClosed) InventoryStatus.Text = UiText.Get(_snapshot is null ? "Hardware details unavailable. Choose Refresh to retry." : "Refresh failed. Showing the previous hardware snapshot."); }
        finally
        {
            _refreshing = false;
            if (!_isClosed) { RefreshInfo.IsEnabled = true; CopyDetails.IsEnabled = _snapshot is not null; }
        }
    }
}
