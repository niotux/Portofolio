using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class TruncationTests
{
    [Fact]
    public void HeadLeavesSmallInputUntouched()
    {
        var r = Truncation.TruncateHead("hello\nworld\n");
        Assert.False(r.Truncated);
        Assert.Equal("hello\nworld\n", r.Content);
        Assert.Equal(2, r.TotalLines);
    }

    [Fact]
    public void HeadEmptyInput()
    {
        var r = Truncation.TruncateHead("");
        Assert.False(r.Truncated);
        Assert.Equal("", r.Content);
        Assert.Equal(0, r.TotalLines);
    }

    [Fact]
    public void HeadHitsLineLimit()
    {
        var content = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"L{i}"));
        var r = Truncation.TruncateHead(content, maxLines: 3, maxBytes: 50_000);
        Assert.True(r.Truncated);
        Assert.Equal(TruncatedBy.Lines, r.TruncatedBy);
        Assert.Equal("L1\nL2\nL3", r.Content);
        Assert.Equal(3, r.OutputLines);
        Assert.Equal(10, r.TotalLines);
    }

    [Fact]
    public void HeadHitsByteLimitOnWholeLines()
    {
        var content = "aaa\nbbb\nccc";
        var r = Truncation.TruncateHead(content, maxLines: 100, maxBytes: 7);
        Assert.True(r.Truncated);
        Assert.Equal(TruncatedBy.Bytes, r.TruncatedBy);
        Assert.Equal("aaa\nbbb", r.Content);
        Assert.False(r.FirstLineExceedsLimit);
    }

    [Fact]
    public void HeadFirstLineExceedsByteLimit()
    {
        var r = Truncation.TruncateHead("abcdefghij", maxLines: 10, maxBytes: 3);
        Assert.True(r.Truncated);
        Assert.Equal("", r.Content);
        Assert.True(r.FirstLineExceedsLimit);
        Assert.Equal(TruncatedBy.Bytes, r.TruncatedBy);
        Assert.Equal(0, r.OutputLines);
    }

    [Fact]
    public void TailKeepsLastLines()
    {
        var content = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"L{i}"));
        var r = Truncation.TruncateTail(content, maxLines: 3, maxBytes: 50_000);
        Assert.True(r.Truncated);
        Assert.Equal(TruncatedBy.Lines, r.TruncatedBy);
        Assert.Equal("L8\nL9\nL10", r.Content);
    }

    [Fact]
    public void TailCutsUtf8OnCharacterBoundary()
    {
        // "é" is C3 A9. A 3-byte budget from the end of "xé" (78 C3 A9) must not start on A9.
        var r = Truncation.TruncateTail("xé", maxLines: 10, maxBytes: 2);
        Assert.True(r.Truncated);
        Assert.True(r.LastLinePartial);
        Assert.Equal("é", r.Content);
        Assert.False(r.Content.Contains('\uFFFD'));
    }

    [Fact]
    public void FormatSize()
    {
        Assert.Equal("512B", Truncation.FormatSize(512));
        Assert.Equal("1.0KB", Truncation.FormatSize(1024));
        Assert.Equal("50.0KB", Truncation.FormatSize(50 * 1024));
        Assert.Equal("1.5MB", Truncation.FormatSize((long)(1.5 * 1024 * 1024)));
    }

    [Fact]
    public void TruncateLine()
    {
        var (shortText, shortCut) = Truncation.TruncateLine("abc", 10);
        Assert.Equal("abc", shortText);
        Assert.False(shortCut);

        var (longText, longCut) = Truncation.TruncateLine(new string('x', 510), 500);
        Assert.True(longCut);
        Assert.EndsWith("... [truncated]", longText);
        Assert.Equal(500 + "... [truncated]".Length, longText.Length);
    }

    [Fact]
    public void ToJsonOmitsTruncatedByWhenNone()
    {
        var json = Truncation.TruncateHead("ok").ToJson();
        Assert.False((bool)json["truncated"]!);
        Assert.True(json["truncatedBy"] is null);
    }
}
