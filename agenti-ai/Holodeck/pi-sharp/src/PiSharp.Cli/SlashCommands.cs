// Slash commands. Ported from pi: packages/coding-agent/src/core/slash-commands.ts @ 71dca871b
// KEEP-SIMPLIFIED: /model /think /compact /clear /session /tools /skills /mcp /quit + prompt templates.
using PiSharp.Core.Config;
using PiSharp.Core.Models;

namespace PiSharp.Cli;

public abstract record SlashResult;
public sealed record SlashMessage(string Text) : SlashResult;
public sealed record SlashPrompt(string Text) : SlashResult;
public sealed record SlashQuit(int Code = 0) : SlashResult;
public sealed record SlashCompact : SlashResult;
public sealed record SlashClear : SlashResult;
public sealed record SlashModelChange(ModelConfig Model) : SlashResult;
public sealed record SlashMcpPrompt(PiSharp.Core.Skills.PromptTemplate Template, string Arguments) : SlashResult;
public sealed record SlashMcpReconnect(string Alias) : SlashResult;
public sealed record SlashUnknown(string Name) : SlashResult;

public static class SlashCommands
{
    public static readonly (string Name, string Description, string? Hint)[] Builtins =
    [
        ("model", "Show or switch model", "<id>"),
        ("think", "Show or set thinking level", "<level>"),
        ("compact", "Compact the session context", null),
        ("clear", "Start a new session", null),
        ("session", "Show session path and stats", null),
        ("stats", "Show notebook-share stats (p50/p95 per seat)", null),
        ("settings", "Show effective operational settings and cached factory switches", null),
        ("tools", "List available tools", null),
        ("skills", "List loaded skills", null),
        ("mcp", "Show MCP status or reconnect a server", "[reconnect <alias>]"),
        ("help", "List slash commands", null),
        ("quit", "Exit", null),
    ];

