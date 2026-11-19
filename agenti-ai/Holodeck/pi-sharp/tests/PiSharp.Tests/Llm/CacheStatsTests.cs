using PiSharp.Llm;

namespace PiSharp.Tests.Llm;

public class CacheStatsTests
{
    private static CacheStats New(Func<DateTimeOffset>? clock = null) => new(clock);

    [Fact]
    public void BelowNoiseFloorIsAMissWithFullContextMissed()
    {
        var s = New();
        var sample = s.Record(1023, 4000, "m");
        Assert.True(sample.CacheMiss);
        Assert.Equal(4000, sample.MissedTokens);
        Assert.False(s.Reported);
    }

    [Fact]
    public void AtNoiseFloorIsAHitWithOnlyUncachedPortionMissed()
    {
        var s = New();
        var sample = s.Record(1024, 4000, "m");
        Assert.False(sample.CacheMiss);
        Assert.Equal(4000 - 1024, sample.MissedTokens);
        Assert.True(s.Reported);
    }

    [Fact]
    public void ReportedIsStickyOnceCacheIsSeen()
    {
        var s = New();
        s.Record(0, 100, "m");
        s.Record(2000, 5000, "m");
        var third = s.Record(0, 100, "m");
        Assert.True(s.Reported);
        Assert.True(third.Reported);
        Assert.True(third.CacheMiss, "a 0-report turn reads as a miss until the sticky flag says the seat does report at all");
    }

    [Fact]
    public void AllZeroReportingSeatNeverReports()
    {
        var s = New();
        for (var i = 0; i < 3; i++)
        {
            var sample = s.Record(0, 5000, "vllm");
            Assert.False(sample.Reported);
            Assert.True(sample.CacheMiss);
            Assert.Equal(5000, sample.MissedTokens);
        }
    }

    [Fact]
    public void ModelChangedOnlyOnTheTurnWhereItChanges()
    {
        var s = New();
        Assert.True(s.Record(1024, 4000, "m1").ModelChanged);
        Assert.False(s.Record(1024, 4000, "m1").ModelChanged);
        Assert.True(s.Record(1024, 4000, "m2").ModelChanged);
        Assert.False(s.Record(1024, 4000, "m2").ModelChanged);
    }

    [Fact]
    public void IdleIsZeroOnFirstTurnAndCountsSinceThePreviousCall()
    {
        var now = DateTimeOffset.UtcNow;
        var s = New(() => now);
        Assert.Equal(0, s.Record(1024, 4000, "m").IdleMs);
        now = now.AddMilliseconds(1234);
        Assert.Equal(1234, s.Record(1024, 4000, "m").IdleMs);
    }

    [Fact]
    public void IdleBeyondTtlIsACacheMissEvenWithAHit()
    {
        var now = DateTimeOffset.UtcNow;
        var s = New(() => now);
        s.Record(1024, 4000, "m");
        now = now.AddMilliseconds(CacheStats.CacheTtlMs + 1);
        var sample = s.Record(1024, 4000, "m");
        Assert.True(sample.CacheMiss);
        Assert.True(sample.IdleMs > CacheStats.CacheTtlMs);
    }

    [Fact]
    public void IdleWithinTtlIsNotACacheMiss()
    {
        var now = DateTimeOffset.UtcNow;
        var s = New(() => now);
        s.Record(1024, 4000, "m");
        now = now.AddMilliseconds(CacheStats.CacheTtlMs - 1);
        Assert.False(s.Record(1024, 4000, "m").CacheMiss);
    }

    [Fact]
    public void FormatLineMatchesTheAdrShape()
    {
        var now = DateTimeOffset.UtcNow;
        var s = New(() => now);
        s.Record(1024, 4000, "m1");
        now = now.AddMilliseconds(123);
        var sample = s.Record(2048, 5000, "m2");
        Assert.Equal(
            "cache_miss seat=coder-06-pi6 host=.101 missed=2952 idle=123 model_changed=true reported=true",
            sample.FormatLine("coder-06-pi6", ".101"));
    }

