using System.Buffers.Binary;
using MonCore.MacOS;
using NUnit.Framework;

namespace MonCore.MacOS.Tests;

public sealed class CpuFrequencyTests
{
    [Test]
    public void FrequencyRequiresMatchingLayoutsAndWeightsOnlyActiveResidency()
    {
        CpuFrequencyChannel[] prior = [new("ECPU0", ["IDLE", "P0", "P1"], [100, 10, 20]), new("PCPU0", ["DOWN", "IDLE", "P0", "P1"], [10, 100, 0, 0])];
        CpuFrequencyChannel[] next = [prior[0] with { Residency = [1000, 20, 20] }, prior[1] with { Residency = [20, 200, 0, 30] }];
        Dictionary<char, double[]> tables = new() { ['E'] = [1000, 2000], ['P'] = [2000, 3000] };
        Assert.That(DarwinCpuFrequency.Calculate(prior, next.Reverse().ToArray(), tables, 2), Is.EqualTo(2500));
        Assert.That(DarwinCpuFrequency.Calculate(prior, prior, tables, 2), Is.Null, "Idle has no measured active clock");
        Assert.That(DarwinCpuFrequency.Calculate(prior, next, tables, 3), Is.Null);
        Assert.That(DarwinCpuFrequency.Calculate(prior, [next[0], next[0]], tables, 2), Is.Null);
        Assert.That(DarwinCpuFrequency.Calculate(prior, [next[0] with { States = ["IDLE", "P0"] }, next[1]], tables, 2), Is.Null);
        Assert.That(DarwinCpuFrequency.Calculate(prior, [next[0] with { Residency = [99, 20, 20] }, next[1]], tables, 2), Is.Null, "Counter rollback requires a new baseline");
        Assert.That(DarwinCpuFrequency.Calculate(prior, [next[0] with { Name = "Unknown0" }, next[1]], tables, 2), Is.Null);
    }
    [Test]
    public void TablesRejectUnknownUnitsMalformedEntriesAndNonmonotonicFrequencies()
    {
        byte[] data = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 1000000000);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 2000000000);
        Assert.That(DarwinCpuFrequency.DecodeTable(data, 1000000), Is.EqualTo(new[] { 1000d, 2000d }));
        Assert.That(DarwinCpuFrequency.DecodeTable(data, 1000), Is.Empty);
        Assert.That(DarwinCpuFrequency.DecodeTable(data[..9], 1000000), Is.Empty);
        Assert.That(DarwinCpuFrequency.DecodeTable(data, 1), Is.Empty);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 500000000);
        Assert.That(DarwinCpuFrequency.DecodeTable(data, 1000000), Is.Empty);
    }
}
