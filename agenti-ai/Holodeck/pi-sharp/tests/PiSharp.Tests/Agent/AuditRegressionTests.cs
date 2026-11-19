using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Checkpoints;
using PiSharp.Core.Factory;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using PiSharp.Llm.Responses;
using PiSharp.Tools;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Agent;

public sealed class AuditRegressionTests
{
    [Fact]
    public async Task VerificationRefusalStartsRepairWithoutAnExternalWake()
    {
        using var ws = new TempWorkspace();
        using var store = new NotebookStore(ws.Dir("nb"), "audit");
        var verifier = new Verifier(ws.Path, _ => [new("file", DoneCheckKind.FileExists, "required.txt")]);
        var gate = new VerificationStopGate(verifier, store, () => "task", 3);
        var script = new ScriptedStream().Then(Fakes.Text("done"))
            .Then(() => { ws.File("required.txt", "fixed"); return Fakes.Text("repaired"); });
        var config = new AgentLoopConfig
        {
            Model = Fakes.Model,
            DecideTurn = async t => await gate.ShouldStopAfterTurnAsync(t) ? TurnDecision.Stop : TurnDecision.Continue,
            PrepareNextTurn = gate.ComposePrepareNextTurn(null),
        };
        await Fakes.Collect(AgentLoop.Run([new UserMessage("create the file")], new AgentContext(), config, script.Stream));
        Assert.Equal(2, script.Calls);
        Assert.Contains(script.Contexts[1].Messages.OfType<UserMessage>(), m => m.Text.Contains("Harness verification failed"));
        Assert.Null(gate.LastFailure);
    }

    [Fact]
    public async Task MissingFingerprintsNeverReuseAPassingCheck()
    {
        using var ws = new TempWorkspace();
        ws.File("required.txt", "present");
        using var store = new NotebookStore(ws.Dir("nb"), "audit");
        var verifier = new Verifier(ws.Path, _ => [new("file", DoneCheckKind.FileExists, "required.txt")]);
        VerificationRecords.Record(store, await verifier.RunAllAsync("task"));
        File.Delete(Path.Combine(ws.Path, "required.txt"));
        var gate = new VerificationStopGate(verifier, store, () => "task", 3);
        Assert.False(await gate.ShouldStopAfterTurnAsync(new(Fakes.Text("done"), [], new AgentContext(), [])));
    }

    [Fact]
    public void EvidenceIdentityChangesWithTheChecksAndTheirProvenance()
    {
        var check = new DoneCheck("c", DoneCheckKind.FileExists, "one");
        var source = new DoneCheckProvenance("approved", "1", new string('a', 64), true);
        var verifier = new Verifier(".", _ => [check], provenanceSource: _ => source);
        var original = verifier.EvidenceIdentityFor("task");
        check = check with { Spec = "two" };
        Assert.NotEqual(original, verifier.EvidenceIdentityFor("task"));
        var changed = verifier.EvidenceIdentityFor("task");
        source = source with { Revision = "2" };
        Assert.NotEqual(changed, verifier.EvidenceIdentityFor("task"));
    }

    [Theory]
    [InlineData("openai-codex", "B")]
    [InlineData("xai", "A")]
    public void SwitchingModelsReplaysTheTranscript(string provider, string model)
    {
        var origin = Fakes.Model with { Id = "A", Provider = "openai-codex" };
        var context = new LlmContext("", [new UserMessage("original instruction"),
            new AssistantMessage { Model = "A", Provider = "openai-codex", Content = [new ThinkingContent("", "{\"type\":\"compaction\",\"encrypted_content\":\"opaque\"}")] },
            new UserMessage("continue")], []);
        Assert.Contains("opaque", ResponsesRequest.Build(origin, context, new())["input"]!.ToJsonString());
        var input = ResponsesRequest.Build(origin with { Id = model, Provider = provider }, context, new())["input"]!.ToJsonString();
        Assert.Contains("original instruction", input);
        Assert.DoesNotContain("opaque", input);
    }

