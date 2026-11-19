using PiSharp.Cli;

namespace PiSharp.Tests.Cli;

/// <summary>2026-10-30 ("also 3"): a seat's log lines carry their time, so a quiet spell can be read off the log.</summary>
public sealed class StampedWriterTests
{
    [Fact]
    public void EveryLineBeginsWithItsUtcTime_WhetherWrittenWholeOrInPieces()
    {
        var at = new DateTimeOffset(2026, 10, 30, 4, 12, 5, TimeSpan.FromHours(2));
        var sink = new StringWriter();
        var stamped = new StampedWriter(sink, () => at);
        stamped.WriteLine("[notebook] remote sync unconfirmed");
        stamped.Write("[compaction] compacted ");
        stamped.Write("between turns\n\nsecond");
        stamped.WriteLine();
        Assert.Equal(
            "2026-10-30T02:12:05Z [notebook] remote sync unconfirmed\n" +
            "2026-10-30T02:12:05Z [compaction] compacted between turns\n" +
            "\n" +
            "2026-10-30T02:12:05Z second\n", sink.ToString());
    }
}
