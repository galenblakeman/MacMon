using MacMon;
using MonCore.MacOS;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class LaunchdMonitorTests
{
    private static LaunchdSnapshot Snapshot => new(DateTimeOffset.Now, new(Guid.NewGuid(), 501, "Aqua"), []);

    [Test]
    public async Task RefreshesCoalesceAndOnlyBecomeDueOnVisibleMonotonicCadence()
    {
        var clock = new Clock();
        var result = new TaskCompletionSource<LaunchdSnapshot>();
        var calls = 0;
        var monitor = new LaunchdMonitor(_ => { calls++; return result.Task; }, clock);
        Assert.That(monitor.IsDue(false), Is.False);
        Assert.That(monitor.IsDue(true), Is.True);
        var first = monitor.Refresh();
        Assert.That(monitor.Refresh(), Is.SameAs(first));
        clock.Advance(20);
        Assert.That(monitor.IsDue(true), Is.False, "no overlapping reads");
        result.SetResult(Snapshot);
        await first;
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(monitor.IsDue(true), Is.True);
        await monitor.Refresh();
        Assert.That(monitor.IsDue(true), Is.False);
        clock.Advance(4);
        Assert.That(monitor.IsDue(true), Is.False);
        clock.Advance(1);
        Assert.That(monitor.IsDue(true), Is.True);
        await monitor.Stop();
    }

    [Test]
    public async Task FailedReadsRetainRowsButDoNotFreshenTheirAgeAndRecover()
    {
        var clock = new Clock();
        var next = Snapshot;
        var monitor = new LaunchdMonitor(_ => Task.FromResult(next), clock);
        await monitor.Refresh();
        Assert.That(monitor.Inventory.IsStale, Is.False);
        clock.Advance(15);
        Assert.That(monitor.Inventory.IsStale, Is.True);
        next = new(DateTimeOffset.Now, null, [], "denied");
        await monitor.Refresh();
        Assert.That(monitor.Inventory.LastSuccess, Is.Not.Null);
        Assert.That(monitor.Inventory.Age.TotalSeconds, Is.EqualTo(15));
        Assert.That(monitor.Inventory.IsStale, Is.True);
        Assert.That(monitor.IsDue(true), Is.False, "failures obey the retry cadence");
        next = Snapshot;
        await monitor.Refresh();
        Assert.That(monitor.Inventory.IsStale, Is.False);
        Assert.That(monitor.Inventory.Error, Is.Null);
        await monitor.Stop();
    }

    [Test]
    public async Task StopCancelsPendingReadAndPreventsMoreWork()
    {
        var calls = 0;
        var monitor = new LaunchdMonitor(async token =>
        {
            calls++;
            await Task.Delay(Timeout.Infinite, token);
            return Snapshot;
        });
        var pending = monitor.Refresh();
        await monitor.Stop();
        await pending;
        await monitor.Refresh();
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(monitor.Inventory.LastSuccess, Is.Null);
        Assert.That(monitor.IsDue(true), Is.False);
        await monitor.Stop();
    }

    [Test]
    public async Task LateCompletionAfterStopCannotPublishRows()
    {
        var result = new TaskCompletionSource<LaunchdSnapshot>();
        var monitor = new LaunchdMonitor(_ => result.Task);
        _ = monitor.Refresh();
        var stopping = monitor.Stop();
        result.SetResult(Snapshot);
        await stopping;
        Assert.That(monitor.Inventory.LastSuccess, Is.Null);
    }

    private sealed class Clock : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public void Advance(int seconds) => _now += seconds * 1000;
    }
}
