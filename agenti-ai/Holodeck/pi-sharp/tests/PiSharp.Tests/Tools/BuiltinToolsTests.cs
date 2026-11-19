using PiSharp.Tools;

namespace PiSharp.Tests.Tools;

public class BuiltinToolsTests
{
    [Fact]
    public void FactoryReturnsPiToolsPlusApplyPatch()
    {
        using var tmp = new TempWorkspace();
        var tools = BuiltinTools.Create(tmp.Path);
        Assert.Equal(["read", "bash", "edit", "write", "apply_patch", "grep", "find", "ls"], tools.Select(t => t.Name));
    }

    [Fact]
    public void FactoryHonorsSupportsImages()
    {
        using var tmp = new TempWorkspace();
        var tools = BuiltinTools.Create(tmp.Path, supportsImages: false);
        var read = Assert.IsType<ReadTool>(tools[0]);
        Assert.False(read.SupportsImages);
    }
}
