// Hand-written flag parser. Ported from pi: packages/coding-agent/src/cli/args.ts @ 71dca871b
// Kept flags are the Phase 5 subset in docs/03-conversion-plan.md. Extension/TUI/auth flags dropped.
using PiSharp.Core.Models;

namespace PiSharp.Cli;

public enum CliMode
{
    Repl,
    Print,
    Json,
}

public sealed class CliArgs
{
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? SystemPrompt { get; set; }
    public List<string> AppendSystemPrompt { get; } = [];
    public ThinkingLevel? Thinking { get; set; }
    public bool Continue { get; set; }
    /// <summary>--fork: continue a copy of the session (--session, or the most recent with -c), leaving the original.</summary>
    public bool Fork { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }
    public CliMode Mode { get; set; } = CliMode.Repl;
    public string? Name { get; set; }
    public bool NoSession { get; set; }
    public string? Session { get; set; }
    public string? SessionDir { get; set; }
    public string? Cwd { get; set; }
    public string? ConfigDir { get; set; }
    public List<string> Tools { get; } = [];
    public List<string> ExcludeTools { get; } = [];
    public bool NoTools { get; set; }
    public bool NoSkills { get; set; }
    public bool NoPromptTemplates { get; set; }
    public bool NoContextFiles { get; set; }
    public bool ListModels { get; set; }
    public string? CubicleUrl { get; set; }
    public string? CubicleTokenEnv { get; set; }
    public List<string> Messages { get; } = [];
    public List<string> FileArgs { get; } = [];
    public List<string> Diagnostics { get; } = [];
}

public static class ArgsParser
{
    private static readonly string[] ThinkingLevels = ["off", "minimal", "low", "medium", "high", "xhigh", "max"];

    public static CliArgs Parse(IReadOnlyList<string> args)
    {
        var result = new CliArgs();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                for (var j = i + 1; j < args.Count; j++)
                {
                    if (args[j].StartsWith('@')) result.FileArgs.Add(args[j][1..]);
                    else result.Messages.Add(args[j]);
                }
                break;
            }
            if (arg is "--help" or "-h") { result.Help = true; continue; }
            if (arg is "--version" or "-v") { result.Version = true; continue; }
            if (arg is "--print" or "-p")
            {
                if (result.Mode != CliMode.Json) result.Mode = CliMode.Print;
                if (i + 1 < args.Count)
                {
                    var next = args[i + 1];
                    if (!next.StartsWith('@') && (!next.StartsWith('-') || next.StartsWith("---")))
                    {
                        result.Messages.Add(next);
                        i++;
                    }
                }
                continue;
            }
            if (arg == "--json") { result.Mode = CliMode.Json; continue; }
            if (arg is "--continue" or "-c") { result.Continue = true; continue; }
            if (arg is "--fork") { result.Fork = true; continue; }
            if (arg is "--no-session") { result.NoSession = true; continue; }
            if (arg is "--no-tools" or "-nt") { result.NoTools = true; continue; }
            if (arg is "--no-skills" or "-ns") { result.NoSkills = true; continue; }
            if (arg is "--no-prompt-templates" or "-np") { result.NoPromptTemplates = true; continue; }
            if (arg is "--no-context-files" or "-nc") { result.NoContextFiles = true; continue; }
            if (arg == "--list-models") { result.ListModels = true; continue; }
            if (arg.StartsWith('@')) { result.FileArgs.Add(arg[1..]); continue; }

