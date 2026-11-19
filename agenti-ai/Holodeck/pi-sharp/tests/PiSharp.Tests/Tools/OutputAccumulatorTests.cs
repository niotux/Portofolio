using System.Text;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class OutputAccumulatorTests
{
    [Fact]
    public void SmallOutputIsNotTruncated()
    {
        using var acc = new OutputAccumulator();
        acc.Append("hello\n"u8);
        acc.Finish();
        var snap = acc.Snapshot();
        Assert.False(snap.Truncation.Truncated);
        Assert.Equal("hello\n", snap.Content);
        Assert.Null(snap.FullOutputPath);
    }

    [Fact]
    public void ByteOverflowPersistsTempFile()
    {
        using var acc = new OutputAccumulator(maxLines: 10_000, maxBytes: 64, tempFilePrefix: "pi-test");
        acc.Append(Encoding.UTF8.GetBytes(new string('x', 200) + "\n"));
        acc.Finish();
        var snap = acc.Snapshot(persistIfTruncated: true);
        Assert.True(snap.Truncation.Truncated);
        Assert.NotNull(snap.FullOutputPath);
        Assert.True(File.Exists(snap.FullOutputPath));
        try { File.Delete(snap.FullOutputPath!); } catch { /* temp */ }
    }

    [Fact]
    public void LargeOutputKeepsHeadAndTailWithinBudgetAndPersistsFullOutput()
    {
        using var acc = new OutputAccumulator(maxLines: 20_000, maxBytes: 50 * 1024, tempFilePrefix: "pi-test");
        acc.Append(Encoding.UTF8.GetBytes("FIRST\n" + new string('x', 10_000_000) + "\nLAST\n"));
        acc.Finish();
        var snap = acc.Snapshot(persistIfTruncated: true);
        Assert.True(snap.Truncation.Truncated);
        Assert.StartsWith("FIRST\n", snap.Content);
        Assert.Contains("[... omitted output ...]", snap.Content);
        Assert.EndsWith("LAST\n", snap.Content);
        Assert.True(File.Exists(snap.FullOutputPath));
        try { File.Delete(snap.FullOutputPath!); } catch { /* temp */ }
    }
}
