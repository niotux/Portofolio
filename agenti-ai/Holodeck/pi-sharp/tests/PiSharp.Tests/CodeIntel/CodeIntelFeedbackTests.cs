// codeintel epic slice 3: settings, mcp.json injection, parallel-safe code_* tools and the
// CodeIntelFeedback hook (sync + diagnostics delta -> Trailing note, fail open).
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.CodeIntelFeedback;
using PiSharp.Core.Config;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;
using PiSharp.Tests.Mcp;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.CodeIntel;

public class CodeIntelSettingsTests
{
    [Fact]
    public void ParsesCodeIntelSection()
    {
        var s = SettingsLoader.Parse(
            """{"codeintel":{"feedback":"errors+warnings","url":"http://ls:8081","token":"tk"}}""");
        Assert.NotNull(s.CodeIntel);
        Assert.Equal(CodeIntelFeedbackMode.ErrorsAndWarnings, s.CodeIntel.Feedback);
        Assert.Equal("http://ls:8081", s.CodeIntel.Url);
        Assert.Equal("tk", s.CodeIntel.Token);
        Assert.Equal("http://ls:8081", s.CodeIntel.ResolveUrl());
        Assert.Equal("tk", s.CodeIntel.ResolveToken());
    }

    [Fact]
    public void FeedbackModesParseTolerantly()
    {
        Assert.Equal(CodeIntelFeedbackMode.Off, SettingsLoader.Parse("""{"codeintel":{"feedback":"off"}}""").CodeIntel!.Feedback);
        Assert.Equal(CodeIntelFeedbackMode.Off, SettingsLoader.Parse("""{"codeintel":{"feedback":"disabled"}}""").CodeIntel!.Feedback);
        Assert.Equal(CodeIntelFeedbackMode.Errors, SettingsLoader.Parse("""{"codeintel":{"feedback":"errors"}}""").CodeIntel!.Feedback);
        Assert.Equal(CodeIntelFeedbackMode.ErrorsAndWarnings,
            SettingsLoader.Parse("""{"codeintel":{"feedback":"errors and warnings"}}""").CodeIntel!.Feedback);
        // Unknown values stay Unset, which behaves like Errors; nothing crashes.
        Assert.Equal(CodeIntelFeedbackMode.Unset, SettingsLoader.Parse("""{"codeintel":{"feedback":"loud"}}""").CodeIntel!.Feedback);
        // A settings object without a codeintel section at all: the property stays null.
        Assert.Null(SettingsLoader.Parse("""{"defaultModel":"x"}""").CodeIntel);
    }

    [Fact]
    public void ProjectOverridesGlobalCodeIntel()
    {
        var lower = new CodeIntelSettings { Url = "http://g:8081", Feedback = CodeIntelFeedbackMode.ErrorsAndWarnings };
        var higher = new CodeIntelSettings { Token = "t" };
        var merged = CodeIntelSettings.Merge(lower, higher)!;
        Assert.Equal("http://g:8081", merged.Url);
        // The higher layer left feedback unset: the lower one wins.
        Assert.Equal(CodeIntelFeedbackMode.ErrorsAndWarnings, merged.Feedback);
        Assert.Equal("t", merged.Token);
    }

    [Fact]
    public void ResolveTokenFallsBackToEnvironment()
    {
        Environment.SetEnvironmentVariable(CodeIntelSettings.TokenEnv, "env-token");
        try
        {
            Assert.Equal("env-token", new CodeIntelSettings().ResolveToken());
            Assert.Equal("settings", new CodeIntelSettings { Token = "settings" }.ResolveToken());
            // No url anywhere: code-intel is not configured, there is no guessed default.
            Assert.Null(new CodeIntelSettings().ResolveUrl());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CodeIntelSettings.TokenEnv, null);
        }
    }
}

public class CodeServerConfigTests
{
    private static McpServerConfig StdioServer(string alias) => new()
    {
        Alias = alias,
        Kind = McpTransportKind.Stdio,
        Command = "cmd",
    };

    [Fact]
    public void WithCodeServerAppendsCodeEntryWithBearerHeader()
    {
        var config = McpConfig.WithCodeServer(
            new McpConfig(new[] { StdioServer("kb") }),
            new CodeIntelSettings { Url = "http://ci:8081", Token = "tok" });
        var code = Assert.Single(config.Servers, s => s.Alias == McpServerConfig.CodeServerAlias);
        Assert.Equal(McpTransportKind.Http, code.Kind);
        Assert.Equal("http://ci:8081", code.Url);
        Assert.Equal("Bearer tok", code.Headers["Authorization"]);
        // The existing servers are untouched.
        Assert.Contains(config.Servers, s => s.Alias == "kb");
    }

    [Fact]
    public void WithCodeServerIsIdempotentWhenAliasExists()
    {
        var mine = new McpConfig(new[] { StdioServer(McpServerConfig.CodeServerAlias) });
        var same = McpConfig.WithCodeServer(mine, new CodeIntelSettings { Token = "t" });
        Assert.Same(mine, same);
    }

