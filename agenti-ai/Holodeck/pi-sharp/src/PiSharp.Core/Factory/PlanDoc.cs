using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace PiSharp.Core.Factory;

/// <summary>The epic's doc <c>plan</c> as the seat's plan tool keeps it: the coder's own text first (the flow, the why), then a
/// steps section the tool rewrites on every change, so the epic's record and whoever takes the epic next see where it stands.</summary>
public static class PlanDoc
{
    /// <summary>The heading of the section the plan tool owns; everything from it on is replaced.</summary>
    public const string StepsHeading = "## Steps (kept by the seat's plan tool)";

    /// <summary>The document with its steps section replaced by <paramref name="steps"/> (added when it has none).</summary>
    public static string Merge(string? current, string steps)
    {
        var text = current ?? "";
        var at = text.IndexOf(StepsHeading, StringComparison.Ordinal);
        var head = (at >= 0 ? text[..at] : text).TrimEnd();
        return head.Length == 0 ? steps : head + "\n\n" + steps;
    }

    /// <summary>The steps section: one numbered line per step, in order, with its state: done (with what it settled),
    /// doing (the first open step), todo, or blocked; then the last change's reason.</summary>
    public static string Render(IReadOnlyList<JsonObject> steps, DateTimeOffset at, string? why = null)
    {
        var text = new StringBuilder(StepsHeading).Append(", ").Append(at.UtcDateTime.ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture)).Append('\n');
        var doing = false;
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var status = step["status"]?.GetValue<string>();
            var state = status switch
            {
                "completed" => "done",
                "blocked" => "blocked",
                _ when !doing => "doing",
                _ => "todo",
            };
            if (state == "doing") doing = true;
            var title = step["title"]?.GetValue<string>()?.Trim();
            var body = step["text"]?.GetValue<string>()?.Trim();
            // A step's text starts with its title when the plan tool set it from one line; say it once.
            if (title is { Length: > 0 } && body is not null && body.StartsWith(title, StringComparison.Ordinal))
                body = body[title.Length..].TrimStart();
            text.Append(CultureInfo.InvariantCulture, $"{i + 1}. **{state}**: {title ?? body}");
            if (body is { Length: > 0 } && !string.Equals(body, title, StringComparison.Ordinal))
                text.Append(" - ").Append(body.Replace("\n", " ", StringComparison.Ordinal));
            text.Append('\n');
        }
        if (why is { Length: > 0 }) text.Append("\nLast change: ").Append(why.Trim()).Append('\n');
        return text.ToString();
    }
}
