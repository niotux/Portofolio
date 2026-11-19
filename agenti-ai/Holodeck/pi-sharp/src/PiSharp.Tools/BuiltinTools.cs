// Factory for the built-in tools. pi: packages/coding-agent/src/core/tools/index.ts
// createAllTools minus powershell (dropped, decision 9 / bash only), plus apply_patch (Codex format).
using PiSharp.Core.Agent;
using PiSharp.Core.Skills;

namespace PiSharp.Tools;

/// <summary>Creates the built-in coding-agent tools bound to a working directory.</summary>
public static class BuiltinTools
{
    /// <summary>The CLI's built-in tools: read, bash, edit, write, apply_patch, grep, find, ls.</summary>
    public static IReadOnlyList<IAgentTool> Create(
        string cwd,
        bool supportsImages = true,
        ISkillUriResolver? skillUris = null,
        int warningPixels = ImageWarnings.DefaultWarnPixels,
        int contextWindow = 0,
        Action<string>? warningSink = null,
        Action<string, int>? commandExitSink = null)
    {
        // One tracker per tool set: what read saw is what edit/write may change.
        var tracker = new FileReadTracker();
        return
        [
            new ReadTool(cwd, supportsImages)
            {
                SkillUriResolver = skillUris,
                WarningPixels = warningPixels,
                ContextWindow = contextWindow,
                WarningSink = warningSink,
                ReadTracker = tracker,
            },
            // pi-sharp-checkpoints-3: the command-exit sink feeds the checkpoint green tracker; it is
            // null in tests and in seats that keep the custom ToolsFactory.
            new BashTool(cwd) { CommandExitSink = commandExitSink },
            new EditTool(cwd) { ReadTracker = tracker },
            new WriteTool(cwd) { ReadTracker = tracker },
            new PatchTool(cwd) { ReadTracker = tracker },
            new GrepTool(cwd),
            new FindTool(cwd),
            new LsTool(cwd),
        ];
    }
}
