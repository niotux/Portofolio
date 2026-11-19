using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class EditDiffTests
{
    [Fact]
    public void DetectsCrlfVsLf()
    {
        Assert.Equal("\r\n", EditDiff.DetectLineEnding("a\r\nb\n"));
        Assert.Equal("\n", EditDiff.DetectLineEnding("a\nb\r\n"));
        Assert.Equal("\n", EditDiff.DetectLineEnding("no newline"));
    }

    [Fact]
    public void SplitBom()
    {
        var (bom, text) = EditDiff.SplitBom("\uFEFFhello");
        Assert.Equal("\uFEFF", bom);
        Assert.Equal("hello", text);
        var (empty, rest) = EditDiff.SplitBom("hello");
        Assert.Equal("", empty);
        Assert.Equal("hello", rest);
    }

    [Fact]
    public void ExactReplace()
    {
        var applied = EditDiff.ApplyEditsToNormalizedContent(
            "alpha\nbeta\ngamma\n",
            [new TextEdit("beta", "BETA")],
            "f.txt");
        Assert.Equal("alpha\nBETA\ngamma\n", applied.NewContent);
    }

    [Fact]
    public void FuzzyMatchIgnoresTrailingWhitespaceAndSmartQuotes()
    {
        var content = "say \u201Chello\u201D   \n";
        var applied = EditDiff.ApplyEditsToNormalizedContent(
            content,
            [new TextEdit("say \"hello\"", "say \"hi\"")],
            "f.txt",
            fuzzy: true);
        Assert.Contains("say \"hi\"", applied.NewContent);
    }

    [Fact]
    public void FuzzyMatchIsOptIn()
    {
        var content = "say “hello”   \n";
        Assert.Throws<InvalidOperationException>(() =>
            EditDiff.ApplyEditsToNormalizedContent(content, [new TextEdit("say \"hello\"", "say \"hi\"")], "f.txt"));
    }

    [Fact]
    public void ExactModeCountsUniquenessExactly()
    {
        // "a b" and "a b" are one fuzzy text but two distinct exact ones.
        var applied = EditDiff.ApplyEditsToNormalizedContent("a b\na b\n", [new TextEdit("a b", "X")], "f.txt");
        Assert.Equal("X\na b\n", applied.NewContent);
    }

    [Fact]
    public void OverlappingEditsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EditDiff.ApplyEditsToNormalizedContent(
                "abcdef",
                [new TextEdit("abc", "x"), new TextEdit("cde", "y")],
                "f.txt"));
        Assert.Contains("overlap", ex.Message);
    }

    [Fact]
    public void DuplicateOldTextRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EditDiff.ApplyEditsToNormalizedContent(
                "foo\nfoo\n",
                [new TextEdit("foo", "bar")],
                "f.txt"));
        Assert.Contains("2 occurrences", ex.Message);
        Assert.Contains("must be unique", ex.Message);
    }

    [Fact]
    public void NotFoundUsesExactMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EditDiff.ApplyEditsToNormalizedContent("abc", [new TextEdit("zzz", "y")], "f.txt"));
        Assert.Equal("Could not find the exact text in f.txt. The old text must match exactly including all whitespace and newlines.", ex.Message);
    }

    [Fact]
    public void EmptyOldTextRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EditDiff.ApplyEditsToNormalizedContent("abc", [new TextEdit("", "y")], "f.txt"));
        Assert.Equal("oldText must not be empty in f.txt.", ex.Message);
    }

    [Fact]
    public void NoChangeRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EditDiff.ApplyEditsToNormalizedContent("abc", [new TextEdit("abc", "abc")], "f.txt"));
        Assert.Contains("No changes made to f.txt", ex.Message);
    }

    [Fact]
    public void MultipleDisjointEdits()
    {
        var applied = EditDiff.ApplyEditsToNormalizedContent(
            "one\ntwo\nthree\n",
            [new TextEdit("one", "ONE"), new TextEdit("three", "THREE")],
            "f.txt");
        Assert.Equal("ONE\ntwo\nTHREE\n", applied.NewContent);
    }

    [Fact]
    public void CrlfNormalizedThenRestored()
    {
        var raw = "a\r\nb\r\nc\r\n";
        var ending = EditDiff.DetectLineEnding(raw);
        var lf = EditDiff.NormalizeToLf(raw);
        var applied = EditDiff.ApplyEditsToNormalizedContent(lf, [new TextEdit("b", "B")], "f.txt");
        var restored = EditDiff.RestoreLineEndings(applied.NewContent, ending);
        Assert.Equal("a\r\nB\r\nc\r\n", restored);
    }

    [Fact]
    public void DisplayDiffIncludesLineNumbers()
    {
        var diff = EditDiff.GenerateDiffString("a\nb\nc\n", "a\nB\nc\n");
        Assert.Equal(2, diff.FirstChangedLine);
        Assert.Contains("-2 b", diff.Diff);
        Assert.Contains("+2 B", diff.Diff);
    }

    [Fact]
    public void FuzzyPreservesUnchangedLineBytes()
    {
        // trailing spaces on the untouched line must survive a fuzzy edit on another line
        var content = "keep   \nchange please\n";
        var applied = EditDiff.ApplyEditsToNormalizedContent(
            content,
            [new TextEdit("change please", "changed")],
            "f.txt",
            fuzzy: true);
        Assert.StartsWith("keep   \n", applied.NewContent);
    }
}
