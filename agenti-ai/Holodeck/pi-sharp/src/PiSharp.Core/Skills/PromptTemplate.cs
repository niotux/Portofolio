// Prompt templates from local prompts/*.md and from MCP prompts/list + prompts/get.
// Local names take precedence. MCP prompts are registered as {alias}:{name}.
// Port of pi harness/prompt-templates.ts @ 71dca871b plus docs/04.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Config;
using PiSharp.Core.Mcp;
using PiSharp.Core.Messages;

namespace PiSharp.Core.Skills;

public enum PromptSourceKind
{
    Local,
    Mcp,
}

/// <summary>A slash-command prompt. ExpandAsync produces the user (and optional extra) messages.</summary>
public sealed partial class PromptTemplate
{
    [GeneratedRegex(@"\$\{(\d+|ARGUMENTS|@):-([^}]*)\}|\$\{@:(\d+)(?::(\d+))?\}|\$(ARGUMENTS|@|\d+)")]
    private static partial Regex Placeholder();

    public PromptTemplate() { }

    public PromptTemplate(
        string name,
        string description,
        string content,
        string filePath,
        string? argumentHint = null)
    {
        Name = name;
        Description = description;
        Body = content;
        FilePath = filePath;
        ArgumentHint = argumentHint;
        Source = PromptSourceKind.Local;
    }

    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? ArgumentHint { get; init; }
    public PromptSourceKind Source { get; init; }
    public string? FilePath { get; init; }
    public string? Body { get; init; }
    public string? ServerAlias { get; init; }
    public IReadOnlyList<McpPromptArgument> Arguments { get; init; } = [];
    public McpClient? Client { get; init; }

    public string Content => Body ?? "";

    /// <summary>Slash command without the leading slash, e.g. <c>review</c> or <c>store:review</c>.</summary>
    public string SlashName => Name;

    public async Task<IReadOnlyList<Message>> ExpandAsync(
        string argumentText,
        CancellationToken cancellationToken = default)
    {
        if (Source == PromptSourceKind.Local)
            return [new UserMessage([new TextContent(ExpandLocal(Body ?? "", argumentText))])];

        if (Client is null)
            throw new InvalidOperationException($"MCP prompt {Name} has no client.");

        var args = BindMcpArguments(argumentText);
        var result = await Client.GetPromptAsync(RemoteName(), args, cancellationToken).ConfigureAwait(false);
        var messages = new List<Message>();
        foreach (var msg in result.Messages)
        {
            var parts = McpAgentTool.MapContent(msg.Content);
            if (parts.Count == 0) continue;
            messages.Add(msg.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? new AssistantMessage { Content = parts, Model = "mcp-prompt" }
                : new UserMessage(parts));
        }
        if (messages.Count == 0)
            messages.Add(new UserMessage(result.Description ?? Name));
        return messages;
    }

    private string RemoteName()
    {
        if (ServerAlias is not null && Name.StartsWith(ServerAlias + ":", StringComparison.Ordinal))
            return Name[(ServerAlias.Length + 1)..];
        return Name;
    }

    private JsonObject BindMcpArguments(string argumentText)
    {
        var obj = new JsonObject();
        if (Arguments.Count == 0)
        {
            if (argumentText.Length > 0) obj["ARGUMENTS"] = argumentText;
            return obj;
        }

        if (Arguments.Count == 1)
        {
            obj[Arguments[0].Name] = argumentText;
            return obj;
        }

        var words = ParseCommandArgs(argumentText);
        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i < words.Count) obj[Arguments[i].Name] = words[i];
            else if (Arguments[i].Required) obj[Arguments[i].Name] = "";
        }
        return obj;
    }

    /// <summary>Substitute <c>$ARGUMENTS</c> and <c>$1</c>..<c>$n</c> the way pi does.</summary>
    public static string ExpandLocal(string body, string argumentText)
        => SubstituteArgs(body, ParseCommandArgs(argumentText));

    /// <summary>Bash-style quoted argument split used by local and MCP templates.</summary>
    public static IReadOnlyList<string> ParseCommandArgs(string argsString)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        char? inQuote = null;
        foreach (var ch in argsString)
        {
            if (inQuote is { } q)
            {
                if (ch == q) inQuote = null;
                else current.Append(ch);
            }
            else if (ch is '"' or '\'')
            {
                inQuote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else current.Append(ch);
        }
        if (current.Length > 0) args.Add(current.ToString());
        return args;
    }

    public static string SubstituteArgs(string content, IReadOnlyList<string> args)
    {
        var allArgs = string.Join(' ', args);
        return Placeholder().Replace(content, match =>
        {
            if (match.Groups[1].Success)
            {
                var target = match.Groups[1].Value;
                var defaultValue = match.Groups[2].Value;
                var value = target is "@" or "ARGUMENTS"
                    ? allArgs
                    : GetArg(args, int.Parse(target, System.Globalization.CultureInfo.InvariantCulture));
                return string.IsNullOrEmpty(value) ? defaultValue : value;
            }
            if (match.Groups[3].Success)
            {
                var start = Math.Max(0, int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) - 1);
                if (match.Groups[4].Success)
                {
                    var length = int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);
                    return string.Join(' ', args.Skip(start).Take(length));
                }
                return string.Join(' ', args.Skip(start));
            }
            var simple = match.Groups[5].Value;
            return simple is "ARGUMENTS" or "@"
                ? allArgs
                : GetArg(args, int.Parse(simple, System.Globalization.CultureInfo.InvariantCulture)) ?? "";
        });
    }

    public static string? Expand(string text, IReadOnlyList<PromptTemplate> templates)
    {
        if (!text.StartsWith('/')) return null;
        var space = text.IndexOfAny([' ', '\t']);
        var name = space < 0 ? text[1..] : text[1..space];
        var args = space < 0 ? "" : text[(space + 1)..];
        var template = templates.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return template is null ? null : SubstituteArgs(template.Content, ParseCommandArgs(args));
    }

    private static string? GetArg(IReadOnlyList<string> args, int oneBased) =>
        oneBased >= 1 && oneBased <= args.Count ? args[oneBased - 1] : null;
}

