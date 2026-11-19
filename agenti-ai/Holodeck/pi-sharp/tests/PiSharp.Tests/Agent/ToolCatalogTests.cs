using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Core.Models;
using static PiSharp.Tests.Agent.Fakes;

namespace PiSharp.Tests.Agent;

public sealed class ToolCatalogTests
{
    private static DelegateTool T(string name, string description = "d") => new() { Name = name, Description = description };

    [Fact]
    public void Select_KeepsRecentlyUsedToolsWhenCapping()
    {
        var tools = Enumerable.Range(1, 10).Select(i => (ITool)T($"t{i}")).ToList();
        var history = new List<Message>
        {
            new UserMessage("q"),
            ToolCalls(Call("c1", "t9"), Call("c2", "t10")),
        };
        var selected = ToolCatalog.Select(tools, max: 3, history);
        Assert.Equal(["t9", "t10", "t1"], selected.Select(t => t.Name));
    }

    [Fact]
    public void Present_ShortensDescriptionsOnSmallContextWindows()
    {
        var longDesc = new string('x', 200) + " tail";
        var tool = T("read", longDesc);
        var small = new ModelConfig { Id = "m", BaseUrl = "http://x/v1", ContextWindow = 8192 };
        var presented = Assert.Single(ToolCatalog.Present([tool], small));
        Assert.True(presented.Description.Length <= ToolCatalog.ShortDescriptionLimit);
        Assert.EndsWith("…", presented.Description);

        var large = small with { ContextWindow = 32768 };
        Assert.Equal(longDesc, Assert.Single(ToolCatalog.Present([tool], large)).Description);
    }

    [Fact]
    public void Present_CapsAtEightOnSmallContextUnlessMaxToolsOverrides()
    {
        var tools = Enumerable.Range(1, 12).Select(i => (ITool)T($"t{i}")).ToList();
        var small = new ModelConfig { Id = "m", BaseUrl = "http://x/v1", ContextWindow = 8192 };
        Assert.Equal(8, ToolCatalog.Present(tools, small).Count);

        var overrideMax = small with { MaxTools = 2 };
        Assert.Equal(["t1", "t2"], ToolCatalog.Present(tools, overrideMax).Select(t => t.Name));
    }

    [Fact]
    public void SelectPinsSkillSearchToolsWhenCapping()
    {
        var tools = new ITool[]
        {
            T("read"),
            T("store__search_skills"),
            T("store__get_skill_manifest"),
            T("store__install_skill"),
        };

        var selected = ToolCatalog.Select(tools, max: 2);

        Assert.Equal(["store__search_skills", "read"], selected.Select(t => t.Name));
    }

    [Fact]
    public void BatchSignature_IsStableUnderKeyOrder()
    {
        var a = Call("1", "read", Json("""{"b":1,"a":2}"""));
        var b = Call("2", "read", Json("""{"a":2,"b":1}"""));
        Assert.Equal(ToolCatalog.BatchSignature([a]), ToolCatalog.BatchSignature([b]));
        Assert.NotEqual(ToolCatalog.BatchSignature([a]), ToolCatalog.BatchSignature([Call("1", "write", a.Arguments)]));
    }
}
