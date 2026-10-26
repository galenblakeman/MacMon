using MonCore.MacOS;

namespace MacMon;

/// <summary>Serialized, cancellable refreshes with a monotonic retry cadence. Called from one UI context.</summary>
public sealed class LaunchdMonitor
{
    private readonly Func<CancellationToken, Task<LaunchdSnapshot>> _read;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private Task? _pending;
    private bool _attempted;
    private long _attemptAt;
    public LaunchdInventory Inventory { get; }
    public bool Stopped { get; private set; }
    public bool IsRefreshing => _pending is { IsCompleted: false };

    public LaunchdMonitor(Func<CancellationToken, Task<LaunchdSnapshot>> read, TimeProvider? clock = null)
    { _read = read; _clock = clock ?? TimeProvider.System; Inventory = new(_clock); }

    public bool IsDue(bool visible) => visible && !Stopped && !IsRefreshing &&
        (!_attempted || _clock.GetElapsedTime(_attemptAt).TotalSeconds >= 5);

    public Task Refresh()
    {
        if (Stopped) return Task.CompletedTask;
        if (IsRefreshing) return _pending!;
        _attempted = true; _attemptAt = _clock.GetTimestamp();
        return _pending = Read();
    }

    private async Task Read()
    {
        try
        {
            var snapshot = await _read(_stop.Token);
            if (!Stopped) Inventory.Apply(snapshot);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task Stop()
    {
        if (Stopped) return;
        Stopped = true;
        await _stop.CancelAsync();
        if (_pending is not null) await _pending;
        _stop.Dispose();
    }
}
