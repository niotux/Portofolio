using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PiSharp.Core.Compaction;
using PiSharp.Core.Factory;
using PiSharp.Core.Messages;
using PiSharp.Core.Notebook;

namespace PiSharp.Core.Guard;

/// <summary>Captures explicit authority corrections into a durable, message-id keyed notebook record.</summary>
public sealed class CorrectionCapture(NotebookStore notebook, string workingDirectory)
{
    private static readonly Regex NegativeMarker = new(
        @"\b(?:don't|never|stop|wrong|again|instead|revert|not\s+allowed)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex ReviewReopen = new(
        @"\breview\s*(?:-|→)\s*>?\s*in_progress\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));
    private static readonly Regex Assignment = new(
        @"\bASSIGNED\s+([a-z0-9][a-z0-9-]*)\s*->\s*@([a-z0-9][a-z0-9-]*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(50));

    /// <summary>Returns true only when a durable correction record was newly written.</summary>
    public bool Capture(FactoryInboxItem item, bool postedInThread, IReadOnlySet<string> correctionAuthorities,
        IReadOnlySet<string> ownWorkItems, IReadOnlyList<string> recentToolCalls, string? seat,
        string? sourceId = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(correctionAuthorities);
        ArgumentNullException.ThrowIfNull(ownWorkItems);
        var message = item.Message;
        var source = sourceId ?? "chat-source-" + message.Id;
        var authority = correctionAuthorities.Contains(message.Author);
        var replyCorrection = item.Reason == "reply" && postedInThread && authority && NegativeMarker.IsMatch(message.Body);
        var ownItem = message.AboutSlugs.Any(ownWorkItems.Contains);
        var reviewCorrection = ownItem && HasReviewNote(message.Body);
        if (!replyCorrection && !reviewCorrection) return false;

        var git = LiveFacts.ReadGit(workingDirectory);
        var task = "chat-" + message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var id = "correction-" + message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var payload = new JsonObject
        {
            ["messageId"] = message.Id,
            ["sourceId"] = source,
            ["reason"] = reviewCorrection ? "review-reopened" : "authority-reply",
            ["author"] = message.Author,
            ["thread"] = message.Thread,
            ["toolCalls"] = new JsonArray(recentToolCalls.TakeLast(10).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
            ["branch"] = git?.Branch,
            ["sha"] = git?.Head,
            ["workItem"] = message.AboutSlugs.FirstOrDefault(ownWorkItems.Contains),
            ["seat"] = seat,
        };
        var prior = notebook.Get(id);
        if (prior is not null) return false;
        try
        {
            notebook.Put(id, "correction", payload.ToJsonString(), task, "open", 0, sourceId: source,
                operationId: id, title: "Correction", tags: ["guard", "correction"]);
        }
        catch (InvalidOperationException) when (notebook.Get(id) is not null)
        {
            // A concurrent or replayed delivery already committed the message's stable ID.
            return false;
        }
        return true;
    }

    /// <summary>Extract the seat's assigned work-item slugs from an explicit assignment message.</summary>
    public static (string WorkItem, string Seat)? ParseAssignment(string body)
    {
        var match = Assignment.Match(body);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : null;
    }

    private static bool HasReviewNote(string body)
    {
        var transition = ReviewReopen.Match(body);
        if (!transition.Success) return false;
        var note = body[(transition.Index + transition.Length)..].Trim().TrimStart(':', '-', '—').Trim();
        return note.Length > 0;
    }

    /// <summary>Serializes a tool call without persisting obvious credential values.</summary>
    public static string SafeToolCall(ToolCallContent call)
    {
        var payload = new JsonObject { ["name"] = call.Name, ["arguments"] = Redact(call.Arguments) };
        return payload.ToJsonString();
    }

    private static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var safe = new JsonObject();
            foreach (var (key, value) in obj)
            {
                if (Regex.IsMatch(key, @"token|secret|password|credential|authorization|private.?key|api.?key", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
                    safe[key] = "[redacted]";
                else safe[key] = Redact(value);
            }
            return safe;
        }
        else if (node is JsonArray array)
        {
            var safe = new JsonArray();
            foreach (var value in array) safe.Add(Redact(value));
            return safe;
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            var sanitized = Regex.Replace(text!, "(?i)\\b(?:Bearer\\s+|Basic\\s+|kbt_|glpat-)[^\\s\\\"']+", "[redacted]",
                RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
            return JsonValue.Create(sanitized);
        }
        return node?.DeepClone();
    }
}
