using PiSharp.Cli;
using PiSharp.Core.Config;
using PiSharp.Core.Models;

namespace PiSharp.Tests.Cli;

public sealed class SubscriptionCompactionTests
{
    private static ModelConfig Assigned(int window, int maxOutput, int? compactAt = null) =>
        new() { Id = "m", BaseUrl = "https://x", Provider = "xai", ContextWindow = window, MaxOutputTokens = maxOutput, CompactAtTokens = compactAt };

    [Theory]
    // No budget: as much as the window holds beside the output.
    [InlineData(200000, 32000, null, 168000)]
    // A budget wins.
    [InlineData(200000, 32000, 100000, 100000)]
    // A budget below the window holds, whatever the output: each request is cut to what the window leaves
    // (digital-scribe, 2026-10-20: compaction from the Subscriptions settings); until then 168000.
    [InlineData(200000, 32000, 190000, 190000)]
    // Only a window not larger than the budget moves it to the window less the output.
    [InlineData(200000, 32000, 200000, 168000)]
    public void ASeatCompactsAtItsBudgetAndDownToHalf(int window, int maxOutput, int? compactAt, int expected)
    {
        var model = Assigned(window, maxOutput, compactAt);
        Assert.Equal(expected, SubscriptionAssignment.CompactAt(model));
        var settings = SubscriptionAssignment.WithCompaction(new Settings(), model);
        Assert.Equal(expected, settings.CompactionTriggerTokens);
        Assert.Equal(expected / 2, settings.CompactionTargetTokens);
    }

    /// <summary>digital-scribe, 2026-10-20: "it's 180k at which we compact down to 90k at this point across the board";
    /// "page is source of truth"; "use the settings one sets in the UI". The tab showed "Window 180k · output 32k ·
    /// compacts at 90k" while the seat compacted at 117k down to 58.5k.</summary>
    [Fact]
    public void TheTabsWindowIsWhereAnAssignedSeatCompactsDownToHalf()
    {
        var tab = SubscriptionAssignment.WindowIsBudget(Assigned(180000, 32000));
        Assert.Equal(180000, tab.CompactAtTokens);
        Assert.Equal(212000, tab.ContextWindow); // a request carries the whole budget plus its output
        var settings = SubscriptionAssignment.WithCompaction(new Settings(), tab);
        Assert.Equal(180000, settings.CompactionTriggerTokens);
        Assert.Equal(90000, settings.CompactionTargetTokens);
        Assert.Equal(32000, settings.CompactionReserveTokens);
        var compaction = settings.ToCompactionSettings();
        Assert.False(PiSharp.Core.Compaction.Compactor.ShouldCompact(180000, tab, compaction));
        Assert.True(PiSharp.Core.Compaction.Compactor.ShouldCompact(180001, tab, compaction));

        // A profile's budget stays where the seat compacts, inside the model's documented window.
        var profiled = SubscriptionAssignment.WindowIsBudget(Assigned(1_050_000, 32000, 256000));
        Assert.Equal(1_050_000, profiled.ContextWindow);
        Assert.Equal(256000, SubscriptionAssignment.CompactAt(profiled));
    }

    /// <summary>digital-scribe, 2026-10-20: the compaction point comes from the Subscriptions settings; a model's large
    /// output no longer pulls it down, the reserve stays inside the room above it.</summary>
    [Fact]
    public void ALargeOutputDoesNotPullTheCompactionBelowTheBudget()
    {
        var local = Assigned(200000, 131072, 180000) with { Provider = "openai-completions" };
        var settings = SubscriptionAssignment.WithCompaction(new Settings(), local);
        Assert.Equal(180000, settings.CompactionTriggerTokens);
        Assert.Equal(90000, settings.CompactionTargetTokens);
        Assert.Equal(20000, settings.CompactionReserveTokens);
        var compaction = settings.ToCompactionSettings();
        Assert.False(PiSharp.Core.Compaction.Compactor.ShouldCompact(180000, local, compaction));
        Assert.True(PiSharp.Core.Compaction.Compactor.ShouldCompact(180001, local, compaction));
    }

    [Fact]
    public void ALocalModelCompactsByTheSameRuleUnlessSettingsNameATrigger()
    {
        var miniclaude = Assigned(100000, 8192) with { Provider = "openai-completions" };
        var aligned = SubscriptionAssignment.WithDefaultCompaction(new Settings(), miniclaude);
        Assert.Equal(91808, aligned.CompactionTriggerTokens);
        Assert.Equal(45904, aligned.CompactionTargetTokens);
        Assert.Equal(8192, aligned.CompactionReserveTokens);

        var handSet = new Settings { CompactionTriggerTokens = 50000, CompactionTargetTokens = 25000 };
        Assert.Same(handSet, SubscriptionAssignment.WithDefaultCompaction(handSet, miniclaude));
    }
}
