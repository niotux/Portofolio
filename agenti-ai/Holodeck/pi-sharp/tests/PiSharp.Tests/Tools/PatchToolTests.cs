using System.Text.Json.Nodes;
using PiSharp.Core.Messages;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public sealed class PatchToolTests
{
    private static Task<PiSharp.Core.Agent.AgentToolResult> Apply(PatchTool tool, string patch) =>
        tool.ExecuteAsync("t1", new JsonObject { ["input"] = patch.Replace("\r\n", "\n") }, null, CancellationToken.None);

    private static string Text(PiSharp.Core.Agent.AgentToolResult result) => ((TextContent)result.Content[0]).Text;

    [Fact]
    public async Task AddUpdateDeleteInOnePatch()
    {
        using var ws = new TempWorkspace();
        var keep = ws.File("keep.txt", "one\ntwo\nthree\nfour\n");
        var gone = ws.File("gone.txt", "bye\n");
        var result = await Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Add File: sub/new.txt
            +hello
            +world
            *** Update File: keep.txt
            @@
             one
            -two
            +TWO
             three
            *** Delete File: gone.txt
            *** End Patch
            """);

        Assert.Equal("hello\nworld\n", File.ReadAllText(Path.Combine(ws.Path, "sub", "new.txt")));
        Assert.Equal("one\nTWO\nthree\nfour\n", File.ReadAllText(keep));
        Assert.False(File.Exists(gone));
        Assert.Equal("Success. Updated the following files:\nA sub/new.txt\nM keep.txt\nD gone.txt", Text(result));
        var details = Assert.IsType<JsonObject>(result.Details);
        Assert.Contains("+2 TWO", details["diff"]!.GetValue<string>());
        Assert.Contains("--- keep.txt", details["patch"]!.GetValue<string>());
    }

    [Fact]
    public async Task SeekLineAndMultipleHunks()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("a.cs", "class A {\n  void F() {\n    x = 1;\n  }\n  void G() {\n    x = 1;\n  }\n}\n");
        await Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Update File: a.cs
            @@ void G() {
            -    x = 1;
            +    x = 2;
            *** End Patch
            """);
        // The @@ line picks G's body although F's line is identical and comes first.
        Assert.Equal("class A {\n  void F() {\n    x = 1;\n  }\n  void G() {\n    x = 2;\n  }\n}\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task EndOfFileAnchorsAtTheEnd()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "x\ny\nx\n");
        await Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Update File: f.txt
            @@
            -x
            +z
            *** End of File
            *** End Patch
            """);
        Assert.Equal("x\ny\nz\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task TrailingWhitespaceFallbackKeepsFileContextBytes()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "a   \nb\nc\n");
        await Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Update File: f.txt
            @@
             a
            -b
            +B
            *** End Patch
            """);
        Assert.Equal("a   \nB\nc\n", File.ReadAllText(file));
    }

    [Fact]
    public async Task PreservesCrlfAndBom()
    {
        using var ws = new TempWorkspace();
        var file = Path.Combine(ws.Path, "f.txt");
        File.WriteAllBytes(file, [0xEF, 0xBB, 0xBF, .. "a\r\nb\r\n"u8.ToArray()]);
        await Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Update File: f.txt
            @@
            -b
            +c
            *** End Patch
            """);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "a\r\nc\r\n"u8.ToArray()], File.ReadAllBytes(file));
    }

    [Fact]
    public async Task MoveRenamesAndEdits()
    {
        using var ws = new TempWorkspace();
        var from = ws.File("old.txt", "a\nb\n");
        var result = await Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Update File: old.txt
            *** Move to: dir/new.txt
            @@
             a
            -b
            +c
            *** End Patch
            """);
        Assert.False(File.Exists(from));
        Assert.Equal("a\nc\n", File.ReadAllText(Path.Combine(ws.Path, "dir", "new.txt")));
        Assert.Contains("M dir/new.txt (moved from old.txt)", Text(result));
    }

    [Fact]
    public async Task FailingHunkChangesNothingAndNamesTheHunk()
    {
        using var ws = new TempWorkspace();
        var a = ws.File("a.txt", "one\n");
        var b = ws.File("b.txt", "two\nthree\n");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Add File: c.txt
            +new
            *** Update File: a.txt
            @@
            -one
            +ONE
            *** Update File: b.txt
            @@
            -two
            +TWO
            @@
            -missing
            +x
            *** End Patch
            """));

        Assert.Contains("nothing was changed", ex.Message);
        Assert.Contains("Update File b.txt: hunk 2 of 2", ex.Message);
        Assert.Contains("missing", ex.Message);
        Assert.Equal("one\n", File.ReadAllText(a));
        Assert.Equal("two\nthree\n", File.ReadAllText(b));
        Assert.False(File.Exists(Path.Combine(ws.Path, "c.txt")));
    }

    [Fact]
    public async Task GuardRefusesUnreadFileWhenWiredByFactory()
    {
        using var ws = new TempWorkspace();
        var file = ws.File("f.txt", "a\n");
        var tools = BuiltinTools.Create(ws.Path);
        var patch = (PatchTool)tools.Single(t => t.Name == "apply_patch");
        const string Patch = """
            *** Begin Patch
            *** Update File: f.txt
            @@
            -a
            +b
            *** End Patch
            """;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(patch, Patch));
        Assert.Contains("has not been read", ex.Message);

        await ToolRun.Exec(tools.Single(t => t.Name == "read"), """{"path":"f.txt"}""");
        await Apply(patch, Patch);
        Assert.Equal("b\n", File.ReadAllText(file));
        // apply_patch recorded what it wrote, so edit may follow without a new read.
        await ToolRun.Exec(tools.Single(t => t.Name == "edit"), """{"path":"f.txt","edits":[{"oldText":"b","newText":"c"}]}""");
    }

    [Theory]
    [InlineData("*** Update File: f.txt\n@@\n-a\n+b\n*** End Patch", "first line")]
    [InlineData("*** Begin Patch\n*** Update File: f.txt\n@@\n-a\n+b", "last line")]
    [InlineData("*** Begin Patch\n*** Frobnicate File: f.txt\n*** End Patch", "expected '*** Add File:'")]
    [InlineData("*** Begin Patch\n*** Add File: f.txt\nno plus\n*** End Patch", "starts with '+'")]
    [InlineData("*** Begin Patch\n*** Update File: f.txt\n@@\n a\n*** End Patch", "changes nothing")]
    [InlineData("*** Begin Patch\n*** End Patch", "no file operations")]
    public void ParseRejects(string patch, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PatchTool.Parse(patch));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void ParseAcceptsHeredocWrapperAndBareFirstHunk()
    {
        var ops = PatchTool.Parse("apply_patch <<'EOF'\n*** Begin Patch\n*** Update File: f.txt\n-a\n+b\n*** End Patch\nEOF\n".Replace("apply_patch ", ""));
        var op = Assert.Single(ops);
        Assert.Equal("f.txt", op.Path);
        Assert.Single(op.Hunks);
    }

    [Fact]
    public async Task DuplicatePathRejected()
    {
        using var ws = new TempWorkspace();
        ws.File("f.txt", "a\n");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(new PatchTool(ws.Path), """
            *** Begin Patch
            *** Delete File: f.txt
            *** Add File: f.txt
            +x
            *** End Patch
            """));
        Assert.Contains("more than once", ex.Message);
    }

    [Fact]
    public void PrepareArgumentsAcceptsPatchAlias()
    {
        var prepared = new PatchTool(".").PrepareArguments(new JsonObject { ["patch"] = "*** Begin Patch" });
        Assert.Equal("*** Begin Patch", prepared!["input"]!.GetValue<string>());
    }
}
