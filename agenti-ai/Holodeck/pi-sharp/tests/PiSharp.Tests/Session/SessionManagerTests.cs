using PiSharp.Core.Messages;
using PiSharp.Core.Session;
using PiSharp.Tests.Tools;

namespace PiSharp.Tests.Session;

public class SessionManagerTests
{
    [Fact]
    public void InMemory_AppendsLinearParentChain()
    {
        var session = SessionManager.InMemory("/tmp/proj");
        var a = session.AppendMessage(new UserMessage("hello"));
        var b = session.AppendMessage(new AssistantMessage { Content = [new TextContent("hi")], Model = "m", StopReason = StopReason.Stop });
        Assert.Equal(2, session.Entries.Count);
        Assert.Null(session.Entries[0].ParentId);
        Assert.Equal(a, session.Entries[1].ParentId);
        Assert.Equal(b, session.LeafId);
        Assert.Equal("hello", ((UserMessage)((SessionMessageEntry)session.Entries[0]).Message).Text);
    }

    [Fact]
    public void BuildContextMessages_UsesLatestCompactionAndKeptTail()
    {
        var session = SessionManager.InMemory("/tmp/proj");
        session.AppendMessage(new UserMessage("old1"));
        session.AppendMessage(new AssistantMessage { Content = [new TextContent("a1")], Model = "m", StopReason = StopReason.Stop });
        var kept = session.AppendMessage(new UserMessage("keep me"));
        session.AppendMessage(new AssistantMessage { Content = [new TextContent("a2")], Model = "m", StopReason = StopReason.Stop });
        session.AppendCompaction("SUM", kept, tokensBefore: 99);

        var messages = session.BuildContextMessages();
        Assert.IsType<CompactionSummaryMessage>(messages[0]);
        Assert.Equal("SUM", ((CompactionSummaryMessage)messages[0]).Summary);
        Assert.Equal("keep me", ((UserMessage)messages[1]).Text);
        Assert.Equal(3, messages.Count);
        Assert.DoesNotContain(messages, m => m is UserMessage u && u.Text == "old1");
    }

    [Fact]
    public void RoundTrip_Jsonl_SkipsMalformedLines()
    {
        using var tmp = new TempWorkspace();
        var path = Path.Combine(tmp.Path, "s.jsonl");
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        session.AppendMessage(new UserMessage("u"));
        session.AppendMessage(new AssistantMessage { Content = [new TextContent("a")], Model = "m", StopReason = StopReason.Stop });
        session.AppendModelChange("openai", "m");
        session.AppendSessionInfo("My session");
        Assert.NotNull(session.SessionFile);
        File.Copy(session.SessionFile!, path, overwrite: true);

        var raw = File.ReadAllText(path);
        File.WriteAllText(path, raw + "not json\n{\"type\":\"mystery\",\"id\":\"x\"}\n");

        var loaded = SessionManager.Open(path);
        Assert.Equal(4, loaded.Entries.Count);
        Assert.Equal("My session", loaded.GetSessionName());
        Assert.Equal("u", ((UserMessage)loaded.BuildContextMessages()[0]).Text);
        var ctx = loaded.BuildSessionContext();
        Assert.Equal("openai", ctx.ModelProvider);
        Assert.Equal("m", ctx.ModelId);
    }

    [Fact]
    public void ConvertToLlm_WrapsCompactionSummary()
    {
        var summary = new CompactionSummaryMessage("checkpoint", 10);
        var llm = SessionConvert.ToLlm([summary, new UserMessage("go")]);
        var user = Assert.IsType<UserMessage>(llm[0]);
        Assert.Contains("<summary>", user.Text);
        Assert.Contains("checkpoint", user.Text);
        Assert.Contains("</summary>", user.Text);
        Assert.Equal("go", ((UserMessage)llm[1]).Text);
    }

    [Fact]
    public void UserRequestIsDurableBeforeFirstAssistantMessage()
    {
        using var tmp = new TempWorkspace();
        var session = SessionManager.Create(tmp.Path, tmp.Path);
        session.AppendMessage(new UserMessage("only user"));
        Assert.True(File.Exists(session.SessionFile));
        Assert.Contains("only user", File.ReadAllText(session.SessionFile!));
        session.AppendMessage(new AssistantMessage { Content = [new TextContent("ok")], Model = "m", StopReason = StopReason.Stop });
        Assert.True(File.Exists(session.SessionFile));
        var text = File.ReadAllText(session.SessionFile!);
        Assert.Contains("\"type\":\"session\"", text);
        Assert.Contains("\"role\":\"user\"", text);
        Assert.Contains("\"role\":\"assistant\"", text);
    }

    [Fact]
    public void ContinueRecent_PicksLatestMatchingCwd()
    {
        using var tmp = new TempWorkspace();
        var first = SessionManager.Create(tmp.Path, tmp.Path);
        first.AppendMessage(new UserMessage("one"));
        first.AppendMessage(new AssistantMessage { Content = [new TextContent("a")], Model = "m", StopReason = StopReason.Stop });
        Thread.Sleep(20);
        var second = SessionManager.Create(tmp.Path, tmp.Path);
        second.AppendMessage(new UserMessage("two"));
        second.AppendMessage(new AssistantMessage { Content = [new TextContent("b")], Model = "m", StopReason = StopReason.Stop });

        var continued = SessionManager.ContinueRecent(tmp.Path, tmp.Path);
        Assert.Equal(second.SessionId, continued.SessionId);
        Assert.Equal("two", ((UserMessage)continued.BuildContextMessages()[0]).Text);
    }

    [Fact]
    public void AssertValidSessionId_RejectsBadIds()
    {
        Assert.Throws<ArgumentException>(() => SessionManager.AssertValidSessionId(""));
        Assert.Throws<ArgumentException>(() => SessionManager.AssertValidSessionId("-abc"));
        Assert.Throws<ArgumentException>(() => SessionManager.AssertValidSessionId("abc-"));
        SessionManager.AssertValidSessionId("abc_1.json");
    }
}
