// pi-sharp-checkpoints-3: green marker + last-green hint.
// The tracker sees every bash command exit (command text + exit code, wired through
// BashTool.CommandExitSink) and decides: does this command count as a "build" (one of the
// configured green commands), did it pass, and has the seat failed enough of them since the
// last green that a one-shot hint about rewinding to the last green is due?
namespace PiSharp.Core.Checkpoints;

/// <summary>
/// Decides when a checkpoint should be marked green and when the last-green hint is due.
/// Pure state: no git, no session; the caller performs the store marking via
/// <see cref="GreenRequested"/> and reads the hint from <see cref="DrainHint"/>.
/// Thread-safe by construction (one seat, one loop); no locks.
/// </summary>
public sealed class CheckpointGreenTracker
{
    /// <summary>
    /// The built-in green commands. A bash command counts as a build when its trimmed text
    /// starts with one of these (ordinal, case-sensitive) — so <c>dotnet build --no-restore</c>
    /// counts and <c>echo dotnet build</c> does not (the prefix must be followed by whitespace or
    /// end the command).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultGreenCommands = ["dotnet build", "dotnet test"];

    /// <summary>Failed green-command runs since the last green before the hint is due (default 3).</summary>
    public const int DefaultHintFailures = 3;

    /// <summary>Failed green-command runs since the last green before the hint is due.</summary>
    public int HintFailures { get; set; } = DefaultHintFailures;

    private readonly IReadOnlyList<string> _greenCommands;
    private int _failedBuilds;
    private int _batchesSinceGreen;
    private bool _hintArmed;
    private string? _pendingHint;

    /// <summary>
    /// Invoked when a green command succeeds; runs outside the tool's execution path so a slow mark
    /// (git) does not hold the shell's output pump. May block; it is called from the bash tool's
    /// command-exit sink.
    /// </summary>
    public Action<string>? MarkGreen { get; set; }

    /// <summary>Whether any green has been marked in this run.</summary>
    public bool HasGreen { get; private set; }

    /// <summary>The index of the latest green in this run, or null.</summary>
    public int? LastGreenIndex { get; private set; }

    /// <summary>The green index the hint was issued for, or null (so the caller can stay quiet after it).</summary>
    public int? HintedGreenIndex { get; private set; }

    public CheckpointGreenTracker(IReadOnlyList<string>? greenCommands = null)
    {
        _greenCommands = greenCommands is { Count: > 0 } ? greenCommands : DefaultGreenCommands;
    }

    /// <summary>True when <paramref name="command"/> is one of the configured green commands (prefix match).</summary>
    public bool IsGreenCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var trimmed = command.TrimStart();
        foreach (var green in _greenCommands)
        {
            if (trimmed.StartsWith(green, StringComparison.Ordinal))
            {
                // The prefix must be a word boundary: "dotnet build" matches "dotnet build -c Release"
                // and bare "dotnet build" but not "dotnet buildx" (longer, glued continuation).
                var rest = trimmed[green.Length..];
                if (rest.Length == 0 || rest[0] is ' ' or '\t' or '\r' or '\n') return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Feed one finished bash command. On a green-command success the caller is asked to mark the
    /// latest checkpoint green (<see cref="MarkGreen"/> with the latest checkpoint's commit); the
    /// failed-build counter resets and the hint disarms until the next green. On a green-command
    /// failure the counter grows; at <see cref="HintFailures"/> it becomes due.
    /// Non-green commands are ignored entirely.
    /// </summary>
    public void OnCommandExit(string command, int exitCode)
    {
        if (!IsGreenCommand(command)) return;
        if (exitCode == 0)
        {
            _failedBuilds = 0;
            _hintArmed = true;
            MarkGreen?.Invoke(command);
            return;
        }
        _failedBuilds++;
        if (HasGreen && _hintArmed && _failedBuilds >= HintFailures)
        {
            _hintArmed = false;
            if (LastGreenIndex is { } index)
            {
                _pendingHint =
                    $"[checkpoint] last green cp-{index} ({_batchesSinceGreen} batches ago): " +
                    $"rewind{{to:\"last-green\"}} available";
                HintedGreenIndex = index;
            }
        }
    }

    /// <summary>Called after every tool batch (not just green commands): counts distance from the last green.</summary>
    public void AfterBatch()
    {
        if (HasGreen) _batchesSinceGreen++;
    }

    /// <summary>Take the pending hint, if one is due; null when none. A hint is issued at most once per green.</summary>
    public string? DrainHint()
    {
        var hint = _pendingHint;
        _pendingHint = null;
        return hint;
    }

    /// <summary>
    /// The caller's confirmation that the mark landed: the 1-based green index that was recorded.
    /// Called from the <see cref="MarkGreen"/> action. When the mark fails (no checkpoint yet), the
    /// caller does not call this and the counter stays reset — the next green build retries.
    /// </summary>
    public void GreenMarked(int index)
    {
        HasGreen = true;
        LastGreenIndex = index;
        _batchesSinceGreen = 0;
        _hintArmed = true;
        _failedBuilds = 0;
        _pendingHint = null;
        HintedGreenIndex = null;
    }
}