    [Fact]
    public void WithCodeServerNeedsAToken()
    {
        Environment.SetEnvironmentVariable(CodeIntelSettings.TokenEnv, null);
        Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, null);
        try
        {
            var mine = new McpConfig(new[] { StdioServer("kb") });
            var same = McpConfig.WithCodeServer(mine, new CodeIntelSettings { Url = "http://ci:8081" });
            Assert.Same(mine, same);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CodeIntelSettings.TokenEnv, null);
            Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, null);
        }
    }

    [Fact]
    public void WithCodeServerNeedsAUrl()
    {
        // A seat with a factory token but no codeintel url must not get a 'code' server: a guessed default
        // made every such CLI start wait out a connect timeout and print "MCP server code unavailable".
        Environment.SetEnvironmentVariable(CodeIntelSettings.UrlEnv, null);
        Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, "factory-token");
        try
        {
            var mine = new McpConfig(new[] { StdioServer("kb") });
            Assert.Same(mine, McpConfig.WithCodeServer(mine, new CodeIntelSettings()));
            Assert.Same(mine, McpConfig.WithCodeServer(mine, new CodeIntelSettings { Token = "t" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, null);
        }
    }
}

public class CodeToolsParallelSafetyTests
{
    [Fact]
    public void CodeReadToolsAreParallelSafe()
    {
        foreach (var name in new[] { "code_definition", "code_references", "code_hover", "code_symbols", "code_diagnostics" })
            Assert.True(ParallelSafeTools.IsParallelSafe("code__" + name), name);
    }

    [Fact]
    public void CodeMutatingToolsAreNotParallelSafe()
    {
        foreach (var name in new[] { "code_attach", "code_sync", "code_detach" })
            Assert.False(ParallelSafeTools.IsParallelSafe("code__" + name), name);
    }
}

public class CodeIntelFeedbackTests
{
    private const string DiagnosticsWithDelta = """
        {"delta":2,"files":[{"file":"src/App.cs","errors":[
          {"file":"src/App.cs","start_line":7,"end_line":7,"code":"CS0103","severity":1,"message":"The name 'x' does not exist in the current context"}]}]}
        """;

    private const string DiagnosticsEmpty = """{"delta":0,"files":[]}""";

    private sealed record FakeRepo : IDisposable
    {
        public string Path { get; }

