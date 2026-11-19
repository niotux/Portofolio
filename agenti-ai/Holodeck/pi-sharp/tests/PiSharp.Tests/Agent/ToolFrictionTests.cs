using System.Text.Json.Nodes;
using PiSharp.Core.Agent;
using PiSharp.Core.Notebook;
using PiSharp.Tests.Tools;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Agent;

/// <summary>The argument shapes and tool errors seats hit most (session logs, 48 h to 2026-09-27), made to work.</summary>
public class ToolFrictionTests
{
    private static DelegateTool Tool(string schema) => new() { Parameters = Json(schema) };

    private const string ChatPost = """
        {"type":"object","properties":{"body":{"type":"string"},"room":{"type":["string","null"]},"thread":{"type":["integer","null"]},
        "to":{"type":["string","null"]},"about":{"type":["array","null"],"items":{"type":["string","null"]}}},"required":["body"]}
        """;

    private const string KbTransition = """
        {"type":"object","properties":{"work_item":{"type":"string"},"to_status":{"type":"string"},"assign_to":{"type":["string","null"]},
        "note":{"type":["string","null"]}},"required":["work_item","to_status"]}
        """;

    private const string KbWrite = """
        {"type":"object","properties":{"op":{"type":"string"},
        "entity":{"type":["object","null"],"properties":{"kind":{"type":["string","null"]},"slug":{"type":["string","null"]}},"required":["kind"]},
        "doc":{"type":["object","null"],"properties":{"body":{"type":["string","null"]},"entity":{"type":["string","null"]},"key":{"type":["string","null"]},
        "title":{"type":["string","null"]}},"required":["entity","body"]}},"required":["op"]}
        """;

    [Theory]
    [InlineData("\"wi-12\"", "[\"wi-12\"]")]
    [InlineData("\"wi-12, pi-sharp\"", "[\"wi-12\",\"pi-sharp\"]")]
    [InlineData("\"[\\\"wi-12\\\"]\"", "[\"wi-12\"]")]
    [InlineData("[\"wi-12\"]", "[\"wi-12\"]")]
    public void AScalarWhereAListIsExpectedBecomesTheList(string about, string expected)
    {
        var result = ToolArgumentValidator.Validate(Tool(ChatPost), Json($$"""{"body":"x","room":"board","about":{{about}}}"""));
        Assert.Equal(expected, result["about"]!.ToJsonString());
    }

    [Fact]
    public void ChannelIsTheRoom()
    {
        var result = ToolArgumentValidator.Validate(Tool(ChatPost), Json("""{"body":"x","channel":"board"}"""));
        Assert.Equal("board", result["room"]!.GetValue<string>());
        Assert.False(result.ContainsKey("channel"));
    }