    // Gap 1: the TTL comparison is a strict >. A hit with idle EXACTLY at the TTL is still served
    // from the provider's cache - only going past it reads as a miss. Existing tests pin -1 and +1;
    // this pins the boundary itself.
    [Fact]
    public void IdleExactlyAtTtlWithAHitIsNotAMiss()
    {
        var now = DateTimeOffset.UtcNow;
        var s = New(() => now);
        s.Record(1024, 4000, "m");
        now = now.AddMilliseconds(CacheStats.CacheTtlMs);
        var sample = s.Record(1024, 4000, "m");
        Assert.Equal(CacheStats.CacheTtlMs, sample.IdleMs);
        Assert.False(sample.CacheMiss, "strict > CacheTtlMs: exactly at the TTL the cache is still assumed warm");
    }

    // Gap 2: missed = max(0, contextTokens - cacheReadTokens) on a hit - a provider that reports more
    // cache-read tokens than the context had clamps to 0 instead of going negative.
    [Fact]
    public void MissedClampsToZeroWhenCacheReadExceedsContext()
    {
        var s = New();
        var sample = s.Record(8000, 4000, "m");
        Assert.False(sample.CacheMiss);
        Assert.Equal(0, sample.MissedTokens);
        Assert.True(s.Reported, "8000 reported tokens clears the sticky flag");
    }

    // Gap 3: turn numbering, sample order and CurrentLine lifecycle.
    [Fact]
    public void TurnsStartAtOneAndSamplesAreOldestFirst()
    {
        var s = New();
        var first = s.Record(1024, 4000, "m");
        var second = s.Record(2048, 5000, "m");
        var third = s.Record(0, 100, "m");
        Assert.Equal(1, first.Turn);
        Assert.Equal(2, second.Turn);
        Assert.Equal(3, third.Turn);
        Assert.Equal(3, s.Samples.Count);
        Assert.Same(first, s.Samples[0]); // insertion order, oldest first
        Assert.Same(second, s.Samples[1]);
        Assert.Same(third, s.Samples[2]);
        Assert.Same(third, s.CurrentLine); // CurrentLine is the latest sample
    }

    [Fact]
    public void CurrentLineIsNullBeforeTheFirstTurn()
    {
        var s = New();
        Assert.Null(s.CurrentLine);
        Assert.Empty(s.Samples);
        var first = s.Record(0, 100, "m");
        Assert.Same(first, s.CurrentLine);
    }

    // Gap 4: ModelChanged transitions with a null modelId. The first recorded model establishes the
    // baseline (true - there was nothing to continue from); a later m1 -> null and null -> m1 are
    // changes; null -> null is not; the first turn with a null modelId (nothing recorded yet and
    // nothing now) is not.
    [Fact]
    public void ModelChangedTransitionsWithNullModelId()
    {
        var s = New();
        Assert.True(s.Record(1024, 4000, "m1").ModelChanged, "first recorded model establishes the baseline");
        Assert.True(s.Record(1024, 4000, null).ModelChanged, "m1 -> null is a change");
        Assert.False(s.Record(1024, 4000, null).ModelChanged, "null -> null is not");
        Assert.True(s.Record(1024, 4000, "m1").ModelChanged, "null -> m1 is a change");
    }

    [Fact]
    public void FirstTurnWithNullModelIsNotAChange()
    {
        var s = New();
        Assert.False(s.Record(1024, 4000, null).ModelChanged, "no prior model and no model: nothing to change from");
        Assert.False(s.Record(1024, 4000, null).ModelChanged);
        Assert.True(s.Record(1024, 4000, "m1").ModelChanged, "null -> m1 is a change");
    }

    // Gap 5: the sticky Reported flag is per instance. Two CacheStats trackers never see each other's
    // cache activity (e.g. per-process trackers must not share a flag).
    [Fact]
    public void ReportedIsPerInstanceNotShared()
    {
        var a = New();
        var b = New();
        a.Record(2000, 5000, "m");
        Assert.True(a.Reported);
        Assert.False(b.Reported, "Reported must not leak across instances");
        var bSample = b.Record(0, 100, "m");
        Assert.False(bSample.Reported);
    }
}
