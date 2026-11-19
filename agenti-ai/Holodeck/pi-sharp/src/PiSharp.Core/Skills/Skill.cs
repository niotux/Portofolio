// Skills (SKILL.md folders) from local dirs and from MCP skill:// resources.
// Port of pi harness/skills.ts @ 71dca871b plus docs/04 MCP mapping.
using System.Text.RegularExpressions;
using PiSharp.Core.Config;
using PiSharp.Core.Mcp;

namespace PiSharp.Core.Skills;

public enum SkillSourceKind
{
    Local,
    Mcp,
}

/// <summary>One skill the model can see (name + description) and later read in full.</summary>
public sealed record Skill(
    string Name,
    string Description,
    SkillSourceKind Source,
    string Location,
    string? ServerAlias = null,
    string? Version = null,
    string? FilePathOverride = null)
{
    /// <summary>True when <see cref="Location"/> is a <c>skill://</c> URI rather than a filesystem path.</summary>
    public bool IsRemote => Source == SkillSourceKind.Mcp;

    /// <summary>Path of SKILL.md for a local skill; URI for a remote one.</summary>
    public string SkillMdPath => FilePathOverride ?? (Source == SkillSourceKind.Local
        ? Path.Combine(Location, "SKILL.md")
        : Location);

    /// <summary>Compatibility name used by the CLI and prompt formatter.</summary>
    public string FilePath => SkillMdPath;

    /// <summary>Whether the skill is hidden from the model's prompt catalog.</summary>
    public bool DisableModelInvocation { get; init; }

    /// <summary>Local discovery source such as user, project, agents or path.</summary>
    public string? DiscoverySource { get; init; }
}

/// <summary>Used by the <c>read</c> tool to resolve <c>skill://</c> URIs via MCP <c>resources/read</c>.</summary>
public interface ISkillUriResolver
{
    bool CanResolve(string uri);
    Task<string> ReadTextAsync(string uri, CancellationToken cancellationToken = default);
}

