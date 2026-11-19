using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Config;
using PiSharp.Core.Mcp;
using PiSharp.Core.Models;
using PiSharp.Core.Session;
using PiSharp.Core.Skills;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Cli;

public class SlashCommandTests
{
    [Fact]
    public void MapsBuiltins()
    {
        var state = State();
        Assert.IsType<SlashQuit>(SlashCommands.Handle("/quit", state));
        Assert.IsType<SlashCompact>(SlashCommands.Handle("/compact", state));
        Assert.IsType<SlashClear>(SlashCommands.Handle("/clear", state));
        Assert.IsType<SlashUnknown>(SlashCommands.Handle("/nope", state));
        var help = Assert.IsType<SlashMessage>(SlashCommands.Handle("/help", state));
        Assert.Contains("/model", help.Text);
    }

    [Fact]
    public void ModelSwitchAndThink()
    {
        var other = new ModelConfig { Id = "other", BaseUrl = "http://x/v1" };
        var state = State(other);
        var change = Assert.IsType<SlashModelChange>(SlashCommands.Handle("/model other", state));
        Assert.Equal("other", change.Model.Id);

        var listed = Assert.IsType<SlashMessage>(SlashCommands.Handle("/model", state));
        Assert.Contains("test-model", listed.Text);

        var think = Assert.IsType<SlashMessage>(SlashCommands.Handle("/think high", state));
        Assert.Equal(ThinkingLevel.High, state.Thinking);
        Assert.Contains("high", think.Text);
    }

    [Fact]
    public void ExpandsPromptTemplates()
    {
        var state = State();
        state = state with
        {
            Templates = [new PromptTemplate("review", "Review", "Look at $1", "/t/review.md")],
        };
        var prompt = Assert.IsType<SlashPrompt>(SlashCommands.Handle("/review src/a.cs", state));
        Assert.Equal("Look at src/a.cs", prompt.Text);
    }

    [Fact]
    public void SessionAndToolsAndMcp()
    {
        var state = State();
        Assert.Contains("session", Assert.IsType<SlashMessage>(SlashCommands.Handle("/session", state)).Text);
        Assert.Contains("No tools", Assert.IsType<SlashMessage>(SlashCommands.Handle("/tools", state)).Text);
        Assert.Contains("No MCP servers configured", Assert.IsType<SlashMessage>(SlashCommands.Handle("/mcp", state)).Text);
        var reconnect = Assert.IsType<SlashMcpReconnect>(SlashCommands.Handle("/mcp reconnect chat", state));
        Assert.Equal("chat", reconnect.Alias);
    }

    [Fact]
    public void EffectiveSettingsShowsInstalledStateWithoutCredentials()
    {
        var state = State() with { Settings = new Settings { MaxJobs = 7, ToolCallTimeoutSeconds = 0, Factory = new FactorySettings { Token = "secret-marker" } } };
        var text = Assert.IsType<SlashMessage>(SlashCommands.Handle("/settings", state)).Text;
        Assert.Contains("maxJobs: 7", text);
        Assert.Contains("toolCallTimeoutSeconds: 900", text);
        Assert.Contains("taskTool: False", text);
        Assert.DoesNotContain("secret-marker", text);
    }

    private static CliState State(ModelConfig? extra = null)
    {
        var models = extra is null ? new List<ModelConfig> { Fakes.Model } : [Fakes.Model, extra];
        var session = SessionManager.InMemory("/tmp");
        var context = new AgentContext();
        return new CliState
        {
            Cwd = "/tmp",
            Model = Fakes.Model,
            Thinking = ThinkingLevel.Off,
            Models = models,
            Skills = [],
            Templates = [],
            Mcp = new McpManager(),
            Agent = new AgentSession(session, context),
            Context = context,
            Settings = new Settings(),
            Stream = new ScriptedStream().Then(Fakes.Text("ok")).Stream,
        };
    }
}
