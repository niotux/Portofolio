// Reference MCP skill store: a folder of SKILL.md skills + prompts/*.md served over JSON-RPC.
// Test fixture for Phase 6. See docs/04-skill-store-via-mcp.md.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PiSharp.Core.Mcp;
using PiSharp.Core.Skills;

namespace PiSharp.SkillStore;

/// <summary>Serves <paramref name="root"/> as MCP tools, prompts and skill:// resources.</summary>
public sealed class SkillStoreHost
{
    private readonly string _root;
    private readonly string _alias;
    private readonly McpServer _server;

    public McpServer Server => _server;

    public SkillStoreHost(string root, string alias = "store")
    {
        _root = Path.GetFullPath(root);
        _alias = alias;
        Directory.CreateDirectory(_root);
        _server = new McpServer("pi-sharp-skill-store")
        {
            Capabilities = new JsonObject
            {
                ["tools"] = new JsonObject { ["listChanged"] = false },
                ["prompts"] = new JsonObject { ["listChanged"] = false },
                ["resources"] = new JsonObject { ["subscribe"] = false, ["listChanged"] = false },
            },
        };
        _server.Handle("tools/list", ToolsList);
        _server.Handle("tools/call", ToolsCall);
        _server.Handle("prompts/list", PromptsList);
        _server.Handle("prompts/get", PromptsGet);
        _server.Handle("resources/list", ResourcesList);
        _server.Handle("resources/templates/list", ResourceTemplates);
        _server.Handle("resources/read", ResourcesRead);
    }

    public Task ServeAsync(Stream input, Stream output, CancellationToken cancellationToken = default) =>
        _server.ServeAsync(input, output, cancellationToken);

    public JsonRpcPeer Attach(Stream input, Stream output) => _server.Attach(input, output);

    public static async Task RunStdioAsync(string root, string alias = "store", CancellationToken cancellationToken = default)
    {
        var host = new SkillStoreHost(root, alias);
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        await host.ServeAsync(stdin, stdout, cancellationToken).ConfigureAwait(false);
    }

    private JsonNode? ToolsList(JsonNode? _) => new JsonObject
    {
        ["tools"] = new JsonArray(
            Tool("search_skills", "Search skills in this store by query string.", """
                {"type":"object","properties":{"query":{"type":"string"},"tags":{"type":"array","items":{"type":"string"}}},"required":["query"]}
                """),
            Tool("get_skill_manifest", "File list, version and checksums for one skill.", """
                {"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}
                """),
            Tool("install_skill", "Return the files of a skill so the agent can write them locally.", """
                {"type":"object","properties":{"name":{"type":"string"},"version":{"type":"string"}},"required":["name"]}
                """),
            Tool("list_updates", "Skills whose installed version differs from the store.", """
                {"type":"object","properties":{"installed":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"version":{"type":"string"}}}}}}
                """)),
    };

