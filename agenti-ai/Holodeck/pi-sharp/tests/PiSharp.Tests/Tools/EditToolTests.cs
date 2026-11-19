using System.Text.Json.Nodes;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class EditToolTests
{
    [Fact]
    public async Task ReplacesAndReturnsDiff()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "hello world\n");
        var tool = new EditTool(tmp.Path);
        var result = await ToolRun.Exec(tool, """
            {"path":"a.txt","edits":[{"oldText":"world","newText":"there"}]}
            """);
        Assert.Equal("Successfully replaced 1 block(s) in a.txt.", result.Text);
        Assert.Equal("hello there\n", File.ReadAllText(Path.Combine(tmp.Path, "a.txt")));
        Assert.Contains("hello", result.Details!["diff"]!.GetValue<string>());
        Assert.Contains("patch", result.Details.AsObject().Select(p => p.Key));
        Assert.Equal(1, (int)result.Details["firstChangedLine"]!);
    }

    [Fact]
    public async Task PreservesBomAndCrlf()
    {
        using var tmp = new TempWorkspace();
        var path = Path.Combine(tmp.Path, "bom.txt");
        var payload = "one\r\ntwo\r\n"u8.ToArray();
        var bomFile = new byte[3 + payload.Length];
        bomFile[0] = 0xEF; bomFile[1] = 0xBB; bomFile[2] = 0xBF;
        payload.CopyTo(bomFile.AsSpan(3));
        File.WriteAllBytes(path, bomFile);
        var tool = new EditTool(tmp.Path);
        await ToolRun.Exec(tool, """
            {"path":"bom.txt","edits":[{"oldText":"two","newText":"TWO"}]}
            """);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("\uFEFF", text);
        Assert.Contains("\r\n", text);
        Assert.Contains("TWO", text);
    }

    [Fact]
    public void PrepareArgumentsLiftsLegacyAndStringEdits()
    {
        var tool = new EditTool("/tmp");

        var legacy = ToolRun.Json("""{"path":"f","oldText":"a","newText":"b"}""");
        var prepared = tool.PrepareArguments(legacy)!.AsObject();
        Assert.Single(prepared["edits"]!.AsArray());
        Assert.Null(prepared["oldText"]);

        var asString = ToolRun.Json("""{"path":"f","edits":"[{\"oldText\":\"a\",\"newText\":\"b\"}]"}""");
        var fromString = tool.PrepareArguments(asString)!.AsObject();
        Assert.Equal("a", fromString["edits"]![0]!["oldText"]!.GetValue<string>());

        var single = ToolRun.Json("""{"path":"f","edits":{"oldText":"a","newText":"b"}}""");
        var fromSingle = tool.PrepareArguments(single)!.AsObject();
        Assert.IsType<JsonArray>(fromSingle["edits"]);
        Assert.Single(fromSingle["edits"]!.AsArray());
    }

    [Fact]
    public async Task MissingFile()
    {
        using var tmp = new TempWorkspace();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(new EditTool(tmp.Path), """{"path":"nope","edits":[{"oldText":"a","newText":"b"}]}"""));
        Assert.Contains("Could not edit file: nope", ex.Message);
        Assert.Contains("ENOENT", ex.Message);
    }
}
