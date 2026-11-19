// mcp.json: stdio and streamable-HTTP server definitions.
// Locations: ~/.config/pi-sharp/mcp.json (global) and .pi-sharp/mcp.json (project).
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PiSharp.Core.Config;
using PiSharp.Core.Json;

namespace PiSharp.Core.Mcp;

/// <summary>One server entry after loading and merging mcp.json files.</summary>
public sealed record McpServerConfig
{
    /// <summary>The code-intel epic's default alias for the code server; per-folder mcp.json files may override it.</summary>
    public const string CodeServerAlias = "code";

    public required string Alias { get; init; }
    public McpTransportKind Kind { get; init; }
    public string? Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
    public string? Cwd { get; init; }
    public string? Url { get; init; }
    public string? Protocol { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

/// <summary>Merged set of MCP servers keyed by alias. Project file wins on alias clash.</summary>
public sealed class McpConfig
{
    public IReadOnlyList<McpServerConfig> Servers { get; }

    public McpConfig(IEnumerable<McpServerConfig> servers) =>
        Servers = servers.ToList();

    public static McpConfig LoadDefault(string cwd, string? globalDir = null)
    {
        var global = ConfigPaths.McpFile(ConfigPaths.GlobalConfigDir(globalDir));
        var project = ConfigPaths.McpFile(ConfigPaths.ProjectConfigDir(cwd));
        return Load(
            File.Exists(global) ? global : null,
            File.Exists(project) ? project : null);
    }

    /// <summary>Parse and merge two mcp.json files. <paramref name="projectPath"/> overrides aliases from global.</summary>
    public static McpConfig Load(string? globalPath, string? projectPath)
    {
        var map = new Dictionary<string, McpServerConfig>(StringComparer.Ordinal);
        if (globalPath is not null) Merge(map, ParseFile(globalPath));
        if (projectPath is not null) Merge(map, ParseFile(projectPath));
        return new McpConfig(map.Values);
    }

    public static McpConfig ParseJson(string json, string? sourcePath = null)
    {
        var map = new Dictionary<string, McpServerConfig>(StringComparer.Ordinal);
        Merge(map, Parse(json, sourcePath ?? "mcp.json"));
        return new McpConfig(map.Values);
    }

    /// <summary>Expand <c>${VAR}</c> and <c>$VAR</c> from the process environment. Unknown vars become empty.</summary>
    public static string ExpandEnv(string value)
    {
        if (value.IndexOf('$') < 0) return value;
        var sb = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '$')
            {
                sb.Append(value[i]);
                continue;
            }

            if (i + 1 < value.Length && value[i + 1] == '{')
            {
                var end = value.IndexOf('}', i + 2);
                if (end < 0) { sb.Append(value[i]); continue; }
                var name = value[(i + 2)..end];
                sb.Append(Environment.GetEnvironmentVariable(name) ?? "");
                i = end;
                continue;
            }

            var j = i + 1;
            while (j < value.Length && (char.IsLetterOrDigit(value[j]) || value[j] == '_')) j++;
            if (j == i + 1) { sb.Append('$'); continue; }
            sb.Append(Environment.GetEnvironmentVariable(value[(i + 1)..j]) ?? "");
            i = j - 1;
        }
        return sb.ToString();
    }

    private static void Merge(Dictionary<string, McpServerConfig> map, IEnumerable<McpServerConfig> servers)
    {
        foreach (var s in servers) map[s.Alias] = s;
    }

    /// <summary>
    /// The code-intel epic's <c>code</c> server entry, added when an mcp.json does not already define one:
    /// streamable HTTP at the configured service URL (settings codeintel.url, else CODEINTEL_URL; none = not
    /// added), bearer token from the per-folder seat config (settings, CODEINTEL_TOKEN, then PI_FACTORY_TOKEN).
    /// A project mcp.json may still override it with its own alias <c>code</c> (for example a stdio server or an
    /// explicit ${CODEINTEL_TOKEN} header).
    /// </summary>
    public static McpConfig WithCodeServer(McpConfig config, PiSharp.Core.Config.CodeIntelSettings settings)
    {
        var url = settings.ResolveUrl();
        if (url is null) return config; // code-intel not configured for this folder
        var token = settings.ResolveToken();
        if (token is null) return config; // no credentials: a project mcp.json can still define 'code' itself
        if (config.Servers.Any(s => System.StringComparer.Ordinal.Equals(s.Alias, McpServerConfig.CodeServerAlias))) return config;
        var headers = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["Authorization"] = $"Bearer {token}",
        };
        var server = new McpServerConfig
        {
            Alias = McpServerConfig.CodeServerAlias,
            Kind = McpTransportKind.Http,
            Url = url,
            Headers = headers,
        };
        return new McpConfig(config.Servers.Append(server));
    }

    private static IEnumerable<McpServerConfig> ParseFile(string path) =>
        Parse(File.ReadAllText(path), path);

    private static IEnumerable<McpServerConfig> Parse(string json, string source)
    {
        McpConfigFile? file;
        try
        {
            file = JsonSerializer.Deserialize(json, CoreJsonContext.Default.McpConfigFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid MCP config {source}: {ex.Message}", ex);
        }

        if (file?.McpServers is null || file.McpServers.Count == 0)
            yield break;

        foreach (var (alias, entry) in file.McpServers)
        {
            if (entry is null || string.IsNullOrWhiteSpace(alias)) continue;
            var hasUrl = !string.IsNullOrWhiteSpace(entry.Url);
            var hasCommand = !string.IsNullOrWhiteSpace(entry.Command);
            if (hasUrl == hasCommand)
            {
                throw new InvalidOperationException(
                    $"MCP server \"{alias}\" in {source} must set exactly one of url or command.");
            }

            yield return new McpServerConfig
            {
                Alias = alias,
                Kind = hasUrl ? McpTransportKind.Http : McpTransportKind.Stdio,
                Command = entry.Command,
                Args = entry.Args ?? [],
                Env = entry.Env ?? new Dictionary<string, string>(),
                Cwd = entry.Cwd,
                Url = entry.Url,
                Protocol = entry.Type,
                Headers = entry.Headers ?? new Dictionary<string, string>(),
            };
        }
    }
}

/// <summary>Wire shape of mcp.json.</summary>
public sealed class McpConfigFile
{
    public Dictionary<string, McpServerEntry>? McpServers { get; set; }
}

public sealed class McpServerEntry
{
    public string? Type { get; set; }
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
    public string? Cwd { get; set; }
    public string? Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}
