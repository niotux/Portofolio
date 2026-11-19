using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Verification;

/// <summary>Runs the done-checks the spec declared and returns the results the harness can post or grade from.
/// The harness is the only executor: the model reads the summary, never the raw output beyond the trimmed tail.
/// Command checks run through the same bash machinery as the bash/job tools and are killed with their whole
/// process group on timeout.</summary>
public sealed class Verifier(string cwd, Func<string, IReadOnlyList<DoneCheck>>? checkSource = null,
    DoneCheckProvenance? checkProvenance = null,
    Func<string, DoneCheckProvenance?>? provenanceSource = null)
{
    public const int OutputTailLimit = 8000;
    public const int MaxChecks = 20;

    public string Cwd { get; } = cwd;

    public IReadOnlyList<DoneCheck> ChecksFor(string task) => checkSource?.Invoke(task) ?? [];

    /// <summary>Source-independent identity of the exact checks and approved provenance used by a run.</summary>
    public string EvidenceIdentityFor(string task)
    {
        var source = checkProvenance ?? provenanceSource?.Invoke(task);
        var manifest = new JsonObject
        {
            ["source"] = source is null ? null : new JsonObject { ["name"] = source.Source, ["revision"] = source.Revision,
                ["sha256"] = source.Sha256, ["approved"] = source.Approved },
            ["checks"] = new JsonArray([.. ChecksFor(task).Take(MaxChecks).Select(c => (JsonNode)new JsonObject
            {
                ["id"] = c.Id, ["kind"] = c.Kind.ToString(), ["spec"] = c.Spec, ["cwd"] = c.Cwd,
                ["timeout"] = c.TimeoutSeconds, ["exit"] = c.ExpectedExit, ["filter"] = c.Filter, ["state"] = c.DbState,
                ["pinned"] = new JsonArray([.. (c.PinnedPaths ?? []).Select(p => (JsonNode)JsonValue.Create(p))]),
            })]),
        };
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest.ToJsonString())));
    }

    /// <summary>Runs every check of the task. The workspace is captured before and after the whole set so the
    /// record carries the tree state both the checks ran against and the one they left.</summary>
    public async Task<VerificationReport> RunAllAsync(string task, CancellationToken ct = default)
    {
        var checks = ChecksFor(task).Take(MaxChecks).ToArray();
        var identity = EvidenceIdentityFor(task);
        var provenance = checkProvenance ?? provenanceSource?.Invoke(task);
        if (checks.Length == 0) return new VerificationReport(task, [], false, "no done-checks declared for this task", provenance);
        var before = await WorkspaceEvidence.CaptureAsync(Cwd, ct).ConfigureAwait(false);
        var results = new List<VerificationResult>(checks.Length);
        foreach (var check in checks)
        {
            ct.ThrowIfCancellationRequested();
            var one = await RunOneAsync(check, before, ct).ConfigureAwait(false);
            results.Add(one with { After = before });
        }
        var after = await WorkspaceEvidence.CaptureAsync(Cwd, ct).ConfigureAwait(false);
        results = [.. results.Select(r => r with { After = after })];
        var stable = identity == EvidenceIdentityFor(task);
        return new VerificationReport(task, results, stable && results.All(r => r.Passed), stable ? null : "Check definitions changed during verification", provenance)
            { CheckIdentity = identity };
    }

    public async Task<VerificationResult> RunOneAsync(DoneCheck check, JsonObject? before = null, CancellationToken ct = default)
    {
        before ??= await WorkspaceEvidence.CaptureAsync(Cwd, ct).ConfigureAwait(false);
        var baseDir = BaseDir(check);
        var pinBefore = PinHashes(check, baseDir);
        var (ok, exit, tail) = check.Kind switch
        {
            DoneCheckKind.Command => await CommandAsync(check, baseDir, ct),
            DoneCheckKind.FileExists => FileExists(baseDir, check),
            DoneCheckKind.Grep => Grep(baseDir, check),
            DoneCheckKind.JsonPath => JsonPath(baseDir, check),
            DoneCheckKind.KbState => KbState(check),
            _ => (false, null, $"unknown done-check kind '{check.Kind}'"),
        };
        var after = await WorkspaceEvidence.CaptureAsync(Cwd, ct).ConfigureAwait(false);
        var pinAfter = PinHashes(check, baseDir);
        var pinned = pinBefore.Length == 0
            ? true
            : pinBefore.Zip(pinAfter).All(p => p.First.Hash.AsSpan().SequenceEqual(p.Second.Hash));
        var passed = ok && pinned;
        if (!pinned) tail = (tail.Length > 0 ? tail + "\n" : "") + "pinned path(s) changed during the check (the checker was edited; the result is invalid)";
        return new VerificationResult(check.Id, passed, exit, Tail(tail), before, after, pinned,
            after["head"]?.GetValue<string>() ?? "", DateTimeOffset.UtcNow);
    }

    private string BaseDir(DoneCheck check)
    {
        if (check.Cwd is null) return Cwd;
        return Path.IsPathRooted(check.Cwd) ? check.Cwd : Path.GetFullPath(Path.Combine(Cwd, check.Cwd));
    }

    private (bool Ok, int? Exit, string Tail) FileExists(string baseDir, DoneCheck check)
    {
        var ok = File.Exists(Path.IsPathRooted(check.Spec) ? check.Spec : Path.GetFullPath(Path.Combine(baseDir, check.Spec)));
        return (ok, null, ok ? "" : $"file not found: {check.Spec}");
    }

    /// <summary>Spec: "regex::file" (one file) or "regex::path" (every file under the path). The file the check
    /// reads is the checker; pin it via PinnedPaths so the maker cannot edit it in.</summary>
    private (bool Ok, int? Exit, string Tail) Grep(string baseDir, DoneCheck check)
    {
        var (pattern, target) = SplitSpec(check);
        try
        {
            var files = EnumerateFiles(baseDir, target);
            var hits = files
                .SelectMany(f => { try { return Regex.Matches(File.ReadAllText(f), pattern).Select(_ => f); } catch { return []; } })
                .Distinct()
                .Take(50)
                .ToArray();
            var ok = hits.Length > 0;
            return (ok, null, ok ? string.Join('\n', hits) : $"no match for /{pattern}/ in {target}");
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return (false, null, $"grep probe failed: {e.Message}");
        }
    }

    /// <summary>Spec: "file::json.dotted.path" — a JSON file that, at the (dotted) path, holds a truthy value;
    /// the value is carried in the tail so the record says what was seen.</summary>
    private (bool Ok, int? Exit, string Tail) JsonPath(string baseDir, DoneCheck check)
    {
        var (file, expression) = SplitSpec(check);
        var full = Path.IsPathRooted(file) ? file : Path.GetFullPath(Path.Combine(baseDir, file));
        if (!File.Exists(full)) return (false, null, $"file not found: {file}");
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(full)); }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException)
        {
            return (false, null, $"invalid JSON in {file}: {e.Message}");
        }
        if (root is null) return (false, null, $"invalid JSON in {file}");
        var value = Navigate(root, expression);
        if (value is null) return (false, null, $"path '{expression}' not found in {file}");
        var ok = Truthy(value);
        return (ok, null, $"'{expression}' = {value.ToJsonString()}");
    }

    /// <summary>Spec: the expected declaration, e.g. "fresh". The spec declares the DB state and the check
    /// carries it (DoneCheck.DbState); the harness records the declaration — the state the counts are
    /// measured on — and passes when it matches.</summary>
    private static (bool Ok, int? Exit, string Tail) KbState(DoneCheck check)
    {
        var expected = check.Spec;
        var actual = check.DbState ?? "";
        var ok = actual.Length > 0 && (actual == expected || actual.StartsWith(expected + ':', StringComparison.Ordinal));
        return (ok, null, ok
            ? $"db state '{actual}' matches '{expected}'"
            : $"declared db state '{(actual.Length == 0 ? "(none)" : actual)}' does not match '{expected}'");
    }

    private async Task<(bool Ok, int? Exit, string Tail)> CommandAsync(DoneCheck check, string baseDir, CancellationToken ct)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(check.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                WorkingDirectory = baseDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add("pipefail");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(check.Spec);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("failed to start the check command");
            if (process.Id > 0)
            {
                try { Setpgid(process.Id, process.Id); } catch { /* raced with exec or already a leader */ }
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var done = process.WaitForExitAsync(linked.Token);
            var timedOut = false;
            try { await done.ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                timedOut = true;
                KillProcessTree(process);
                try { await done.ConfigureAwait(false); } catch { /* already gone */ }
            }
            string output;
            try { output = (await stdout) + (await stderr); }
            catch (Exception) when (timedOut) { output = ""; } // streams closed by the kill
            var exit = process.ExitCode;
            if (timedOut) return (false, exit, $"timed out after {check.TimeoutSeconds}s; process group killed");
            var ok = exit == check.ExpectedExit;
            var reason = ok ? "" : $"exit {exit} (expected {check.ExpectedExit})";
            return (ok, exit, reason + (output.Length > 0 ? (reason.Length > 0 ? "\n" : "") + output : ""));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return (false, null, $"command probe failed: {e.Message}");
        }
    }

    /// <summary>Sha256 over every pinned path that exists; a missing file hashes as deleted. Empty when nothing
    /// is pinned. The verifier re-measures after the run: the maker cannot edit the checker while it runs.</summary>
    private (string Path, byte[] Hash)[] PinHashes(DoneCheck check, string baseDir)
    {
        if (check.PinnedPaths is not { Length: > 0 }) return [];
        var result = new (string, byte[])[check.PinnedPaths.Length];
        for (var i = 0; i < check.PinnedPaths.Length; i++)
        {
            var relative = check.PinnedPaths[i];
            var full = Path.IsPathRooted(relative) ? relative : Path.GetFullPath(Path.Combine(baseDir, relative));
            result[i] = (relative, File.Exists(full) ? SHA256.HashData(File.ReadAllBytes(full)) : "deleted"u8.ToArray());
        }
        return result;
    }

    private static string[] EnumerateFiles(string baseDir, string target)
    {
        var dir = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(baseDir, target));
        if (File.Exists(dir)) return [dir];
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Take(1000).ToArray();
    }

    private static (string Left, string Right) SplitSpec(DoneCheck check)
    {
        var index = check.Spec.IndexOf("::", StringComparison.Ordinal);
        return index < 0
            ? (check.Spec, ".")
            : (check.Spec[..index].Trim().Trim('/'), check.Spec[(index + 2)..].Trim());
    }

    private static JsonNode? Navigate(JsonNode root, string dotted)
    {
        var node = root;
        foreach (var part in dotted.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (node is JsonObject obj && obj[part] is { } next) { node = next; continue; }
            if (node is JsonArray array && int.TryParse(part, out var i) && i >= 0 && i < array.Count) { node = array[i]; continue; }
            return null;
        }
        return node;
    }

    private static bool Truthy(JsonNode value) => value switch
    {
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<int>(out var n) => n != 0,
        JsonValue v when v.TryGetValue<double>(out var d) => d != 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        _ => true,
    };

    internal static string Tail(string text)
    {
        text = text.TrimEnd();
        return text.Length > OutputTailLimit ? text[^OutputTailLimit..] : text;
    }

    internal static void KillProcessTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        if (process.Id > 0)
        {
            try { Kill(-process.Id, 9); } catch { /* not a process group leader */ }
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int Kill(int pid, int sig);

    [DllImport("libc", SetLastError = true)]
    private static extern int Setpgid(int pid, int pgid);
}

/// <summary>The full outcome of one verification run: every result plus the aggregate the gate decides on.</summary>
public sealed record VerificationReport(string Task, IReadOnlyList<VerificationResult> Results, bool Passed, string? Reason,
    DoneCheckProvenance? CheckSource = null, BackwardReviewResult? BackwardReview = null,
    RubricReviewResult? RubricReview = null)
{
    public string? CheckIdentity { get; init; }
    /// <summary>The one-line-per-check block a Verification note carries (SHA + DB state + filter, per the
    /// evidence-hygiene rule).</summary>
    public string Summary()
    {
        var lines = Results.Select(r => r.SummaryLine()).ToArray();
        var head = Results.FirstOrDefault(r => r.After["head"]?.GetValue<string>() is { Length: > 0 } h)?.After["head"]!.GetValue<string>() ?? "";
        var filter = string.Join(", ", Results.Select(r => r.CheckId));
        return string.Join('\n', [.. lines, $"head {head[..Math.Min(7, head.Length)]} / filter {filter}"]);
    }
}