    [Fact]
    public async Task AlreadyCancelledFileWritesDoNotWaitForTheLock()
    {
        using var ws = new TempWorkspace();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(ws.Path, "file");
        var owner = FileMutationQueue.RunAsync(path, async () => { entered.SetResult(); await release.Task; return 1; });
        await entered.Task;
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WriteTool(ws.Path).ExecuteAsync("w",
                new JsonObject { ["path"] = path, ["content"] = "late write" }, null, cancel.Token).WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(File.Exists(path));
        }
        finally { release.TrySetResult(); await owner; }
    }

    [Fact]
    public async Task CancellingANoncooperativeToolHasABoundedReturn()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<AgentToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new DelegateTool { Execute = (_, _, _, _) => { entered.SetResult(); return release.Task; } };
        var stream = new ScriptedStream().Then(Fakes.ToolCalls(Fakes.Call("c", tool.Name)));
        using var cancel = new CancellationTokenSource();
        var run = Fakes.Collect(AgentLoop.Run([new UserMessage("run")], new AgentContext { Tools = [tool] },
            new AgentLoopConfig { Model = Fakes.Model, ToolCleanupTimeout = TimeSpan.FromMilliseconds(50) }, stream.Stream, cancel.Token));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); cancel.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
            Assert.True(run.IsCompleted);
        }
        finally { release.TrySetResult(AgentToolResult.FromText("late")); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateCancelledWorkBlocksNewMutationsUntilItActuallyStops(bool sameBatch)
    {
        var release = new TaskCompletionSource<AgentToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var tool = new DelegateTool { Execute = (_, _, _, _) => ++calls == 1 ? release.Task : Task.FromResult(AgentToolResult.FromText("new mutation")) };
        var context = new AgentContext { Tools = [tool] };
        var config = new AgentLoopConfig { Model = Fakes.Model, ToolCallTimeout = TimeSpan.FromMilliseconds(20), ToolCleanupTimeout = TimeSpan.FromMilliseconds(20) };
        async Task RunAsync(bool batch = false) => await Fakes.Collect(AgentLoop.Run([new UserMessage("mutate")], context, config,
            new ScriptedStream().Then(Fakes.ToolCalls(batch ? [Fakes.Call("first", tool.Name), Fakes.Call("second", tool.Name)] : [Fakes.Call("only", tool.Name)])).Then(Fakes.Text("done")).Stream));
        try
        {
            await RunAsync(sameBatch).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(context.PendingCleanup.HasUnfinished);
            await RunAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, calls);
            Assert.Contains(context.Messages.OfType<ToolResultMessage>(), m => m.IsError && m.Content.OfType<TextContent>().Any(t => t.Text.Contains("still stopping")));
            release.SetResult(AgentToolResult.FromText("late"));
            // Releasing the tool schedules its continuations; one yield does not prove they finished.
            using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (context.PendingCleanup.HasUnfinished) await Task.Delay(1, cleanupDeadline.Token);
            Assert.False(context.PendingCleanup.HasUnfinished);
            await RunAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, calls);
        }
        finally { release.TrySetResult(AgentToolResult.FromText("late")); }
    }

    [Fact]
    public async Task McpFramingPreservesUnicodeAtEveryReadSize()
    {
        const string text = "{\"value\":\"é漢😀é\"}";
        var bytes = Encoding.UTF8.GetBytes(text + "\n" + text);
        for (var size = 1; size <= bytes.Length; size++)
        {
            using var stream = new FragmentedStream(bytes, size);
            var reader = new BoundedLineReader(stream);
            Assert.Equal(text, await reader.ReadLineAsync(default));
            Assert.Equal(text, await reader.ReadLineAsync(default));
            Assert.Null(await reader.ReadLineAsync(default));
        }
    }

    [Fact]
    public void LinuxCaseDistinctWorkspacesHaveDistinctCheckpointIdentities()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.NotEqual(CheckpointStore.HashOf("/tmp/Repo"), CheckpointStore.HashOf("/tmp/repo"));
    }

    [Fact]
    public async Task CaseDistinctCheckoutsRestoreIndependently()
    {
        if (OperatingSystem.IsWindows()) return;
        using var ws = new TempWorkspace();
        var upper = ws.Dir("Repo"); var lower = ws.Dir("repo"); var checkpoints = ws.Dir("checkpoints");
        File.WriteAllText(Path.Combine(upper, "file"), "upper");
        File.WriteAllText(Path.Combine(lower, "file"), "lower");
        await new PiSharp.Tests.Verification.GitCommands(upper).AddCommitAsync("upper");
        await new PiSharp.Tests.Verification.GitCommands(lower).AddCommitAsync("lower");
        var a = new CheckpointStore(upper, checkpoints); var b = new CheckpointStore(lower, checkpoints);
        var sa = await a.CreateSnapshotAsync(); var sb = await b.CreateSnapshotAsync();
        Assert.True(sa.Created, sa.SkipReason); Assert.True(sb.Created, sb.SkipReason);
        File.WriteAllText(Path.Combine(upper, "file"), "changed upper");
        File.WriteAllText(Path.Combine(lower, "file"), "changed lower");
        Assert.True((await a.RestoreAsync(sa.Commit!, sa.Head!)).Applied);
        Assert.Equal("upper", File.ReadAllText(Path.Combine(upper, "file")));
        Assert.Equal("changed lower", File.ReadAllText(Path.Combine(lower, "file")));
        Assert.True((await b.RestoreAsync(sb.Commit!, sb.Head!)).Applied);
        Assert.Equal("lower", File.ReadAllText(Path.Combine(lower, "file")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"criterion\":\"read README\",\"result\":\"pass\",\"evidence\":\"read\"}]")]
    [InlineData("[{\"criterion\":\"tests pass\",\"result\":\"pass\",\"evidence\":\"run 1\"}]")]
    [InlineData("[{\"criterion\":\"tests pass\",\"result\":\"pass\",\"evidence\":\"run 1\"},{\"criterion\":\"tests pass\",\"result\":\"pass\",\"evidence\":\"run 1\"}]")]
    public void HandInsRequireExactCanonicalCoverage(string json) =>
        Assert.NotNull(HandInGate.Refusal("epic", "Acceptance: tests pass; installers boot", JsonNode.Parse(json)));

    [Fact]
    public void CriterionIdsRequireTheCurrentRevision()
    {
        var canonical = AcceptanceCriteria.Read(JsonNode.Parse("[{\"id\":\"tests\",\"text\":\"tests pass\"}]"), "");
        var supplied = JsonNode.Parse("[{\"criterion\":\"tests\",\"result\":\"pass\",\"evidence\":\"run 1\"}]");
        Assert.NotNull(HandInGate.Refusal("e", "", supplied, canonical));
        Assert.NotNull(HandInGate.Refusal("e", "", supplied, canonical, "old"));
        Assert.Null(HandInGate.Refusal("e", "", supplied, canonical, AcceptanceCriteria.Revision(canonical)));
    }

    [Fact]
    public async Task SessionPollReturnsOutputAlreadyBufferedBeforeTheCall()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var ws = new TempWorkspace();
        using var store = new NotebookStore(ws.Dir("nb"), "audit");
        await using var jobs = new JobTool(ws.Path, store);
        var started = await jobs.ExecuteAsync("s", new JsonObject { ["op"] = "start", ["command"] = "sleep 1.2; echo unread-marker; sleep 30" }, null, default);
        var id = JsonNode.Parse(started.Text)!["id"]!.GetValue<string>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var status = await jobs.ExecuteAsync("s", new JsonObject { ["op"] = "status", ["id"] = id }, null, deadline.Token);
            if (status.Text.Contains("liveOutput") && JsonNode.Parse(status.Text)!["liveOutput"]!.GetValue<string>().Contains("unread-marker")) break;
            await Task.Delay(20, deadline.Token);
        }
        var stdin = new WriteStdinTool(jobs);
        var poll = new JsonObject { ["session_id"] = id, ["yield_time_ms"] = 0 };
        Assert.Contains("unread-marker", (await stdin.ExecuteAsync("p", poll, null, default)).Text);
        Assert.Equal("", JsonNode.Parse((await stdin.ExecuteAsync("p", poll, null, default)).Text)!["output"]!.GetValue<string>());
    }

    [Fact]
    public void SmallOutputBudgetsKeepUndeliveredTextForTheNextPoll()
    {
        var buffer = new SessionOutput();
        buffer.Append("ab😀cdef");
        Assert.Equal("ab", buffer.Read(3));
        Assert.True(buffer.HasUnread);
        Assert.Equal("😀c", buffer.Read(3));
        Assert.Equal("def", buffer.Read(3));
        Assert.False(buffer.HasUnread);
    }

    [Fact]
    public void SessionRetentionReportsLostOutputAndDoesNotReplayDeliveredText()
    {
        var buffer = new SessionOutput(8);
        buffer.Append("1234567890");
        Assert.Equal("[session retention: 2 unread characters dropped]\n34567890", buffer.Read());
        Assert.Equal("", buffer.Read());
        buffer.Append("same\nsame\n");
        Assert.Contains("me\nsame\n", buffer.Read());
    }

    private sealed class FragmentedStream(byte[] bytes, int size) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, size)], cancellationToken);
    }
}
