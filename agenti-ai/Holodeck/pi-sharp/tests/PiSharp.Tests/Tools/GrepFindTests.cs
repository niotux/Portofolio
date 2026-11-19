using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class GrepFindTests
{
    // The schemas declare counts as "number", so models send 50.0, "50" or 2.5 for them; the
    // int? records must take all of those instead of failing with "could not convert ... $.limit".
    [Theory]
    [InlineData("""{"pattern":"alpha","limit":1.0}""")]
    [InlineData("""{"pattern":"alpha","limit":"1"}""")]
    [InlineData("""{"pattern":"alpha","limit":"1.0"}""")]
    [InlineData("""{"pattern":"alpha","limit":1.4,"context":0.0}""")]
    public async Task GrepAcceptsNonIntegralNumbers(string json)
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "alpha\nbeta\nalpha2\n");
        var tool = new GrepTool(tmp.Path);
        var args = PiSharp.Core.Agent.ToolArgumentValidator.Validate(tool, ToolRun.Json(json));
        var result = await tool.ExecuteAsync("t1", args, null, CancellationToken.None);
        Assert.Contains("a.txt:1: alpha", result.Text);
        Assert.Contains("1 matches limit reached", result.Text);
    }

    [Fact]
    public async Task ReadFindLsAcceptNonIntegralNumbers()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "one\ntwo\nthree\n");
        var read = await ToolRun.Exec(new ReadTool(tmp.Path), """{"path":"a.txt","offset":2.0,"limit":"1"}""");
        Assert.Contains("two", read.Text);
        Assert.DoesNotContain("three", read.Text);
        var find = await ToolRun.Exec(new FindTool(tmp.Path), """{"pattern":"*.txt","limit":5.0}""");
        Assert.Contains("a.txt", find.Text);
        var ls = await ToolRun.Exec(new LsTool(tmp.Path), """{"limit":10.0}""");
        Assert.Contains("a.txt", ls.Text);
    }

    [Fact]
    public async Task GrepFindsMatchesWithPathAndLine()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "alpha\nbeta\nalpha2\n");
        tmp.File("b.txt", "gamma\n");
        var result = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"alpha"}""");
        Assert.Contains("a.txt:1: alpha", result.Text);
        Assert.Contains("a.txt:3: alpha2", result.Text);
        Assert.DoesNotContain("b.txt", result.Text);
    }

    [Fact]
    public async Task GrepNoMatches()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "hello");
        var result = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"zzz"}""");
        Assert.Equal("No matches found", result.Text);
    }

    [Fact]
    public async Task GrepRespectsGitignoreAndGlob()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".gitignore", "skip.txt\n");
        tmp.File("keep.ts", "needle");
        tmp.File("skip.txt", "needle");
        tmp.File("other.js", "needle");

        var ignored = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"needle"}""");
        Assert.Contains("keep.ts", ignored.Text);
        Assert.DoesNotContain("skip.txt", ignored.Text);

        var globbed = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"needle","glob":"*.ts"}""");
        Assert.Contains("keep.ts", globbed.Text);
        Assert.DoesNotContain("other.js", globbed.Text);
    }

    [Fact]
    public async Task GrepLiteralAndIgnoreCase()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "Hello (world)\n");
        var literal = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"(world)","literal":true}""");
        Assert.Contains("a.txt:1:", literal.Text);

        var folded = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"HELLO","ignoreCase":true}""");
        Assert.Contains("a.txt:1:", folded.Text);
    }

    [Fact]
    public async Task GrepMatchLimitNotice()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "x\nx\nx\n");
        var result = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","limit":1}""");
        Assert.Contains("1 matches limit reached. Use limit=2 for more, or refine pattern", result.Text);
        Assert.Equal(1, (int)result.Details!["matchLimitReached"]!);
    }

    [Fact]
    public async Task GrepContextLines()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "a\nMATCH\nb\n");
        var result = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"MATCH","context":1}""");
        Assert.Contains("a.txt-1- a", result.Text);
        Assert.Contains("a.txt:2: MATCH", result.Text);
        Assert.Contains("a.txt-3- b", result.Text);
    }

    [Fact]
    public async Task GrepFilesModeListsEachMatchingFileOnce()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "x\nx\n");
        tmp.File("b.txt", "none\n");
        tmp.File("c/d.txt", "x\n");
        var result = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","outputMode":"files"}""");
        Assert.Equal(["a.txt", "c/d.txt"], result.Text.Split('\n').Order());
    }

    [Fact]
    public async Task GrepCountModeCountsMatchingLinesPerFile()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "x\nx x\ny\n");
        tmp.File("b.txt", "x\n");
        var result = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","outputMode":"count"}""");
        Assert.Equal(["a.txt:2", "b.txt:1"], result.Text.Split('\n').Order());
    }

    [Fact]
    public async Task GrepOffsetPagesAndNamesTheNextOffset()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "x1\nx2\nx3\nx4\nx5\n");
        var first = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","limit":2}""");
        Assert.Contains("a.txt:2: x2", first.Text);
        Assert.Contains("more results: offset=2", first.Text);

        var second = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","limit":2,"offset":2}""");
        Assert.DoesNotContain("x2", second.Text);
        Assert.Contains("a.txt:3: x3", second.Text);
        Assert.Contains("a.txt:4: x4", second.Text);
        Assert.Contains("more results: offset=4", second.Text);

        var last = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","limit":2,"offset":4}""");
        Assert.Equal("a.txt:5: x5", last.Text);

        var files = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"x","outputMode":"files","offset":1}""");
        Assert.StartsWith("No more matches", files.Text);
    }

    [Fact]
    public async Task GrepTypeFiltersByExtension()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.cs", "needle");
        tmp.File("b.ts", "needle");
        tmp.File("c.yml", "needle");
        var cs = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"needle","type":"cs","outputMode":"files"}""");
        Assert.Equal("a.cs", cs.Text);
        var yaml = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"needle","type":"yaml","outputMode":"files"}""");
        Assert.Equal("c.yml", yaml.Text);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"needle","type":"cobol"}"""));
        Assert.Contains("Known types", ex.Message);
    }

    [Fact]
    public async Task GrepMultilineMatchesAcrossLines()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.cs", "class Foo\n{\n    void Bar() {}\n}\nclass Baz {}\n");
        var single = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"Foo\\s*\\{\\s*void"}""");
        Assert.Equal("No matches found", single.Text);

        var multi = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"Foo\\s*\\{\\s*void","multiline":true}""");
        Assert.Contains("a.cs:1: class Foo", multi.Text);
        Assert.Contains("a.cs:2: {", multi.Text);
        Assert.Contains("a.cs:3:     void Bar() {}", multi.Text);
        Assert.DoesNotContain("Baz", multi.Text);

        var anchored = await ToolRun.Exec(new GrepTool(tmp.Path), """{"pattern":"^class \\w+","multiline":true,"outputMode":"count"}""");
        Assert.Equal("a.cs:2", anchored.Text);
    }

    [Fact]
    public async Task FindByBasenameGlob()
    {
        using var tmp = new TempWorkspace();
        tmp.File("src/foo.ts", "");
        tmp.File("src/foo.js", "");
        tmp.File("bar.ts", "");
        var result = await ToolRun.Exec(new FindTool(tmp.Path), """{"pattern":"*.ts"}""");
        Assert.Contains("src/foo.ts", result.Text);
        Assert.Contains("bar.ts", result.Text);
        Assert.DoesNotContain("foo.js", result.Text);
    }

    [Fact]
    public async Task FindPathGlobAndGitignore()
    {
        using var tmp = new TempWorkspace();
        tmp.File(".gitignore", "secret/\n");
        tmp.File("src/a.spec.ts", "");
        tmp.File("other/a.spec.ts", "");
        tmp.File("secret/hidden.spec.ts", "");

        var result = await ToolRun.Exec(new FindTool(tmp.Path), """{"pattern":"src/**/*.spec.ts"}""");
        Assert.Contains("src/a.spec.ts", result.Text);
        Assert.DoesNotContain("other/", result.Text);
        Assert.DoesNotContain("secret/", result.Text);
    }

    [Fact]
    public async Task FindNoMatch()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "");
        var result = await ToolRun.Exec(new FindTool(tmp.Path), """{"pattern":"*.md"}""");
        Assert.Equal("No files found matching pattern", result.Text);
    }

    [Fact]
    public async Task FindResultLimitNotice()
    {
        using var tmp = new TempWorkspace();
        tmp.File("a.txt", "");
        tmp.File("b.txt", "");
        tmp.File("c.txt", "");
        var result = await ToolRun.Exec(new FindTool(tmp.Path), """{"pattern":"*.txt","limit":1}""");
        Assert.Contains("1 results limit reached. Use limit=2 for more, or refine pattern", result.Text);
    }
}