        public FakeRepo()
        {
            Path = TempPath();
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Path, "src"));
            File.WriteAllText(System.IO.Path.Combine(Path, "App.cs"), "class App {}\n");
            File.WriteAllText(System.IO.Path.Combine(Path, "src/App.cs"), "class App {}\n");
            Run("init -q");
            Run("-c user.email=t@t -c user.name=t commit -q --allow-empty -m init");
            Run("remote add origin https://gitlab.example/devs/repo.git");
        }

        private void Run(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10_000)) p.Kill(true);
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"git {args}: {p.StandardError.ReadToEnd()}");
        }

        private static string TempPath()
        {
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "ci3fake-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void Dispose()
        {
            // git writes its object files read-only; Windows refuses to delete those.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
    }

    private static TurnContext TurnWith(string toolName, JsonNode? details = null)
    {
        var message = new AssistantMessage
        {
            Content = [new TextContent("ok")],
            Model = "test-model",
        };
        var result = new ToolResultMessage
        {
            ToolCallId = "c1",
            ToolName = toolName,
            Content = [new TextContent("edited src/App.cs")],
            Details = details,
        };
        return new TurnContext(message, [result], new AgentContext(), []);
    }

    private static async Task<McpClient> ConnectClient(RpcPipes pipes, string diagnostics,
        Action<string, JsonObject, TaskCompletionSource<JsonNode?>>? onCall = null)
    {
        // The test server never completes, so the client's per-call timeout must be short for the budget test
        // to exercise its own budget instead of the default 60s deadline.
        // Note: no `await using` here — the server peer must outlive ConnectClient; its read loop
        // serves the client's requests. Lifetime is managed by the caller's `await using var pipes`.
        var serverPeer = new JsonRpcPeer(pipes.ServerIn, pipes.ServerOut, (method, p, ct) =>
        {
            switch (method)
            {
                case "initialize":
                    return Task.FromResult<JsonNode?>(new JsonObject
                    {
                        ["protocolVersion"] = McpClient.ProtocolVersion,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = "codeintel", ["version"] = "1" },
                    });
                case "tools/list":
                    return Task.FromResult<JsonNode?>(new JsonObject { ["tools"] = new JsonArray() });
                case "tools/call":
                    var name = p!["name"]!.GetValue<string>();
                    var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    if (onCall is null)
                    {
                        tcs.SetResult(MakeResult(name == "code_diagnostics" ? diagnostics : "ok"));
                    }
                    else
                    {
                        onCall(name, p["arguments"]!.AsObject(), tcs);
                    }
                    return tcs.Task;
                default:
                    return Task.FromResult<JsonNode?>(new JsonObject());
            }
        });
        // The client's per-call timeout must be shorter than the diagnostics budget (10s default), or a
        // hung code_diagnostics call would fail open on the client's 60s deadline instead of the budget.
        // 250ms is far shorter than the 10s budget, so the tests exercise the budget fast.
        var client = new McpClient("code", new PeerTransport(new JsonRpcPeer(pipes.ClientIn, pipes.ClientOut)),
            TimeSpan.FromMilliseconds(250));
        await client.InitializeAsync();
        return client;
    }

    private static JsonNode? MakeResult(string text) =>
        new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = false,
        };

    [Fact]
    public async Task NewDiagnosticsBecomeATrailingNote()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsWithDelta);
        var feedback = new CodeIntelFeedback(client, repo.Path, new CodeIntelSettings());
        var prepare = feedback.ComposePrepareNextTurn(null);

        var turn = TurnWith("edit", new JsonObject { ["path"] = "src/App.cs" });
        var update = await prepare(turn);

        Assert.NotNull(update);
        var note = Assert.IsType<UserMessage>(Assert.Single(update!.Trailing!));
        Assert.Contains("Code-intel diagnostics", Assert.IsType<TextContent>(note.Content[0]).Text);
        Assert.Contains("src/App.cs", Assert.IsType<TextContent>(note.Content[0]).Text);
        Assert.Contains("CS0103", Assert.IsType<TextContent>(note.Content[0]).Text);
        // At most 1.5k characters.
        Assert.True(Assert.IsType<TextContent>(note.Content[0]).Text.Length <= 1500);
    }

    [Fact]
    public async Task EmptyDeltaMeansNoNote()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsEmpty);
        var feedback = new CodeIntelFeedback(client, repo.Path, new CodeIntelSettings());
        var prepare = feedback.ComposePrepareNextTurn(null);

        var update = await prepare(TurnWith("edit", new JsonObject { ["path"] = "src/App.cs" }));

        Assert.Null(update);
    }

    [Fact]
    public async Task ReadOnlyTurnsNeverTouchTheService()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsWithDelta);
        var feedback = new CodeIntelFeedback(client, repo.Path, new CodeIntelSettings());
        var prepare = feedback.ComposePrepareNextTurn(null);

        var update = await prepare(TurnWith("grep"));

        Assert.Null(update);
    }

    [Fact]
    public async Task FeedbackOffNeverTouchesTheService()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsWithDelta);
        var feedback = new CodeIntelFeedback(client, repo.Path,
            new CodeIntelSettings { Feedback = CodeIntelFeedbackMode.Off });
        var prepare = feedback.ComposePrepareNextTurn(null);

        var update = await prepare(TurnWith("edit", new JsonObject { ["path"] = "src/App.cs" }));

        Assert.Null(update);
    }

    [Fact]
    public async Task TheInnerPrepareIsComposedUnderTheNote()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsWithDelta);
        var feedback = new CodeIntelFeedback(client, repo.Path, new CodeIntelSettings());
        var inner = new List<Message>
        {
            new UserMessage("inner-note"),
        };
        var prepare = feedback.ComposePrepareNextTurn(_ => ValueTask.FromResult<TurnUpdate?>(
            new TurnUpdate { Trailing = inner }));

        var update = await prepare(TurnWith("edit", new JsonObject { ["path"] = "src/App.cs" }));

        Assert.Equal(2, update!.Trailing!.Count);
        Assert.Equal("inner-note", Assert.IsType<TextContent>(Assert.IsType<UserMessage>(update.Trailing[0]).Content[0]).Text);
        Assert.Contains("Code-intel diagnostics", Assert.IsType<TextContent>(Assert.IsType<UserMessage>(update.Trailing[1]).Content[0]).Text);
    }

    [Fact]
    public async Task AHungDiagnosticsCallFailsOpenWithinTheBudget()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsEmpty, (name, args, tcs) =>
        {
            if (name == "code_diagnostics")
                return; // never complete: the budget must fail open
            tcs.SetResult(MakeResult("ok"));
        });
        var feedback = new CodeIntelFeedback(client, repo.Path, new CodeIntelSettings(), null,
            TimeSpan.FromMilliseconds(150));
        var prepare = feedback.ComposePrepareNextTurn(null);

        var vt = prepare(TurnWith("edit", new JsonObject { ["path"] = "src/App.cs" }));
        var update = await vt.AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Null(update); // no note, no throw
    }

    [Fact]
    public async Task AnErrorFromTheServerMeansNoNote()
    {
        using var repo = new FakeRepo();
        await using var pipes = new RpcPipes();
        await using var client = await ConnectClient(pipes, DiagnosticsEmpty, (name, args, tcs) =>
        {
            tcs.SetResult(new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "boom" }),
                ["isError"] = true,
            });
        });
        var feedback = new CodeIntelFeedback(client, repo.Path, new CodeIntelSettings());
        var prepare = feedback.ComposePrepareNextTurn(null);

        var update = await prepare(TurnWith("edit", new JsonObject { ["path"] = "src/App.cs" }));

        Assert.Null(update);
    }
}
