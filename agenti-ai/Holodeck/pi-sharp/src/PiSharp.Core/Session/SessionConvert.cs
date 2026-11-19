// Ported from pi: packages/coding-agent/src/core/messages.ts (compaction summary wrapping) @ 71dca871b
using PiSharp.Core.Messages;

namespace PiSharp.Core.Session;

/// <summary>Maps session/runtime messages onto the three LLM roles.</summary>
public static class SessionConvert
{
    public const string CompactionSummaryPrefix =
        """
        The conversation history before this point was compacted into the following summary, written by the model that did the work so far. Build on it and do not repeat work it records as done; the tools' state (files, branches, the KB) is as that work left it.

        <summary>
        """;

    public const string CompactionSummarySuffix =
        """

        </summary>
        """;

    public static IReadOnlyList<Message> ToLlm(IReadOnlyList<Message> messages)
    {
        var result = new List<Message>(messages.Count);
        foreach (var message in messages)
        {
            switch (message)
            {
                case CompactionSummaryMessage compaction:
                    result.Add(new UserMessage(CompactionSummaryPrefix + compaction.Summary + CompactionSummarySuffix)
                    {
                        Timestamp = compaction.Timestamp,
                    });
                    break;
                case UserMessage or AssistantMessage or ToolResultMessage:
                    result.Add(message);
                    break;
            }
        }
        return result;
    }

    public static ValueTask<IReadOnlyList<Message>> ToLlmAsync(IReadOnlyList<Message> messages) =>
        ValueTask.FromResult(ToLlm(messages));
}
