using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Tests.Agent;

namespace PiSharp.Tests.Cli;

public class JsonEventTests
{
    [Fact]
    public void SerializesCoreEvents()
    {
        Assert.Equal("agent_start", JsonEventSink.ToJson(new AgentStart())!["type"]!.GetValue<string>());
        Assert.Equal("turn_start", JsonEventSink.ToJson(new TurnStart())!["type"]!.GetValue<string>());

        var start = JsonEventSink.ToJson(new MessageStart(new UserMessage("hi")));
        Assert.Equal("user", start!["role"]!.GetValue<string>());

        var delta = JsonEventSink.ToJson(new MessageUpdate(
            Fakes.Text("x"),
            new TextDelta(0, "hel", Fakes.Text("hel"))));
        Assert.Equal("text_delta", delta!["type"]!.GetValue<string>());
        Assert.Equal("hel", delta["delta"]!.GetValue<string>());

        var tool = JsonEventSink.ToJson(new ToolExecutionStart("c1", "read", Fakes.Json("""{"path":"a"}""")));
        Assert.Equal("read", tool!["toolName"]!.GetValue<string>());
    }

    [Fact]
    public void SerializesPartialToolExecutionUpdates()
    {
        var evt = new ToolExecutionUpdate("c1", "read", new JsonObject(), AgentToolResult.FromText("x"));
        var json = JsonEventSink.ToJson(evt);
        Assert.Equal("tool_execution_update", json!["type"]!.GetValue<string>());
        Assert.Equal("x", json["text"]!.GetValue<string>());
    }

    [Fact]
    public void SerializesPartialToolArguments()
    {
        var partial = Fakes.Assistant(StopReason.Pending,
            new ToolCallContent("c1", "read", Fakes.Json("{\"path\":\"a\"}")));

        var start = JsonEventSink.ToJson(new MessageUpdate(
            partial,
            new ToolCallStart(0, partial)));
        var delta = JsonEventSink.ToJson(new MessageUpdate(
            partial,
            new ToolCallDelta(0, "{\"path\":\"a\"}", partial)));

        Assert.Equal("toolcall_start", start!["type"]!.GetValue<string>());
        Assert.Equal("toolcall_delta", delta!["type"]!.GetValue<string>());
        Assert.Equal("{\"path\":\"a\"}", delta["delta"]!.GetValue<string>());
    }
}
