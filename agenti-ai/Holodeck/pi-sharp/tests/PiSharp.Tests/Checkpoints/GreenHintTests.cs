// pi-sharp-checkpoints-3: the green-marker + last-green hint state machine, unit-tested
// without git. The tracker sees every bash command exit and decides: green command? pass or
// fail? is the one-shot last-green hint due? The store marking (refs/pi/cp/green/N) happens in
// CheckpointStore; here we only verify the tracker's own state and the hint text.
using PiSharp.Core.Checkpoints;
using Xunit;

namespace PiSharp.Tests.Checkpoints;

public class GreenHintTests
{
    private static CheckpointGreenTracker New(int hintFailures = 3) => new() { HintFailures = hintFailures };

    // ---- IsGreenCommand: prefix match with a word boundary ----

    [Theory]
    [InlineData("dotnet build", true)]
    [InlineData("dotnet build -c Release", true)]
    [InlineData("dotnet build --no-restore", true)]
    [InlineData("dotnet test", true)]
    [InlineData("dotnet test --filter Category=CP", true)]
    [InlineData("dotnet test --no-build -v n", true)]
    [InlineData("  dotnet build", true)] // leading whitespace is trimmed first
    [InlineData("dotnet buildx imagetools", false)] // glued continuation is not a word boundary
    [InlineData("dotnet build2", false)]
    [InlineData("echo dotnet build", false)] // mid-command: the prefix must be at the start
    [InlineData("make build", false)]
    [InlineData("dotnet publish -c Release", false)] // not in the default green set
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsGreenCommand_MatchesOnlyWordBoundaryPrefixes(string command, bool expected)
        => Assert.Equal(expected, New().IsGreenCommand(command));

    [Fact]
    public void IsGreenCommand_RespectsCustomGreenCommands()
    {
        var t = new CheckpointGreenTracker(["cargo build"]);
        Assert.True(t.IsGreenCommand("cargo build --release"));
        Assert.False(t.IsGreenCommand("dotnet build"));
    }

    // ---- green command success: MarkGreen fires, counter resets ----

    [Fact]
    public void GreenSuccess_FiresMarkGreenExactlyOnce()
    {
        var t = New();
        var calls = 0;
        t.MarkGreen = _ => calls++;
        t.OnCommandExit("dotnet build", 0);
        Assert.Equal(1, calls);
        t.OnCommandExit("dotnet build -c Release", 0);
        Assert.Equal(2, calls);
        // Non-green commands never fire.
        t.OnCommandExit("echo dotnet build", 0);
        Assert.Equal(2, calls);
    }

    // ---- failures before any green: no hint (there is nothing to rewind to) ----

    [Fact]
    public void FailuresBeforeAnyGreen_NeverHint()
    {
        var t = New();
        for (var i = 0; i < 10; i++)
        {
            t.OnCommandExit("dotnet build", 1);
            Assert.Null(t.DrainHint());
        }
        Assert.Null(t.DrainHint());
    }

    // ---- 3 failed builds after a green: exactly one hint, then silence ----

    [Fact]
    public void ThreeFailedBuilds_EmitExactlyOneHint()
    {
        var t = New();
        t.GreenMarked(4); // the seat already has cp-4 as its last green
        t.OnCommandExit("dotnet build", 1); // 1
        Assert.Null(t.DrainHint());
        t.OnCommandExit("dotnet build", 2); // 2
        Assert.Null(t.DrainHint());
        t.OnCommandExit("dotnet build", 1); // 3 -> due
        var hint = t.DrainHint();
        Assert.NotNull(hint);
        Assert.Contains("last green cp-4", hint!);
        Assert.Contains("rewind{to:\"last-green\"}", hint);
        // The same three failures must not hint again: the hint is one-shot per green.
        t.OnCommandExit("dotnet build", 1); // 4
        t.OnCommandExit("dotnet build", 1); // 5
        Assert.Null(t.DrainHint());
        Assert.Null(t.DrainHint());
    }

    [Fact]
    public void HintText_CarriesBatchDistance()
    {
        var t = New();
        t.GreenMarked(2);
        t.AfterBatch(); // 1 batch after the green
        t.AfterBatch(); // 2
        t.OnCommandExit("dotnet test", 1);
        t.OnCommandExit("dotnet test", 1);
        t.OnCommandExit("dotnet test", 1);
        var hint = t.DrainHint();
        Assert.NotNull(hint);
        Assert.Contains("2 batches ago", hint!);
    }

    // ---- a green build suppresses (disarms) any pending hint ----

    [Fact]
    public void SuccessfulBuild_AfterFailures_SuppressesAndRearms()
    {
        var t = New();
        t.GreenMarked(1);
        t.OnCommandExit("dotnet build", 1); // 1
        t.OnCommandExit("dotnet build", 1); // 2
        t.OnCommandExit("dotnet build", 0); // green: the build recovered
        Assert.Null(t.DrainHint());
        t.GreenMarked(2); // the caller confirmed the mark landed
        // Counter reset: the next hint needs three fresh failures.
        t.OnCommandExit("dotnet build", 1);
        t.OnCommandExit("dotnet build", 1);
        Assert.Null(t.DrainHint());
    }

    // ---- hint threshold is configurable ----

    [Fact]
    public void HintFailures_One_FiresOnFirstFailure()
    {
        var t = New(hintFailures: 1);
        t.GreenMarked(7);
        t.OnCommandExit("dotnet build", 1);
        var hint = t.DrainHint();
        Assert.NotNull(hint);
        Assert.Contains("last green cp-7", hint!);
    }

    [Fact]
    public void HintFailures_Default_IsThree()
        => Assert.Equal(3, CheckpointGreenTracker.DefaultHintFailures);

    // ---- non-green failures do not count toward the hint ----

    [Fact]
    public void NonGreenFailures_DoNotCount()
    {
        var t = New();
        t.GreenMarked(1);
        t.OnCommandExit("curl -s example.com", 1);
        t.OnCommandExit("git push", 128);
        t.OnCommandExit("dotnet build", 1); // 1 real failure
        Assert.Null(t.DrainHint());
    }

    // ---- DrainHint is one-shot ----

    [Fact]
    public void DrainHint_ReturnsNullAfterFirstDrain()
    {
        var t = New();
        t.GreenMarked(1);
        t.OnCommandExit("dotnet build", 1);
        t.OnCommandExit("dotnet build", 1);
        t.OnCommandExit("dotnet build", 1);
        Assert.NotNull(t.DrainHint());
        Assert.Null(t.DrainHint());
    }
}