    [Fact]
    public void ToStaysTheRecipientWhereTheSchemaHasTo()
    {
        var result = ToolArgumentValidator.Validate(Tool(ChatPost), Json("""{"body":"x","to":"coder-06-pi3"}"""));
        Assert.Equal("coder-06-pi3", result["to"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"slug":"wi-12","status":"done","assignTo":"coder-06-pi3"}""")]
    [InlineData("""{"id":"wi-12","to":"done","assignee":"coder-06-pi3"}""")]
    [InlineData("""{"workItem":"wi-12","toStatus":"done","assign_to":"coder-06-pi3"}""")]
    public void KbTransitionAliasesMapToTheSchemaNames(string args)
    {
        var result = ToolArgumentValidator.Validate(Tool(KbTransition), Json(args));
        Assert.Equal("wi-12", result["work_item"]!.GetValue<string>());
        Assert.Equal("done", result["to_status"]!.GetValue<string>());
        Assert.Equal("coder-06-pi3", result["assign_to"]!.GetValue<string>());
    }

    [Fact]
    public void AnAliasNeverOverwritesTheRealName()
    {
        var result = ToolArgumentValidator.Validate(Tool(KbTransition), Json("""{"work_item":"wi-12","slug":"wi-99","to_status":"done"}"""));
        Assert.Equal("wi-12", result["work_item"]!.GetValue<string>());
    }

    [Fact]
    public void AFlatDocWriteFoldsIntoTheDocPayload()
    {
        var result = ToolArgumentValidator.Validate(Tool(KbWrite), Json("""{"op":"doc","entity":"wi-12","title":"Handover","body":"state"}"""));
        Assert.False(result.ContainsKey("entity"));
        Assert.Equal("wi-12", result["doc"]!["entity"]!.GetValue<string>());
        Assert.Equal("Handover", result["doc"]!["title"]!.GetValue<string>());
        Assert.Equal("state", result["doc"]!["body"]!.GetValue<string>());
    }

    [Fact]
    public void AnEntityWriteIsLeftAlone()
    {
        var result = ToolArgumentValidator.Validate(Tool(KbWrite), Json("""{"op":"entity","entity":{"kind":"idea","slug":"x"}}"""));
        Assert.Equal("idea", result["entity"]!["kind"]!.GetValue<string>());
        Assert.False(result.ContainsKey("doc"));
    }

    [Fact]
    public async Task NotebookTransitionNeedsNoRevisionAndNotFoundNamesTheClosestIds()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        store.Put("commit-wi-12-tests", "commitment", "run tests", "chat-1", "open", 0);
        store.Put("note-other", "note", "x", "chat-1", "open", 0);
        var tool = new NotebookTool(store);

        var ok = await tool.ExecuteAsync("t", new() { ["op"] = "transition", ["id"] = "commit-wi-12-tests", ["status"] = "completed" }, null, default);
        Assert.False(ok.IsError);
        Assert.Equal("completed", store.Get("commit-wi-12-tests")!["status"]!.GetValue<string>());
        var again = await tool.ExecuteAsync("t", new() { ["op"] = "transition", ["id"] = "commit-wi-12-tests", ["status"] = "completed" }, null, default);
        Assert.False(again.IsError);
        Assert.Equal(2, store.Get("commit-wi-12-tests")!["revision"]!.GetValue<long>());

        var missing = await tool.ExecuteAsync("t", new() { ["op"] = "get", ["id"] = "commitment-wi-12" }, null, default);
        Assert.True(missing.IsError);
        Assert.Contains("Closest IDs: commit-wi-12-tests", Text(missing));
    }

    [Fact]
    public async Task NotebookPutConflictShowsTheCurrentRecord()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        store.Put("plan", "note", "v1", "chat-1", "open", 0);
        var tool = new NotebookTool(store);
        var conflict = await tool.ExecuteAsync("t", new() { ["op"] = "put", ["id"] = "plan", ["text"] = "v2", ["expectedRevision"] = 0L }, null, default);
        Assert.True(conflict.IsError);
        Assert.True(Text(conflict).Contains("\"revision\":1"), Text(conflict));
        Assert.Contains("That ID is taken", Text(conflict));
    }

    private static readonly string BigSchema = "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\",\"description\":\"" + new string('d', ToolDiscovery.MaxSchemaCharacters * 3 / 8) + "\"}}}";

    [Fact]
    public void AFullSchemaBudgetReleasesTheOldestPromotionAtARebase()
    {
        var a = new DelegateTool { Name = "kb__alpha", Description = "alpha", Parameters = Json(BigSchema) };
        var b = new DelegateTool { Name = "kb__beta", Description = "beta", Parameters = Json(BigSchema) };
        var c = new DelegateTool { Name = "kb__gamma", Description = "gamma", Parameters = Json(BigSchema) };
        IReadOnlyList<IAgentTool> tools = [a, b, c];
        var discovery = new ToolDiscovery(() => tools);
        tools = [a, b, c, discovery];

        foreach (var used in new[] { a, b, c }) discovery.NoteUse(used.Name);
        Assert.False(discovery.IsActive(a)); // nothing changes mid-task
        discovery.Observe(10);
        discovery.Observe(2); // the history shrank: compaction, fresh task or rewind

        Assert.False(discovery.IsActive(a));
        Assert.True(discovery.IsActive(b));
        Assert.True(discovery.IsActive(c));
    }

    [Theory]
    [InlineData("plan", "plan")]
    [InlineData("session.plan", "plan")]
    [InlineData("session__plan", "plan")]
    [InlineData("kb.alpha", "kb__alpha")]
    [InlineData("kb__alpha", "kb__alpha")]
    public void ToolkitCallFindsAToolByTheQualifiedNamesModelsWrite(string asked, string found)
    {
        // pi5 and pi7, 2026-10-26: the plan tool behind toolkit was asked for as "session.plan" and "session__plan" and refused.
        var plan = new DelegateTool { Name = "plan", Description = "the working plan" };
        var alpha = new DelegateTool { Name = "kb__alpha", Description = "alpha" };
        IReadOnlyList<IAgentTool> tools = [plan, alpha];
        var discovery = new ToolDiscovery(() => tools);
        tools = [plan, alpha, discovery];

        var (tool, _, error) = discovery.Dispatch(new JsonObject { ["op"] = "call", ["name"] = asked });

        Assert.Null(error);
        Assert.Equal(found, tool!.Name);
        Assert.NotNull(discovery.Dispatch(new JsonObject { ["op"] = "call", ["name"] = "session.nothing" }).Error);
    }

    [Fact]
    public void ThePlanToolIsCoreSoItIsAdvertisedWhileTheSeatHasIt() => Assert.True(ToolDiscovery.IsCore("plan"));

    [Fact]
    public async Task OpenSearchAndCloseNeverChangeTheAdvertisedListMidTask()
    {
        var a = new DelegateTool { Name = "kb__alpha", Description = "shared alpha", Parameters = Json(BigSchema) };
        var b = new DelegateTool { Name = "kb__beta", Description = "shared beta" };
        var d = new DelegateTool { Name = "desktop__windows", Description = "capture a window" };
        IReadOnlyList<IAgentTool> tools = [a, b, d];
        var discovery = new ToolDiscovery(() => tools);
        tools = [a, b, d, discovery];
        var before = ToolCatalog.Present(tools, Model).Select(t => t.Name + t.Description).ToArray();
        Assert.Contains("- desktop: windows", discovery.Description);
        Assert.Contains("- kb: alpha, beta", discovery.Description);

        var kit = await discovery.ExecuteAsync("1", new JsonObject { ["op"] = "open", ["name"] = "kb" }, null, default);
        Assert.Contains("too large to show whole", Text(kit));
        Assert.Contains("- kb__beta: shared beta", Text(kit));
        var one = await discovery.ExecuteAsync("2", new JsonObject { ["op"] = "open", ["name"] = "kb__beta" }, null, default);
        Assert.Contains("args schema: {", Text(one));
        var found = await discovery.ExecuteAsync("3", new JsonObject { ["op"] = "search", ["query"] = "window" }, null, default);
        Assert.StartsWith("desktop__windows:", Text(found));
        Assert.True((await discovery.ExecuteAsync("4", new JsonObject { ["op"] = "open", ["name"] = "nope" }, null, default)).IsError);
        Assert.False((await discovery.ExecuteAsync("5", new JsonObject { ["op"] = "close", ["name"] = "desktop" }, null, default)).IsError);

        Assert.Equal(before, ToolCatalog.Present(tools, Model).Select(t => t.Name + t.Description));
        Assert.Equal(["toolkit"], ToolCatalog.Present(tools, Model).Select(t => t.Name));
    }

    [Fact]
    public async Task AClosedToolkitIsNotPromotedAtTheNextRebase()
    {
        var d = new DelegateTool { Name = "desktop__windows" };
        var k = new DelegateTool { Name = "kb__get" };
        IReadOnlyList<IAgentTool> tools = [d, k];
        var discovery = new ToolDiscovery(() => tools);
        tools = [d, k, discovery];
        discovery.NoteUse(d.Name);
        discovery.NoteUse(k.Name);
        await discovery.ExecuteAsync("c", new JsonObject { ["op"] = "close", ["name"] = "desktop" }, null, default);
        discovery.Rebase();
        Assert.Equal(["kb__get", "toolkit"], ToolCatalog.Present(tools, Model).Select(t => t.Name));
    }

    [Fact]
    public void ARestoredActivationSetOverBudgetIsTrimmedNotThrown()
    {
        using var tmp = new TempWorkspace();
        using var store = new NotebookStore(tmp.Path, "seat1");
        store.Put("runtime-tool-activation", "attempt", """["kb__alpha","kb__beta","kb__gamma"]""", "", "open", 0);
        var a = new DelegateTool { Name = "kb__alpha", Description = "alpha", Parameters = Json(BigSchema) };
        var b = new DelegateTool { Name = "kb__beta", Description = "beta", Parameters = Json(BigSchema) };
        var c = new DelegateTool { Name = "kb__gamma", Description = "gamma", Parameters = Json(BigSchema) };
        IReadOnlyList<IAgentTool> tools = [a, b, c];
        var discovery = new ToolDiscovery(() => tools);
        discovery.Restore(store);

        var presented = ToolCatalog.Present([a, b, c, discovery], Model).Select(t => t.Name).ToArray();
        Assert.Equal(new[] { "kb__beta", "kb__gamma", "toolkit" }, presented);
        Assert.Equal("""["kb__beta","kb__gamma"]""", store.Get("runtime-tool-activation")!["text"]!.GetValue<string>());
    }

    private static string Text(AgentToolResult result) => string.Concat(result.Content.OfType<PiSharp.Core.Messages.TextContent>().Select(t => t.Text));
}
