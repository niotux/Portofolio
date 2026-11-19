using PiSharp.Core.Session;
using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public sealed class CheckpointSelectorTests
{
    [Fact]
    public void LastGreenSelectsNewestGreenCheckpoint()
    {
        var first = Checkpoint("cp-1", green: true);
        var second = Checkpoint("cp-2");
        var third = Checkpoint("cp-3", green: true);

        Assert.Same(third, CheckpointSelector.Resolve("last-green", [first, second, third]));
    }

    [Fact]
    public void IdSelectsCheckpointBySessionEntryId()
    {
        var checkpoint = Checkpoint("cp-1");

        Assert.Same(checkpoint, CheckpointSelector.Resolve("cp-1", [checkpoint]));
    }

    [Fact]
    public void BeforeSelectsCheckpointContainingToolCallId()
    {
        var preceding = Checkpoint("cp-before", toolCallIds: ["call-1"]);
        var later = Checkpoint("cp-after", toolCallIds: ["call-2"]);

        Assert.Same(preceding, CheckpointSelector.Resolve("before:call-1", [preceding, later]));
    }

    [Fact]
    public void UnknownSelectorIsRefused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CheckpointSelector.Resolve("cp-missing", []));

        Assert.Contains("No checkpoint matches", error.Message);
    }

    private static CheckpointEntry Checkpoint(string id, bool green = false, string[]? toolCallIds = null) =>
        new() { Id = id, Commit = "commit-" + id, Green = green, ToolCallIds = toolCallIds ?? [] };
}
