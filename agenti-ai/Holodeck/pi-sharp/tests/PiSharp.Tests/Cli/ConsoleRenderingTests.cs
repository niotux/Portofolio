using System.IO;
using System.Text.Json.Nodes;
using PiSharp.Cli;
using PiSharp.Core.Agent;
using PiSharp.Core.Messages;
using PiSharp.Tests.Agent;
using Xunit;

namespace PiSharp.Tests.Cli;

public class ConsoleRenderingTests
{
    [Fact]
    public void AnsiDisabled_ReturnsEmptyColorsAndUnchangedDiff()
    {
        var ansi = new Ansi(false);
        Assert.Equal("", ansi.Red);
        const string diff = "--- a\n+++ b\n-old\n+new\n ctx";
        Assert.Equal(diff, ansi.ColorizeDiff(diff));
    }

    [Fact]
    public void AnsiEnabled_ColorizesDiffLines()
    {
        var ansi = new Ansi(true);
        var result = ansi.ColorizeDiff("--- a\n+++ b\n@@ -1 +1 @@\n-old\n+new\n ctx");
        var expected = string.Join('\n', new[]
        {
            "\u001b[36m--- a\u001b[0m",
            "\u001b[36m+++ b\u001b[0m",
            "\u001b[36m@@ -1 +1 @@\u001b[0m",
            "\u001b[31m-old\u001b[0m",
            "\u001b[32m+new\u001b[0m",
            " ctx",
        });
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AnsiEnabled_NormalizesCrLfToLf()
    {
        var ansi = new Ansi(true);
        var result = ansi.ColorizeDiff("-a\r\n+b");
        Assert.Equal("\u001b[31m-a\u001b[0m\n\u001b[32m+b\u001b[0m", result);
    }

    [Fact]
    public void Renderer_StreamsDeltasOnce()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        renderer.OnEvent(new MessageUpdate(Fakes.Text("hel"), new TextDelta(0, "hel", Fakes.Text("hel"))));
        renderer.OnEvent(new MessageUpdate(Fakes.Text("hello"), new TextDelta(1, "lo", Fakes.Text("hello"))));
        renderer.OnEvent(new MessageEnd(Fakes.Text("hello")));
        var text = output.ToString();
        Assert.Contains("hello", text);
        // Count non-overlapping occurrences.
        Assert.Equal(1, CountChar(text, 'h'));
    }

