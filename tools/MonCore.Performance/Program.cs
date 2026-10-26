using System.Diagnostics;
using System.Text.Json;
using WinMon.Monitoring;
var rows = Enumerable.Range(1, 850).Select(i => new ProcessUsage(i, i, "app" + i, i % 10 / 100d, 1_000_000) { ImagePath = "C:\\App" + i % 100 + "\\app.exe" }).ToArray();
var samples = Enumerable.Range(0, 6).Select(i => new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(i), 30, rows, 0) { IntervalSeconds = 1, IntervalCpuTicks = 80_000_000 }).ToArray();
for (var i=0;i<1000;i++) ProcessRanking.Rank(samples, grouped:true);
var results = new List<object>();
for (var batch=0;batch<7;batch++) {
 GC.Collect(); var bytes=GC.GetAllocatedBytesForCurrentThread(); var start=Stopwatch.GetTimestamp();
 for(var i=0;i<500;i++) ProcessRanking.Rank(samples, grouped:true);
 results.Add(new { operation="grouped ranking", batch, microseconds=Stopwatch.GetElapsedTime(start).TotalMicroseconds/500, bytes=(GC.GetAllocatedBytesForCurrentThread()-bytes)/500 });
 var ledger = new UsageLedger(DateTimeOffset.Now);
 for(var i=0;i<1000;i++) ledger.Add(samples[^1]);
 bytes=GC.GetAllocatedBytesForCurrentThread(); start=Stopwatch.GetTimestamp();
 for(var i=0;i<500;i++) ledger.Add(samples[^1]);
 results.Add(new { operation="usage", batch, microseconds=Stopwatch.GetElapsedTime(start).TotalMicroseconds/500, bytes=(GC.GetAllocatedBytesForCurrentThread()-bytes)/500 });
}
Console.WriteLine(JsonSerializer.Serialize(results));
