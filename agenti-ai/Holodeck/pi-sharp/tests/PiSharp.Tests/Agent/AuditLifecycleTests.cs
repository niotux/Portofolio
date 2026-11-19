using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Processes;
using PiSharp.Tests.Tools;
using PiSharp.Tools;

namespace PiSharp.Tests.Agent;

public sealed class AuditLifecycleTests
{
    [Fact]
    public async Task HelperScopeChangesAndShutdownCancelOwnedWork()
    {
        using var ws = new TempWorkspace();
        var scope = "first";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async IAsyncEnumerable<AssistantMessageEvent> Held(ModelConfig m, LlmContext c, StreamOptions o, [EnumeratorCancellation] CancellationToken ct)
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { cancelled.TrySetResult(); }
            yield break;
        }
        var tool = new TaskTool(ws.Path, () => (Fakes.Model, Held), () => scope);
        var spawned = await tool.ExecuteAsync("s", new JsonObject { ["op"] = "spawn", ["role"] = "worker", ["task"] = "wait" }, null, default);
        Assert.False(spawned.IsError);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        scope = "second";
        tool.StopOutsideScope();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await tool.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => tool.ExecuteAsync("s", new JsonObject { ["role"] = "worker", ["task"] = "late" }, null, default));
    }

    [Fact]
    public async Task HelperUsageCarriesItsOriginalParentScope()
    {
        using var ws = new TempWorkspace();
        var script = new ScriptedStream().Then(Fakes.Text("done") with { Usage = new Usage { Input = 10, Output = 3 } });
        await using var tool = new TaskTool(ws.Path, () => (Fakes.Model, script.Stream), () => "parent-task");
        (string Scope, int Tokens)? recorded = null;
        tool.UsageRecorded = (_, scope, usage) => recorded = (scope, usage.Output);
        await tool.ExecuteAsync("s", new JsonObject { ["role"] = "triage", ["task"] = "summarize" }, null, default);
        Assert.Equal(("parent-task", 3), recorded);
    }

    [Fact]
    public async Task ProcessRunnerDrainsBothPipesAndEnforcesItsDeadline()
    {
        if (!OperatingSystem.IsLinux()) return;
        var start = new ProcessStartInfo("bash");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("for i in {1..10000}; do echo output; echo error >&2; done");
        var result = await ProcessRunner.RunAsync(start, TimeSpan.FromSeconds(10));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(10000, result.Output.Count(c => c == '\n'));
        Assert.Equal(10000, result.Error.Count(c => c == '\n'));
        var held = new ProcessStartInfo("sleep"); held.ArgumentList.Add("30");
        await Assert.ThrowsAsync<TimeoutException>(() => ProcessRunner.RunAsync(held, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void EveryScalarSettingHasAnExplicitMergePolicy()
    {
        // Reflection is restricted to tests; production remains compatible with Native AOT.
        foreach (var property in typeof(Settings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType);
            if (type != typeof(bool) && type != typeof(int) && type != typeof(long) && type != typeof(double)) continue;
            var low = new Settings(); var high = new Settings();
            object a = Convert.ChangeType(1, type); object b = Convert.ChangeType(2, type);
            if (type == typeof(bool)) { a = false; b = true; }
            property.SetValue(low, a); property.SetValue(high, b);
            Assert.Equal(b, property.GetValue(SettingsLoader.Merge(low, high)));
            if (property.Name != nameof(Settings.CheckpointsAutoOnTurnEnd))
                Assert.Equal(a, property.GetValue(SettingsLoader.Merge(low, new Settings())));
            else Assert.Null(property.GetValue(SettingsLoader.Merge(low, new Settings())));
        }
    }

    [Fact]
    public void LateHelperUsageStaysWithItsOriginalEpic()
    {
        using var ws = new TempWorkspace();
        using var store = new PiSharp.Core.Notebook.NotebookStore(ws.Dir("nb"), "audit");
        var ledger = new PiSharp.Core.Factory.EpicUsageLedger(new(store, 1000));
        ledger.Bind("task-a", ("epic-a", null));
        ledger.Bind("task-b", ("epic-b", null));
        ledger.Charge("task-b", 100);
        ledger.Charge("task-a", 50);
        Assert.Equal(50, JsonNode.Parse(store.Get("epic-budget-epic-a")!["text"]!.GetValue<string>())!["output"]!.GetValue<int>());
        Assert.Equal(100, JsonNode.Parse(store.Get("epic-budget-epic-b")!["text"]!.GetValue<string>())!["output"]!.GetValue<int>());
    }

    [Fact]
    public void InterleavedPipesPreserveTheirOwnDecoderState()
    {
        using var output = new OutputAccumulator();
        output.Append([0xc3], "");
        output.Append([0x78], "x");
        output.Append([0xa9], "é");
        output.Finish();
        Assert.Equal("xé", output.Snapshot().Content);
    }

    [Fact]
    public async Task McpDecoderHandlesAPendingEmojiBeforeAFullAsciiBuffer()
    {
        var text = new string('a', 8191) + "😀" + new string('b', 8192);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text + "\n"));
        Assert.Equal(text, await new PiSharp.Core.Mcp.BoundedLineReader(stream).ReadLineAsync(default));
    }

    [Fact]
    public void PendingBatchesPreserveOutboxOrderAndAcknowledgements()
    {
        using var ws = new TempWorkspace();
        using var store = new PiSharp.Core.Notebook.NotebookStore(ws.Dir("nb"), "audit");
        for (var i = 0; i < 150; i++) store.Write("note-" + i, "text");
        var first = store.Pending(100);
        Assert.Equal(100, first.Count);
        Assert.Equal("note-0", first[0]["record"]!["id"]!.GetValue<string>());
        foreach (var op in first) store.Acknowledge(op["operationId"]!.GetValue<string>());
        var rest = store.Pending(100);
        Assert.Equal(50, rest.Count);
        Assert.Equal("note-100", rest[0]["record"]!["id"]!.GetValue<string>());
    }
}
