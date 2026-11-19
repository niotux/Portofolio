using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using PiSharp.Tests.Agent;
using Xunit;

namespace PiSharp.Tests.Verification;

public sealed class RubricReviewerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pi-rubric-review-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static string NullDerefVerdict =>
        """
        {"overall_correctness":"partially_correct","findings":[{"priority":0,"confidence":0.9,"file":"src/Foo.cs","start_line":12,"end_line":14,"summary":"Null dereference on live path","evidence":"bar.Baz() called without null check"}],"reason":"Null dereference in changed code"}
        """;

    private static string CleanVerdict =>
        """
        {"overall_correctness":"correct","findings":[],"reason":"No issues found"}
        """;

    [Fact]
    public async Task SeededNullDerefProducesBlockingP0FindingAtRightFileAndLine()
    {
        var startSha = await InitGitWithNullDerefAsync();
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat", ContextWindow = 32768, MaxOutputTokens = 8192 };
        var reviewer = new RubricReviewer(_root, model, null, Stream(NullDerefVerdict));

        var result = await reviewer.ReviewAsync(startSha, "fix null handling", "chat-1");

        Assert.True(result.Ran);
        Assert.NotNull(result.Verdict);
        Assert.Equal("partially_correct", result.OverallCorrectness);
        var blocking = result.Verdict!.Blocking;
        Assert.Single(blocking);
        Assert.Equal(0, blocking[0].Priority);
        Assert.Equal("src/Foo.cs", blocking[0].File);
        Assert.Equal(12, blocking[0].StartLine);
        Assert.Equal(15, result.Tokens);
        Assert.Equal(1, result.Turns);
    }

    [Fact]
    public void VerdictReasonsCanContainLiteralBraces()
    {
        var verdict = RubricReviewer.ParseVerdict("prefix " + CleanVerdict.Replace("No issues found", "Checked closing } and opening { braces") + " suffix");
        Assert.NotNull(verdict);
        Assert.Equal("Checked closing } and opening { braces", verdict.Reason);
    }

    [Fact]
    public async Task CleanDiffProducesNoFindingsAndGateProceeds()
    {
        await InitGitWithCleanChangeAsync();
        using var notebook = new NotebookStore(Path.Combine(_root, "nb"), "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("pass", DoneCheckKind.Command, "true")]);
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat", ContextWindow = 32768, MaxOutputTokens = 8192 };
        var reviewer = new RubricReviewer(_root, model, null, Stream(CleanVerdict));
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 3, rubricReviewer: reviewer);
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        var turn = new TurnContext(Fakes.Text("done"), [], ctx, []);

        Assert.True(await gate.ShouldStopAfterTurnAsync(turn));
        var record = JsonNode.Parse(notebook.Get("verify-chat-1-1")!["text"]!.GetValue<string>())!;
        Assert.True(record["passed"]!.GetValue<bool>());
        Assert.NotNull(record["rubricReview"]);
        Assert.True(record["rubricReview"]!["ran"]!.GetValue<bool>());
        Assert.Equal("correct", record["rubricReview"]!["overallCorrectness"]!.GetValue<string>());
        Assert.Empty(record["rubricReview"]!["findings"]!.AsArray());
    }

    [Fact]
    public async Task FindingsPersistedInVerifyRecordWithBlockingFlag()
    {
        await InitGitWithNullDerefAsync();
        using var notebook = new NotebookStore(Path.Combine(_root, "nb"), "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("pass", DoneCheckKind.Command, "true")]);
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat", ContextWindow = 32768, MaxOutputTokens = 8192 };
        var reviewer = new RubricReviewer(_root, model, null, Stream(NullDerefVerdict));
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 3, rubricReviewer: reviewer);
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        var turn = new TurnContext(Fakes.Text("done"), [], ctx, []);

        Assert.False(await gate.ShouldStopAfterTurnAsync(turn));
        var record = JsonNode.Parse(notebook.Get("verify-chat-1-1")!["text"]!.GetValue<string>())!;
        Assert.True(record["passed"]!.GetValue<bool>());
        var findings = record["rubricReview"]!["findings"]!.AsArray();
        Assert.Single(findings);
        Assert.True(findings[0]!["blocking"]!.GetValue<bool>());
        Assert.Equal(0, findings[0]!["priority"]!.GetValue<int>());
        Assert.Equal("src/Foo.cs", findings[0]!["file"]!.GetValue<string>());
        Assert.Equal(12, findings[0]!["startLine"]!.GetValue<int>());
    }

    [Fact]
    public async Task P0FindingKeepsTurnAliveOnceThenSecondStopProceeds()
    {
        await InitGitWithNullDerefAsync();
        using var notebook = new NotebookStore(Path.Combine(_root, "nb"), "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("pass", DoneCheckKind.Command, "true")]);
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat", ContextWindow = 32768, MaxOutputTokens = 8192 };
        // The sub-run keeps returning the same P0 finding; the gate must block only once.
        var reviewer = new RubricReviewer(_root, model, null, Stream(NullDerefVerdict, NullDerefVerdict, NullDerefVerdict));
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 3, rubricReviewer: reviewer);
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        var turn = new TurnContext(Fakes.Text("done"), [], ctx, []);

        // First stop: blocked by the P0 finding.
        Assert.False(await gate.ShouldStopAfterTurnAsync(turn));
        var prepare = gate.ComposePrepareNextTurn(null);
        var feedback = await prepare(turn);
        Assert.Single(feedback!.Trailing!);
        Assert.IsType<UserMessage>(feedback.Trailing![0]);
        var feedbackText = ((UserMessage)feedback.Trailing![0]).Text;
        Assert.Contains("Rubric review found a blocking finding", feedbackText);

        // Second stop: the rubric block is consumed; the gate proceeds.
        Assert.True(await gate.ShouldStopAfterTurnAsync(turn));
    }

    [Fact]
    public async Task JsonParseFailureFallsBackToAdvisoryAndGateProceeds()
    {
        await InitGitWithNullDerefAsync();
        using var notebook = new NotebookStore(Path.Combine(_root, "nb"), "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("pass", DoneCheckKind.Command, "true")]);
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat", ContextWindow = 32768, MaxOutputTokens = 8192 };
        // The final answer is prose, not JSON.
        var reviewer = new RubricReviewer(_root, model, null, Stream("I looked at the diff and it seems fine to me."));
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 3, rubricReviewer: reviewer);
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        var turn = new TurnContext(Fakes.Text("done"), [], ctx, []);

        Assert.True(await gate.ShouldStopAfterTurnAsync(turn));
        var record = JsonNode.Parse(notebook.Get("verify-chat-1-1")!["text"]!.GetValue<string>())!;
        Assert.True(record["rubricReview"]!["ran"]!.GetValue<bool>());
        Assert.Contains("could not be parsed", record["rubricReview"]!["reason"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaxAttemptsStillBoundsTheLoop()
    {
        await InitGitWithNullDerefAsync();
        using var notebook = new NotebookStore(Path.Combine(_root, "nb"), "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("pass", DoneCheckKind.Command, "true")]);
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat", ContextWindow = 32768, MaxOutputTokens = 8192 };
        var reviewer = new RubricReviewer(_root, model, null, Stream(NullDerefVerdict, NullDerefVerdict));
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 2, rubricReviewer: reviewer);
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        var turn = new TurnContext(Fakes.Text("done"), [], ctx, []);

        // Check passes, rubric P0 blocks once (consumes the attempt). Second call: rubric skipped
        // (block consumed), report still passes, the gate must proceed even though maxAttempts=2 was
        // incremented by the rubric block.
        Assert.False(await gate.ShouldStopAfterTurnAsync(turn));
        Assert.True(await gate.ShouldStopAfterTurnAsync(turn));
    }

    private async Task<string> InitGitWithNullDerefAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "Foo.cs"), "class Foo { void M() { Bar? bar = null; bar.Baz(); } }\n");
        var start = await InitGitAsync();
        File.WriteAllText(Path.Combine(_root, "src", "Foo.cs"), "class Foo { void M() { Bar? bar = Get(); bar.Baz(); } }\n");
        Git("add", "-A");
        Git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "change");
        return start;
    }

    private async Task InitGitWithCleanChangeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "Ok.cs"), "class Ok { int Add(int a, int b) => a + b; }\n");
        await InitGitAsync();
        File.WriteAllText(Path.Combine(_root, "src", "Ok.cs"), "class Ok { int Add(int a, int b) => a + b; int Mul(int a, int b) => a * b; }\n");
        Git("add", "-A");
        Git("-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "change");
    }

    private async Task<string> InitGitAsync()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "user.name", "tests");
        Git("add", "-A");
        Git("commit", "-qm", "baseline");
        var start = GitText("rev-parse", "HEAD").Trim();
        Git("update-ref", "refs/remotes/origin/main", start);
        await Task.CompletedTask;
        return start;
    }

    private static StreamFunction Stream(params string[] replies)
    {
        var queue = new Queue<string>(replies);
        return (_, _, _, _) => Respond(queue);
    }

    private static IAsyncEnumerable<AssistantMessageEvent> Respond(Queue<string> replies)
    {
        return RespondAsync(replies);
    }

    private static async IAsyncEnumerable<AssistantMessageEvent> RespondAsync(Queue<string> replies)
    {
        await Task.Yield();
        var text = replies.Count > 0 ? replies.Dequeue() : "done";
        yield return new AssistantDone(StopReason.Stop, new AssistantMessage
        {
            Model = "reviewer",
            Content = [new TextContent(text)],
            Usage = new Usage(Input: 10, Output: 5, TotalTokens: 15),
        });
    }

    private void Git(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error}");
    }

    private string GitText(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {p.StandardError.ReadToEnd()}");
        return output;
    }
}