    [Fact]
    public void Renderer_PrintsWholeMessageWithoutDeltas()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        renderer.OnEvent(new MessageEnd(Fakes.Text("whole")));
        Assert.Contains("whole", output.ToString());
    }

    [Fact]
    public void Renderer_PrintsToolCallNameAndArgs()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        renderer.OnEvent(new ToolExecutionStart("c1", "read", Fakes.Json("""{"path":"src/a.cs"}""")));
        var text = output.ToString();
        Assert.Contains("read", text);
        Assert.Contains("src/a.cs", text);
    }

    [Fact]
    public void Renderer_StreamsPartialToolArguments()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        var start = Fakes.Assistant(StopReason.Pending,
            new ToolCallContent("c1", "read", new JsonObject()));
        var partial = start with
        {
            Content = [new ToolCallContent("c1", "read", Fakes.Json("{\"path\":\"src/a.cs\"}"))],
        };

        renderer.OnEvent(new MessageUpdate(start, new ToolCallStart(0, start)));
        renderer.OnEvent(new MessageUpdate(partial, new ToolCallDelta(0, "{\"path\":\"src/a.cs\"}", partial)));
        renderer.OnEvent(new MessageUpdate(partial, new ToolCallEnd(0, partial.ToolCalls.Single(), partial)));

        var text = output.ToString();
        Assert.Contains("read", text);
        Assert.Contains("{\"path\":\"src/a.cs\"}", text);
    }

    [Fact]
    public void Renderer_DoesNotRepeatStreamedToolHeaderAndCapsLiveArguments()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        var argumentValue = new string('x', 200);
        var start = Fakes.Assistant(StopReason.Pending,
            new ToolCallContent("c1", "bash", new JsonObject()));
        var final = start with
        {
            StopReason = StopReason.ToolUse,
            Content = [new ToolCallContent("c1", "bash", Fakes.Json($$"""{"command":"{{argumentValue}}"}"""))],
        };
        var delta = $$"""{"command":"{{argumentValue}}"}""";

        renderer.OnEvent(new MessageUpdate(start, new ToolCallStart(0, start)));
        renderer.OnEvent(new MessageUpdate(final, new ToolCallDelta(0, delta, final)));
        renderer.OnEvent(new MessageUpdate(final, new ToolCallEnd(0, final.ToolCalls.Single(), final)));
        renderer.OnEvent(new MessageEnd(final));
        renderer.OnEvent(new ToolExecutionStart("c1", "bash", final.ToolCalls.Single().Arguments));
        renderer.OnEvent(new ToolExecutionEnd("c1", "bash", AgentToolResult.FromText("ok"), false));

        var text = output.ToString();
        Assert.Equal(1, text.Split("bash", StringSplitOptions.None).Length - 1);
        Assert.Contains("\u2026", text);
        Assert.DoesNotContain(new string('x', 81), text);
    }

    [Fact]
    public void Renderer_TruncatesLongCommandArg()
    {
        var command = new string('x', 100);
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        renderer.OnEvent(new ToolExecutionStart("c1", "bash", Fakes.Json($"{{\"command\":\"{command}\"}}")));
        var text = output.ToString();
        Assert.Contains(new string('x', 77) + "...", text);
        Assert.DoesNotContain(new string('x', 78), text);
    }

    [Fact]
    public void Renderer_TruncatesToolResultByLines()
    {
        var lines = string.Join('\n', Enumerable.Range(1, 25).Select(i => $"l{i}"));
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false));
        renderer.OnEvent(new ToolExecutionEnd("c1", "read", AgentToolResult.FromText(lines), false));
        var text = output.ToString();
        Assert.Contains("l20", text);
        Assert.DoesNotContain("l21", text);
        Assert.Contains("5 more lines", text);
    }

    [Fact]
    public void Renderer_ColorsToolResultErrorRed()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(true));
        renderer.OnEvent(new ToolExecutionEnd("c1", "read", AgentToolResult.FromText("boom"), true));
        var text = output.ToString();
        Assert.Contains("\u001b[31m", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Renderer_ColorizesDiffResult()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(true));
        renderer.OnEvent(new ToolExecutionEnd("c1", "edit",
            AgentToolResult.FromText("unused", Fakes.Json("""{"diff":"-a\n+b"}""")), false));
        var text = output.ToString();
        Assert.Contains("\u001b[31m-a", text);
        Assert.Contains("\u001b[32m+b", text);
    }

    [Fact]
    public void Renderer_PrintFinalOnly_SuppressesDeltasUntilAgentEnd()
    {
        var output = new StringWriter();
        var renderer = new ConsoleRenderer(output, new StringWriter(), new Ansi(false), printFinalOnly: true);
        renderer.OnEvent(new MessageUpdate(Fakes.Text("hel"), new TextDelta(0, "hel", Fakes.Text("hel"))));
        renderer.OnEvent(new MessageUpdate(Fakes.Text("final"), new TextDelta(1, "lo", Fakes.Text("final"))));
        Assert.Equal("", output.ToString());
        renderer.OnEvent(new MessageEnd(Fakes.Text("final")));
        renderer.OnEvent(new AgentEnd(Array.Empty<PiSharp.Core.Messages.Message>()));
        var text = output.ToString();
        Assert.Contains("final", text);
        Assert.Equal(1, CountChar(text, 'f'));
    }

    private static int CountChar(string text, char c) => text.Count(x => x == c);
}
