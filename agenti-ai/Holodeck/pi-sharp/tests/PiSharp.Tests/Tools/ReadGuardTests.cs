using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

/// <summary>Read-before-edit guard wired by <see cref="BuiltinTools.Create"/>.</summary>
public sealed class ReadGuardTests
{
    private static IAgentTool Tool(IReadOnlyList<IAgentTool> tools, string name) => tools.Single(t => t.Name == name);

    [Fact]
    public async Task EditOfUnreadFileIsRefused()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "alpha\n");
        var tools = BuiltinTools.Create(ws.Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(Tool(tools, "edit"), """{"path":"f.txt","edits":[{"oldText":"alpha","newText":"beta"}]}"""));
        Assert.Contains("has not been read", ex.Message);
        Assert.Contains("Read it first", ex.Message);
        Assert.Equal("alpha\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task EditAfterReadSucceedsAndChainsWithoutRereading()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "alpha\nbeta\n");
        var tools = BuiltinTools.Create(ws.Path);

        await ToolRun.Exec(Tool(tools, "read"), """{"path":"f.txt"}""");
        await ToolRun.Exec(Tool(tools, "edit"), """{"path":"f.txt","edits":[{"oldText":"alpha","newText":"ALPHA"}]}""");
        // The edit recorded what it wrote, so a second edit needs no new read.
        await ToolRun.Exec(Tool(tools, "edit"), """{"path":"f.txt","edits":[{"oldText":"beta","newText":"BETA"}]}""");
        Assert.Equal("ALPHA\nBETA\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task PartialReadCounts()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "one\ntwo\nthree\n");
        var tools = BuiltinTools.Create(ws.Path);

        await ToolRun.Exec(Tool(tools, "read"), """{"path":"f.txt","offset":2,"limit":1}""");
        await ToolRun.Exec(Tool(tools, "edit"), """{"path":"f.txt","edits":[{"oldText":"three","newText":"3"}]}""");
        Assert.Equal("one\ntwo\n3\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task EditAfterExternalChangeIsRefused()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "alpha\n");
        var tools = BuiltinTools.Create(ws.Path);

        await ToolRun.Exec(Tool(tools, "read"), """{"path":"f.txt"}""");
        File.WriteAllText(file, "alpha changed\n");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(Tool(tools, "edit"), """{"path":"f.txt","edits":[{"oldText":"alpha","newText":"beta"}]}"""));
        Assert.Contains("changed on disk", ex.Message);
        Assert.Contains("Read it again", ex.Message);
        Assert.Equal("alpha changed\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task WriteOfNewFileIsAllowed()
    {
        using var ws = new TempWorkspace();
        var tools = BuiltinTools.Create(ws.Path);

        await ToolRun.Exec(Tool(tools, "write"), """{"path":"new/f.txt","content":"hi"}""");
        Assert.Equal("hi", File.ReadAllText(Path.Combine(ws.Path, "new", "f.txt")));
        // The write recorded it, so an edit may follow.
        await ToolRun.Exec(Tool(tools, "edit"), """{"path":"new/f.txt","edits":[{"oldText":"hi","newText":"ho"}]}""");
    }

    [Fact]
    public async Task OverwriteWithoutReadIsRefused()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "keep");
        var tools = BuiltinTools.Create(ws.Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(Tool(tools, "write"), """{"path":"f.txt","content":"gone"}"""));
        Assert.Contains("has not been read", ex.Message);
        Assert.Equal("keep", File.ReadAllText(file));

        await ToolRun.Exec(Tool(tools, "read"), """{"path":"f.txt"}""");
        await ToolRun.Exec(Tool(tools, "write"), """{"path":"f.txt","content":"gone"}""");
        Assert.Equal("gone", File.ReadAllText(file));
    }

    [Fact]
    public async Task StandaloneToolsHaveNoGuard()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "alpha");
        await ToolRun.Exec(new EditTool(ws.Path), """{"path":"f.txt","edits":[{"oldText":"alpha","newText":"beta"}]}""");
        Assert.Equal("beta", File.ReadAllText(file));
    }

    [Fact]
    public async Task FuzzyIsOffByDefaultAndOptIn()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "say “hello”\n");
        var edit = new EditTool(ws.Path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(edit, """{"path":"f.txt","edits":[{"oldText":"say \"hello\"","newText":"say \"hi\""}]}"""));
        var result = await ToolRun.Exec(edit, """{"path":"f.txt","fuzzy":true,"edits":[{"oldText":"say \"hello\"","newText":"say \"hi\""}]}""");
        Assert.Contains("Successfully", ((TextContent)result.Content[0]).Text);
        Assert.Equal("say \"hi\"\n", File.ReadAllText(file));
    }
}