public static class PromptTemplateLoader
{
    public static IReadOnlyList<PromptTemplate> LoadLocal(
        string cwd,
        string? globalDir = null,
        IReadOnlyList<string>? extraPaths = null,
        bool includeDefaults = true)
    {
        var resolvedCwd = Path.GetFullPath(cwd);
        var templates = new List<PromptTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddRange(IEnumerable<PromptTemplate> items)
        {
            foreach (var item in items)
            {
                if (seen.Add(item.Name)) templates.Add(item);
            }
        }

        if (includeDefaults)
        {
            AddRange(LoadFromDir(ConfigPaths.PromptsDir(ConfigPaths.GlobalConfigDir(globalDir))));
            AddRange(LoadFromDir(ConfigPaths.PromptsDir(ConfigPaths.ProjectConfigDir(resolvedCwd))));
        }
        if (extraPaths is not null)
        {
            foreach (var raw in extraPaths)
            {
                var resolved = Path.GetFullPath(ConfigPaths.ExpandHome(raw), resolvedCwd);
                if (Directory.Exists(resolved)) AddRange(LoadFromDir(resolved));
                else if (File.Exists(resolved) && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var one = TryLoadFile(resolved);
                    if (one is not null) AddRange([one]);
                }
            }
        }
        return templates;
    }

    private static IReadOnlyList<PromptTemplate> LoadFromDir(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var list = new List<PromptTemplate>();
        try
        {
            foreach (var file in Directory.GetFiles(dir, "*.md"))
            {
                var template = TryLoadFile(file);
                if (template is not null) list.Add(template);
            }
        }
        catch { }
        return list;
    }

    public static IReadOnlyList<PromptTemplate> FromMcp(McpManager mcp)
    {
        var list = new List<PromptTemplate>();
        foreach (var conn in mcp.Connections)
        {
            if (conn.Client is null) continue;
            foreach (var p in conn.Prompts)
            {
                list.Add(new PromptTemplate
                {
                    Name = $"{conn.Alias}:{p.Name}",
                    Description = p.Description ?? p.Title,
                    ArgumentHint = p.Arguments.Count > 0
                        ? string.Join(' ', p.Arguments.Select(a => a.Required ? $"<{a.Name}>" : $"[{a.Name}]"))
                        : null,
                    Source = PromptSourceKind.Mcp,
                    ServerAlias = conn.Alias,
                    Arguments = p.Arguments,
                    Client = conn.Client,
                });
            }
        }
        return list;
    }

    /// <summary>Local templates first; MCP templates as <c>alias:name</c>. Local name wins on clash.</summary>
    public static IReadOnlyList<PromptTemplate> LoadAll(
        string cwd,
        McpManager? mcp = null,
        string? globalDir = null,
        IReadOnlyList<string>? extraPaths = null,
        bool includeDefaults = true)
    {
        var map = new Dictionary<string, PromptTemplate>(StringComparer.OrdinalIgnoreCase);
        if (mcp is not null)
        {
            foreach (var t in FromMcp(mcp)) map[t.Name] = t;
        }
        foreach (var t in LoadLocal(cwd, globalDir, extraPaths, includeDefaults)) map[t.Name] = t;
        return map.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IEnumerable<string> DiscoverDirs(string cwd, string? globalDir = null)
    {
        yield return ConfigPaths.PromptsDir(ConfigPaths.GlobalConfigDir(globalDir));
        yield return ConfigPaths.PromptsDir(ConfigPaths.ProjectConfigDir(cwd));
    }

    public static PromptTemplate? TryLoadFile(string path)
    {
        if (!File.Exists(path)) return null;
        var parsed = Frontmatter.Parse(File.ReadAllText(path));
        var name = Path.GetFileNameWithoutExtension(path);
        var description = parsed["description"];
        if (string.IsNullOrWhiteSpace(description))
        {
            var first = parsed.Body.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
            description = first.Length > 60 ? first[..60] + "..." : first;
        }
        return new PromptTemplate
        {
            Name = parsed["name"] ?? name,
            Description = description,
            ArgumentHint = parsed["argument-hint"] ?? parsed["argument_hint"],
            Source = PromptSourceKind.Local,
            FilePath = Path.GetFullPath(path),
            Body = parsed.Body,
        };
    }
}
