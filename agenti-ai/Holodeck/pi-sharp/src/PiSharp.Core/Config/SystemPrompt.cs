// Ported from pi: packages/coding-agent/src/core/system-prompt.ts @ 71dca871b
// KEEP-SIMPLIFIED: AGENTS.md discovery stays; pi-docs self-reference section is dropped.
using PiSharp.Core.Agent;
using PiSharp.Core.Skills;

namespace PiSharp.Core.Config;

public sealed record SystemPromptOptions
{
    public string Cwd { get; init; } = "";
    public string? CustomPrompt { get; init; }
    public string? AppendSystemPrompt { get; init; }
    public IReadOnlyList<string>? SelectedTools { get; init; }
    public IReadOnlyDictionary<string, string>? ToolSnippets { get; init; }
    public IReadOnlyList<string>? PromptGuidelines { get; init; }
    public IReadOnlyList<ContextFile> ContextFiles { get; init; } = [];
    public IReadOnlyList<PiSharp.Core.Skills.Skill> Skills { get; init; } = [];
    public int RemoteSkillsPromptCap { get; init; } = 20;
    /// <summary>The seat has a notebook (it is attached after the first prompt build, so the tool list alone does
    /// not show it). Either this or a <c>notebook</c> tool adds <see cref="SystemPrompt.MemoryGuideline"/>.</summary>
    public bool NotebookMemory { get; init; }
    /// <summary>The family of the model the seat runs (<c>gpt</c> for OpenAI's): picks the instructions tuned for it
    /// (plan-pi-sharp-codex item 9). Null = none.</summary>
    public string? ModelFamily { get; init; }
    /// <summary>The most bytes of project instruction files put in the prompt (Codex project_doc_max_bytes, 32 KiB).</summary>
    public int ProjectDocMaxBytes { get; init; } = 32 * 1024;
}

/// <summary>Assembles the system prompt: tools, guidelines, project context, skills index.</summary>
public static class SystemPrompt
{
    public static string Build(SystemPromptOptions options)
    {
        var cwd = options.Cwd.Replace('\\', '/');
        var append = string.IsNullOrWhiteSpace(options.AppendSystemPrompt)
            ? ""
            : "\n\n" + options.AppendSystemPrompt;
        var tools = options.SelectedTools ?? ["read", "bash", "edit", "write"];
        var skillReadTool = tools.Contains("read") ? "read" : tools.Contains("bash") ? "bash" : null;

        var family = FamilySection(options.ModelFamily, tools);
        if (!string.IsNullOrWhiteSpace(options.CustomPrompt))
        {
            var custom = options.CustomPrompt + family + append;
            custom += FormatContext(options.ContextFiles, options.ProjectDocMaxBytes);
            if (HasSkillSearchTool(tools))
                custom += "\n\n" + SkillRetrievalHint;
            if (options.NotebookMemory || tools.Contains("notebook"))
                custom += "\n\n" + MemoryGuideline;
            if (skillReadTool is not null && options.Skills.Count > 0)
                custom += SkillLoader.FormatForPrompt(options.Skills, skillReadTool, options.RemoteSkillsPromptCap);
            custom += $"\nCurrent working directory: {cwd}\n";
            return custom;
        }

        var snippets = options.ToolSnippets;
        var visible = snippets is null
            ? []
            : tools.Where(name => snippets.ContainsKey(name)).ToList();
        var toolsList = visible.Count == 0
            ? "(none)"
            : string.Join('\n', visible.Select(name => $"- {name}: {snippets![name]}"));

        var guidelines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string g)
        {
            if (seen.Add(g)) guidelines.Add(g);
        }

        var hasBash = tools.Contains("bash");
        var hasGrep = tools.Contains("grep");
        var hasFind = tools.Contains("find");
        var hasLs = tools.Contains("ls");
        if (hasBash && !hasGrep && !hasFind && !hasLs)
            Add("Use bash for file operations like ls, rg, find");

        if (options.PromptGuidelines is not null)
        {
            foreach (var g in options.PromptGuidelines)
            {
                var trimmed = g.Trim();
                if (trimmed.Length > 0) Add(trimmed);
            }
        }

