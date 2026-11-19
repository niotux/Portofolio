using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Verification;

namespace PiSharp.Core.Notebook;

/// <summary>Bounded, evidence-linked observations. No model call and no invented causal explanation.</summary>
public static class NotebookLessons
{
    public const int MaxLessonsPerTask = 3;

    public static string RepositoryScope(string cwd)
    {
        var root = Path.GetFullPath(cwd);
        for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git)) { root = git; break; }
            if (!File.Exists(git)) continue;
            var text = File.ReadAllText(git).Trim();
            if (text.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                var gitDir = Path.GetFullPath(Path.Combine(dir.FullName, text[7..].Trim()));
                var common = Path.Combine(gitDir, "commondir");
                root = File.Exists(common) ? Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(common).Trim())) : gitDir;
            }
            break;
        }
        if (OperatingSystem.IsWindows()) root = root.ToLowerInvariant();
        return "repo:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..24].ToLowerInvariant();
    }

    public static void FromVerification(NotebookStore store, VerificationReport report, string evidenceId)
    {
        if (report.Results.Count == 0) return;
        var taskNotes = store.List(task: report.Task, kind: "note").Where(r => r["id"]!.GetValue<string>().StartsWith("learning-", StringComparison.Ordinal)).ToArray();
        // Reserve the last slot for a recovery, even after many failed checks.
        if (taskNotes.Length >= (report.Passed ? MaxLessonsPerTask : MaxLessonsPerTask - 1)) return;
        _ = store.Get(evidenceId) ?? throw new ArgumentException("Verification evidence missing.");
        var key = "learning-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceId)))[..24].ToLowerInvariant();
        if (store.Get(key) is not null) return;
        var source = store.List(task: report.Task, kind: "source").LastOrDefault()
            ?? store.List(kind: "source").OrderByDescending(r => r["updatedAt"]!.GetValue<string>()).FirstOrDefault();
        var plan = store.List(task: report.Task, kind: "plan-step");
        var previous = store.List(task: report.Task, kind: "check").Where(r => r["id"]!.GetValue<string>() != evidenceId
            && r["id"]!.GetValue<string>().StartsWith(VerificationRecords.VerifyPrefix, StringComparison.Ordinal))
            .OrderByDescending(r => r["updatedAt"]!.GetValue<string>()).FirstOrDefault();
        var failedEvidence = previous is not null && JsonNode.Parse(previous["text"]!.GetValue<string>())?["passed"]?.GetValue<bool>() == false
            ? previous["id"]!.GetValue<string>() : null;
        var outcome = report.Passed ? failedEvidence is null ? "verified-success" : "verified-recovery" : "verification-failed";
        var lesson = new JsonObject
        {
            ["situation"] = NotebookStore.Lead(source?["text"]?.GetValue<string>() ?? report.Task, 240),
            ["approach"] = NotebookStore.Lead(string.Join("; ", plan.Select(r => r["text"]!.GetValue<string>())), 240),
            ["outcome"] = outcome,
            ["checks"] = new JsonArray(report.Results.Take(5).Select(r => (JsonNode?)JsonValue.Create(r.CheckId + ": " + (r.Passed ? "passed" : "failed"))).ToArray()),
            ["evidence"] = evidenceId,
            ["previousFailure"] = failedEvidence,
            ["scope"] = store.RecallScope,
            ["applicability"] = "Observed on this task and workspace; recheck before applying elsewhere."
        };
        store.Write(key, lesson.ToJsonString(), NotebookStore.Lead(lesson["situation"]!.GetValue<string>() + " / " + outcome, 120),
            tags: store.RecallScope is { } scope ? ["lesson", scope] : ["lesson"], kind: "note", task: report.Task,
            status: "completed", sourceId: evidenceId);
    }

    public static JsonObject Feedback(NotebookStore store, string id, string eventName, string task, string evidenceId)
    {
        if (eventName is not ("helpful" or "harmful")) throw new ArgumentException("feedback must be helpful or harmful.");
        var note = store.Get(id) ?? throw new ArgumentException("Memory record not found.");
        if (note["kind"]!.GetValue<string>() is not ("note" or "decision")) throw new ArgumentException("Feedback applies to notes and decisions.");
        var evidence = store.Get(evidenceId) ?? throw new ArgumentException("Verification evidence not found.");
        if (!evidenceId.StartsWith(VerificationRecords.VerifyPrefix, StringComparison.Ordinal)
            || evidence["kind"]!.GetValue<string>() != "check" || evidence["task"]!.GetValue<string>() != task)
            throw new ArgumentException("Feedback requires harness verification evidence for the current task.");
        var passed = JsonNode.Parse(evidence["text"]!.GetValue<string>())?["passed"]?.GetValue<bool>();
        if (passed != (eventName == "helpful")) throw new ArgumentException("Helpful feedback requires passing evidence; harmful feedback requires failing evidence.");
        if (!store.TryRecordUsage(id, eventName, task, evidenceId)) throw new IOException("Feedback could not be persisted; retry after resolving the usage-sidecar storage error.");
        return new JsonObject { ["id"] = id, ["feedback"] = eventName, ["evidence"] = evidenceId };
    }
}
