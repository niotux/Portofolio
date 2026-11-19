using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PiSharp.CodeIntel;
using PiSharp.CodeIntel.Auth;
using PiSharp.CodeIntel.Mcp;
using PiSharp.CodeIntel.Query;

namespace PiSharp.Tests.CodeIntel;

// Regression tests for the async attach lifecycle (pi-sharp-codeintel-mcp-async-attach-lifecycle):
// a cold attach commits the workspace and answers promptly without awaiting LS-ready (so the
// 60s client call bound no longer 499s a cold code_attach); a repeat attach is idempotent; a
// query while the LS is still warming returns a retryable warming result; a dead LS faults the
// workspace and a repeat attach restarts it. The post-commit warm-up is owned by the
// workspace/session lifecycle, not the HTTP request token: a client abort after the fast
// attach does NOT tear down the committed slot, but a workspace detach does.
public sealed class AsyncAttachLifecycleTests : IDisposable
{
    private static readonly CodeIntelActor Actor = new("seat-test", "Test seat", [], null);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ci2-async-{Guid.NewGuid():N}");
    private readonly string _slowDll;
    private readonly string _warmingDll;
    private readonly List<Process> _processes = new();

    public AsyncAttachLifecycleTests()
    {
        Directory.CreateDirectory(_root);
        _slowDll = BuildSlowServer();
        _warmingDll = BuildWarmingServer();
    }

