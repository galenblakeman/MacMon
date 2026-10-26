using MonCore.MacOS;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class MacLaunchdTests
{
    private static readonly LaunchdContext Context = new(Guid.NewGuid(), 501, "Aqua");

    [Test]
    public void LegacyRowsKeepLabelsStatusesAndContextIdentity()
    {
        // Sanitized shape captured from macOS 26.6.2 launchctl list; LF and CRLF accepted.
        var jobs = MacLaunchdReader.Parse("PID\tStatus\tLabel\r\n42\t-9\tcom.example.running\r\n-\t0\tcom.example.idle\r\n-\t23\tjob with spaces\r\n", Context);
        Assert.That(jobs.Select(j => j.ReportedPid), Is.EqualTo(new int?[] { 42, null, null }));
        Assert.That(jobs.Select(j => j.LastExitStatus), Is.EqualTo(new[] { -9, 0, 23 }));
        Assert.That(jobs[2].Id.Label, Is.EqualTo("job with spaces"));
        Assert.That(jobs[0].Id, Is.Not.EqualTo(new LaunchdJobId(Context with { ContextId = Guid.NewGuid() }, jobs[0].Id.Label)));
        Assert.That(MacLaunchdReader.Parse("PID\tStatus\tLabel\n", Context), Is.Empty);
    }

    [TestCase("PID Status Label\n42 0 job")]
    [TestCase("PID\tStatus\tLabel\n0\t0\tjob")]
    [TestCase("PID\tStatus\tLabel\n-\t?\tjob")]
    [TestCase("PID\tStatus\tLabel\n-\t0\tjob\n-\t0\tjob")]
    [TestCase("PID\tStatus\tLabel\n42\t0\tjob\textra")]
    [TestCase("PID\tStatus\tLabel\n42\t0\t")]
    [TestCase("PID\tStatus\tLabel\n42\t0\tbad\u001bname")]
    public void ChangedOrPartialFormatsFailClosed(string text) =>
        Assert.Throws<InvalidDataException>(() => MacLaunchdReader.Parse(text, Context));

    [Test]
    public void LargeOutputAndLabelsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => MacLaunchdReader.Parse(new string('x', 2_097_153), Context));
        var tooMany = "PID\tStatus\tLabel\n" + string.Concat(Enumerable.Range(0, 20_001).Select(i => $"-\t0\tjob{i}\n"));
        Assert.Throws<InvalidDataException>(() => MacLaunchdReader.Parse(tooMany, Context));
        Assert.Throws<InvalidDataException>(() => MacLaunchdReader.Parse("PID\tStatus\tLabel\n-\t0\t" + new string('x', 4097), Context));
    }

    [Test]
    public async Task JobLinkRequiresSameUserLifetimeOnBothSidesOfDiscovery()
    {
        var processes = new Processes();
        var runner = new Runner { OnList = () => processes.After = true };
        var snapshot = await new MacLaunchdReader(runner, processes).Read();
        Assert.That(snapshot.Available, Is.True);
        Assert.That(snapshot.Jobs.Single(j => j.ReportedPid == 42).ObservedIdentity, Is.Not.Null);
        Assert.That(snapshot.Jobs.Single(j => j.ReportedPid == 43).ObservedIdentity, Is.Null, "PID reused");
        Assert.That(snapshot.Jobs.Single(j => j.ReportedPid == 44).ObservedIdentity, Is.Null, "other user");
        Assert.That(snapshot.Jobs.Single(j => j.ReportedPid == 45).ObservedIdentity, Is.Null, "exited");
        Assert.That(snapshot.Jobs.Single(j => j.ReportedPid == 46).ObservedIdentity, Is.Null, "mismatched BSD PID");
        Assert.That(runner.Commands, Is.EqualTo(new[] { "manageruid", "managername", "list" }));
    }

    [Test]
    public async Task FailedDiscoveryExposesNoPartialInventoryAndCancellationPropagates()
    {
        var runner = new Runner { ExitCode = 5 };
        var reader = new MacLaunchdReader(runner, new Processes());
        var result = await reader.Read();
        Assert.That(result.Available, Is.False);
        Assert.That(result.Jobs, Is.Empty);
        Assert.That(result.Error, Does.Contain("exit 5"));
        runner.ExitCode = 0; runner.Manager = "System"; runner.User = "0";
        Assert.That((await reader.Read()).Available, Is.False);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.Read(canceled.Token));
    }

    [Test]
    public async Task NativeReadOnlyInventoryHasContextAndUniqueLabels()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Ignore("macOS only");
        var result = await new MacLaunchdReader().Read();
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Context!.UserId, Is.GreaterThan(0));
        Assert.That(result.Jobs, Is.Not.Empty);
        Assert.That(result.Jobs.Select(job => job.Id).Distinct().Count(), Is.EqualTo(result.Jobs.Count));
    }

    private sealed class Runner : ILaunchctlRunner
    {
        public List<string> Commands { get; } = [];
        public Action? OnList { get; set; }
        public int ExitCode { get; set; }
        public string Manager { get; set; } = "Aqua";
        public string User { get; set; } = "501";
        public Task<LaunchctlResult> Run(string command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            if (command == "list") OnList?.Invoke();
            return Task.FromResult(new LaunchctlResult(ExitCode, command switch
            {
                "manageruid" => User + "\n", "managername" => Manager + "\n",
                _ => "PID\tStatus\tLabel\n42\t0\ta\n43\t0\tb\n44\t0\tc\n45\t0\td\n46\t0\te\n-\t0\tidle\n"
            }));
        }
    }

    private sealed class Processes : IMacProcessApi
    {
        public bool After { get; set; }
        public int[] EnumeratePids() => [42, 43, 44, 45, 46];
        public string ReadPath(int pid) => "";
        public NativeProcessInfo? ReadInfo(int pid) => ReadIdentity(pid);
        public NativeProcessInfo? ReadIdentity(int pid) => After && pid == 45 ? null :
            new(pid == 46 ? 999 : pid, 1, (ulong)(After && pid == 43 ? 999 : 100), 123, "job", null, null, null)
            { EffectiveUserId = pid == 44 ? 502u : 501u };
    }
}