    public static SlashResult Handle(string line, CliState state)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('/')) return new SlashPrompt(trimmed);

        var space = trimmed.IndexOfAny([' ', '\t']);
        var name = (space < 0 ? trimmed[1..] : trimmed[1..space]).Trim();
        var args = space < 0 ? "" : trimmed[(space + 1)..].Trim();
        if (name.Length == 0) return new SlashUnknown("");

        return name.ToLowerInvariant() switch
        {
            "quit" or "exit" or "q" => new SlashQuit(),
            "help" or "?" => new SlashMessage(HelpText(state)),
            "model" => HandleModel(args, state),
            "think" or "thinking" => HandleThink(args, state),
            "compact" => new SlashCompact(),
            "clear" or "new" => new SlashClear(),
            "session" => new SlashMessage(SessionInfo(state)),
            "stats" => new SlashMessage(NotebookShareInfo(state)),
            "settings" => new SlashMessage(EffectiveSettings.Describe(state)),
            "tools" => new SlashMessage(ToolsInfo(state)),
            "skills" => new SlashMessage(SkillsInfo(state)),
            "mcp" => HandleMcp(args, state),
            _ => ExpandTemplate(name, args, state),
        };
    }

    public static string HelpText(CliState state)
    {
        var lines = new List<string> { "Commands:" };
        foreach (var (n, d, hint) in Builtins)
            lines.Add(hint is null ? $"  /{n,-12} {d}" : $"  /{n,-12} {d}  {hint}");
        if (state.Templates.Count > 0)
        {
            lines.Add("Prompt templates:");
            foreach (var t in state.Templates)
            {
                var hint = t.ArgumentHint is null ? "" : "  " + t.ArgumentHint;
                lines.Add($"  /{t.Name,-12} {t.Description}{hint}");
            }
        }
        if (state.McpTemplates.Count > 0)
        {
            lines.Add("MCP prompt templates:");
            foreach (var t in state.McpTemplates)
            {
                var hint = t.ArgumentHint is null ? "" : "  " + t.ArgumentHint;
                lines.Add($"  /{t.Name,-12} {t.Description ?? ""}{hint}");
            }
        }
        return string.Join('\n', lines);
    }

    private static SlashResult HandleModel(string args, CliState state)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            var lines = state.Models.Select(m =>
                (m.Id == state.Model.Id ? "* " : "  ") + m.Id + (m.Name is null ? "" : $" ({m.Name})") + "  " + m.BaseUrl);
            return new SlashMessage("Models:\n" + string.Join('\n', lines));
        }
        var found = ModelsFile.Find(state.Models, args);
        if (found is null) return new SlashMessage($"Unknown model: {args}");
        return new SlashModelChange(found);
    }

    private static SlashResult HandleThink(string args, CliState state)
    {
        if (string.IsNullOrWhiteSpace(args))
            return new SlashMessage($"Thinking level: {state.Thinking.ToString().ToLowerInvariant()}");
        if (!ArgsParser.TryParseThinking(args, out var level))
            return new SlashMessage($"Invalid thinking level: {args}. Valid: off, minimal, low, medium, high, xhigh, max");
        state.Thinking = level;
        return new SlashMessage($"Thinking level set to {level.ToString().ToLowerInvariant()}");
    }

    private static string SessionInfo(CliState state)
    {
        var session = state.Agent.Session;
        var name = session.GetSessionName() ?? state.SessionName ?? "(unnamed)";
        var file = session.SessionFile ?? "(ephemeral)";
        return $"session {session.SessionId}\nname: {name}\nfile: {file}\nentries: {session.Entries.Count}\nmodel: {state.Model.Id}\nthinking: {state.Thinking.ToString().ToLowerInvariant()}\ncwd: {state.Cwd}";
    }

    /// <summary>
    /// /stats (pi-sharp-memory-curation-1): the notebook's share of the request this process - the
    /// notebook's characters against the rest of the request, and the rolling p50/p95 of that share.
    /// One line per seat when the share spans seats, but this process is one seat: a single block.
    /// </summary>
    private static string NotebookShareInfo(CliState state)
    {
        if (state.NotebookShare is not { } tracker) return "notebook share: (no notebook)";
        var (samples, p50, p95) = tracker.Percentiles();
        var seat = state.CacheSeatId ?? "local";
        var lines = new List<string> { $"notebook share (seat {seat})" };
        if (samples == 0)
        {
            lines.Add("  (no requests measured yet)");
            return string.Join('\n', lines);
        }
        lines.Add($"  requests: {samples}");
        lines.Add($"  p50: {Pct(p50)}  p95: {Pct(p95)}");
        if (tracker.Current is { } latest)
            lines.Add($"  last: notebook {latest.NotebookChars} / request {latest.RequestChars} = {Pct(latest.Share)} " +
                     $"pinned {latest.Pinned}, deltas {latest.Deltas}, recalls {latest.Recalls}");
        return string.Join('\n', lines);
    }

    private static string Pct(double share) => share.ToString("P1");

    private static string ToolsInfo(CliState state)
    {
        if (state.Context.Tools.Count == 0) return "No tools enabled.";
        return "Tools:\n" + string.Join('\n', state.Context.Tools.Select(t =>
        {
            var desc = t.Description;
            var dot = desc.IndexOf('.');
            if (dot > 0) desc = desc[..(dot + 1)];
            return $"  {t.Name,-8} {desc}";
        }));
    }

    private static string SkillsInfo(CliState state)
    {
        if (state.Skills.Count == 0) return "No skills loaded.";
        return "Skills:\n" + string.Join('\n', state.Skills.Select(s =>
            $"  {s.Name,-16} {s.Description}\n                   {s.FilePath}"));
    }

    private static string McpInfo(CliState state)
    {
        var status = state.Mcp.FormatStatus();
        // Attached mode verified this slug against the KB at startup; show it next to the MCP servers.
        return string.IsNullOrEmpty(state.FactoryActor) ? status : status + "\nfactory  @" + state.FactoryActor + " verified";
    }

    private static SlashResult HandleMcp(string args, CliState state)
    {
        if (string.IsNullOrWhiteSpace(args)) return new SlashMessage(McpInfo(state));

        var parts = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("reconnect", StringComparison.OrdinalIgnoreCase))
            return new SlashMcpReconnect(parts[1]);

        return new SlashMessage("Usage: /mcp [reconnect <alias>]");
    }

    private static SlashResult ExpandTemplate(string name, string args, CliState state)
    {
        var expanded = PiSharp.Core.Skills.PromptTemplate.Expand("/" + name + (args.Length == 0 ? "" : " " + args), state.Templates);
        if (expanded is not null) return new SlashPrompt(expanded);
        var remote = state.McpTemplates.FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (remote is not null) return new SlashMcpPrompt(remote, args);
        return new SlashUnknown(name);
    }
}
