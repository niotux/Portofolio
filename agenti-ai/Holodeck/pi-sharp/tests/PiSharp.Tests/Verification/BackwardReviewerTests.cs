using System.Diagnostics;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Verification;

public sealed class BackwardReviewerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pi-backward-review-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public async Task UsesDiffAloneThenComparesSpecAndReturnsAdvisoryMismatchWithTokenCount()
    {
        File.WriteAllText(Path.Combine(_root, "fix.cs"), "class Fix { int Add(int a, int b) => a + b; }\n");
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "user.name", "tests");
        Git("add", "fix.cs");
        Git("commit", "-qm", "baseline");
        var start = GitText("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(_root, "fix.cs"), "class Fix { int Add(int a, int b) => a - b; }\n");
        Git("add", "fix.cs");
        Git("commit", "-qm", "change");

        var requests = new List<LlmContext>();
        var replies = new Queue<(string Text, int Tokens)>([("The patch appears to change integer addition behavior.", 4), ("MISMATCH: the spec requests date parsing, not arithmetic changes.", 5)]);
        StreamFunction stream = (_, context, _, _) =>
        {
            requests.Add(context);
            return Respond(replies.Dequeue());
        };
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat" };
        var result = await new BackwardReviewer(_root, model, stream).ReviewAsync(start, "date parsing must accept ISO-8601 dates");

        Assert.True(result.Ran);
        Assert.True(result.Mismatch);
        Assert.Contains("mismatch warning", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(9, result.Tokens);
        Assert.Equal(2, requests.Count);
        Assert.Contains("a - b", Assert.IsType<UserMessage>(Assert.Single(requests[0].Messages)).Text);
        Assert.DoesNotContain("date parsing", Assert.IsType<UserMessage>(Assert.Single(requests[0].Messages)).Text);
        Assert.Contains("date parsing", Assert.IsType<UserMessage>(Assert.Single(requests[1].Messages)).Text);

        using var tmp = new TempWorkspace();
        using var notebook = new NotebookStore(tmp.Path, "seat");
        var recordId = VerificationRecords.Record(notebook,
            new VerificationReport("task-1", [], true, null, BackwardReview: result));
        var evidence = System.Text.Json.Nodes.JsonNode.Parse(notebook.Get(recordId)!["text"]!.GetValue<string>())!;
        Assert.Contains("mismatch warning", evidence["summary"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mismatch warning", evidence["backwardReview"]!["warning"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(9, evidence["backwardReview"]!["tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task NonSubscriptionModelMakesNoCalls()
    {
        var calls = 0;
        StreamFunction stream = (_, _, _, _) => CountCall();
        var model = new ModelConfig { Id = "local", BaseUrl = "http://localhost/v1" };
        var result = await new BackwardReviewer(_root, model, stream).ReviewAsync(new string('a', 40), "spec");
        Assert.False(result.Ran);
        Assert.Equal(0, calls);

        async IAsyncEnumerable<AssistantMessageEvent> CountCall()
        {
            calls++;
            await Task.Yield();
            yield break;
        }
    }

    [Fact]
    public async Task MismatchWarningDoesNotBlockPassingVerification()
    {
        File.WriteAllText(Path.Combine(_root, "fix.cs"), "class Fix { }\n");
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "tests@example.invalid");
        Git("config", "user.name", "tests");
        Git("add", "fix.cs");
        Git("commit", "-qm", "baseline");
        var start = GitText("rev-parse", "HEAD").Trim();
        Git("update-ref", "refs/remotes/origin/main", start);
        File.WriteAllText(Path.Combine(_root, "fix.cs"), "class Fix { int Add(int a, int b) => a - b; }\n");
        Git("add", "fix.cs");
        Git("commit", "-qm", "change");

        var replies = new Queue<(string Text, int Tokens)>([("It changes addition behavior.", 4), ("MISMATCH: task asked for parsing dates.", 5)]);
        StreamFunction stream = (_, _, _, _) => Respond(replies.Dequeue());
        var model = new ModelConfig { Id = "reviewer", BaseUrl = "http://localhost/v1", Connection = "subscription-seat" };
        var reviewer = new BackwardReviewer(_root, model, stream);
        using var tmp = new TempWorkspace();
        using var notebook = new NotebookStore(tmp.Path, "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("pass", DoneCheckKind.Command, "true")]);
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 3, backwardReviewer: reviewer);
        var context = new AgentContext { SystemPrompt = "", Tools = [] };
        var turn = new TurnContext(Fakes.Text("Implement date parsing"), [], context, []);

        Assert.True(await gate.ShouldStopAfterTurnAsync(turn));
        var record = System.Text.Json.Nodes.JsonNode.Parse(
            notebook.Get(VerificationRecords.IdPrefix("chat-1") + "1")!["text"]!.GetValue<string>())!;
        Assert.True(record["passed"]!.GetValue<bool>());
        Assert.True(record["backwardReview"]!["mismatch"]!.GetValue<bool>());
        Assert.Equal(9, record["backwardReview"]!["tokens"]!.GetValue<int>());
    }

    private static async IAsyncEnumerable<AssistantMessageEvent> Respond((string Text, int Tokens) reply)
    {
        await Task.Yield();
        yield return new AssistantDone(StopReason.Stop, new AssistantMessage
        {
            Model = "reviewer",
            Content = [new TextContent(reply.Text)],
            Usage = new Usage(Input: reply.Tokens - 1, Output: 1, TotalTokens: reply.Tokens),
        });
    }

    private void Git(params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardError = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {error}");
    }

    private string GitText(params string[] args)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
        return output;
    }
}
