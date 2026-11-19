using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class LineDiffTests
{
    [Fact]
    public void EqualTextsProduceEmptyPatch()
    {
        Assert.Equal("", LineDiff.UnifiedPatch("a.txt", "hello\n", "hello\n"));
    }

    [Fact]
    public void UnifiedPatchMatchesFileHeadersOnlyHunk()
    {
        var oldContent = "alpha\nbeta\ngamma\n";
        var newContent = "alpha\nBETA\ngamma\n";
        var patch = LineDiff.UnifiedPatch("file.txt", oldContent, newContent, contextLines: 4);
        var expected = string.Join('\n',
            "--- file.txt",
            "+++ file.txt",
            "@@ -1,3 +1,3 @@",
            " alpha",
            "-beta",
            "+BETA",
            " gamma",
            "");
        Assert.Equal(expected, patch);
    }

    [Fact]
    public void UnifiedPatchNotesMissingNewline()
    {
        var patch = LineDiff.UnifiedPatch("f", "hello", "hello!");
        Assert.Contains("\\ No newline at end of file", patch);
        Assert.Contains("-hello", patch);
        Assert.Contains("+hello!", patch);
    }

    [Fact]
    public void DiffGroupsDeleteThenInsert()
    {
        var parts = LineDiff.Diff("a\nb\nc\n", "a\nB\nc\n");
        Assert.Equal(DiffOp.Equal, parts[0].Op);
        Assert.Equal(["a"], parts[0].Lines);
        Assert.Equal(DiffOp.Delete, parts[1].Op);
        Assert.Equal(["b"], parts[1].Lines);
        Assert.Equal(DiffOp.Insert, parts[2].Op);
        Assert.Equal(["B"], parts[2].Lines);
        Assert.Equal(DiffOp.Equal, parts[3].Op);
        Assert.Equal(["c"], parts[3].Lines);
    }

    [Fact]
    public void EmptyToContent()
    {
        var patch = LineDiff.UnifiedPatch("n", "", "x\n");
        Assert.Contains("@@ -0,0 +1,1 @@", patch);
        Assert.Contains("+x", patch);
    }
}
