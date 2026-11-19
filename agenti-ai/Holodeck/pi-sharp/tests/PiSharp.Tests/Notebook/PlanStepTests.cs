using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Notebook;

/// <summary>plan-tool-1: the plan-step kind, the order/check fields, and the plan group in the briefing.</summary>
public class PlanStepTests
{
    private const string Task = "wi-test";

    private static void AddStep(NotebookStore store, int n, string text, string status = "open", double? order = null, string? check = null)
        => store.Put($"plan-{Task}-{n:D2}", "plan-step", text, Task, status, 0, order: order, check: check);

    // Acceptance (a): 7 steps render in order with the current step detailed and "3/7 done".
    [Fact]
    public void SevenStepsRenderInOrderWithCurrentDetailedAndDoneCount()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        for (var n = 1; n <= 7; n++)
        {
            var status = n <= 3 ? "completed" : "open";
            AddStep(store, n, $"Step {n} details go here {new string('x', 40)}", status, order: n - 1, check: n == 4 ? "dotnet test" : null);
        }
        var entries = store.BriefingEntries();
        var planKeys = entries.Where(e => e.Key.StartsWith("plan:")).ToArray();
        Assert.Contains(planKeys, e => e.Key == "plan:~summary");
        Assert.Contains("3/7 done", planKeys.First(e => e.Key == "plan:~summary").Line);
        // Current = step 4, detailed, with its check; next two by title; order preserved (4 before 5 before 6).
        var current = planKeys.Single(e => e.Key == "plan:plan-wi-test-04");
        Assert.StartsWith("CURRENT", current.Line);
        Assert.Contains("check: dotnet test", current.Line);
        var next5 = planKeys.Single(e => e.Key == "plan:plan-wi-test-05");
        var next6 = planKeys.Single(e => e.Key == "plan:plan-wi-test-06");
        Assert.StartsWith("NEXT", next5.Line);
        Assert.StartsWith("NEXT", next6.Line);
        // Step 7 is omitted from the listing (current + 2 next) and counted instead.
        Assert.Contains(planKeys, e => e.Key == "plan:~more" && e.Line.Contains("+1 more open steps"));
        Assert.DoesNotContain(planKeys, e => e.Key == "plan:plan-wi-test-07");
        // The completed steps are not listed individually; the count carries them.
        Assert.DoesNotContain(planKeys, e => e.Key == "plan:plan-wi-test-01");
    }

    // Acceptance (b): 20 steps fit in the plan budget.
    [Fact]
    public void TwentyStepsFitWithinThePlanBudget()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        for (var n = 1; n <= 20; n++)
            AddStep(store, n, new string('w', 160), "open", order: n - 1, check: new string('c', 120));
        var entries = store.BriefingEntries();
        var planChars = entries.Where(e => e.Key.StartsWith("plan:")).Sum(e => e.Line.Length);
        Assert.True(planChars <= NotebookStore.PlanBudget, $"plan group is {planChars} chars, budget {NotebookStore.PlanBudget}");
        // The whole briefing still starts with the plan group.
        Assert.Equal("plan:~summary", entries[0].Key);
        Assert.Contains("0/20 done", entries[0].Line);
    }

    // Acceptance (c): changing one step's status yields a delta of exactly that key (+ its summary line).
    [Fact]
    public void ChangingOneStepStatusChangesOnlyThatKey()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        for (var n = 1; n <= 4; n++) AddStep(store, n, $"Step {n} {new string('y', 30)}", "open", order: n - 1);
        var before = store.BriefingEntries().ToDictionary(e => e.Key, e => e.Line);
        store.Transition("plan-wi-test-02", "completed");
        var after = store.BriefingEntries().ToDictionary(e => e.Key, e => e.Line);
        var changed = before.Keys.Union(after.Keys).Where(k => !before.TryGetValue(k, out var b) || !after.TryGetValue(k, out var a) || b != a).ToArray();
        // Step 2's line disappears (completed), the summary gains "1/4 done", and step 2's slot in the
        // listing shifts to step 4: keys plan-04 and the summary change. No other key changes.
        Assert.DoesNotContain(changed, k => k is "plan:plan-wi-test-01" or "plan:plan-wi-test-03");
        Assert.Contains(changed, k => k == "plan:~summary");
        Assert.Contains("1/4 done", after["plan:~summary"]);
    }

    // plan-steps are not obligations: a blocked plan-step shows in the plan group, not in the obligations lines.
    [Fact]
    public void PlanStepsAreExcludedFromObligations()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        store.Put("inst-1", "instruction", "Do the thing", Task, "open", 0);
        AddStep(store, 1, "First step blocked by env", "blocked", order: 0);
        var entries = store.BriefingEntries();
        // The step appears in the plan group (blocked steps are always listed) with the BLOCKED marker...
        Assert.Contains(entries, e => e.Key == "plan:plan-wi-test-01" && e.Line.Contains("BLOCKED"));
        // ...and NOT as an obligations line (blocked plan-steps are listed in the plan group only).
        Assert.DoesNotContain(entries, e => e.Key == "plan-wi-test-01");
        // A plain instruction is still an obligations line.
        Assert.Contains(entries, e => e.Key == "inst-1" && e.Line.Contains("Do the thing"));
        // The plan group comes before the obligations group.
        var arr = entries.ToArray();
        Assert.True(Array.FindIndex(arr, e => e.Key.StartsWith("plan:")) < Array.FindIndex(arr, e => e.Key == "inst-1"));
    }

    // order + check survive a full round-trip through NotebookSync (server stores the whole record).
    [Fact]
    public async Task SyncRoundTripKeepsOrderAndCheck()
    {
        using var disk = new TempWorkspace();
        var server = new PlanSyncServer();
        using (var store = new NotebookStore(disk.Path, "seat"))
        using (var sync = new NotebookSync("http://kb/mcp", "token", server))
        {
            store.Put("plan-t-01", "plan-step", "Clone the repo", Task, "open", 0, order: 1, check: "git clone works");
            await sync.FlushAsync(store, default);
        }
        var committed = server.Records.Single(r => r["id"]!.GetValue<string>() == "plan-t-01");
        Assert.Equal(1.0, committed["order"]!.GetValue<double>());
        Assert.Equal("git clone works", committed["check"]!.GetValue<string>());
        using var fresh = new TempWorkspace();
        using var restored = new NotebookStore(fresh.Path, "seat");
        using var restore = new NotebookSync("http://kb/mcp", "token", server);
        await restore.FlushAsync(restored, default);
        var rec = restored.Get("plan-t-01")!;
        Assert.Equal(1.0, rec["order"]!.GetValue<double>());
        Assert.Equal("git clone works", rec["check"]!.GetValue<string>());
        Assert.Equal(1.0, rec["order"]!.GetValue<double>());
        // The restored step renders in the plan group with its check.
        var entries = restored.BriefingEntries();
        Assert.Contains(entries, e => e.Key == "plan:plan-t-01" && e.Line.Contains("git clone works"));
    }

    // order/check are in Put's idempotency comparison: same operationId with a changed order is a conflict.
    [Fact]
    public void OrderAndCheckAreInTheIdempotencyComparison()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, order: 1, check: "x", operationId: "op");
        // Replay with identical payload: idempotent.
        Assert.Equal(1, store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, order: 1, check: "x", operationId: "op")["revision"]!.GetValue<long>());
        // Same operationId, different order: conflict.
        Assert.Throws<InvalidOperationException>(() => store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, order: 2, check: "x", operationId: "op"));
        // Same operationId, different check: conflict.
        Assert.Throws<InvalidOperationException>(() => store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, order: 1, check: "y", operationId: "op"));
    }

    [Fact]
    public void OrderAndCheckValidateAndKeepValuesOnLaterPuts()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        Assert.Throws<ArgumentException>(() => store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, order: -1));
        Assert.Throws<ArgumentException>(() => store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, check: new string('z', 201)));
        store.Put("plan-t-01", "plan-step", "A", Task, "open", 0, order: 1, check: "x");
        // Omitted fields keep their values.
        var updated = store.Put("plan-t-01", "plan-step", "A changed", Task, "open", 1);
        Assert.Equal(1.0, updated["order"]!.GetValue<double>());
        Assert.Equal("x", updated["check"]!.GetValue<string>());
        // The plan-step kind is accepted; unknown kinds still fail.
        Assert.Throws<ArgumentException>(() => store.Put("x", "plan", "A", Task, "open", 0));
    }

    // The model-facing tool exposes order/check on put and write.
    [Fact]
    public async Task NotebookToolExposesOrderAndCheck()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        var tool = new NotebookTool(store);
        var put = await tool.ExecuteAsync("t", new JsonObject
        {
            ["op"] = "put", ["id"] = "plan-t-01", ["kind"] = "plan-step", ["text"] = "A", ["task"] = Task,
            ["order"] = 3.0, ["check"] = "echo ok", ["expectedRevision"] = 0,
        }, null, default);
        Assert.Contains("\"order\":3", put.Text);
        Assert.Contains("\"check\":\"echo ok\"", put.Text);
        var write = await tool.ExecuteAsync("t", new JsonObject
        {
            ["op"] = "write", ["id"] = "plan-t-02", ["kind"] = "plan-step", ["text"] = "B", ["task"] = Task,
            ["order"] = 4.0, ["check"] = "echo b",
        }, null, default);
        Assert.Contains("\"order\":4", write.Text);
        var rec = store.Get("plan-t-02")!;
        Assert.Equal(4.0, rec["order"]!.GetValue<double>());
        Assert.Equal("echo b", rec["check"]!.GetValue<string>());
    }

    // Unspecified order sorts after specified orders; ties break on id.
    [Fact]
    public void UnspecifiedOrderSortsLastAndTiesBreakById()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        AddStep(store, 1, "no order", "open");
        AddStep(store, 2, "order one", "open", order: 1);
        AddStep(store, 3, "order zero", "open", order: 0);
        var steps = store.List(kind: "plan-step")
            .OrderBy(r => r["order"] is JsonValue v ? v.GetValue<double>() : double.MaxValue)
            .ThenBy(r => r["id"]!.GetValue<string>(), StringComparer.Ordinal).ToArray();
        Assert.Equal(["plan-wi-test-03", "plan-wi-test-02", "plan-wi-test-01"], steps.Select(r => r["id"]!.GetValue<string>()));
    }

    private sealed class PlanSyncServer : HttpMessageHandler
    {
        public List<JsonObject> Records = [];
        protected override void Dispose(bool disposing) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                var op = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
                Records.Add((JsonObject)op["record"]!.DeepClone());
                return Json(new JsonObject { ["operationId"] = op["operationId"]!.DeepClone(), ["committed"] = true,
                    ["revision"] = op["record"]!["revision"]!.DeepClone() }.ToJsonString());
            }
            if (request.RequestUri!.AbsolutePath == "/notebook") return Json("{\"owner\":\"seat\",\"generation\":1,\"protocolVersion\":1}");
            var rows = request.RequestUri.Query == "?after=" || request.RequestUri.Query.Length == 0
                ? Records.Select(r => r.DeepClone()).ToArray() : [];
            return Json(new JsonArray(rows).ToJsonString());
        }
        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    }
}