            if (Take(args, ref i, "--model", out var model)) { result.Model = model; continue; }
            if (Take(args, ref i, "--api-key", out var apiKey)) { result.ApiKey = apiKey; continue; }
            if (Take(args, ref i, "--system-prompt", out var system)) { result.SystemPrompt = system; continue; }
            if (Take(args, ref i, "--append-system-prompt", out var append)) { result.AppendSystemPrompt.Add(append); continue; }
            if (Take(args, ref i, "--session", out var session)) { result.Session = session; continue; }
            if (Take(args, ref i, "--session-dir", out var sessionDir)) { result.SessionDir = sessionDir; continue; }
            if (Take(args, ref i, "--cwd", out var cwd)) { result.Cwd = cwd; continue; }
            if (Take(args, ref i, "--config-dir", out var configDir)) { result.ConfigDir = configDir; continue; }
            if (Take(args, ref i, "--cubicle-url", out var cubicleUrl)) { result.CubicleUrl = cubicleUrl; continue; }
            if (Take(args, ref i, "--cubicle-token-env", out var cubicleTokenEnv)) { result.CubicleTokenEnv = cubicleTokenEnv; continue; }
            if (Take(args, ref i, "--name", "-n", out var name)) { result.Name = name; continue; }
            if (Take(args, ref i, "--tools", "-t", out var tools))
            {
                result.Tools.AddRange(SplitCsv(tools));
                continue;
            }
            if (Take(args, ref i, "--exclude-tools", "-xt", out var exclude))
            {
                result.ExcludeTools.AddRange(SplitCsv(exclude));
                continue;
            }
            if (Take(args, ref i, "--thinking", out var thinking))
            {
                if (TryParseThinking(thinking, out var level)) result.Thinking = level;
                else result.Diagnostics.Add($"Invalid thinking level \"{thinking}\". Valid: {string.Join(", ", ThinkingLevels)}");
                continue;
            }

            if (arg.StartsWith('-'))
            {
                result.Diagnostics.Add($"Unknown option: {arg}");
                continue;
            }
            result.Messages.Add(arg);
        }
        return result;
    }

    public static bool TryParseThinking(string raw, out ThinkingLevel level) =>
        Enum.TryParse(raw.Replace("-", ""), ignoreCase: true, out level)
        && Enum.IsDefined(level);

    public static string HelpText() => """
        pi-sharp - coding agent for local OpenAI-compatible models

        Usage:
          pi-sharp [options] [--] [@files...] [prompt...]
          pi-sharp notebook import <dir>   import work/*.md files into the notebook as notes (seat stopped)

        Options:
          -p, --print                 One-shot: run the prompt and exit
          --json                      One-shot: emit agent events as JSONL
          --model <id>                Model id from models.json
          --session <file>            Open a specific session JSONL file
          -c, --continue              Continue the most recent session
          --fork                      With --session or -c: continue a copy of it, leaving the original untouched
          --cwd <dir>                 Working directory
          --config-dir <dir>          Global config directory override
          --thinking <level>          off, minimal, low, medium, high, xhigh, max
          --api-key <key>             Override the model API key
          --system-prompt <text>      Replace the default system prompt
          --append-system-prompt <t>  Append to the system prompt (repeatable)
          --no-session                Do not persist a session
          --session-dir <dir>         Directory for session files
          --name, -n <name>           Session display name
          --tools, -t <a,b>           Enable only these tools
          --exclude-tools, -xt <a,b>  Disable these tools
          --no-tools, -nt             Disable all tools
          --no-skills, -ns            Do not load skills
          --no-prompt-templates, -np  Do not load prompt templates
          --no-context-files, -nc     Do not load AGENTS.md / CLAUDE.md
          --list-models               Print models.json entries and exit
          --cubicle-url <url>         AI Factory chat base url; traces go to the actor's cubicle
          --cubicle-token-env <NAME>  Env var holding the cubicle bearer token (default PI_FACTORY_TOKEN)
          -h, --help                  Show this help
          -v, --version               Show version

        Config:
          ~/.config/pi-sharp/settings.json, models.json, mcp.json, skills/, prompts/
          <cwd>/.pi-sharp/            project overlay of the same files

        Slash commands (REPL):
          /model /think /compact /clear /session /tools /skills /mcp /quit
          plus one command per prompt template in prompts/*.md
        """;

    private static bool Take(IReadOnlyList<string> args, ref int i, string flag, out string value) =>
        Take(args, ref i, flag, null, out value);

    private static bool Take(IReadOnlyList<string> args, ref int i, string flag, string? shortFlag, out string value)
    {
        value = "";
        var arg = args[i];
        string? raw = null;
        if (arg == flag || (shortFlag is not null && arg == shortFlag))
        {
            if (i + 1 >= args.Count) return false;
            raw = args[++i];
        }
        else if (arg.StartsWith(flag + "=", StringComparison.Ordinal))
        {
            raw = arg[(flag.Length + 1)..];
        }
        if (raw is null) return false;
        value = raw;
        return true;
    }

    private static IEnumerable<string> SplitCsv(string raw) =>
        raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
