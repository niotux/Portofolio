// plan-pi-sharp-codex item 17 (Codex /review): a read-only review of a branch against its merge base with main, before the
// epic is handed in. It runs the rubric review sub-run (the adapted Codex rubric, read-only tools) and returns the findings
// by priority; with an epic named, they are also written on it as a note, so the product owner reads them with the proof.
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Models;
using PiSharp.Core.Verification;

namespace PiSharp.Tools;

public sealed class ReviewTool(
    string cwd,
    Func<(ModelConfig Model, StreamFunction Stream)> runtime,
    Func<string> taskKey,
    Func<string, string, string, CancellationToken, Task<bool>>? writeNote = null) : IAgentTool
{
    private static readonly JsonObject ArgsSchema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "The repository checkout to review (default: the working directory)" },
            "base": { "type": "string", "description": "What the branch is reviewed against (default origin/main; origin/master is tried too)" },
            "epic": { "type": "string", "description": "The epic's slug: the findings are also written on it as a note" },
            "focus": { "type": "string", "description": "What the change is meant to do (the epic's outcome), so the review checks it" }
          }
        }
        """)!.AsObject();

    public const string ToolName = "review";
    public string Name => ToolName;
    public string Label => ToolName;
    public string Description =>
        "Review your branch before you hand the epic in (Codex /review): a read-only reviewer reads the diff against the merge " +
        "base with main and returns findings by priority (P0 must fix .. P3 nit), with file and line. Fix the P0/P1 findings, or " +
        "say in your hand-in why they do not apply. With epic=<slug> the findings are also written on the epic.";
    public JsonObject Parameters => ArgsSchema;
    public ToolExecutionMode? ExecutionMode => ToolExecutionMode.Sequential;
    public TimeSpan? MaxDuration => TimeSpan.FromMinutes(15);

    public async Task<AgentToolResult> ExecuteAsync(string toolCallId, JsonObject arguments, Action<AgentToolResult>? onUpdate,
        CancellationToken cancellationToken)
    {
        var path = arguments["path"]?.GetValue<string>() is { Length: > 0 } p ? Path.GetFullPath(Path.Combine(cwd, p)) : cwd;
        if (!Directory.Exists(path)) return AgentToolResult.Error($"review: {path} does not exist.");
        var bases = arguments["base"]?.GetValue<string>() is { Length: > 0 } b ? [b] : new[] { "origin/main", "origin/master" };
        string? start = null;
        foreach (var candidate in bases)
            if ((await GitAsync(path, ["merge-base", "HEAD", candidate], cancellationToken).ConfigureAwait(false)) is (0, var sha) && sha.Trim().Length == 40)
            { start = sha.Trim(); break; }
        if (start is null) return AgentToolResult.Error($"review: no merge base between HEAD and {string.Join(" or ", bases)} in {path} (fetch first?).");
        var (model, stream) = runtime();
        var reviewer = new RubricReviewer(path, model, null, stream,
            [new ReadTool(path, supportsImages: false) { WarningSink = _ => { } }, new GrepTool(path), new BashTool(path)]);
        var focus = arguments["focus"]?.GetValue<string>() ?? "Review the branch's change for correctness, regressions and missing tests.";
        var result = await reviewer.ReviewAsync(start, focus, taskKey(), cancellationToken).ConfigureAwait(false);
        if (!result.Ran) return AgentToolResult.Error("review did not run: " + result.Reason);
        var head = (await GitAsync(path, ["rev-parse", "--short=12", "HEAD"], cancellationToken).ConfigureAwait(false)).Output.Trim();
        var text = Format(result, start[..12], head);
        if (arguments["epic"]?.GetValue<string>() is { Length: > 0 } epic && writeNote is not null)
        {
            var written = await writeNote(epic, $"Review of {head} against {start[..12]}", text, cancellationToken).ConfigureAwait(false);
            text += written ? $"\n(written on {epic} as a note)" : $"\n(could not write the note on {epic})";
        }
        return AgentToolResult.FromText(text, new JsonObject { ["findings"] = result.Findings.Count, ["blocking"] = result.Verdict?.Blocking.Count ?? 0 });
    }

    /// <summary>The findings by priority, most severe first, with what the reviewer concluded overall.</summary>
    public static string Format(RubricReviewResult result, string start, string head)
    {
        var sb = new StringBuilder($"Review of {head} against {start}: {result.OverallCorrectness ?? "no verdict"}" +
            (result.Verdict?.Reason is { Length: > 0 } reason ? $" - {reason}" : "") + "\n");
        var findings = result.Findings.OrderBy(f => f.Priority).ThenByDescending(f => f.Confidence).ToList();
        if (findings.Count == 0) sb.Append("No findings.\n");
        foreach (var f in findings)
            sb.Append($"- P{f.Priority} ({f.Confidence:0.##}) {f.File}:{f.StartLine}: {f.Summary}\n");
        if (result.Verdict?.Blocking.Count is > 0 and var blocking)
            sb.Append($"{blocking} finding(s) at P0/P1: fix them, or say in your hand-in why they do not apply.\n");
        return sb.ToString().TrimEnd();
    }

    private static async Task<(int ExitCode, string Output)> GitAsync(string dir, IReadOnlyList<string> args, CancellationToken ct)
    {
        var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-C"); info.ArgumentList.Add(dir);
        foreach (var a in args) info.ArgumentList.Add(a);
        var result = await PiSharp.Core.Processes.ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return (result.ExitCode, result.Output);
    }
}