        if (HasSkillSearchTool(tools))
            Add(SkillRetrievalHint);
        if (options.NotebookMemory || tools.Contains("notebook"))
            Add(MemoryGuideline);

        Add("Be concise in your responses");
        Add("Show file paths clearly when working with files");

        var prompt = """
            You are an expert coding assistant operating inside pi-sharp, a coding agent harness. You help users by reading files, executing commands, editing code, and writing new files.

            Available tools:
            """ + "\n" + toolsList + "\n\n" +
            """
            In addition to the tools above, you may have access to other custom tools depending on the project.

            Guidelines:
            """ + "\n" + string.Join('\n', guidelines.Select(g => "- " + g));

        prompt += family;
        prompt += append;
        prompt += FormatContext(options.ContextFiles, options.ProjectDocMaxBytes);
        if (skillReadTool is not null && options.Skills.Count > 0)
            prompt += SkillLoader.FormatForPrompt(options.Skills, skillReadTool, options.RemoteSkillsPromptCap);
        prompt += $"\nCurrent working directory: {cwd}";
        return prompt;
    }

    public static IReadOnlyDictionary<string, string> SnippetsFrom(IEnumerable<ITool> tools)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var text = tool.Description.Trim();
            var dot = text.IndexOf('.');
            map[tool.Name] = dot > 0 && dot < 160 ? text[..(dot + 1)] : (text.Length > 160 ? text[..157] + "..." : text);
        }
        return map;
    }

    /// <summary>The project instruction files, in their order (the closest last), until <paramref name="maxBytes"/> is reached
    /// (Codex project_doc_max_bytes): a file that would pass it is cut there, and the rest are named so the model can read
    /// them itself.</summary>
    private static string FormatContext(IReadOnlyList<ContextFile> files, int maxBytes = int.MaxValue)
    {
        if (files.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("\n\n<project_context>\n\n");
        sb.Append("Project-specific instructions and guidelines:\n\n");
        var left = maxBytes;
        var skipped = new List<string>();
        foreach (var file in files)
        {
            var path = file.Path.Replace('\\', '/');
            if (left <= 0) { skipped.Add(path); continue; }
            var content = file.Content;
            var bytes = System.Text.Encoding.UTF8.GetByteCount(content);
            if (bytes > left)
            {
                content = CapInstructions(content, left, $"read {path} for the rest", maxBytes);
                bytes = left;
            }
            left -= bytes;
            sb.Append("<project_instructions path=\"").Append(path).Append("\">\n");
            sb.Append(content);
            sb.Append("\n</project_instructions>\n\n");
        }
        if (skipped.Count > 0)
            sb.Append($"[More instruction files past the {maxBytes / 1024} KiB limit, read them when you work there: {string.Join(", ", skipped)}]\n\n");
        sb.Append("</project_context>\n");
        return sb.ToString();
    }

    /// <summary>Standing instructions cut to <paramref name="maxBytes"/> of UTF-8 (Codex project_doc_max_bytes), with a line saying
    /// where the rest is; text within the cap comes back unchanged. <paramref name="limit"/> is the limit named in that line.</summary>
    public static string CapInstructions(string text, int maxBytes, string rest, int? limit = null)
    {
        if (maxBytes <= 0 || System.Text.Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        var cut = text.Length;
        while (cut > 0 && System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, cut)) > maxBytes) cut = Math.Max(0, cut - Math.Max(1, (cut - maxBytes) / 2));
        return text[..cut] + $"\n[... cut at the {(limit ?? maxBytes) / 1024} KiB instruction limit; {rest}]";
    }

    /// <summary>Instructions tuned for a model family, appended after the base prompt.</summary>
    public static string FamilySection(string? family, IReadOnlyCollection<string>? tools = null) =>
        family == "gpt" ? "\n\n" + ForTools(GptWorkingStyle, tools) : "";

    /// <summary>Lines that name a tool, kept only when the seat has it (a plan tool that is off must not be asked for).</summary>
    private static readonly (string Marker, string Tool)[] ToolLines =
    [
        ("the plan tool", "plan"), ("`apply_patch`", "apply_patch"), ("the task tool", "task"), ("the review tool", "review"),
    ];

    /// <summary>The text without the lines naming a tool the seat does not have, and without a heading left empty.</summary>
    internal static string ForTools(string text, IReadOnlyCollection<string>? tools)
    {
        if (tools is null) return text;
        var kept = text.Split('\n').Where(line => !ToolLines.Any(t => line.Contains(t.Marker, StringComparison.Ordinal) && !tools.Contains(t.Tool))).ToList();
        for (var i = kept.Count - 1; i >= 0; i--)
            if (kept[i].StartsWith("## ", StringComparison.Ordinal) && (i + 1 >= kept.Count || kept[i + 1].Length == 0 && (i + 2 >= kept.Count || kept[i + 2].StartsWith('#'))))
                kept.RemoveRange(i, i + 1 < kept.Count ? 2 : 1);
        return string.Join('\n', kept);
    }

    /// <summary>
    /// How a GPT model works best as a coding agent, adapted for unattended factory seats from OpenAI Codex's base
    /// instructions (codex-rs models-manager/prompt.md, Apache-2.0; plan-pi-sharp-codex item 9). The parts about an
    /// interactive terminal user and approvals are left out: a seat reports through chat and its harness guards it.
    /// </summary>
    public const string GptWorkingStyle =
        """
        # How you work (adapted from OpenAI Codex)

        ## Task execution
        - Keep going until the task is completely resolved before you end your turn. End it only when you are sure the problem is solved, or when it is blocked and you have said who or what must act. Do not guess or make up an answer.
        - Fix problems at the root cause rather than with surface patches. Avoid unneeded complexity. Keep changes minimal, focused and consistent with the style of the code around them.
        - Do not fix unrelated bugs or broken tests: name them in your hand-in instead.
        - Use `git log` and `git blame` when you need the history behind a piece of code.
        - Do not add copyright or license headers. Add comments only where the code around them has that kind of comment. No one-letter names.
        - Edit files with `apply_patch`. Do not re-read a file after a patch succeeded: the tool fails when it does not apply.
        - Never revert changes you did not make. Never run destructive git commands (`reset --hard`, `checkout --`, `clean -fdx`) over work that is not yours.

        ## Planning
        - For work with several steps, keep a plan with the plan tool: meaningful steps that can each be checked, marked done as you finish them, updated with a reason when the plan changes. No plan for a one-step task, and no filler steps.

        ## Validating your work
        - Start with the most specific tests of what you changed, then widen as you gain confidence. Add a test where the codebase has a natural place for one; do not add a test framework where there is none.
        - Run builds and tests yourself: nobody approves each step. Give a full test suite or a long log to the task tool (role=triage): only the failures and their causes come back to your context.
        - Before you hand work in, run the review tool on your branch and fix its P0/P1 findings or say why they do not apply.

        ## Shell
        - Search with `rg` and `rg --files` (fall back to `grep` and `find` when `rg` is missing). Read files with the read tool, not with scripts that print them.

        ## Your final message
        - It is a chat reply to whoever woke you: concise (usually under ten lines), plain sentences or a short list, no heavy formatting. Reference files as `path:line`. Say what changed, how it was proven, and what comes next or who must act.
        """;

    /// <summary>The notebook is the seat's working memory: it is searched, recalled at task start and after a
    /// compaction, and synced to the KB; a .md file in the repo is none of those, and ends up committed.</summary>
    public const string MemoryGuideline = PiSharp.Core.Notebook.NotebookTool.MemoryGuideline;

    private const string SkillRetrievalHint =
        "If no listed skill fits, use a *__search_skills tool to search, then read the returned skill://.../SKILL.md or use install_skill before acting.";

    private static bool HasSkillSearchTool(IReadOnlyList<string> tools) =>
        tools.Any(IsSkillSearchTool);

    private static bool IsSkillSearchTool(string name) =>
        name.Equals("search_skills", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("__search_skills", StringComparison.OrdinalIgnoreCase);
}