/// <summary>
/// Discovers SKILL.md folders from:
/// global ~/.config/pi-sharp/skills/, project .pi-sharp/skills/,
/// and .agents/skills/ in cwd and ancestors.
/// </summary>
public static partial class SkillLoader
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1024;

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex ValidName();

    /// <summary>Loads local skills with diagnostics, settings paths and pi's discovery order.</summary>
    public static LoadSkillsResult Load(
        string cwd,
        string? globalDir = null,
        IReadOnlyList<string>? extraPaths = null,
        bool includeDefaults = true)
    {
        var resolvedCwd = Path.GetFullPath(cwd);
        var byName = new Dictionary<string, Skill>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<SkillDiagnostic>();

        void Add(LoadSkillsResult result)
        {
            diagnostics.AddRange(result.Diagnostics);
            foreach (var skill in result.Skills)
            {
                string real;
                try { real = Path.GetFullPath(skill.FilePath); }
                catch { real = skill.FilePath; }
                if (!seenFiles.Add(real)) continue;
                if (byName.TryGetValue(skill.Name, out var existing))
                {
                    diagnostics.Add(new SkillDiagnostic(
                        "collision",
                        $"name \"{skill.Name}\" collision (keeping {existing.FilePath})",
                        skill.FilePath));
                    continue;
                }
                byName[skill.Name] = skill;
            }
        }

        if (includeDefaults)
        {
            Add(LoadFromDir(ConfigPaths.SkillsDir(ConfigPaths.GlobalConfigDir(globalDir)), "user"));
            Add(LoadFromDir(ConfigPaths.SkillsDir(ConfigPaths.ProjectConfigDir(resolvedCwd)), "project"));
            foreach (var agentsDir in FindAgentsSkillsDirs(resolvedCwd))
                Add(LoadFromDir(agentsDir, "agents"));
        }

        if (extraPaths is not null)
        {
            foreach (var raw in extraPaths)
            {
                var resolved = Path.GetFullPath(ConfigPaths.ExpandHome(raw), resolvedCwd);
                if (!Path.Exists(resolved))
                {
                    diagnostics.Add(new SkillDiagnostic("warning", "skill path does not exist", resolved));
                    continue;
                }
                if (Directory.Exists(resolved)) Add(LoadFromDir(resolved, "path"));
                else if (File.Exists(resolved) && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    Add(LoadFromFile(resolved, "path"));
                else
                    diagnostics.Add(new SkillDiagnostic("warning", "skill path is not a markdown file", resolved));
            }
        }

        return new LoadSkillsResult(byName.Values.ToList(), diagnostics);
    }

    public static IReadOnlyList<Skill> LoadLocal(string cwd, string? globalDir = null)
        => Load(cwd, globalDir).Skills;

    public static LoadSkillsResult LoadFromDir(string dir, string source)
    {
        if (!Directory.Exists(dir)) return new LoadSkillsResult([], []);
        var skills = new List<Skill>();
        var diagnostics = new List<SkillDiagnostic>();
        Walk(dir, includeRootFiles: true, skills, diagnostics, source);
        return new LoadSkillsResult(skills, diagnostics);
    }

    /// <summary>Turn MCP <c>skill://</c> SKILL.md resources into <see cref="Skill"/> entries.</summary>
    public static IReadOnlyList<Skill> FromMcpResources(IEnumerable<(string Alias, McpResourceInfo Resource)> resources)
    {
        var list = new List<Skill>();
        foreach (var (alias, resource) in resources)
        {
            if (!resource.Uri.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)) continue;
            if (!resource.Uri.EndsWith("/SKILL.md", StringComparison.OrdinalIgnoreCase)
                && !resource.Uri.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = SkillNameFromUri(resource.Uri) ?? resource.Name;
            list.Add(new Skill(
                name,
                resource.Description ?? resource.Title ?? name,
                SkillSourceKind.Mcp,
                resource.Uri,
                alias));
        }
        return list;
    }

    public static Skill? TryLoadFromDirectory(string directory)
    {
        var skillMd = Path.Combine(directory, "SKILL.md");
        if (!File.Exists(skillMd)) return null;
        return LoadFromFile(skillMd, "store").Skills.FirstOrDefault();
    }

    public static string? SkillNameFromUri(string uri)
    {
        // skill://store/name/SKILL.md  or skill://store/name/path
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != "skill")
            return null;
        var parts = parsed.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : parsed.Host;
    }

    public static string FormatForPrompt(
        IReadOnlyList<Skill> skills,
        string fileReadTool = "read",
        int remoteSkillCap = int.MaxValue)
    {
        var visible = skills.Where(s => !s.DisableModelInvocation).ToList();
        if (visible.Count == 0) return "";

        var remote = visible.Where(s => s.IsRemote).ToList();
        var cap = remoteSkillCap < 0 ? 0 : remoteSkillCap;
        var shownRemote = 0;
        var promptSkills = new List<Skill>(visible.Count);
        foreach (var skill in visible)
        {
            if (!skill.IsRemote)
            {
                promptSkills.Add(skill);
                continue;
            }
            if (shownRemote < cap)
            {
                promptSkills.Add(skill);
                shownRemote++;
            }
        }

        var hint = fileReadTool == "bash"
            ? "Use bash to load a skill's file when the task matches its description."
            : "Use the read tool to load a skill's file when the task matches its description.";
        var lines = new List<string>
        {
            "",
            "",
            "The following skills provide specialized instructions for specific tasks.",
            hint,
            "When a skill file references a relative path, resolve it against the skill directory (parent of SKILL.md / dirname of the path) and use that absolute path in tool commands.",
            "",
            "<available_skills>",
        };
        if (remote.Count > shownRemote)
            lines.Add($"There are {remote.Count - shownRemote} additional remote skills; use a search_skills tool to find them.");
        foreach (var skill in promptSkills)
        {
            lines.Add("  <skill>");
            lines.Add($"    <name>{EscapeXml(skill.Name)}</name>");
            lines.Add($"    <description>{EscapeXml(skill.Description)}</description>");
            lines.Add($"    <location>{EscapeXml(skill.FilePath)}</location>");
            lines.Add("  </skill>");
        }
        lines.Add("</available_skills>");
        return string.Join('\n', lines);
    }

    private static void Walk(
        string dir,
        bool includeRootFiles,
        List<Skill> skills,
        List<SkillDiagnostic> diagnostics,
        string source)
    {
        string[] entries;
        try { entries = Directory.GetFileSystemEntries(dir); }
        catch { return; }

        foreach (var full in entries)
        {
            if (string.Equals(Path.GetFileName(full), "SKILL.md", StringComparison.OrdinalIgnoreCase)
                && File.Exists(full))
            {
                var loaded = LoadFromFile(full, source);
                skills.AddRange(loaded.Skills);
                diagnostics.AddRange(loaded.Diagnostics);
                return;
            }
        }

        foreach (var full in entries)
        {
            var name = Path.GetFileName(full);
            if (name.StartsWith('.') || name == "node_modules") continue;
            try
            {
                if (Directory.Exists(full))
                {
                    Walk(full, includeRootFiles: false, skills, diagnostics, source);
                    continue;
                }
                if (includeRootFiles && File.Exists(full) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var loaded = LoadFromFile(full, source);
                    skills.AddRange(loaded.Skills);
                    diagnostics.AddRange(loaded.Diagnostics);
                }
            }
            catch { }
        }
    }

    private static LoadSkillsResult LoadFromFile(string filePath, string source)
    {
        var diagnostics = new List<SkillDiagnostic>();
        var isDeclared = string.Equals(Path.GetFileName(filePath), "SKILL.md", StringComparison.OrdinalIgnoreCase);
        string raw;
        try { raw = File.ReadAllText(filePath); }
        catch (Exception ex)
        {
            diagnostics.Add(new SkillDiagnostic("warning", ex.Message, filePath));
            return new LoadSkillsResult([], diagnostics);
        }

        var parsed = Frontmatter.Parse(raw);
        var description = parsed.Get("description");
        if (!isDeclared && string.IsNullOrWhiteSpace(description)) return new LoadSkillsResult([], diagnostics);
        if (string.IsNullOrWhiteSpace(description))
        {
            diagnostics.Add(new SkillDiagnostic("warning", "description is required", filePath));
            return new LoadSkillsResult([], diagnostics);
        }
        if (description.Length > MaxDescriptionLength)
            diagnostics.Add(new SkillDiagnostic("warning", $"description exceeds {MaxDescriptionLength} characters", filePath));

        var skillDir = Path.GetDirectoryName(filePath) ?? ".";
        var name = parsed.Get("name") ?? Path.GetFileName(skillDir);
        foreach (var error in ValidateName(name))
            diagnostics.Add(new SkillDiagnostic("warning", error, filePath));
        var skill = new Skill(
            name,
            description,
            SkillSourceKind.Local,
            Path.GetFullPath(skillDir),
            Version: parsed.Get("version"),
            FilePathOverride: isDeclared ? null : Path.GetFullPath(filePath))
        {
            DisableModelInvocation = parsed.GetBool("disable-model-invocation"),
            DiscoverySource = source,
        };
        return new LoadSkillsResult([skill], diagnostics);
    }

    private static IEnumerable<string> ValidateName(string name)
    {
        if (name.Length > MaxNameLength) yield return $"name exceeds {MaxNameLength} characters ({name.Length})";
        if (!ValidName().IsMatch(name)) yield return "name contains invalid characters (must be lowercase a-z, 0-9, hyphens only)";
        if (name.StartsWith('-') || name.EndsWith('-')) yield return "name must not start or end with a hyphen";
        if (name.Contains("--", StringComparison.Ordinal)) yield return "name must not contain consecutive hyphens";
    }

    private static IEnumerable<string> FindAgentsSkillsDirs(string cwd)
    {
        var current = Path.GetFullPath(cwd);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var dir = Path.Combine(current, ".agents", "skills");
            if (seen.Add(dir) && Directory.Exists(dir)) yield return dir;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current) yield break;
            current = parent;
        }
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}

