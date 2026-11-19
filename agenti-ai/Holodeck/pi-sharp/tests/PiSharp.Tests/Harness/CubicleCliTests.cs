using System.Text.Json.Nodes;
using PiSharp.Core.Config;
using PiSharp.Cli;
using PiSharp.Tests.Agent;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Harness;

[Collection("cubicle-env")]
public class CubicleCliTests
{
    [Fact]
    public async Task ConfiguredCubicleReceivesPromptAssistantAndRunEnd()
    {
        using var stub = new CubicleStub();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), new JsonObject
        {
            ["cubicle"] = new JsonObject { ["url"] = stub.Url, ["token"] = "cubicle-token", ["about"] = new JsonArray((JsonNode)"wi-1") },
        }.ToJsonString());
        var stream = new ScriptedStream().Then(Fakes.Text("pong"));
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "ping"], tmp, stream);

        Assert.Equal(0, code);
        Assert.Contains("pong", stdout, StringComparison.Ordinal);
        Assert.Equal("", stderr);
        var posts = stub.Posts.ToList();
        Assert.NotEmpty(posts);
        Assert.All(posts, p => Assert.Equal("Bearer cubicle-token", p.Authorization));
        var events = posts.SelectMany(p => p.Events).Select(e => e!.AsObject()).ToList();
        var types = events.Select(e => e["type"]!.GetValue<string>()).ToList();
        // One cache_miss line is emitted after each assistant turn (adr-pi-sharp-cache-stats).
        Assert.Equal(["turn_start", "prompt", "assistant", "cache_miss", "run_end"], types);
        events = events.Where(e => e["type"]!.GetValue<string>() != "turn_start").ToList(); // turns are markers; check the content rows
        Assert.Equal("ping", events[0]["payload"]!["text"]!.GetValue<string>());
        Assert.Equal("pong", events[1]["payload"]!["text"]!.GetValue<string>());
        Assert.NotEmpty(events[2]["payload"]!["line"]!.GetValue<string>());
        Assert.Equal("stop", events[3]["payload"]!["reason"]!.GetValue<string>());
        Assert.Equal("wi-1", events[0]["about"]![0]!.GetValue<string>());
        Assert.All(events, e => Assert.Equal(events[0]["run_id"]!.GetValue<string>(), e["run_id"]!.GetValue<string>()));
        // Steering is polled at run start and after the turn, with wait=0 and a cursor starting at 0.
        var gets = stub.Gets.ToList();
        Assert.Equal(2, gets.Count);
        Assert.All(gets, g => Assert.Equal(0, g.Since));
        Assert.All(gets, g => Assert.Equal(0, g.Wait));
    }

    [Fact]
    public async Task SteeringMessageBecomesUserTurn()
    {
        using var stub = new CubicleStub();
        var polls = 0;
        stub.Handler = req => Task.FromResult(req.Method == "POST"
            ? StubResponse.Accepted()
            : ++polls == 2 ? StubResponse.Steering(3, (3, "digital-scribe", "also say bye")) : StubResponse.Steering(polls == 1 ? 0 : 3));
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), """{"cubicle":{"token":"cubicle-token"}}""");
        var stream = new ScriptedStream().Then(Fakes.Text("hi")).Then(Fakes.Text("bye"));
        var (code, stdout, _) = await Run(["-p", "--no-session", "--cubicle-url", stub.Url, "hello"], tmp, stream);

        Assert.Equal(0, code);
        Assert.Contains("bye", stdout, StringComparison.Ordinal);
        Assert.Equal(2, stream.Calls);
        var steered = stream.Contexts[1].Messages.OfType<PiSharp.Core.Messages.UserMessage>().Last();
        Assert.Equal("[steering from @digital-scribe] also say bye", steered.Text);
        var types = stub.Posts.SelectMany(p => p.Events).Select(e => e!["type"]!.GetValue<string>()).ToList();
        // Each assistant turn is followed by a cache_miss line (adr-pi-sharp-cache-stats).
        Assert.Equal(["turn_start", "prompt", "assistant", "cache_miss", "turn_start", "prompt", "assistant", "cache_miss", "run_end"], types);
        Assert.All(stub.Posts, p => Assert.Equal("Bearer cubicle-token", p.Authorization));
    }

    [Fact]
    public async Task FlagsOverrideSettingsAndTokenComesFromNamedEnv()
    {
        using var stub = new CubicleStub();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), """{"cubicle":{"url":"http://127.0.0.1:9","token":"wrong"}}""");
        var env = "PI_SHARP_CUBICLE_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(env, "from-env");
        try
        {
            var (code, _, _) = await Run(["-p", "--no-session", "--cubicle-url", stub.Url, "--cubicle-token-env", env, "ping"], tmp);
            Assert.Equal(0, code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, null);
        }
        Assert.NotEmpty(stub.Posts);
        Assert.All(stub.Posts, p => Assert.Equal("Bearer from-env", p.Authorization));
    }

    [Fact]
    public async Task UnavailableCubicleDoesNotAffectTheRun()
    {
        using var stub = new CubicleStub();
        var url = stub.Url;
        stub.Dispose();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), """{"cubicle":{"token":"cubicle-token"}}""");
        var (code, stdout, stderr) = await Run(["-p", "--no-session", "--cubicle-url", url, "ping"], tmp);
        Assert.Equal(0, code);
        Assert.Contains("ok", stdout, StringComparison.Ordinal);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task MissingCubicleTokenDisablesCubicleWithoutRequests()
    {
        using var stub = new CubicleStub();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var env = "PI_SHARP_CUBICLE_MISSING_" + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable(env);
        Environment.SetEnvironmentVariable(env, null);
        try
        {
            var (code, stdout, stderr) = await Run(["-p", "--no-session", "--cubicle-url", stub.Url, "--cubicle-token-env", env, "ping"], tmp);

            Assert.Equal(0, code);
            Assert.Contains("ok", stdout, StringComparison.Ordinal);
            Assert.Equal($"cubicle: environment variable {env} is empty; cubicle disabled.{Environment.NewLine}", stderr);
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, previous);
        }

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task CubicleUrlUsesDefaultTokenEnvironment()
    {
        using var stub = new CubicleStub();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var previous = Environment.GetEnvironmentVariable(CubicleSettings.TokenEnv);
        Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, "fallback-token");
        try
        {
            var (code, _, stderr) = await Run(["-p", "--no-session", "--cubicle-url", stub.Url, "ping"], tmp);

            Assert.Equal(0, code);
            Assert.Equal("", stderr);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CubicleSettings.TokenEnv, previous);
        }

        Assert.NotEmpty(stub.Posts);
        Assert.All(stub.Posts, p => Assert.Equal("Bearer fallback-token", p.Authorization));
    }

    [Fact]
    public async Task ReplPrintsNoticeForSteeringMessage()
    {
        using var stub = new CubicleStub();
        var polls = 0;
        stub.Handler = req => Task.FromResult(req.Method == "POST"
            ? StubResponse.Accepted()
            : ++polls == 2
                ? StubResponse.Steering(3, (3, "digital-scribe", "also say bye"))
                : StubResponse.Steering(polls == 1 ? 0 : 3));
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        File.WriteAllText(Path.Combine(tmp.Path, "settings.json"), """{"cubicle":{"token":"cubicle-token"}}""");
        var stream = new ScriptedStream().Then(Fakes.Text("hi")).Then(Fakes.Text("bye"));
        var (code, stdout, stderr) = await Run(
            ["--cubicle-url", stub.Url],
            tmp,
            stream,
            new StringReader("hello\n/quit\n"));

        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        var marker = "[steering] [steering from @digital-scribe] also say bye";
        Assert.Contains(marker, stdout, StringComparison.Ordinal);
        Assert.True(stdout.IndexOf(marker, StringComparison.Ordinal) < stdout.IndexOf("bye", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithoutConfigurationNothingIsPosted()
    {
        using var stub = new CubicleStub();
        using var tmp = new TempWorkspace();
        WriteModel(tmp);
        var (code, _, _) = await Run(["-p", "--no-session", "ping"], tmp);
        Assert.Equal(0, code);
        Assert.Empty(stub.Requests);
    }

    private static void WriteModel(TempWorkspace tmp)
    {
        File.WriteAllText(Path.Combine(tmp.Path, "models.json"), """
            {"models":[{"id":"test-model","baseUrl":"http://localhost:9/v1","apiKey":"dummy","contextWindow":32768,"maxOutputTokens":8192}]}
            """);
    }

    private static async Task<(int Code, string Stdout, string Stderr)> Run(
        string[] argv,
        TempWorkspace tmp,
        ScriptedStream? stream = null,
        TextReader? input = null)
    {
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
        };
        var code = await host.RunAsync(argv);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
