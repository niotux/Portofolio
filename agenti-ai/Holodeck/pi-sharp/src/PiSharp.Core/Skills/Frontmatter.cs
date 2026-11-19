// Flat YAML frontmatter parser for SKILL.md and prompt templates.
// docs/02: key: value, quoted strings, simple lists. No nested YAML.
using System.Text;

namespace PiSharp.Core.Skills;

/// <summary>Parsed <c>---</c> frontmatter plus the remaining markdown body.</summary>
public sealed class Frontmatter
{
    public IReadOnlyDictionary<string, string> Fields { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lists { get; }
    public string Body { get; }

    public Frontmatter(
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lists,
        string body)
    {
        Fields = fields;
        Lists = lists;
        Body = body;
    }

    public string? this[string key] => Fields.TryGetValue(key, out var v) ? v : null;

    public string? Get(string key) => this[key];

    public bool GetBool(string key, bool defaultValue = false)
    {
        var raw = Get(key);
        if (raw is null) return defaultValue;
        if (bool.TryParse(raw, out var parsed)) return parsed;
        if (string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(raw, "no", StringComparison.OrdinalIgnoreCase)) return false;
        return defaultValue;
    }

    public IReadOnlyList<string> List(string key) =>
        Lists.TryGetValue(key, out var v) ? v : [];

    public static Frontmatter Parse(string text)
    {
        var span = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (span.Length > 0 && span[0] == '\uFEFF') span = span[1..];
        if (!span.StartsWith("---", StringComparison.Ordinal))
            return new Frontmatter(new Dictionary<string, string>(), new Dictionary<string, IReadOnlyList<string>>(), text);

        var rest = span[3..];
        if (rest.StartsWith('\n')) rest = rest[1..];

        var end = rest.IndexOf("\n---", StringComparison.Ordinal);
        string yaml, body;
        if (end < 0)
        {
            // tolerate a file that is only frontmatter
            yaml = rest;
            body = "";
        }
        else
        {
            yaml = rest[..end];
            body = rest[(end + 4)..];
            if (body.StartsWith('\n')) body = body[1..];
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                lists[key] = ParseList(value);
                continue;
            }
            fields[key] = Unquote(value);
        }

        return new Frontmatter(fields, lists, body);
    }

    private static IReadOnlyList<string> ParseList(string bracketed)
    {
        var inner = bracketed[1..^1].Trim();
        if (inner.Length == 0) return [];
        var items = new List<string>();
        var sb = new StringBuilder();
        var inQuote = '\0';
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (inQuote != '\0')
            {
                if (c == '\\' && i + 1 < inner.Length) { sb.Append(inner[++i]); continue; }
                if (c == inQuote) { inQuote = '\0'; continue; }
                sb.Append(c);
                continue;
            }
            if (c is '"' or '\'') { inQuote = c; continue; }
            if (c == ',')
            {
                var item = sb.ToString().Trim();
                if (item.Length > 0) items.Add(item);
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        var last = sb.ToString().Trim();
        if (last.Length > 0) items.Add(last);
        return items;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            var inner = value[1..^1];
            return inner.Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\\\", "\\");
        }
        return value;
    }
}
