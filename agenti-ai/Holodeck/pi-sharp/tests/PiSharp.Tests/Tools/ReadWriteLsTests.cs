using PiSharp.Core.Messages;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class ReadWriteLsTests
{
    [Fact]
    public async Task WriteThenReadRoundTrip()
    {
        using var tmp = new TempWorkspace();
        var write = new WriteTool(tmp.Path);
        var read = new ReadTool(tmp.Path);

        var written = await ToolRun.Exec(write, """{"path":"hello.txt","content":"line1\nline2\n"}""");
        Assert.Equal("Successfully wrote to hello.txt", written.Text);

        var result = await ToolRun.Exec(read, """{"path":"hello.txt"}""");
        Assert.Equal("line1\nline2\n", result.Text);
    }

    [Fact]
    public async Task ReadOffsetLimitNotice()
    {
        using var tmp = new TempWorkspace();
        tmp.File("n.txt", string.Join('\n', Enumerable.Range(1, 10).Select(i => $"L{i}")));
        var read = new ReadTool(tmp.Path);

        var result = await ToolRun.Exec(read, """{"path":"n.txt","offset":2,"limit":3}""");
        Assert.Contains("L2", result.Text);
        Assert.Contains("L4", result.Text);
        Assert.Contains("more lines in file. Use offset=5 to continue.", result.Text);
    }

    [Fact]
    public async Task ReadOffsetPastEndThrows()
    {
        using var tmp = new TempWorkspace();
        tmp.File("n.txt", "a\nb\n");
        var read = new ReadTool(tmp.Path);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(read, """{"path":"n.txt","offset":99}"""));
        Assert.Contains("beyond end of file", ex.Message);
    }

    [Fact]
    public async Task ReadMissingFileThrows()
    {
        using var tmp = new TempWorkspace();
        var read = new ReadTool(tmp.Path);
        await Assert.ThrowsAsync<FileNotFoundException>(() => ToolRun.Exec(read, """{"path":"nope.txt"}"""));
    }

    [Fact]
    public async Task ReadImageAttachesAndNonVisionNote()
    {
        using var tmp = new TempWorkspace();
        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01 };
        File.WriteAllBytes(Path.Combine(tmp.Path, "pic.jpg"), jpeg);

        var vision = await ToolRun.Exec(new ReadTool(tmp.Path, supportsImages: true), """{"path":"pic.jpg"}""");
        Assert.Contains("Read image file [image/jpeg]", vision.Text);
        Assert.Contains(vision.Content, p => p is ImageContent);

        var noVision = await ToolRun.Exec(new ReadTool(tmp.Path, supportsImages: false), """{"path":"pic.jpg"}""");
        Assert.Contains(ReadTool.NonVisionNote, noVision.Text);
        Assert.Contains(noVision.Content, p => p is ImageContent);
    }

    [Fact]
    public async Task ReadImageAddsLargeImageWarningToResultAndSink()
    {
        using var tmp = new TempWorkspace();
        var path = Path.Combine(tmp.Path, "large.png");
        File.WriteAllBytes(path, PngHeader(2048, 2048));
        var warnings = new List<string>();
        var read = new ReadTool(tmp.Path)
        {
            WarningPixels = 4_000_000,
            ContextWindow = 200_000,
            WarningSink = warnings.Add,
        };

        var result = await ToolRun.Exec(read, "{\"path\":\"large.png\"}");

        Assert.Contains("note: image is 2048x2048", result.Text);
        Assert.Single(warnings);
        Assert.Equal(warnings[0], result.Text.Split("\n").Last());
        Assert.Contains(result.Content, p => p is ImageContent);
    }

    [Fact]
    public async Task LsSortsAndMarksDirectories()
    {
        using var tmp = new TempWorkspace();
        tmp.File("b.txt", "b");
        tmp.File("a.txt", "a");
        tmp.Dir("sub");
        tmp.File(".hidden", "h");

        var result = await ToolRun.Exec(new LsTool(tmp.Path), "{}");
        var lines = result.Text.Split('\n');
        Assert.Contains(".hidden", lines);
        Assert.Contains("a.txt", lines);
        Assert.Contains("b.txt", lines);
        Assert.Contains("sub/", lines);
        Assert.True(Array.IndexOf(lines, "a.txt") < Array.IndexOf(lines, "b.txt"));
    }

    [Fact]
    public async Task LsEmptyDirectory()
    {
        using var tmp = new TempWorkspace();
        tmp.Dir("empty");
        var result = await ToolRun.Exec(new LsTool(tmp.Path), """{"path":"empty"}""");
        Assert.Equal("(empty directory)", result.Text);
    }

    [Fact]
    public async Task LsEntryLimitNotice()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a", "a");
        tmp.File("b", "b");
        tmp.File("c", "c");
        var result = await ToolRun.Exec(new LsTool(tmp.Path), """{"limit":2}""");
        Assert.Contains("2 entries limit reached. Use limit=4 for more", result.Text);
        Assert.Equal(2, (int)result.Details!["entryLimitReached"]!);
    }

    [Fact]
    public async Task WriteCreatesParents()
    {
        using var tmp = new TempWorkspace();
        await ToolRun.Exec(new WriteTool(tmp.Path), """{"path":"nested/dir/f.txt","content":"x"}""");
        Assert.Equal("x", File.ReadAllText(Path.Combine(tmp.Path, "nested", "dir", "f.txt")));
    }

    // The size warning is model-facing advice (crop or downscale), so it is skipped for a model
    // that cannot take images. The image is still sent; only the warning is suppressed.
    [Fact]
    public async Task ReadImageSkipsSizeWarningWhenModelCannotTakeImages()
    {
        using var tmp = new TempWorkspace();
        var path = Path.Combine(tmp.Path, "large.png");
        File.WriteAllBytes(path, PngHeader(2048, 2048));
        var warnings = new List<string>();
        var read = new ReadTool(tmp.Path, supportsImages: false)
        {
            WarningPixels = 4_000_000,
            ContextWindow = 200_000,
            WarningSink = warnings.Add,
        };

        var result = await ToolRun.Exec(read, """{"path":"large.png"}""");

        Assert.DoesNotContain("note: image is", result.Text);
        Assert.Empty(warnings);
        Assert.Contains(ReadTool.NonVisionNote, result.Text.Split("\n").Last());
        Assert.Contains(result.Content, p => p is ImageContent);
    }

    private static byte[] PngHeader(int width, int height) =>
        Convert.FromBase64String(ImageFixtures.Png(width, height).Data);
}
