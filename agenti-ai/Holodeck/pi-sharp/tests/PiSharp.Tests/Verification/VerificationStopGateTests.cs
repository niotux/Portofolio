using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using PiSharp.Tests.Agent;
using Xunit;

namespace PiSharp.Tests.Verification;

public sealed class VerificationStopGateTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pi-stop-gate-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public async Task FailedChecksRetryToCapAndReplaceFeedbackInsteadOfAccumulatingIt()
    {
        await new GitCommands(_root).InitAsync();
        await new GitCommands(_root).AddCommitAsync("seed");
        using var notebook = new NotebookStore(Path.Combine(_root, "notebook"), "seat");
        var verifier = new Verifier(_root, _ => [new DoneCheck("fail", DoneCheckKind.Command, "exit 1")]);
        (string Task, string Failure, int Attempts, int Max)? capped = null;
        var gate = new VerificationStopGate(verifier, notebook, () => "chat-1", 3,
            (task, failure, attempts, max, _) => { capped = (task, failure, attempts, max); return Task.CompletedTask; });
        var ctx = new AgentContext { SystemPrompt = "", Tools = [] };
        TurnContext Turn() => new(Fakes.Text("done"), [], ctx, []);

        Assert.False(await gate.ShouldStopAfterTurnAsync(Turn()));
        var prepare = gate.ComposePrepareNextTurn(null);
        var first = await prepare(Turn());
        var firstTrailing = first!.Trailing!;
        Assert.Single(firstTrailing);
        Assert.IsType<UserMessage>(firstTrailing[0]);
        ctx.Messages.AddRange(firstTrailing);

        Assert.False(await gate.ShouldStopAfterTurnAsync(Turn()));
        var second = await prepare(Turn());
        Assert.Empty(ctx.Messages.OfType<UserMessage>());
        var secondTrailing = second!.Trailing!;
        Assert.Single(secondTrailing);
        ctx.Messages.AddRange(secondTrailing);
        Assert.Single(ctx.Messages.OfType<UserMessage>());

        Assert.True(await gate.ShouldStopAfterTurnAsync(Turn()));
        Assert.NotNull(gate.LastFailure);
        Assert.Equal(("chat-1", gate.LastFailure!, 3, 3), capped);
        Assert.Equal(3, notebook.List().Count(r => r["id"]!.GetValue<string>().StartsWith("verify-chat-1-")));
    }
}