    public void Dispose()
    {
        foreach (var p in _processes)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); p.WaitForExit(5000); p.Dispose(); }
            catch { }
        }
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private Process StartSlowServer(string kind)
    {
        var logFile = Path.Combine(_root, $"slow-{Guid.NewGuid():N}.log");
        File.Create(logFile).Dispose();
        var p = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { _slowDll, logFile },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        _processes.Add(p);
        return p;
    }

    private static WorkspaceManager MakeManager(string workspaceRoot, int maxLive, Func<string, Process> start)
        => new(
            Options.Create(new CodeIntelOptions
            {
                WorkspaceRoot = workspaceRoot,
                LsKind = "fake",
                ReadyTimeout = TimeSpan.FromSeconds(15),
            }),
            Options.Create(new CodeIntelServiceOptions
            {
                MaxLive = maxLive,
                IdleTtl = TimeSpan.FromMinutes(15),
                StartProcess = start,
            }),
            NullLogger<WorkspaceManager>.Instance);

    private static McpEndpoint MakeEndpoint(WorkspaceManager wm)
        => new(
            wm, new CodeIntelligence(wm),
            Options.Create(new CodeIntelOptions { LsKind = "fake", ReadyTimeout = TimeSpan.FromSeconds(15) }),
            NullLogger<McpEndpoint>.Instance);

    private static string AttachCall(int id, string repo) =>
        "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"tools/call\",\"params\":{\"name\":\"code_attach\",\"arguments\":{\"repo\":\"" + repo + "\",\"base_sha\":\"main\"}}}";

    private static async Task<(McpEndpoint Endpoint, string SessionId)> MakeSession(WorkspaceManager wm)
    {
        var ep = MakeEndpoint(wm);
        var init = await ep.HandlePostAsync(null, Actor, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""", CancellationToken.None);
        var sid = init.SessionId ?? throw new InvalidOperationException("initialize did not mint a session");
        return (ep, sid);
    }

    private string MakeGitRepo(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), "using System;\npublic class Program\n{\n}\n");
        void Git(params string[] args)
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(60_000)) throw new InvalidOperationException("git timed out");
            if (p.ExitCode != 0) throw new InvalidOperationException("git failed: " + err);
        }
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "async test");
        Git("add", "Program.cs");
        Git("commit", "-q", "-m", "initial");
        return dir;
    }

    [Fact]
    public async Task ColdAttach_AnswersPromptly_WithoutAwaitingLsReady_AndCommitsWorkspace()
    {
        var repo = MakeGitRepo("repo1");
        await using var wm = MakeManager(Path.Combine(_root, "ws1"), 3, StartSlowServer);
        var (ep, sid) = await MakeSession(wm);

        var stopwatch = Stopwatch.StartNew();
        var resp = await ep.HandlePostAsync(sid, Actor, AttachCall(2, repo), CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(200, resp.Status);
        var parsed = ParseToolResult(resp.Body);
        Assert.True(parsed!["attached"]!.GetValue<bool>());
        Assert.False(parsed["ls_ready"]!.GetValue<bool>(), "slow LS must not report ready yet");
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), "cold attach must answer well inside the 60s client call bound (no 499)");

        // The workspace is committed even though the LS is not ready: the slot counts and is
        // findable, so a repeat attach is idempotent and a query gate can bind to the warm-up.
        Assert.Equal(1, wm.LiveCount);
    }

    [Fact]
    public async Task RepeatAttach_IsIdempotent_ReusesSlotAndWorkspace()
    {
        var repo = MakeGitRepo("repo2");
        await using var wm = MakeManager(Path.Combine(_root, "ws2"), 3, StartSlowServer);
        var (ep, sid) = await MakeSession(wm);

        var attachJson = AttachCall(3, repo);
        var first = await ep.HandlePostAsync(sid, Actor, attachJson, CancellationToken.None);
        var second = await ep.HandlePostAsync(sid, Actor, attachJson, CancellationToken.None);

        var parsed1 = ParseToolResult(first.Body);
        var parsed2 = ParseToolResult(second.Body);
        Assert.Equal(parsed1!["workspace"]!.GetValue<string>(), parsed2!["workspace"]!.GetValue<string>());
        Assert.True(wm.LiveCount == 1, "a repeat attach must not start a second LS");
        var live = wm.EnumerateSeat(Actor.Slug).First();
        Assert.False(live.Ls.IsFaulted);
    }

    [Fact]
    public async Task Query_WhileWarming_ReturnsRetryableWarmingNode_BoundedByPollBudget()
    {
        var repo = MakeGitRepo("repo3");
        await using var wm = MakeManager(Path.Combine(_root, "ws3"), 3, StartWarmingServer);
        var (ep, sid) = await MakeSession(wm);

        await ep.HandlePostAsync(sid, Actor, AttachCall(4, repo), CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        var resp = await ep.HandlePostAsync(sid, Actor, """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"code_definition","arguments":{"path":"Program.cs","line":1,"character":1}}}""", CancellationToken.None);
        stopwatch.Stop();

        Assert.True(resp.Status == 200, "a warming query is a retryable result, not an error");
        var parsed = ParseToolResult(resp.Body);
        Assert.True(parsed!["warming"]!.GetValue<bool>());
        Assert.False(parsed["ls_ready"]!.GetValue<bool>());
        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(6));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6), "the warming answer is bounded by WarmingPollBudget (~5s), never the client's 60s bound");
    }

    [Fact]
    public async Task FaultedLs_ReportsFaulted_RepeatAttach_RestartsFreshLs()
    {
        var repo = MakeGitRepo("repo4");
        await using var wm = MakeManager(Path.Combine(_root, "ws4"), 3, StartDeadServer);
        var (ep, sid) = await MakeSession(wm);

        var attachJson = AttachCall(6, repo);
        await ep.HandlePostAsync(sid, Actor, attachJson, CancellationToken.None);

        // Wait for the dead LS to exit (IsFaulted requires HasExited && !IsReady;
        // the fake shell exits ~10s after answering initialize).
        for (var i = 0; i < 200 && !wm.EnumerateSeat(Actor.Slug).First().Ls.IsFaulted; i++)
            await Task.Delay(50);
        var live = wm.EnumerateSeat(Actor.Slug).First();
        Assert.True(wm.IsWorkspaceFaulted(live));

        // A query on the faulted workspace answers faulted instead of waiting.
        var q = await ep.HandlePostAsync(sid, Actor, """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"code_hover","arguments":{"path":"Program.cs","line":1,"character":1}}}""", CancellationToken.None);
        Assert.Equal(200, q.Status);
        var parsed = ParseToolResult(q.Body);
        Assert.True(parsed!["faulted"]!.GetValue<bool>());
        Assert.False(parsed["warming"]!.GetValue<bool>());

        // Repeat attach restarts the LS in the same slot: still one live, fresh (non-faulted) LS.
        var again = await ep.HandlePostAsync(sid, Actor, attachJson, CancellationToken.None);
        var parsed2 = ParseToolResult(again.Body);
        Assert.True(parsed2!["attached"]!.GetValue<bool>());
        Assert.True(wm.LiveCount == 1, "restart reuses the slot, no second slot");
        var live2 = wm.EnumerateSeat(Actor.Slug).First();
        Assert.False(wm.IsWorkspaceFaulted(live2));
        Assert.NotSame(live, live2);
    }

    // Regression (maker merge-blocker 429515 / PO 429610): the accepted ADR requires
    // post-commit warm-up to be owned by a true workspace-lifetime CTS (LiveWorkspace.WorkspaceCts),
    // NOT the HTTP request token. The acceptance path: a client abort after the fast attach must
    // NOT destroy the committed workspace; the warm-up persists, the LS becomes ready, and
    // code_definition returns results. This test goes through HandlePostAsync (the real HTTP
    // path) so the request token is the one that gets cancelled.
    [Fact]
    public async Task RequestCancel_ThenReady_ThenCodeDefinition_ReturnsResults()
    {
        var repo = MakeGitRepo("repo7");
        await using var wm = MakeManager(Path.Combine(_root, "ws7"), 3, StartSlowServer);
        var (ep, sid) = await MakeSession(wm);

        // Step 1: code_attach via HandlePostAsync with a real request CTS.
        using var attachCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var attachResp = await ep.HandlePostAsync(sid, Actor, AttachCall(1, repo), attachCts.Token);
        Assert.Equal(200, attachResp.Status);
        // Simulate a client abort AFTER the HTTP response returned: cancel the request token.
        // This must NOT tear down the committed workspace.
        attachCts.Cancel();
        await Task.Delay(200); // give the cancellation time to propagate

        // Step 2: the workspace is still committed (the slot survives the request cancel).
        Assert.True(wm.LiveCount == 1, "a post-commit request cancellation must not free the committed slot");
        // The workspace-lifetime CTS is NOT cancelled (only the request token was).
        var live = wm.EnumerateSeat(Actor.Slug).First();
        Assert.False(live.WorkspaceCts.IsCancellationRequested, "WorkspaceCts must not be cancelled by a request abort");

        // Step 3: wait for the LS to become ready (slow server: ~4s).
        var readyTimeout = TimeSpan.FromSeconds(15);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!live.Ls.IsReady)
        {
            if (sw.Elapsed > readyTimeout) throw new TimeoutException("LS did not become ready");
            await Task.Delay(200);
        }
        Assert.True(live.Ls.IsReady, "LS must become ready after the warm-up completes");

        // Step 4: code_definition via a NEW HandlePostAsync request (fresh token, same session).
        var defCall = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"code_definition\",\"arguments\":{\"path\":\"Program.cs\",\"line\":1,\"character\":14}}}";
        var defResp = await ep.HandlePostAsync(sid, Actor, defCall, CancellationToken.None);
        Assert.Equal(200, defResp.Status);
        Assert.True(defResp.Body.Contains("warming") == false, "LS is ready: no warming gate");
        // The code_definition result is wrapped in MCP content[0].text as a JSON string:
        // {"count":1,"limit":5,"truncated":false,"locations":[...]}.
        // Parse the outer response and check the inner JSON.
        var rpc = JsonNode.Parse(defResp.Body)!.AsObject();
        var result = rpc["result"]!.AsObject();
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        var inner = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(1, inner["count"]!.GetValue<int>());
        Assert.True(inner["locations"]! is JsonArray arr && arr.Count == 1);

        // Step 5: workspace detach frees the slot (workspace lifetime, not request).
        await wm.DetachAsync(live.Key, removeWorktree: true, CancellationToken.None);
        Assert.True(wm.LiveCount == 0, "a workspace detach frees the committed slot");
        Assert.True(live.WorkspaceCts.IsCancellationRequested, "DetachAsync cancels WorkspaceCts");
    }

    // A workspace detach during warm-up tears down the committed slot (WorkspaceCts.Cancel).
    [Fact]
    public async Task WorkspaceDetach_TearsDownCommittedSlot_DuringWarmUp()
    {
        var repo = MakeGitRepo("repo5b");
        await using var wm = MakeManager(Path.Combine(_root, "ws5b"), 3, StartSlowServer);

        var live = await wm.AttachAsync(Actor.Slug, repo, "main", null, CancellationToken.None);
        Assert.True(wm.LiveCount == 1, "attach commits the slot before the warm-up resolves");

        // Start the warm-up (bound to WorkspaceCts).
        live.LsWarmUp = wm.RunWarmUpAsync(live);

        // Detach the workspace: this cancels WorkspaceCts, which tears down the slot.
        await wm.DetachAsync(live.Key, removeWorktree: true, CancellationToken.None);
        Assert.True(wm.LiveCount == 0, "a workspace detach frees the committed slot");
        Assert.True(live.WorkspaceCts.IsCancellationRequested, "DetachAsync cancels WorkspaceCts");

        // The warm-up task should fault or complete as cancelled (the workspace is gone).
        try { await live.LsWarmUp; }
        catch (OperationCanceledException) { /* expected: workspace cancellation */ }
        catch (Exception) { /* LS process may also throw; either way the slot is freed */ }
    }

    [Fact]
    public async Task WarmingSlots_CountAgainstCap_ThirdAttachIs429()
    {
        var repoA = MakeGitRepo("repo6a");
        var repoB = MakeGitRepo("repo6b");
        var repoC = MakeGitRepo("repo6c");
        await using var wm = MakeManager(Path.Combine(_root, "ws6"), 2, StartWarmingServer);

        var a = await wm.AttachAsync("seatA", repoA, "main", null, CancellationToken.None);
        var b = await wm.AttachAsync("seatB", repoB, "main", null, CancellationToken.None);
        // Both are warming (slow LS) but already committed: they count against the two-LSP cap.
        Assert.False(a.Ls.IsReady);
        Assert.False(b.Ls.IsReady);
        Assert.Equal(2, wm.LiveCount);

        CodeIntelException ex = await Assert.ThrowsAsync<CodeIntelException>(
            () => wm.AttachAsync("seatC", repoC, "main", null, CancellationToken.None));
        Assert.Equal(429, ex.HttpStatus);
        Assert.True(ex.RetryAfterSeconds > 0);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static JsonNode ParseToolResult(string body)
    {
        var root = JsonNode.Parse(body)!;
        var content = (JsonArray)root!["result"]!["content"]!;
        var text = (JsonNode)content[0]!["text"]!;
        return JsonNode.Parse(text!.GetValue<string>())!;
    }

    private Process StartDeadServer(string kind)
    {
        // Answers initialize with a valid 55-byte Content-Length frame (53-byte JSON + 2-byte
        // trailing CRLF), then exits after 10s: the warm-up faults (LS died before ready)
        // instead of timing out. The shell is alive for the full initialize round-trip.
        var p = Process.Start(new ProcessStartInfo("sh", "-c \"printf 'Content-Length: 55\\r\\n\\r\\n{\\\"jsonrpc\\\":\\\"2.0\\\",\\\"id\\\":1,\\\"result\\\":{\\\"capabilities\\\":{}}}\\r\\n'; sleep 10\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        _processes.Add(p);
        return p;

    }

    // A server that answers initialize but NEVER sends $/progress: the LS stays warming
    // forever, so GateAsync always returns the warming node. Used by the warming-gate tests
    // that need the LS to be in the warming state for the entire test duration.
    private const string WarmingServerSource = """
        using System.Text;
        using System.Text.Json.Nodes;

        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        while (true)
        {
            var length = ReadContentLength(input);
            if (length < 0) break;
            var body = new byte[length];
            var read = 0;
            while (read < body.Length)
            {
                var count = input.Read(body, read, body.Length - read);
                if (count == 0) return;
                read += count;
            }

            var message = JsonNode.Parse(Encoding.UTF8.GetString(body))!.AsObject();
            if (message["method"]?.GetValue<string>() == "initialize")
            {
                Send(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = message["id"]?.DeepClone(),
                    ["result"] = new JsonObject { ["capabilities"] = new JsonObject() },
                });
            }
            // No $/progress: the LS stays warming forever.
        }

        static int ReadContentLength(Stream input)
        {
            var contentLength = -1;
            var line = new StringBuilder();
            while (true)
            {
                var value = input.ReadByte();
                if (value < 0) return line.Length == 0 ? -1 : throw new EndOfStreamException();
                if (value != '\r') { line.Append((char)value); continue; }
                if (input.ReadByte() != '\n') throw new InvalidDataException("malformed LSP header");
                if (line.Length == 0) return contentLength;
                const string prefix = "Content-Length:";
                var text = line.ToString();
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(text[prefix.Length..].Trim());
                line.Clear();
            }
        }

        static void Send(Stream output, JsonObject message)
        {
            lock (output)
            {
                var body = Encoding.UTF8.GetBytes(message.ToJsonString());
                var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
                output.Write(header);
                output.Write(body);
                output.Flush();
            }
        }
        """;

    private Process StartWarmingServer(string kind)
    {
        var logFile = Path.Combine(_root, $"warming-{Guid.NewGuid():N}.log");
        File.Create(logFile).Dispose();
        var p = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { _warmingDll, logFile },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        _processes.Add(p);
        return p;
    }

    private string BuildWarmingServer()
    {
        var dir = Path.Combine(_root, $"warming-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), WarmingServerSource);
        File.WriteAllText(Path.Combine(dir, "warming.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <Nullable>disable</Nullable>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <InvariantGlobalization>true</InvariantGlobalization>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");
        var psi = new ProcessStartInfo("dotnet", $"build {dir} -c Release -o {Path.Combine(dir, "out")} -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        var outTxt = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        var dll = Path.Combine(dir, "out", "warming.dll");
        if (!File.Exists(dll)) throw new InvalidOperationException("warming-ls DLL not produced: " + outTxt + err);
        return dll;
    }

    private string BuildSlowServer()
    {
        var dir = Path.Combine(_root, $"slow-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Program.cs"), SlowServerSource);
        File.WriteAllText(Path.Combine(dir, "slow.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <OutputType>Exe</OutputType>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "    <Nullable>disable</Nullable>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            "    <InvariantGlobalization>true</InvariantGlobalization>\n" +
            "  </PropertyGroup>\n" +
            "</Project>\n");
        var psi = new ProcessStartInfo("dotnet", $"build {dir} -c Release -o {Path.Combine(dir, "out")} -v q")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        var p = Process.Start(psi)!;
        var outTxt = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(240_000)) { p.Kill(true); throw new InvalidOperationException("slow-ls build timed out"); }
        if (p.ExitCode != 0) throw new InvalidOperationException("slow-ls build failed: " + outTxt + err);
        var dll = Path.Combine(dir, "out", "slow.dll");
        if (!File.Exists(dll)) throw new InvalidOperationException("slow-ls DLL not produced: " + outTxt + err);
        return dll;
    }

    // A minimal LSP server that answers initialize but delays its $/progress kind=end by 4s:
    // enough to keep the workspace in the warming state across attach, repeat-attach and query.
    // After ready, it also answers textDocument/definition with a single location so the
    // regression test can prove code_definition works end-to-end.
    private const string SlowServerSource = """
        using System.Text;
        using System.Text.Json.Nodes;

        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
        var ready = new ManualResetEventSlim(false);

        var t = new Thread(() =>
        {
            Thread.Sleep(4000); // slow warm-up: the workspace is committed while it runs
            Send(output, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "$/progress",
                ["params"] = new JsonObject
                {
                    ["token"] = "ready",
                    ["value"] = new JsonObject { ["kind"] = "end" },
                },
            });
            ready.Set();
            Thread.Sleep(60000); // stay alive so the workspace remains usable after ready
        });
        t.IsBackground = true;
        t.Start();

        while (true)
        {
            var length = ReadContentLength(input);
            if (length < 0) break;
            var body = new byte[length];
            var read = 0;
            while (read < body.Length)
            {
                var count = input.Read(body, read, body.Length - read);
                if (count == 0) return;
                read += count;
            }

            var message = JsonNode.Parse(Encoding.UTF8.GetString(body))!.AsObject();
            var method = message["method"]?.GetValue<string>();
            if (method == "initialize")
            {
                Send(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = message["id"]?.DeepClone(),
                    ["result"] = new JsonObject { ["capabilities"] = new JsonObject() },
                });
            }
            else if (method == "textDocument/definition" && ready.IsSet)
            {
                // After ready, answer definition with one location.
                var doc = message["params"]?["textDocument"]?["uri"]?.GetValue<string>() ?? "file:///test";
                Send(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = message["id"]?.DeepClone(),
                    ["result"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["uri"] = doc,
                            ["range"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                                ["end"] = new JsonObject { ["line"] = 0, ["character"] = 20 },
                            },
                        },
                    },
                });
            }
            else if (message["id"] is not null && method is not null && method != "initialize")
            {
                // Unknown method with an id: return null result (not an error).
                Send(output, new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = message["id"]?.DeepClone(),
                    ["result"] = null,
                });
            }
        }

        static int ReadContentLength(Stream input)
        {
            var contentLength = -1;
            var line = new StringBuilder();
            while (true)
            {
                var value = input.ReadByte();
                if (value < 0) return line.Length == 0 ? -1 : throw new EndOfStreamException();
                if (value != '\r') { line.Append((char)value); continue; }
                if (input.ReadByte() != '\n') throw new InvalidDataException("malformed LSP header");
                if (line.Length == 0) return contentLength;
                const string prefix = "Content-Length:";
                var text = line.ToString();
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(text[prefix.Length..].Trim());
                line.Clear();
            }
        }

        static void Send(Stream output, JsonObject message)
        {
            lock (output)
            {
                var body = Encoding.UTF8.GetBytes(message.ToJsonString());
                var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
                output.Write(header);
                output.Write(body);
                output.Flush();
            }
        }
        """;
}
