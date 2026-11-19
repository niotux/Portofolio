// Ported from pi: packages/coding-agent/src/core/compaction/compaction.ts and utils.ts @ 71dca871b
namespace PiSharp.Core.Compaction;

public static class CompactionPrompts
{
    public const string System =
        """
        You are a context summarization assistant. Your task is to read a conversation between a user and an AI assistant, then produce a structured summary following the exact format specified.

        Do NOT continue the conversation. Do NOT respond to any questions in the conversation. ONLY output the structured summary.
        """;

    /// <summary>The rules every summary follows, first and later ones alike.</summary>
    public const string Rules =
        """
        RULES:
        - Quote requests and instructions from others VERBATIM, in double quotes, with who sent them and the message id when there is one (e.g. @alice, message 59494: "..."). Never paraphrase an instruction.
        - Pointers, not content: keep KB slugs, chat message and thread ids, branch@sha, file paths, URLs and exact error messages. Drop file contents and tool output that can be fetched again.
        - Record every failed attempt as "tried X, failed because Y", and every decision with its reason, so dead ends are not retried.
        - Every request received and every promise made that is not finished goes under "Open tasks / commitments": who asked (with message id), what exactly (quoted), its status, the next step, and where partial work is written. Never drop an open task unless it was finished or explicitly handed back.
        - Use exactly the headings below, in this order, and nothing else. Write "(none)" under a heading with nothing to say.
        - Keep the whole summary under about 2000 tokens: short bullets, no prose paragraphs.
        """;

    /// <summary>The fixed template: hard headings a small model follows better than free prose.</summary>
    public const string Template =
        """
        Use this EXACT format:

        ## Task
        [The current work, who asked for it, and any constraints they set]

        ## Done
        - [x] [Completed steps and changes]

        ## Open tasks / commitments
        - [who asked, message id] "[the request, quoted]" - status: [...]; next: [...]; partial work: [where]

        ## Decisions
        - **[Decision]**: [reason]

        ## Failed attempts
        - tried [X], failed because [Y]

        ## Pointers
        - [KB slugs, message/thread ids, branch@sha, file paths needed to continue]

        ## Next step
        1. [The next action, then any that follow. If a plan step is active, copy its current step id and status; do not restate the full plan.]
        """;

    public const string Initial =
        "The messages above are a conversation to summarize. Create a structured context checkpoint summary that another LLM will use to continue the work.\n\n"
        + Rules + "\n\n" + Template;

    public const string UpdateInstructions =
        "Update the existing summary with the new messages. Keep everything from the previous summary that still matters, "
        + "add new progress, decisions, failed attempts and pointers, move finished items from \"Open tasks / commitments\" to \"Done\", "
        + "and update \"Next step\".\n\n"
        + Rules + "\n\n" + Template;

    public static readonly string Update =
        "The messages above are NEW conversation messages to incorporate into the existing summary provided in <previous-summary> tags.\n\n"
        + UpdateInstructions;

    /// <summary>Appended to the seat's own last request, so the summary is computed on the prefix the provider has
    /// already cached. {0} is the kept-verbatim boundary sentence, or empty when everything is summarised.</summary>
    public const string InPlaceMarker = "[harness] STOP the current work: this is a context checkpoint";

    public const string InPlace =
        InPlaceMarker + ", not a new task. Do NOT call tools, do NOT answer anyone, "
        + "do NOT continue the conversation. Write a structured context checkpoint summary of the conversation above that you "
        + "will use to continue the work after older messages are removed. If an earlier checkpoint summary is above, keep "
        + "everything from it that still matters and move finished items to \"Done\".{0}\n\n"
        + Rules + "\n\n" + Template;

    public const string TurnPrefix =
        """
        This is the PREFIX of a turn that was too large to keep. The SUFFIX (recent work) is retained.

        Summarize the prefix to provide context for the retained suffix:

        ## Original Request
        [What was asked in this turn, quoted verbatim with who asked and the message id when there is one]

        ## Early Progress
        - [Key decisions and work done in the prefix]

        ## Context for Suffix
        - [Information needed to understand the retained recent work]

        Be concise. Focus on what's needed to understand the kept suffix.
        """;
}