public sealed record SkillDiagnostic(string Type, string Message, string Path);

public sealed record LoadSkillsResult(IReadOnlyList<Skill> Skills, IReadOnlyList<SkillDiagnostic> Diagnostics);

/// <summary>Merged local + MCP skills. Local names take precedence on clash.</summary>
public sealed class SkillIndex
{
    public IReadOnlyList<Skill> Skills { get; }

    public SkillIndex(IEnumerable<Skill> local, IEnumerable<Skill>? remote = null)
    {
        var map = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        if (remote is not null)
        {
            foreach (var s in remote) map[s.Name] = s;
        }
        foreach (var s in local) map[s.Name] = s; // local wins
        Skills = map.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Skill? Find(string name) =>
        Skills.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Name + description lines for the system prompt.</summary>
    public string FormatForSystemPrompt()
    {
        if (Skills.Count == 0) return "";
        var lines = Skills.Select(s => $"- {s.Name}: {s.Description}");
        return "Skills (read SKILL.md for full instructions):\n" + string.Join('\n', lines);
    }

    public static SkillIndex Load(string cwd, McpManager? mcp = null, string? globalDir = null)
    {
        var local = SkillLoader.LoadLocal(cwd, globalDir);
        var remote = mcp is null ? [] : SkillLoader.FromMcpResources(mcp.SkillResources());
        return new SkillIndex(local, remote);
    }
}
