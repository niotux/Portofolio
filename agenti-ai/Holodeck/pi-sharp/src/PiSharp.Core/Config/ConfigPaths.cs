// pi-sharp config locations. Global: ~/.config/pi-sharp (Linux) or %APPDATA%/pi-sharp
// (Windows). Project: <cwd>/.pi-sharp/. Override with PI_SHARP_CONFIG_DIR.
namespace PiSharp.Core.Config;

/// <summary>Resolves global and project config directories.</summary>
public static class ConfigPaths
{
    public const string ProjectDirName = ".pi-sharp";
    public const string EnvConfigDir = "PI_SHARP_CONFIG_DIR";

    /// <summary>Global config directory. <paramref name="overrideDir"/> wins over the env var.</summary>
    public static string GlobalConfigDir(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir)) return Path.GetFullPath(ExpandHome(overrideDir.Trim()));
        var env = Environment.GetEnvironmentVariable(EnvConfigDir);
        if (!string.IsNullOrWhiteSpace(env)) return Path.GetFullPath(ExpandHome(env.Trim()));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pi-sharp");
    }

    public static string ProjectConfigDir(string cwd) =>
        Path.Combine(Path.GetFullPath(cwd), ProjectDirName);

    public static string SettingsFile(string dir) => Path.Combine(dir, "settings.json");
    public static string ModelsFile(string dir) => Path.Combine(dir, "models.json");
    public static string McpFile(string dir) => Path.Combine(dir, "mcp.json");
    public static string SkillsDir(string dir) => Path.Combine(dir, "skills");
    public static string PromptsDir(string dir) => Path.Combine(dir, "prompts");

    /// <summary>Session JSONL directory for a working directory, matching <c>SessionManager.DefaultSessionDir</c>.</summary>
    public static string SessionDir(string cwd, string? globalDir = null)
    {
        var config = GlobalConfigDir(globalDir);
        var safe = "--" + cwd.TrimStart('/', '\\').Replace('/', '-').Replace('\\', '-').Replace(':', '-') + "--";
        return Path.Combine(config, "sessions", safe);
    }

    public static string ExpandHome(string path)
    {
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/") || (OperatingSystem.IsWindows() && path.StartsWith("~\\")))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        return path;
    }
}
