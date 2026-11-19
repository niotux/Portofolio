using PiSharp.Cli;
using PiSharp.Core.Models;

namespace PiSharp.Tests.Cli;

public class ArgsTests
{
    [Fact]
    public void ParsesPhase5Flags()
    {
        var args = ArgsParser.Parse(["-p", "--json", "--model", "qwen", "--session", "s.jsonl", "--continue", "--cwd", "/tmp", "@notes.md", "hello", "world"]);
        Assert.Equal(CliMode.Json, args.Mode);
        Assert.Equal("qwen", args.Model);
        Assert.Equal("s.jsonl", args.Session);
        Assert.True(args.Continue);
        Assert.Equal("/tmp", args.Cwd);
        Assert.Equal(["notes.md"], args.FileArgs);
        Assert.Equal(["hello", "world"], args.Messages);
    }

    [Fact]
    public void PrintConsumesFollowingPrompt()
    {
        var args = ArgsParser.Parse(["-p", "do the thing"]);
        Assert.Equal(CliMode.Print, args.Mode);
        Assert.Equal(["do the thing"], args.Messages);
    }

    [Fact]
    public void DoubleDashStopsParsing()
    {
        var args = ArgsParser.Parse(["-p", "--", "--not-a-flag", "@file.txt"]);
        Assert.Equal(["--not-a-flag"], args.Messages);
        Assert.Equal(["file.txt"], args.FileArgs);
    }

    [Fact]
    public void ThinkingAndUnknown()
    {
        var args = ArgsParser.Parse(["--thinking", "high", "--nope"]);
        Assert.Equal(ThinkingLevel.High, args.Thinking);
        Assert.Contains(args.Diagnostics, d => d.Contains("Unknown option"));
    }

    [Fact]
    public void ParsesConfigDirectoryOverride()
    {
        var args = ArgsParser.Parse(["--config-dir", "C:/pi-config"]);
        Assert.Equal("C:/pi-config", args.ConfigDir);
    }

    [Fact]
    public void HelpTextListsRequiredFlags()
    {
        var help = ArgsParser.HelpText();
        Assert.Contains("--print", help);
        Assert.Contains("--json", help);
        Assert.Contains("--session", help);
        Assert.Contains("--config-dir", help);
        Assert.Contains("/model", help);
    }
}
