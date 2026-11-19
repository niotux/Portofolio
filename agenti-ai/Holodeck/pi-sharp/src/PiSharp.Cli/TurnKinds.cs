using PiSharp.Core.Agent;
using PiSharp.Core.Messages;

namespace PiSharp.Cli;

/// <summary>What a turn is about to do, for a local pair (<see cref="HybridAnswer.IsPair"/>): the fast side takes
/// <see cref="Watch"/> and <see cref="Routine"/>, the smart side everything else.</summary>
internal enum TurnKind
{
    /// <summary>Nothing known: the smart side.</summary>
    Unknown,
    /// <summary>The first turn of a run on a task.</summary>
    TaskStart,
    /// <summary>Reading the hub's digest without having acted yet.</summary>
    Watch,
    /// <summary>A short follow-up: the last turn's tools read little, changed no file and none failed.</summary>
    Routine,
    /// <summary>A follow-up to real work: a file changed, or the tools returned more than the follow-up limit.</summary>
    Work,
    /// <summary>A follow-up to a failed tool call or test.</summary>
    Failure,
}

internal static class TurnKinds
{
    /// <summary>The tools whose use means the seat is changing code: the next turn thinks about it on the smart side.</summary>
    public static readonly IReadOnlySet<string> ChangingTools = new HashSet<string>(StringComparer.Ordinal)
        { "write", "edit", "multi_edit", "apply_patch" };

    /// <summary>The kind of the turn after <paramref name="turn"/>: <see cref="TurnKind.Failure"/> when a tool failed,
    /// <see cref="TurnKind.Work"/> when a file changed or the results are larger than <paramref name="followupMaxTokens"/>
    /// (estimated at 4 characters a token), else <see cref="TurnKind.Routine"/>.</summary>
    public static TurnKind After(TurnContext turn, int followupMaxTokens)
    {
        if (turn.ToolResults.Any(r => r.IsError)) return TurnKind.Failure;
        if (turn.Message.ToolCalls.Any(c => ChangingTools.Contains(c.Name))) return TurnKind.Work;
        var chars = turn.ToolResults.Sum(r => r.Content.OfType<TextContent>().Sum(t => (long)t.Text.Length));
        return chars / 4 > followupMaxTokens ? TurnKind.Work : TurnKind.Routine;
    }

    public static string Name(TurnKind kind) => kind switch
    {
        TurnKind.TaskStart => "task-start",
        TurnKind.Watch => "watch",
        TurnKind.Routine => "routine",
        TurnKind.Work => "work",
        TurnKind.Failure => "after-failure",
        _ => "unknown",
    };
}
