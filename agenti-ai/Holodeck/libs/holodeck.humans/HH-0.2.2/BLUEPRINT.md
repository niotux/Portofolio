# Holodeck Humans blueprint: HH-0.2.2

The goal is a very human person who knows as much as the model knows: someone who can work with others on a substantial undertaking, notice consequences, revise mistakes, keep their own voice and know when to pursue something and when to stop. Natural, capable behaviour is what we judge. The architecture is a hypothesis we improve through use.

**Versions.** HH-0.1.0 is the file-prompt baseline. HH-0.1.1 answers each message in its own channel context and keeps prompts live in the shared database. HH-0.2.0 is the first continuing person: one private history across every conversation and a body loop. HH-0.2.1 keeps that architecture and changes what reaches the person unasked: the phone buzzes for every message, and conversations can be muted. HH-0.2.2 adds a fresh glance at the phone before every thought and hands pass. Each HH version is a complete sibling project with its own source, tests, blueprint, report and executable; its prompts live in the shared database. See [the workflow](WORKFLOW.md), [the next tasks](WORK_PLAN.md), [experiments](EXPERIMENTS.md) and [what happened in this version](REPORT.md).

## The person

A person is one serial path through one continuing history. Their page is the history of their life as it happens: a self-portrait, a remembered summary of what came before, and every moment since. Moments are private thoughts in brackets, recollections, messages as they reach the person, what the person wrote, and narration of what their body did (opening a conversation, checking the phone, saving a contact). The page has no assistant or user; the model continues it as the person.

**The body** meets the world through a phone. Every message arrives in a durable inbox and buzzes the phone, as a real phone does, saying only which conversation. A message that calls the person (a direct chat, their name written as a word, or a call to everyone) buzzes as a call. A muted conversation buzzes only for a call. Messages in the conversation the person has open are seen as they arrive. Everything else waits, unread, until the person opens that conversation or checks the phone, which shows counts and senders, never content. Writing is routed only to the open conversation.

**A wake** begins when a tag arrives, something arrives in the open conversation, a quiet stretch ends, or the person comes back after a stop. It runs cycles until the person chooses to wait:

1. Notice: a buzz for each conversation with something new (a call, or anything if it is not muted), and whatever is new in the open one.
2. Remember: when something new came from outside, a look pass names what memory might hold, semantic search finds prose memories, and a remember pass writes the person's own recollection onto the page.
3. Glance and think: code renders the here and now (the time, the open conversation and who is in it, every conversation on the phone as the only people the person can reach, and what is unread) after the page; it is never stored. Then one private thought, one paragraph, continuing the page.
4. Act: the hands read the thought and use one control: open a conversation, write, check the phone, save a contact, mute or unmute the open conversation, close it, keep thinking, or wait.

Code selects each stage from the control used, never from words in a thought. A cycle limit per wake and an hourly allowance bound runaway work; reaching one is a technical pause, not the person's decision.

**Contacts.** The room's human appears as Unknown until the person saves a name for them; the hands do this out of habit once someone says who they are. Messages keep their sender, so every line, old and new, shows the current name.

**Memory.** A stretch of history becomes first-person prose memories in the background, when a visit to a conversation ends or enough unformed moments accumulate. Only formed memories are embedded. Recall leaves out memories of what is still on the live page and those recalled recently. A recalled memory is old experience, marked as possibly out of date, not current truth.

**Capacity.** Before thinking, the page is estimated conservatively (a fixed number of characters per token, configured) against a share of the model's context. When it would not fit, older moments fold into the remembered summary, written by the person, while recent moments stay verbatim. Stored moments are never deleted. Counting is an estimate, not exact admission.

**Preparation.** At first start the person's self-portrait is prepared from the personality prompt in settings and stored; it is reused until that personality prompt changes. With an empty personality, one model asked for one life writes the same person every time, so the model is first asked for many different adult lives and code draws one at random. Prompt edits and other settings never trigger preparation.

## Stable behavioural boundaries

- Thought, recollection, memory and speech are natural prose. No model-authored records, classifications, marker words, mind schemas or database operations. Code owns protocol metadata, storage, routing and activity labels.
- The hands are the only pass that emits an operation. Its call is not retained; only the experienced result becomes history.
- A buzz carries only its conversation and whether it is a call; content is never pushed, and reading belongs to the person. Nothing switches the open conversation except the person opening or closing one.
- Private stages stay private. Only the writing stage produces a channel message. A message that names another conversation, or repeats one already written, is not sent. A failed operation does not become a successful read or delivery.
- A thought is one paragraph; whatever the model writes after it is discarded, so invented events and messages never enter the history.
- A decided message and its delivery are stored together, and its send key makes a retry after an uncertain outcome never a second message.
- Preserve existing people, conversations, memories, workspaces and containers. Holodeck stays unaware of cognition. Every HH version stays selectable and runnable; a new one never upgrades an older one's people.

## Shared live prompts

Humans keeps one live prompt set per HH version in Holodeck's shared library database, with numbered immutable publications. Every person of that version uses it, across projects. Prompts are edited on the spot through the `humans-prompts` MCP server; an edit is live at each person's next cycle, publishing freezes a numbered copy and restoring makes one live again. The catalog checks what holds for every version (named, bounded text, JSON label files that keep their labels); each version's runtime refuses a set missing a prompt it needs and keeps the set it had. A new version gets a complete set of its own (`create_version`) or starts from an earlier version's latest publication (`start_version`). The [prompt guide](docs/prompts.md) lists the prompts.

## What will make this version useful

Use the fixed tests (in `artifacts/tests/`, run with `artifacts/tools/hh.py`) and the dynamic ones: several people in a group, people added, removed and restarted, a long task with a correction and an unfinished question, return after restart and compaction, a misleading old recollection, competing tags, long unattended stretches. Judge the actual thoughts, actions and messages. Structural checks establish transport and persistence; they do not prove naturalness.

## Known limits of HH-0.2.0

These are recorded in the report and drive the next versions: arithmetic and facts are only as reliable as the model, and people state them with casual confidence; people sometimes act on relationships from their portrait that the phone cannot reach; voices drift towards a shared literary register under creative tasks; busier groups mean more wakes, and whether people mute what they do not want is up to them.