    private static JsonObject Tool(string name, string description, string schemaJson) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = JsonNode.Parse(schemaJson)!.AsObject(),
    };

    private JsonNode? ToolsCall(JsonNode? @params)
    {
        var obj = @params as JsonObject ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "missing params");
        var name = obj["name"]?.GetValue<string>()
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "missing tool name");
        var args = obj["arguments"] as JsonObject ?? new JsonObject();
        JsonNode payload = name switch
        {
            "search_skills" => SearchSkills(args),
            "get_skill_manifest" => GetManifest(Required(args, "name")),
            "install_skill" => InstallSkill(Required(args, "name")),
            "list_updates" => ListUpdates(args["installed"] as JsonArray),
            _ => throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown tool {name}"),
        };
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = payload.ToJsonString() }),
            ["isError"] = false,
        };
    }

    private JsonNode SearchSkills(JsonObject args)
    {
        var query = args["query"]?.GetValue<string>() ?? "";
        var tagsFilter = args["tags"] is JsonArray t
            ? t.Select(n => n?.GetValue<string>()).Where(s => s is not null).Select(s => s!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var hits = new JsonArray();
        foreach (var skill in ListSkills())
        {
            var blob = skill.Name + " " + skill.Description;
            if (query.Length > 0 && blob.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (tagsFilter is { Count: > 0 } && !skill.Tags.Any(tagsFilter.Contains)) continue;
            hits.Add((JsonNode)new JsonObject
            {
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["version"] = skill.Version,
                ["tags"] = new JsonArray(skill.Tags.Select(x => JsonValue.Create(x)).ToArray()),
            });
        }
        return hits;
    }

    private JsonNode GetManifest(string name)
    {
        var skill = ListSkills().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown skill {name}");
        var files = new JsonArray();
        foreach (var (rel, bytes) in EnumerateFiles(skill.Directory))
        {
            files.Add((JsonNode)new JsonObject
            {
                ["path"] = rel,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                ["bytes"] = bytes.Length,
            });
        }
        return new JsonObject
        {
            ["name"] = skill.Name,
            ["version"] = skill.Version,
            ["description"] = skill.Description,
            ["files"] = files,
        };
    }

    private JsonNode InstallSkill(string name)
    {
        var skill = ListSkills().FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown skill {name}");
        var files = new JsonArray();
        foreach (var (rel, bytes) in EnumerateFiles(skill.Directory))
        {
            files.Add((JsonNode)new JsonObject
            {
                ["path"] = rel,
                ["content"] = Encoding.UTF8.GetString(bytes),
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            });
        }
        return new JsonObject
        {
            ["name"] = skill.Name,
            ["version"] = skill.Version,
            ["files"] = files,
        };
    }

    private JsonNode ListUpdates(JsonArray? installed)
    {
        var current = ListSkills().ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var updates = new JsonArray();
        if (installed is null) return updates;
        foreach (var item in installed)
        {
            if (item is not JsonObject o) continue;
            var name = o["name"]?.GetValue<string>();
            var version = o["version"]?.GetValue<string>();
            if (name is null || !current.TryGetValue(name, out var skill)) continue;
            if (!string.Equals(skill.Version, version, StringComparison.Ordinal))
            {
                updates.Add((JsonNode)new JsonObject
                {
                    ["name"] = name,
                    ["installed"] = version,
                    ["available"] = skill.Version,
                });
            }
        }
        return updates;
    }

    private JsonNode? PromptsList(JsonNode? _)
    {
        var arr = new JsonArray();
        foreach (var t in ListPrompts())
        {
            arr.Add((JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["arguments"] = new JsonArray(new JsonObject
                {
                    ["name"] = "ARGUMENTS",
                    ["description"] = t.ArgumentHint ?? "Template arguments",
                    ["required"] = false,
                }),
            });
        }
        return new JsonObject { ["prompts"] = arr };
    }

    private JsonNode? PromptsGet(JsonNode? @params)
    {
        var obj = @params as JsonObject ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "missing params");
        var name = obj["name"]?.GetValue<string>()
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "missing name");
        var t = ListPrompts().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown prompt {name}");
        var argText = "";
        if (obj["arguments"] is JsonObject args)
        {
            argText = args["ARGUMENTS"]?.GetValue<string>()
                ?? args.FirstOrDefault().Value?.GetValue<string>()
                ?? "";
        }
        var expanded = PromptTemplate.ExpandLocal(t.Body ?? "", argText);
        return new JsonObject
        {
            ["description"] = t.Description,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = expanded },
            }),
        };
    }

    private JsonNode? ResourcesList(JsonNode? _)
    {
        var arr = new JsonArray();
        foreach (var skill in ListSkills())
        {
            foreach (var (rel, _) in EnumerateFiles(skill.Directory))
            {
                var uri = SkillUri(skill.Name, rel);
                arr.Add((JsonNode)new JsonObject
                {
                    ["uri"] = uri,
                    ["name"] = rel == "SKILL.md" ? skill.Name : $"{skill.Name}/{rel}",
                    ["description"] = rel == "SKILL.md" ? skill.Description : $"{skill.Name} {rel}",
                    ["mimeType"] = rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? "text/markdown" : "text/plain",
                });
            }
        }
        return new JsonObject { ["resources"] = arr };
    }

    private JsonNode? ResourceTemplates(JsonNode? _) => new JsonObject
    {
        ["resourceTemplates"] = new JsonArray(new JsonObject
        {
            ["uriTemplate"] = $"skill://{_alias}/{{name}}/{{path}}",
            ["name"] = "skill-file",
            ["description"] = "A file inside a skill folder",
            ["mimeType"] = "text/plain",
        }),
    };

    private JsonNode? ResourcesRead(JsonNode? @params)
    {
        var uri = (@params as JsonObject)?["uri"]?.GetValue<string>()
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, "missing uri");
        var prefix = $"skill://{_alias}/";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown URI {uri}");
        var rest = uri[prefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash < 0) throw new JsonRpcException(JsonRpcException.InvalidParams, $"URI missing path: {uri}");
        var skillName = rest[..slash];
        var relative = rest[(slash + 1)..];
        var skill = ListSkills().FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase))
            ?? throw new JsonRpcException(JsonRpcException.InvalidParams, $"Unknown skill {skillName}");
        var full = Path.GetFullPath(Path.Combine(skill.Directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(skill.Directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, skill.Directory, StringComparison.Ordinal))
            throw new JsonRpcException(JsonRpcException.InvalidParams, "URI path escapes skill dir");
        if (!File.Exists(full))
            throw new JsonRpcException(JsonRpcException.InvalidParams, $"File not found: {uri}");
        var text = File.ReadAllText(full);
        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["uri"] = uri,
                ["mimeType"] = "text/plain",
                ["text"] = text,
            }),
        };
    }

    private string SkillUri(string skillName, string relative) =>
        $"skill://{_alias}/{skillName}/{relative.Replace('\\', '/')}";

    private IEnumerable<StoredSkill> ListSkills()
    {
        if (!Directory.Exists(_root)) yield break;
        foreach (var dir in Directory.GetDirectories(_root))
        {
            if (string.Equals(Path.GetFileName(dir), "prompts", StringComparison.OrdinalIgnoreCase))
                continue;
            var loaded = SkillLoader.TryLoadFromDirectory(dir);
            if (loaded is null) continue;
            var fm = Frontmatter.Parse(File.ReadAllText(loaded.SkillMdPath));
            yield return new StoredSkill(
                loaded.Name,
                loaded.Description,
                loaded.Version ?? "0.0.0",
                fm.List("tags").ToList(),
                loaded.Location);
        }
    }

    private IEnumerable<PromptTemplate> ListPrompts()
    {
        var dir = Path.Combine(_root, "prompts");
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.GetFiles(dir, "*.md"))
        {
            var t = PromptTemplateLoader.TryLoadFile(file);
            if (t is not null) yield return t;
        }
    }

    private static IEnumerable<(string Relative, byte[] Bytes)> EnumerateFiles(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(directory, file).Replace('\\', '/');
            yield return (rel, File.ReadAllBytes(file));
        }
    }

    private static string Required(JsonObject args, string name) =>
        args[name]?.GetValue<string>()
        ?? throw new JsonRpcException(JsonRpcException.InvalidParams, $"missing {name}");

    private sealed record StoredSkill(string Name, string Description, string Version, List<string> Tags, string Directory);
}
