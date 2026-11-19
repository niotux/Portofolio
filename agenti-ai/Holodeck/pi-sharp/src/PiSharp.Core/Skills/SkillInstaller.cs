// install_skill flow: write a store skill under .pi-sharp/skills/ and pin it in skills.lock.json.
// docs/04: checksums, versions, log every install.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PiSharp.Core.Config;
using PiSharp.Core.Json;
using PiSharp.Core.Mcp;

namespace PiSharp.Core.Skills;

public sealed class SkillLockFile
{
    public List<SkillLockEntry> Skills { get; set; } = [];
}

public sealed class SkillLockEntry
{
    public string Name { get; set; } = "";
    public string Store { get; set; } = "";
    public string? Version { get; set; }
    public string? Sha256 { get; set; }
    public List<SkillLockFileEntry> Files { get; set; } = [];
    public string InstalledAt { get; set; } = "";
}

public sealed class SkillLockFileEntry
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed record InstalledSkill(string Name, string Directory, SkillLockEntry Lock);

/// <summary>Installs a skill from an MCP store into the project <c>.pi-sharp/skills/</c> folder.</summary>
public static class SkillInstaller
{
    public static string LockPath(string cwd) =>
        Path.Combine(ConfigPaths.ProjectConfigDir(cwd), "skills.lock.json");

    public static string ProjectSkillsDir(string cwd) =>
        ConfigPaths.SkillsDir(ConfigPaths.ProjectConfigDir(cwd));

    /// <summary>
    /// Prefer the store's <c>install_skill</c> tool; fall back to reading every
    /// <c>skill://{store}/{name}/...</c> resource.
    /// </summary>
    public static async Task<InstalledSkill> InstallAsync(
        McpClient client,
        string skillName,
        string cwd,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        var files = await FetchFilesAsync(client, skillName, version, cancellationToken).ConfigureAwait(false);
        if (files.Count == 0)
            throw new InvalidOperationException($"Store \"{client.Alias}\" returned no files for skill \"{skillName}\".");

        var dest = Path.Combine(ProjectSkillsDir(cwd), skillName);
        Directory.CreateDirectory(dest);

        var lockFiles = new List<SkillLockFileEntry>();
        var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (relative, content) in files.Items.OrderBy(f => f.Relative, StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(Path.Combine(dest, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(Path.GetFullPath(dest) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(full, Path.GetFullPath(dest), StringComparison.Ordinal))
                throw new InvalidOperationException($"Skill file path escapes install dir: {relative}");

            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(full, content, cancellationToken).ConfigureAwait(false);

            var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            lockFiles.Add(new SkillLockFileEntry { Path = relative.Replace('\\', '/'), Sha256 = sha });
            hasher.AppendData(Encoding.UTF8.GetBytes(relative + "\n"));
            hasher.AppendData(content);
        }

        var entry = new SkillLockEntry
        {
            Name = skillName,
            Store = client.Alias,
            Version = version ?? files.Version,
            Sha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant(),
            Files = lockFiles,
            InstalledAt = DateTime.UtcNow.ToString("o"),
        };
        UpsertLock(cwd, entry);
        return new InstalledSkill(skillName, dest, entry);
    }

    public static SkillLockFile ReadLock(string cwd)
    {
        var path = LockPath(cwd);
        if (!File.Exists(path)) return new SkillLockFile();
        return JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.SkillLockFile)
            ?? new SkillLockFile();
    }

    public static void UpsertLock(string cwd, SkillLockEntry entry)
    {
        var file = ReadLock(cwd);
        file.Skills.RemoveAll(s => s.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
        file.Skills.Add(entry);
        file.Skills.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        var path = LockPath(cwd);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(file, CoreJsonContext.Indented.SkillLockFile);
        File.WriteAllText(path, json + "\n");
    }

    private sealed record FetchedFiles(List<(string Relative, byte[] Content)> Items, string? Version)
    {
        public int Count => Items.Count;
    }

    private static async Task<FetchedFiles> FetchFilesAsync(
        McpClient client,
        string skillName,
        string? version,
        CancellationToken cancellationToken)
    {
        var fromTool = await TryInstallToolAsync(client, skillName, version, cancellationToken).ConfigureAwait(false);
        if (fromTool is not null) return fromTool;
        return await FetchViaResourcesAsync(client, skillName, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FetchedFiles?> TryInstallToolAsync(
        McpClient client,
        string skillName,
        string? version,
        CancellationToken cancellationToken)
    {
        var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);
        if (!tools.Any(t => t.Name == "install_skill")) return null;

        var args = new JsonObject { ["name"] = skillName };
        if (version is not null) args["version"] = version;
        var result = await client.CallToolAsync("install_skill", args, cancellationToken).ConfigureAwait(false);
        if (result.IsError) return null;

        var text = string.Concat(result.Content.OfType<McpTextPart>().Select(t => t.Text));
        JsonObject? obj = null;
        try { obj = JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
        if (obj is null) return null;

        var files = new List<(string, byte[])>();
        if (obj["files"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject f) continue;
                var rel = f["path"]?.GetValue<string>();
                var content = f["content"]?.GetValue<string>();
                if (rel is null || content is null) continue;
                files.Add((rel, Encoding.UTF8.GetBytes(content)));
            }
        }
        return new FetchedFiles(files, obj["version"]?.GetValue<string>());
    }

    private static async Task<FetchedFiles> FetchViaResourcesAsync(
        McpClient client,
        string skillName,
        CancellationToken cancellationToken)
    {
        var resources = await client.ListResourcesAsync(cancellationToken).ConfigureAwait(false);
        var prefix = $"skill://{client.Alias}/{skillName}/";
        var files = new List<(string, byte[])>();
        foreach (var r in resources)
        {
            if (!r.Uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = r.Uri[prefix.Length..];
            if (relative.Length == 0) continue;
            var contents = await client.ReadResourceAsync(r.Uri, cancellationToken).ConfigureAwait(false);
            foreach (var c in contents)
            {
                if (c.Text is not null)
                    files.Add((relative, Encoding.UTF8.GetBytes(c.Text)));
                else if (c.Blob is not null)
                    files.Add((relative, Convert.FromBase64String(c.Blob)));
            }
        }
        return new FetchedFiles(files, null);
    }
}
