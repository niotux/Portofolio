using System.Diagnostics;
using System.Text.Json.Nodes;
using PiSharp.Core.Notebook;
using PiSharp.Core.Verification;
using Xunit;

namespace PiSharp.Tests.Verification;

public sealed class VerifierTests : IDisposable
{
    private readonly string _root;
    private readonly string _cwd;

    public VerifierTests()
    {
        _root = Directory.CreateTempSubdirectory("pi-verify-").FullName;
        _cwd = Path.Combine(_root, "repo");
        Directory.CreateDirectory(_cwd);
        Directory.CreateDirectory(Path.Combine(_cwd, ".pi"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static DoneCheck Cmd(string id, string spec, int timeoutSeconds = 30, int expectedExit = 0, string[]? pinned = null)
        => new(id, DoneCheckKind.Command, spec, null, timeoutSeconds, expectedExit, null, null, pinned);

    [Fact]
    public async Task PassingCommandProducesRecordWithExitCodeTailAndTreeState()
    {
        await new GitCommands(_cwd).InitAsync();
        File.WriteAllText(Path.Combine(_cwd, "a.txt"), "hello\n");
        await new GitCommands(_cwd).AddCommitAsync("seed");

        var v = new Verifier(_cwd, _ => [Cmd("build-pass", "echo hello && exit 0")]);
        var report = await v.RunAllAsync("t1");

        Assert.True(report.Passed);
        var r = report.Results.Single();
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("hello", r.OutputTail);
        Assert.True(r.Head.Length >= 7, r.Head);
        Assert.Equal(r.Head, r.After["head"]!.GetValue<string>());
        Assert.True(r.PinnedUnchanged);
    }

    [Fact]
    public async Task FailingCommandFailsWithExitCodeAndTail()
    {
        await new GitCommands(_cwd).InitAsync();
        File.WriteAllText(Path.Combine(_cwd, "a.txt"), "x\n");
        await new GitCommands(_cwd).AddCommitAsync("seed");

        var v = new Verifier(_cwd, _ => [Cmd("build-fail", "echo boom-to-stderr 1>&2; echo boom-to-stdout; exit 4")]);
        var report = await v.RunAllAsync("t1");

        Assert.False(report.Passed);
        var r = report.Results.Single();
        Assert.Equal(4, r.ExitCode);
        Assert.False(r.Passed);
        Assert.Contains("boom-to-stderr", r.OutputTail);
        Assert.Contains("boom-to-stdout", r.OutputTail);
        Assert.Contains("exit 4 (expected 0)", r.OutputTail);
    }

    [Fact]
    public async Task PinnedFileEditedDuringRunFailsWithPinnedUnchangedFalse()
    {
        File.WriteAllText(Path.Combine(_cwd, "checker.sh"), "echo ok\n");
        // The check runs the pinned checker while a background step edits it mid-run; the re-hash must catch it.
        var check = new DoneCheck("edit-check", DoneCheckKind.Command,
            "bash checker.sh & pid=$!; sleep 0.3; printf 'echo evil' > checker.sh; wait $pid",
            null, 30, 0, null, null, ["checker.sh"]);

        var v = new Verifier(_cwd, _ => [check]);
        var r = await v.RunOneAsync(check);

        Assert.False(r.PinnedUnchanged);
        Assert.False(r.Passed);
        Assert.Contains("pinned path(s) changed", r.OutputTail);
        // The pinned file is what the edit wrote: the re-hash saw the change.
        Assert.Equal("echo evil", File.ReadAllText(Path.Combine(_cwd, "checker.sh")).Trim());
    }

    [Fact]
    public async Task TimeoutKillsTheWholeProcessGroup()
    {
        // A shell with a background sleep child: the timeout must kill both, not just the shell.
        var check = Cmd("long", "sleep 30 & sleep 100", 2);

        var v = new Verifier(_cwd, _ => [check]);
        var sw = Stopwatch.StartNew();
        var r = await v.RunOneAsync(check);
        sw.Stop();

        Assert.False(r.Passed);
        Assert.Contains("timed out after 2s", r.OutputTail);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), sw.Elapsed.ToString());
        Assert.Empty(SleepProcesses());
    }

    private static List<string> SleepProcesses()
    {
        var psi = new ProcessStartInfo("pgrep", "-f") { UseShellExecute = false, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit(2000);
        var text = p.StandardOutput.ReadToEnd();
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    [Fact]
    public async Task RecordsAreWrittenToTheNotebookAsCheckKind()
    {
        using var store = new NotebookStore(Path.Combine(_cwd, "nb"), "seat1");
        var v = new Verifier(_cwd, _ => [Cmd("pass", "exit 0"), Cmd("fail", "exit 7")]);
        var report = await v.RunAllAsync("t1");
        var id = VerificationRecords.Record(store, report);

        Assert.Equal("verify-t1-1", id);
        var rec = store.List().Single(r => r["id"]!.GetValue<string>() == id);
        Assert.Equal("check", rec["kind"]!.GetValue<string>());
        var json = JsonNode.Parse(rec["text"]!.GetValue<string>())!;
        Assert.Equal("t1", json["task"]!.GetValue<string>());
        Assert.False(json["passed"]!.GetValue<bool>());
        Assert.Equal(2, json["results"]!.AsArray().Count);
        var fail = json["results"]!.AsArray().Single(x => x! ["id"]!.GetValue<string>() == "fail")!;
        Assert.Equal(7, fail["exitCode"]!.GetValue<int>());

        // A second run appends, never overwrites.
        var id2 = VerificationRecords.Record(store, report);
        Assert.Equal("verify-t1-2", id2);
    }

    [Fact]
    public async Task ModelWritesToVerifyRecordsAreRefused()
    {
        using var store = new NotebookStore(Path.Combine(_cwd, "nb"), "seat1");
        var tool = new NotebookTool(store);
        var refused = await tool.ExecuteAsync("t", new JsonObject
        {
            ["op"] = "put", ["id"] = "verify-t1-1", ["text"] = "model-says-pass", ["expectedRevision"] = 0,
        }, null, default);
        Assert.True(refused.IsError);
        Assert.Contains("Runtime evidence records are read-only", refused.Text);

        var transition = await tool.ExecuteAsync("t", new JsonObject
        {
            ["op"] = "transition", ["id"] = "verify-t1-1", ["status"] = "completed",
        }, null, default);
        Assert.True(transition.IsError);
    }

    [Fact]
    public async Task GrepFileExistsAndJsonPathChecks()
    {
        File.WriteAllText(Path.Combine(_cwd, "code.cs"), "int main() { return 0; }");
        File.WriteAllText(Path.Combine(_cwd, "status.json"), "{\"build\":{\"ok\":true,\"count\":0}}");

        var v = new Verifier(_cwd, _ =>
        [
            new DoneCheck("grep", DoneCheckKind.Grep, "return 0::code.cs"),
            new DoneCheck("grep-miss", DoneCheckKind.Grep, "nope::code.cs"),
            new DoneCheck("file", DoneCheckKind.FileExists, "status.json"),
            new DoneCheck("file-miss", DoneCheckKind.FileExists, "nope.json"),
            new DoneCheck("jp", DoneCheckKind.JsonPath, "status.json::build.ok"),
            new DoneCheck("jp-falsy", DoneCheckKind.JsonPath, "status.json::build.count"),
        ]);
        var report = await v.RunAllAsync("t1");
        var by = report.Results.ToDictionary(r => r.CheckId);

        Assert.True(by["grep"].Passed);
        Assert.False(by["grep-miss"].Passed);
        Assert.True(by["file"].Passed);
        Assert.False(by["file-miss"].Passed);
        Assert.True(by["jp"].Passed);
        Assert.Contains("true", by["jp"].OutputTail);
        Assert.False(by["jp-falsy"].Passed);
    }

    [Fact]
    public async Task DoneCheckSourceRepoFileWinsOverSpecFence()
    {
        var spec = """
            ## Spec
            ```done-check
            {"id": "spec-check", "kind": "command", "spec": "exit 0"}
            ```
            """;
        var fromSpec = new DoneCheckSource(_cwd, task => spec).For("t1").Select(c => c.Id).ToArray();
        Assert.Equal(["spec-check"], fromSpec);

        File.WriteAllText(Path.Combine(_cwd, ".pi", "done-checks.json"),
            """{"task": "t1", "checks": [{"id": "file-check", "kind": "command", "spec": "exit 0"}]}""");
        var fromRepo = new DoneCheckSource(_cwd, task => spec).For("t1").Select(c => c.Id).ToArray();
        Assert.Equal(["file-check"], fromRepo);

        // A file that names another task falls back to the fence.
        File.WriteAllText(Path.Combine(_cwd, ".pi", "done-checks.json"),
            """{"task": "other", "checks": [{"id": "other-check", "kind": "command", "spec": "exit 0"}]}""");
        var fallback = new DoneCheckSource(_cwd, task => spec).For("t1").Select(c => c.Id).ToArray();
        Assert.Equal(["spec-check"], fallback);
    }

    [Fact]
    public async Task NoChecksDeclaredIsAFailWithAReason()
    {
        var v = new Verifier(_cwd, _ => []);
        var report = await v.RunAllAsync("t1");
        Assert.False(report.Passed);
        Assert.Contains("no done-checks", report.Reason!);
    }
}

internal sealed class GitCommands
{
    private readonly string _cwd;
    public GitCommands(string cwd) => _cwd = cwd;
    public Task InitAsync() => RunAsync("git", "init", "-q");
    public Task AddCommitAsync(string msg)
    {
        RunAsync("git", "init", "-q");
        RunAsync("git", "-c", "user.email=t@t", "-c", "user.name=t", "add", "-A");
        return RunAsync("git", "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-q", "-m", msg);
    }

    private Task RunAsync(string git, params string[] args)
    {
        var psi = new ProcessStartInfo(git) { WorkingDirectory = _cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return Task.CompletedTask;
    }
}
