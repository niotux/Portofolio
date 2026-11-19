using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;

// Local deterministic storage/retrieval benchmark. Fresh temporary notebooks only; no model or factory calls.
var counts = args.Length == 0 ? new[] { 100, 1000, 10000 } : args.Select(int.Parse).ToArray();
if (counts.Any(n => n is < 1 or > 100000)) throw new ArgumentException("Record counts must be 1..100000");
var root = Directory.CreateTempSubdirectory("pi-runtime-bench-").FullName;
var runs = new JsonArray();
try
{
    foreach (var count in counts)
    {
        var dir = Path.Combine(root, count.ToString());
        var watch = Stopwatch.StartNew();
        using (var seed = new NotebookStore(dir, "benchmark"))
            for (var i = 0; i < count; i++)
                seed.Write("note-" + i, $"verified subsystem-{i % 100} behavior with regression evidence sample-{i}",
                    "Verified observation " + i, ["repo:benchmark"], status: "completed");
        var writeMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        using var store = new NotebookStore(dir, "benchmark");
        var restoreMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var batch = store.Pending(100);
        var batchBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var batchMs = watch.Elapsed.TotalMilliseconds;
        var latencies = new List<double>();
        for (var i = 0; i < 20; i++)
        {
            watch.Restart();
            var hits = store.Search("subsystem 42", null, null, 5, out _);
            if (hits.Count == 0 && count >= 100) throw new InvalidOperationException("Recall fixture did not match");
            latencies.Add(watch.Elapsed.TotalMilliseconds);
        }
        latencies.Sort();
        runs.Add(new JsonObject { ["records"] = count, ["write_ms"] = writeMs, ["restore_ms"] = restoreMs,
            ["pending_batch_count"] = batch.Count, ["pending_batch_ms"] = batchMs, ["pending_batch_allocated_bytes"] = batchBytes,
            ["search_p50_ms"] = latencies[9], ["search_p95_ms"] = latencies[18], ["journal_bytes"] = Directory.EnumerateFiles(dir).Sum(p => new FileInfo(p).Length) });
    }
    Console.WriteLine(new JsonObject { ["runtime"] = Environment.Version.ToString(), ["os"] = Environment.OSVersion.ToString(),
        ["managed_bytes"] = GC.GetTotalMemory(false), ["runs"] = runs }.ToJsonString());
}
finally { Directory.Delete(root, true); }
