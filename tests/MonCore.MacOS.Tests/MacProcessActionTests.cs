using System.Diagnostics;
using MonCore.MacOS;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.MacOS.Tests;

public sealed class MacProcessActionTests
{
    private static ProcessUsage Selected => new(123, 100_000_456, "test", 0)
    { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = "/test" };

    [Test]
    public void StaleProtectedSelfAndOtherUserTargetsAreRejected()
    {
        var api = new FakeApi();
        var actions = new MacProcessActions(api);
        Assert.That(actions.Prepare(Selected with { ProcessId = 1 }), Is.Null);
        Assert.That(actions.Prepare(Selected with { ProcessId = api.SelfPid }), Is.Null);
        Assert.That(actions.Prepare(Selected with { CreationTicks = 1 }), Is.Null);
        api.Owner = 0; Assert.That(actions.Prepare(Selected), Is.Null);
        api.Owner = 502; Assert.That(actions.Prepare(Selected), Is.Null);
        api.Owner = 501; api.Path = "/replacement"; Assert.That(actions.Prepare(Selected), Is.Null);
        Assert.That(api.Signals, Is.Zero);
    }

    [Test]
    public void ExecutionRevalidatesLifetimeAndRetainsOriginalAuditToken()
    {
        var api = new FakeApi();
        var actions = new MacProcessActions(api);
        var target = actions.Prepare(Selected)!;
        api.Start = 101;
        Assert.That(actions.Terminate(target), Is.Not.Null);
        Assert.That(api.Signals, Is.Zero);
        api.Start = 100;
        api.TokenVersion = 2;
        Assert.That(actions.Terminate(target), Is.Null);
        Assert.That(api.DeliveredTokenVersion, Is.EqualTo(1));
    }

    [Test]
    public async Task NativeTerminationSignalsOnlyThePreparedDisposableChild()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS integration test");
        using var child = System.Diagnostics.Process.Start(new ProcessStartInfo("/bin/sleep")
        { UseShellExecute = false, ArgumentList = { "60" } })!;
        try
        {
            var info = new DarwinProcessApi().ReadIdentity(child.Id)!;
            var usage = new ProcessUsage(child.Id, checked((long)(info.StartSeconds * 1_000_000 + info.StartMicroseconds)), "sleep", 0)
            { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds, ImagePath = new DarwinProcessApi().ReadPath(child.Id) };
            var actions = new MacProcessActions();
            var target = actions.Prepare(usage);
            Assert.That(target, Is.Not.Null, "Own child must have a readable generation-bound action target.");
            Assert.That(actions.Terminate(target!), Is.Null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(timeout.Token);
            Assert.That(actions.Terminate(target!), Is.Not.Null, "An exited target cannot be signalled again.");
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
    }

    private sealed class FakeApi : IMacSignalApi
    {
        public uint UserId => 501;
        public int SelfPid => 999;
        public uint Owner { get; set; } = 501;
        public ulong Start { get; set; } = 100;
        public string Path { get; set; } = "/test";
        public uint TokenVersion { get; set; } = 1;
        public int Signals { get; private set; }
        public uint DeliveredTokenVersion { get; private set; }
        public MacActionSnapshot ReadTarget(int pid) => new(new(pid, 1, Start, 456, "test", null, null, null)
        { EffectiveUserId = Owner }, new AuditToken { Value5 = (uint)pid, Value7 = TokenVersion }, Path);
        public int Signal(AuditToken token, int signal)
        { Signals++; DeliveredTokenVersion = token.Value7; Assert.That(signal, Is.EqualTo(15)); return 0; }
    }
}
