namespace PiSharp.Core.Agent;

/// <summary>
/// A final reply the model did not mean: cut by the output limit, or a sentence repeated until the limit stopped it.
/// coder-06-pi4, 2026-10-06 19:03Z (board 605743): "The solution: I need to check if the ROM is actually supposed to
/// process keyboard input..." over and over, delivered as the task's answer, clipped at 19K chars, and the seat went
/// idle holding wi-114h. Such a text ends no task: the loop cuts it and gives the model one more turn.
/// </summary>
public static class DegenerateReply
{
    /// <summary>Shorter replies are never judged: a short list can legitimately repeat a line.</summary>
    public const int MinChars = 1500;
    /// <summary>A sentence counts as looped once it occurs this many times.</summary>
    public const int MinRepeats = 5;
    /// <summary>What remains of a degenerate reply in the transcript.</summary>
    public const int KeepChars = 1200;

    public const string Nudge =
        "[harness] Your last reply was not delivered: it {0}. Do not restate it. Take the next concrete step with a tool " +
        "(read, run, test), or, if you are done, give a short final answer.";

    /// <summary>Why <paramref name="text"/> is no answer, or null when it is one.</summary>
    public static string? Why(string text, bool cutByLength)
    {
        if (cutByLength) return "was cut by the output limit";
        if (text.Length < MinChars) return null;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var looped = 0;
        foreach (var raw in text.Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var s = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
            if (s.Length < 20) continue;
            counts[s] = counts.GetValueOrDefault(s) + 1;
            if (counts[s] >= MinRepeats) looped += s.Length;
        }
        // The repeats past the threshold alone must be a large share of the text, so a long report that reuses
        // one heading a few times is not caught.
        return looped * 4 >= text.Length ? "repeated the same sentences over and over" : null;
    }

    public static string Cut(string text) =>
        (text.Length <= KeepChars ? text : text[..KeepChars].TrimEnd()) + "\n[cut by the harness: not delivered]";
}
