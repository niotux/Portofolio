using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using PiSharp.Tests.Mcp;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Cli;

public class CliHostTests
{
    private sealed class VerifiedCeilingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("{\"default_generation_settings\":{\"n_ctx\":200192},\"total_slots\":4}") });
    }

    [Fact]
    public async Task JsonModeReportsEffectiveWorkingTriggerWithoutPollutingEventStream()
    {
        using var tmp = new TempWorkspace();
        tmp.File("models.json", """{"models":[{"id":"test-model","baseUrl":"http://localhost:9/v1","contextWindow":100000,"maxOutputTokens":8192}]}""");
        tmp.File("settings.json", """{"compaction":{"triggerTokens":28000,"keepRecentTokens":20000,"reserveTokens":8192}}""");
        var stdout = new StringWriter(); var stderr = new StringWriter();
        var host = new CliHost { Out = stdout, Err = stderr, In = new StringReader(""), Color = false,
            ConfigDir = tmp.Path, HookCancelKey = false, CeilingHandler = new VerifiedCeilingHandler(),
            StreamFactory = _ => new ScriptedStream().Then(Fakes.Text("ok")).Stream, ToolsFactory = (_, _) => [] };
        Assert.Equal(0, await host.RunAsync(["--json", "--no-session", "hello"]));
        Assert.Contains("trigger=28000 keep=20000 hardInputLimit=91808 providerLimit=200192", stderr.ToString());
        foreach (var line in stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            using (JsonDocument.Parse(line)) { }
    }

    /// <summary>plan-pi-sharp-codex item 4: the SessionStart hook fires once at the start and its context joins the first prompt.</summary>
    [Fact]
    public async Task ASessionStartHookFiresAtTheStartAndAddsItsContextToTheFirstPrompt()
    {
        if (OperatingSystem.IsWindows()) return;
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var seen = Path.Combine(tmp.Path, "started.json");
        tmp.File("settings.json", $$"""{"commandHooks":[{"event":"SessionStart","command":"cat > '{{seen}}'; printf '{\"additional_context\":\"the epic memory is loaded\"}'"}]}""");
        var stream = new ScriptedStream().Then(Fakes.Text("ok"));
        var host = new CliHost
        {
            Out = new StringWriter(), Err = new StringWriter(), In = new StringReader(""), Color = false, ConfigDir = tmp.Path,
            HookCancelKey = false, SubscriptionBrokerUrl = string.Empty, StreamFactory = _ => stream.Stream, CeilingHandler = new OfflineCeilingHandler(),
        };

        Assert.Equal(0, await host.RunAsync(["--no-session", "--print", "hello"]));
        Assert.Contains("session_id", File.ReadAllText(seen));
        var first = stream.Contexts.Single().Messages.OfType<UserMessage>().First();
        Assert.Contains("hello", first.Text);
        Assert.Contains("[hook] the epic memory is loaded", first.Text);
    }

    [Fact]
    public async Task CheckpointsEnabledRunRegistersRewindTool()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        tmp.File("settings.json", """{"checkpoints":true}""");
        var stream = new ScriptedStream().Then(Fakes.Text("ok"));
        var host = new CliHost
        {
            Out = new StringWriter(),
            Err = new StringWriter(),
            In = new StringReader(""),
            Color = false,
            ConfigDir = tmp.Path,
            HookCancelKey = false,
            SubscriptionBrokerUrl = string.Empty,
            StreamFactory = _ => stream.Stream,
            CeilingHandler = new OfflineCeilingHandler(),
        };

        Assert.Equal(0, await host.RunAsync(["--print", "hello"]));
        Assert.Contains(stream.Contexts.Single().Tools, tool => tool.Name == "rewind");
    }

    // pi-sharp-checkpoints-7: end-to-end the project-only checkpoints.autoOnTurnEnd flag installs the
    // after-successful-reply snapshot hook. A run that mutates the tree through a tool produces two
    // shadow checkpoints (the before-batch one and the after-reply one); with the flag off only the
    // before-batch one is created. The global value must not leak through.
    public class AutoOnTurnEndCliTests
    {
        private static string Git(string workDir, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        }

        private static TempWorkspace NewRepo(out string cwd)
        {
            var tmp = new TempWorkspace();
            var global = tmp.Dir("global");
            cwd = tmp.Dir("proj");
            File.WriteAllText(Path.Combine(cwd, "readme.md"), "start\n");
            Git(cwd, "init", "-q");
            Git(cwd, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-am", "base");
            WriteModel(global);
            return tmp;
        }

        private static int CountShadowRefs(string configDir)
        {
            var storeDir = Path.Combine(configDir, "sessions", "checkpoints");
            if (!Directory.Exists(storeDir)) return 0;
            var total = 0;
            foreach (var hash in Directory.GetDirectories(storeDir))
            {
                var refs = Git(hash, "for-each-ref", "--format=%(refname)", "refs/pi/cp/").Trim();
                if (refs.Length == 0) continue;
                // Exclude the bookkeeping ref refs/pi/cp/HEAD (created on store init); count only snapshots.
                total += refs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(r => r != "refs/pi/cp/HEAD")
                    .Count();
            }
            return total;
        }

        private static async Task<(int Code, string Stderr)> RunInRepo(string cwd, string global, string projectSettings, ScriptedStream stream)
        {
            Directory.CreateDirectory(Path.Combine(cwd, ".pi-sharp"));
            File.WriteAllText(Path.Combine(cwd, ".pi-sharp", "settings.json"), projectSettings);
            var stderr = new StringWriter();
            var host = new CliHost
            {
                Out = new StringWriter(),
                Err = stderr,
                In = new StringReader(""),
                Color = false,
                ConfigDir = global,
                HookCancelKey = false,
                StreamFactory = _ => stream.Stream,
                CeilingHandler = new OfflineCeilingHandler(),
            };
            var code = await host.RunAsync(["--cwd", cwd, "--print", "hello"]);
            return (code, stderr.ToString());
        }

        private static PiSharp.Core.Messages.ToolCallContent WriteCall()
        {
            // Target an existing file so ResolveWorkTreeRoot can resolve the work-tree root before the
            // write runs (a brand-new path has no file yet and resolves to null).
            var args = new JsonObject
            {
                ["path"] = "readme.md",
                ["content"] = "changed\n",
            };
            return Fakes.Call("c1", "write", args);
        }

        private static ScriptedStream WriteStream() =>
            new ScriptedStream().Then(Fakes.ToolCalls(WriteCall())).Then(Fakes.Text("ok"));

        [Fact]
        public async Task AutoOnTurnEndOn_CreatesTwoShadowCheckpoints_AcrossTheTurn()
        {
            using var tmp = NewRepo(out var cwd);
            var global = Path.Combine(tmp.Path, "global");

            var (code, _) = await RunInRepo(cwd, global,
                """{"checkpoints":{"enabled":true,"autoOnTurnEnd":true}}""", WriteStream());

            Assert.Equal(0, code);
            // The write tool ran, so the tree changed between the before-batch and after-reply snapshots.
            Assert.Equal(2, CountShadowRefs(global));
        }

        [Fact]
        public async Task AutoOnTurnEndOff_CreatesOnlyTheBeforeBatchCheckpoint()
        {
            using var tmp = NewRepo(out var cwd);
            var global = Path.Combine(tmp.Path, "global");

            var (code, _) = await RunInRepo(cwd, global,
                """{"checkpoints":{"enabled":true,"autoOnTurnEnd":false}}""", WriteStream());

            Assert.Equal(0, code);
            // Only the before-batch snapshot fires; the after-reply hook is not installed.
            Assert.Equal(1, CountShadowRefs(global));
        }

        [Fact]
        public async Task GlobalOnlyAutoOnTurnEnd_DoesNotLeakThroughToTheProject()
        {
            using var tmp = NewRepo(out var cwd);
            var global = Path.Combine(tmp.Path, "global");
            // Global settings set the flag; the project does not. The merge must ignore the global.
            File.WriteAllText(Path.Combine(global, "settings.json"), """{"checkpoints":{"enabled":true,"autoOnTurnEnd":true}}""");

            // Project settings turn checkpoints on but leave autoOnTurnEnd absent (null).
            var (code, _) = await RunInRepo(cwd, global,
                """{"checkpoints":{"enabled":true}}""", WriteStream());

            Assert.Equal(0, code);
            // The global true is ignored; the after-reply hook is off, so only before-batch fires.
            Assert.Equal(1, CountShadowRefs(global));
        }
    }

    [Fact]
    public async Task ABrokerSeatWithALocalModelRunsItWhileTheBrokerIsDown()
    {
        using var tmp = new TempWorkspace(); WriteModel(tmp);
        tmp.File("settings.json", """{"factory":{"token":"seat-token"}}""");
        var stderr = new StringWriter();
        var seat = new CliHost { Out = new StringWriter(), Err = stderr, In = new StringReader(""), Color = false, ConfigDir = tmp.Path,
            HookCancelKey = false, SubscriptionBrokerUrl = "http://127.0.0.1:1/", CeilingHandler = new OfflineCeilingHandler(),
            StreamFactory = _ => new ScriptedStream().Then(Fakes.Text("ok")).Stream, ToolsFactory = (_, _) => [] };
        Assert.Equal(0, await seat.RunAsync(["--no-session", "--print", "hello"]));
        Assert.Contains("subscription: broker unavailable", stderr.ToString());
    }

    [Fact]
    public async Task ABrokerSeatWithoutALocalModelFailsClosedWhileTheBrokerIsDown()
    {
        using var tmp = new TempWorkspace();
        tmp.File("settings.json", """{"factory":{"token":"seat-token"}}""");
        var stderr = new StringWriter();
        var seat = new CliHost { Out = new StringWriter(), Err = stderr, In = new StringReader(""), Color = false, ConfigDir = tmp.Path,
            HookCancelKey = false, SubscriptionBrokerUrl = "http://127.0.0.1:1/", CeilingHandler = new OfflineCeilingHandler(),
            StreamFactory = _ => new ScriptedStream().Then(Fakes.Text("ok")).Stream, ToolsFactory = (_, _) => [] };
        Assert.Equal(2, await seat.RunAsync(["--no-session", "--print", "hello"]));
        Assert.Contains("Subscription setup failed", stderr.ToString());
    }

    private sealed class OfflineCeilingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline test fixture"));
    }
    [Fact]
    public async Task JsonModeReturnsFailureExitCodeForProviderError()
    {
        using var tmp = new TempWorkspace(); WriteModel(tmp);
        var stream = new ScriptedStream().Then(Fakes.Error("provider unavailable"));
        var (code, stdout, _) = await Run(["--json", "--no-session", "hello"], tmp, stream);
        Assert.Equal(1, code);
        Assert.Contains("\"outcome\":\"failed\"", stdout);
    }

    [Fact]
    public async Task HelpAndVersion()
    {
        var (code, stdout, _) = await Run(["--help"]);
        Assert.Equal(0, code);
        Assert.Contains("Usage:", stdout);

        (code, stdout, _) = await Run(["-v"]);
        Assert.Equal(0, code);
        Assert.Contains($"pi-sharp {CliHost.Version}", stdout);
    }

    [Fact]
    public async Task PrintModeWritesFinalText()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("pong"));
        var (code, stdout, _) = await Run(["-p", "--no-session", "ping"], tmp, stream);
        Assert.Equal(0, code);
        Assert.Contains("pong", stdout);
        Assert.DoesNotContain("agent_start", stdout);
    }

    [Fact]
    public async Task JsonModeEmitsEvents()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("hi"));
        var (code, stdout, _) = await Run(["--json", "--no-session", "hello"], tmp, stream);
        Assert.Equal(0, code);
        Assert.Contains("\"type\":\"session\"", stdout);
        Assert.Contains("\"type\":\"agent_start\"", stdout);
        Assert.Contains("\"type\":\"message_end\"", stdout);
        Assert.Contains("\"type\":\"agent_end\"", stdout);
    }

    [Fact]
    public async Task ConnectsConfiguredMcpToolsBeforeFirstPrompt()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        McpClientTests.SeedStore(tmp);
        var dll = Path.Combine(AppContext.BaseDirectory, "PiSharp.SkillStore.dll");
        var args = JsonSerializer.Serialize(new[] { dll, tmp.Path, "store" });
        tmp.File("mcp.json", $"{{\"mcpServers\":{{\"store\":{{\"command\":\"dotnet\",\"args\":{args}}}}}}}");

        var stream = new ScriptedStream().Then(Fakes.Text("pong"));
        var (code, _, stderr) = await Run(["-p", "--no-session", "list tools"], tmp, stream);

        Assert.Equal(0, code);
        Assert.DoesNotContain("MCP configuration error", stderr);
        Assert.Contains(stream.Contexts, context =>
            context.Tools.Any(tool => tool.Name == "store__search_skills"));
    }

    [Fact]
    public async Task RefreshesMcpToolsBetweenReplTurns()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        await using var pipes = new RpcPipes();
        var listChanged = false;
        await using var server = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, _, _) =>
        {
            if (method == "initialize")
            {
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["protocolVersion"] = McpClient.ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = true } },
                    ["serverInfo"] = new JsonObject { ["name"] = "dynamic", ["version"] = "1" },
                });
            }
            if (method == "tools/list")
            {
                var names = listChanged ? new[] { "first", "second" } : new[] { "first" };
                return Task.FromResult<JsonNode?>(new JsonObject
                {
                    ["tools"] = new JsonArray(names.Select(name => (JsonNode)new JsonObject
                    {
                        ["name"] = name,
                        ["inputSchema"] = new JsonObject { ["type"] = "object" },
                    }).ToArray()),
                });
            }
            return Task.FromResult<JsonNode?>(new JsonObject());
        });
        var client = await McpClient.ConnectAsync("dynamic", new PeerTransport(
            new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)));
        var manager = new McpManager();
        await manager.AddClientAsync(
            new McpServerConfig { Alias = "dynamic", Kind = McpTransportKind.Http, Url = "http://unused" },
            client);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ListsChanged += _ => refreshed.TrySetResult();

        var contexts = new List<LlmContext>();
        var calls = 0;
        async IAsyncEnumerable<AssistantMessageEvent> Stream(
            ModelConfig model,
            LlmContext context,
            StreamOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            contexts.Add(context);
            calls++;
            var message = Fakes.Text(calls == 1 ? "first response" : "second response");
            yield return new AssistantStart(message with { Content = [] });
            if (calls == 1)
            {
                listChanged = true;
                await server.NotifyAsync("notifications/tools/list_changed", null, cancellationToken);
                await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            yield return new AssistantDone(message.StopReason, message);
        }

        var stream = (StreamFunction)Stream;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = stdout,
            Err = stderr,
            In = new StringReader("first\nsecond\n/quit\n"),
            Color = false,
            ConfigDir = tmp.Path,
            HookCancelKey = false,
            StreamFactory = _ => stream,
            ToolsFactory = (_, _) => [],
            CeilingHandler = new OfflineCeilingHandler(),
            McpFactory = (_, _) => Task.FromResult(manager),
        };

        var code = await host.RunAsync([]);

        Assert.Equal(0, code);
        Assert.Equal(2, contexts.Count);
        Assert.DoesNotContain(contexts[0].Tools, tool => tool.Name == "dynamic__second");
        Assert.Contains(contexts[1].Tools, tool => tool.Name == "dynamic__second");
    }

    [Fact]
    public async Task ReportsSkillCollisionOnceAtStartup()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var globalSkill = tmp.Dir("skills/dup");
        File.WriteAllText(Path.Combine(globalSkill, "SKILL.md"), "---\nname: dup\ndescription: global\n---\n");
        var projectSkill = tmp.Dir(".pi-sharp/skills/dup");
        File.WriteAllText(Path.Combine(projectSkill, "SKILL.md"), "---\nname: dup\ndescription: project\n---\n");

        var (code, _, stderr) = await Run(["-p", "--no-session", "--cwd", tmp.Path, "hello"], tmp, new ScriptedStream().Then(Fakes.Text("ok")));

        Assert.Equal(0, code);
        var collisionLines = stderr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("collision", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var collision = Assert.Single(collisionLines);
        Assert.Contains(globalSkill, collision, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplQuit()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var input = new StringReader("/quit\n");
        var (code, stdout, _) = await Run([], tmp, input: input);
        Assert.Equal(0, code);
        Assert.Contains($"pi-sharp {CliHost.Version}", stdout);
        Assert.Contains("/help", stdout);
    }

    [Fact]
    public async Task ReplSlashHelpAndPrompt()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var stream = new ScriptedStream().Then(Fakes.Text("ack"));
        var input = new StringReader("/help\nhello\n/quit\n");
        var (code, stdout, _) = await Run([], tmp, stream, input);
        Assert.Equal(0, code);
        Assert.Contains("/model", stdout);
        Assert.Contains("ack", stdout);
    }

    [Fact]
    public async Task ListModels()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var (code, stdout, _) = await Run(["--list-models"], tmp);
        Assert.Equal(0, code);
        Assert.Contains("test-model", stdout);
        Assert.Contains("http://localhost:9/v1", stdout);
    }

    [Fact]
    public async Task MissingModelExits2()
    {
        using var tmp = new TempWorkspace();
        tmp.Dir("empty");
        var host = Host(tmp, configDir: Path.Combine(tmp.Path, "empty"));
        var code = await host.RunAsync(["-p", "x"]);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task PrintWithoutPromptExits2()
    {
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var (code, _, stderr) = await Run(["-p", "--no-session"], tmp, input: new StringReader(""));
        Assert.Equal(2, code);
        Assert.Contains("No prompt", stderr);
    }

    [Fact]
    public async Task ContextOverflowExitsWithStructuredEventAndDistinctCode()
    {
        // P4 (wi-8): a run whose provider refuses with the llama.cpp context-limit wording and that
        // cannot be recovered by compaction must exit with a distinct code and a machine-readable
        // event on stdout, not the old single stderr line with exit 1. A supervisor must be able to
        // tell an overflow death from a handled turn (criterion 5).
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        // Every turn overflows, so compaction is impossible and the run dies on the limit.
        var stream = new ScriptedStream()
            .Then(Fakes.Error("Context size has been exceeded."));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "go"], tmp, stream);

        Assert.Equal(CliHost.ExitCodeContextOverflow, code);
        Assert.Contains("\"type\":\"context_overflow\"", stdout);
        Assert.Contains("\"recovered\":false", stdout);
        Assert.Contains("Context size has been exceeded", stderr);
    }

    [Fact]
    public async Task ContextOverflowPersistedTerminalTurnEndsOnDiskSessionFile()
    {
        // A4 alternative (wi-8 PO ruling 2, note-20260915-195357): the persist-skip question
        // needs the on-disk assertion - after a run that dies on a context overflow, the session
        // JSONL on disk must END WITH the terminal error turn. The window is present but
        // wrong-large (1024) so the client path runs with a configured window, the scripted
        // stream refuses with the llama.cpp phrase on every call (turn, compaction summary,
        // retry), and the run exits 4 with nothing recovered.
        using var tmp = new TempWorkspace();
        File.WriteAllText(Path.Combine(tmp.Path, "models.json"), """
            {"models":[{"id":"test-model","baseUrl":"http://localhost:9/v1","apiKey":"dummy","contextWindow":1024,"maxOutputTokens":8192}]}
            """);
        // Disable compaction: with the wrong-large window nothing can be summarised into 1024
        // tokens, so the run surfaces its bounded-compaction death and exits 4 instead of
        // re-sending the prompt. Every stream call then errors with the llama.cpp phrase.
        // (Project settings live in the .pi-sharp/ config dir, per ConfigPaths.)
        tmp.File(".pi-sharp/settings.json", """
            {"compaction":{"enabled":false}}
            """);
        var stream = new ScriptedStream();
        for (var i = 0; i < 8; i++) stream.Then(() => Fakes.Error("Context size has been exceeded."));
        var argsSessionDir = Path.Combine(tmp.Path, "sessions");

        // Drive the real client path (CliHost.RunAsync) with a named session file under
        // --session: models.json carries the wrong-large window (1024), the stream is scripted
        // to refuse with the llama.cpp phrase, and the session persists to that file.
        var sessionFile = Path.Combine(argsSessionDir, "overflow.jsonl");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = stdout,
            Err = stderr,
            In = new StringReader(""),
            Color = false,
            ConfigDir = tmp.Path,
            HookCancelKey = false,
            StreamFactory = _ => stream.Stream,
            ToolsFactory = (_, _) => [],
            CeilingHandler = new OfflineCeilingHandler(),
        };
        var code = await host.RunAsync(["--json", "-p", "--session", sessionFile, "go"]);
        var stdoutText = stdout.ToString();

        Assert.Equal(CliHost.ExitCodeContextOverflow, code);
        Assert.Contains("\"type\":\"context_overflow\"", stdoutText);
        Assert.Contains("\"recovered\":false", stdoutText);
        // With the wrong-large window and nothing to compact, the bounded budget is spent in one
        // call and the death is surfaced deterministically (one stream call, exit 4).
        Assert.Equal(1, stream.Calls);

        // The terminal error turn was persisted to disk before the death: the session JSONL on
        // disk ends with it (no compaction or later entry follows). This is the A4 assertion the
        // PO commissioned.
        Assert.True(File.Exists(sessionFile));
        var lines = File.ReadAllLines(sessionFile).Where(l => l.Trim().Length > 0).ToList();
        Assert.NotEmpty(lines);

        var terminal = lines[^1];
        var last = JsonSerializer.Deserialize<JsonObject>(terminal)!;
        Assert.Equal("message", last["type"]?.GetValue<string>());
        var msg = last["message"]!;
        Assert.Equal("assistant", msg["role"]?.GetValue<string>());
        Assert.Equal("error", msg["stopReason"]?.GetValue<string>());
        Assert.Contains("Context size has been exceeded", terminal);

        // No entry may follow the terminal turn: nothing was written after the death.
        for (var i = lines.Count - 2; i >= 0; i--)
            Assert.DoesNotContain("\"type\":\"compaction\"", lines[i]);
    }

    private static void WriteModel(TempWorkspace tmp)
    {
        WriteModel(tmp.Path);
    }

    private static void WriteModel(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "models.json"), """
            {"models":[{"id":"test-model","baseUrl":"http://localhost:9/v1","apiKey":"dummy","contextWindow":32768,"maxOutputTokens":8192}]}
            """);
    }

    private static CliHost Host(TempWorkspace tmp, ScriptedStream? stream = null, TextReader? input = null, string? configDir = null)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        return new CliHost
        {
            Out = stdout,
            Err = stderr,
            In = input ?? new StringReader(""),
            Color = false,
            ConfigDir = configDir ?? tmp.Path,
            HookCancelKey = false,
            StreamFactory = _ => (stream ?? new ScriptedStream().Then(Fakes.Text("ok"))).Stream,
            ToolsFactory = (_, _) => [],
            CeilingHandler = new OfflineCeilingHandler(),
        };
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(
        string[] argv,
        TempWorkspace? tmp = null,
        ScriptedStream? stream = null,
        TextReader? input = null)
    {
        tmp ??= new TempWorkspace();
        using var owned = tmp;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = stdout,
            Err = stderr,
            In = input ?? new StringReader(""),
            Color = false,
            ConfigDir = tmp.Path,
            HookCancelKey = false,
            StreamFactory = _ => (stream ?? new ScriptedStream().Then(Fakes.Text("ok"))).Stream,
            ToolsFactory = (_, _) => [],
            CeilingHandler = new OfflineCeilingHandler(),
        };
        var code = await host.RunAsync(argv);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(string[] argv)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost
        {
            Out = stdout,
            Err = stderr,
            In = new StringReader(""),
            Color = false,
            HookCancelKey = false,
            StreamFactory = _ => new ScriptedStream().Then(Fakes.Text("ok")).Stream,
            ToolsFactory = (_, _) => [],
            CeilingHandler = new OfflineCeilingHandler(),
        };
        var code = await host.RunAsync(argv);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void AGiveUpNamesTheLastErrorWithoutASinceTheKbWouldMisread()
    {
        var failure = new AssistantMessage
        {
            Content = [],
            Model = "gpt-6.1-terra",
            StopReason = StopReason.Error,
            ErrorMessage = "Subscription request failed (FileNotFoundException: Could not load file or assembly 'X' since the update). Check it.\nsecond line",
        };
        var detail = CliHost.GiveUpDetail(failure);
        Assert.StartsWith("Subscription request failed (FileNotFoundException", detail);
        Assert.DoesNotContain("since", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("second line", detail);
        Assert.Equal("unknown error", CliHost.GiveUpDetail(null));
    }
}
