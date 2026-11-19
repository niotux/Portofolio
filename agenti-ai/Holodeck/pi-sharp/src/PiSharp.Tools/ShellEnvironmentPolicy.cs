using System.Text.RegularExpressions;

namespace PiSharp.Tools;

/// <summary>
/// What the model's commands see of the seat's environment (Codex <c>shell_environment_policy</c>; plan-pi-sharp-codex
/// item 3): variables whose names match <see cref="Exclude"/> (case-insensitive globs, <c>*</c> for any run) are taken
/// out, then <see cref="Set"/> is applied. The seat process itself keeps them all (its MCP servers and models read them);
/// only the commands it runs for the model lose them. Git does not need them: the seats' credentials are in
/// ~/.git-credentials.
/// </summary>
public sealed record ShellEnvironmentPolicy(IReadOnlyList<string> Exclude, IReadOnlyDictionary<string, string> Set)
{
    public static readonly ShellEnvironmentPolicy None = new([], new Dictionary<string, string>());

    /// <summary>The names a seat withholds by default: its factory token and anything named like a key, secret, token or
    /// password.</summary>
    public static readonly string[] SecretNames = ["PI_FACTORY_TOKEN", "*_TOKEN", "*_API_KEY", "*SECRET*", "*PASSWORD*"];

    public bool Excludes(string name) => Exclude.Any(glob => Glob(glob).IsMatch(name));

    /// <summary>Applies the policy to a child process's environment; returns the names it took out.</summary>
    public IReadOnlyList<string> Apply(IDictionary<string, string?> environment)
    {
        var removed = environment.Keys.Where(Excludes).ToList();
        foreach (var name in removed) environment.Remove(name);
        foreach (var (name, value) in Set) environment[name] = value;
        return removed;
    }

    private static Regex Glob(string glob) =>
        new("^" + Regex.Escape(glob).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
