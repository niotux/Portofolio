using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PiSharp.Core.Factory;

public sealed record AcceptanceCriterion(string Id, string Text);

/// <summary>Versioned acceptance identity. Legacy prose is admitted only in an explicit acceptance section.</summary>
public static partial class AcceptanceCriteria
{
    public static string Normalize(string text) => Whitespace().Replace(text.Trim().TrimEnd('.', ';'), " ");

    public static IReadOnlyList<AcceptanceCriterion> Read(JsonNode? structured, string objective)
    {
        if (structured is JsonArray rows)
        {
            var result = new List<AcceptanceCriterion>();
            foreach (var row in rows)
            {
                var text = row is JsonValue v && v.TryGetValue<string>(out var s) ? s : row?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("Acceptance criterion has no text.");
                var normalized = Normalize(text);
                result.Add(new(row is JsonObject ? row["id"]?.GetValue<string>() ?? Id(normalized) : Id(normalized), normalized));
            }
            return result;
        }
        var texts = new List<string>();
        var inSection = false;
        foreach (var line in objective.ReplaceLineEndings("\n").Split('\n'))
        {
            var clean = line.Trim().TrimStart('#').Trim().Replace("**", "");
            var heading = Heading().Match(clean);
            if (heading.Success)
            {
                inSection = true;
                foreach (var part in heading.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (Normalize(part).Length > 0) texts.Add(Normalize(part));
                continue;
            }
            if (!inSection || clean.Length == 0) continue;
            var bullet = Bullet().Match(clean);
            if (!bullet.Success) break;
            texts.Add(Normalize(bullet.Groups[1].Value));
        }
        return texts.Select(t => new AcceptanceCriterion(Id(t), t)).ToArray();
    }

    public static string Revision(IReadOnlyList<AcceptanceCriterion> criteria) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(new JsonArray([.. criteria.Select(c =>
            (JsonNode)new JsonObject { ["id"] = c.Id, ["text"] = c.Text })]).ToJsonString())));

    private static string Id(string text) => "ac-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
    [GeneratedRegex(@"^(?:Acceptance(?: criteria)?|Done when)\s*(?::\s*(.*)|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Heading();
    [GeneratedRegex(@"^(?:[-*+]\s+(?:\[[ xX]\]\s*)?|\d+[.)]\s+)(.+)$")]
    private static partial Regex Bullet();
}
