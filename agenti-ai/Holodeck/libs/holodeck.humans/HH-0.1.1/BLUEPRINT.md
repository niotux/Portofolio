# Holodeck Humans blueprint

The goal is a person who can work with someone on a substantial undertaking, develop an understanding of it, notice consequences, revise mistakes and return to unfinished work with continuity. Natural, capable behaviour is the outcome to judge. The architecture is a hypothesis we improve through use.

**Versions:** HH-0.1.0 is the retained file-prompt baseline. HH-0.1.1 moves prompts into the shared database, edited live through the `humans-prompts` MCP server, and adds complete per-version source checkouts. HH-0.2.0 remains the next cognitive prototype target; its new body loop is not implemented by the prompt infrastructure. Each HH version is a complete direct sibling project, containing its own source, tests, blueprint and executable. Its prompts live in the shared database, separately from the code release. See [the workflow](WORKFLOW.md), [the next tasks](WORK_PLAN.md) and [experiments](EXPERIMENTS.md).

## What exists

The current Person reacts to an incoming conversation message. It can recognise a participant, suggest semantic memory searches, turn retrieved memories into prose recollections, think privately and write a reply. Exchanges, thoughts and pending deliveries are retained; a durable background queue forms prose memories and embeds them. Older dialogue can be compacted while recent turns remain verbatim. Channels exposes streamed activity for inspection.

Generation uses OpenAI-compatible Chat Completions. Native model reasoning is disabled. Personality is currently substituted into authored page prompts; the startup preparation described below is not implemented. Conversation contexts are currently held per channel; the single continuing person and explicit internal/external body loop below are not implemented. Current compaction follows reported usage after a reply; it does not yet provide admission checks for every operation.

Repository separation and a passing build do not establish intelligent behaviour. HH-0.1.0 is a reproducible starting point to challenge, including on an extended task immediately.

## Stable behavioural boundaries

- Thought, recollection, understanding and outward speech are natural prose. Do not require model-authored records, classifications, marker words, a mind schema or database operations. The model develops its own subjects and connections. Code owns protocol metadata, storage, routing and activity labels.
- A focused hands pass may emit an available tool's operation. That operation is not retained as thought; its actual result becomes prose experience. Ordinary thinking and speech do not emit tool calls. Outward writing is routed by the body, without a model send-message tool.
- Only formed contextual prose memories are embedded. Raw packets and keyword search are not substitutes for semantic memory. Old experience must remain distinguishable from the person's revised current understanding.
- The same person and shared hands operate in internal and external modes. Opening a channel establishes external mode and the conversation focus. Leaving through the hands returns to internal mode. Only explicit successful operations change focus.
- A pushed tag conveys only its channel, without sender or message content. It can wake the person and is visible across channel focus. It does not imply reading, replying or switching channels.
- Ordinary notifications queue and are available only when requested through the hands in either mode. They are never pushed, including on waking. Checking them belongs to the person's routine. Events arriving during work queue; tags have priority and do not start a second person loop.
- Private stages stay private. Only the outward-writing stage supplies a channel message. A failed or incomplete operation does not become a successful read, focus change or delivery.
- Preserve existing people, conversations, memories, workspaces and persistent containers. Holodeck remains unaware of cognition internals; agent behaviour belongs to this library.
- Keep every HH code version selectable and runnable as a complete source checkout in the direct sibling folder `HH-X.X.X/`. Adding a new code version never overwrites an older code snapshot or upgrades its instances. Prompt edits are shared within their HH version and apply at the next operation boundary.

## Shared live prompts

Humans keeps one live prompt set for each HH code version in Holodeck's shared library database, with numbered immutable publications. Every person running that version uses the same live set, across projects. Conversations, memories, settings and prepared personality material remain private to each person. Holodeck knows only library database scopes, never the Humans prompt schema.

Prompts are changed on the spot through the `humans-prompts` MCP server, which any MCP client can use. An edit is live at once for every person of that version from their next operation; a set that fails validation never goes live. Publishing freezes the live set as the next numbered publication. Restoring makes a publication live again without deleting anything. A new HH version starts from the latest publication of the version it grew from. Agent settings and management jobs play no part in prompt editing.

The live set is pinned for a complete foreground response, including recall, thought, speech and its compaction. Background memory formation pins its own. Edits apply to the next operation, never halfway through one. Code records the live revision used and the publication it matched; those identifiers never become model prose. Shared prompt edits do not change the personality-preparation trigger agreed below.

The source tree holds no prompt files; the database is the only source. HH-0.1.0 keeps its original file-based behaviour. The [prompt guide](docs/prompts.md) describes the tools and each prompt; back up the database to preserve prompt history.

## Startup and context

At startup compare the personality prompt in settings with the prompt used for retained preparation. Prepare affected operating prompts on first startup or when that personality prompt changed; otherwise reuse them. Default-prompt edits, application-instruction edits and other setting changes do not trigger regeneration. Application instructions are not personality-prepared. Keep the speaker's grounding present. Preparation is startup work with no activation workflow, and mode changes reuse it.

The prototype implementation must retain an incomplete preparation safely and must not claim startup preparation succeeded when it failed. A new prepared set replaces the usable set only when complete. Exact storage is an implementation choice, not a model-authored format.

Use the configured usable model capacity and account for the complete next request plus its generation allowance. Compact older context before a request cannot fit, preserving recent experience and as much useful context as practical. Compaction does not delete durable originals. Keep compaction itself within capacity and return an explicit failure for an input that cannot fit safely.

The transport remains provider-neutral. Select a counting method appropriate to the configured model and document its accuracy; do not assume an OpenAI-compatible endpoint exposes provider-specific capacity or tokenizer routes. Until exact counting is available, explicitly identified conservative budgeting is a prototype limitation, not a claim of exact admission.

## First living prototype: HH-0.2.0

These are provisional implementation choices to build and challenge, rather than settled claims about cognition.

**One continuing prose history.** Keep private thought, experienced results and recollections available across conversation visits. Channel transcripts keep their participants and public messages separate. Keep unfinished work and revised understanding in ordinary prose; no typed belief or project hierarchy is required. Persist completed experience before depending on it after restart.

**One serial execution path.** At a suitable boundary, introduce pending tags, make relevant experience and recall available, and generate a private thought. A focused hands pass can act on the resulting intention using available body controls. Continue thinking when useful, open or inspect a conversation, request notifications, enter outward writing, leave or wait. Code selects the appropriate stage through the operation; it does not parse decision words from thought. The precise control surface must remain small and tied to actual capabilities.

**A first useful cycle.** A tag arrives; the person privately considers it, possibly recalls the undertaking, and chooses whether to visit. Opening reveals only material actually read. The person can think again, investigate or write to that conversation. After leaving, the private history retains what changed and what remains unresolved. Another tag can be considered without losing the current focus or duplicating the loop.

**Memory starts simple.** Reuse semantic recall and the durable prose-formation queue. Make recollection available between stretches of thought without requiring simultaneous workers that edit the person's understanding. Long-term formation may run asynchronously. A newly recalled old view is experience to consider, not an automatic replacement for current understanding. A nested semantic structure and autonomous Memory loop remain experiments.

**Rest stays an opaque wait.** Preserve continuity while paused and resume on a tag. Make the routine of checking ordinary notifications explicit in editable prompts; ordinary events still never become pushed content. Operating limits bound runaway work and allow cancellation. A limit being reached is a technical interruption, not a fabricated decision by the person to rest. Exact scheduling and recovery policies remain implementation details to test.

**Failures are experiences, not successes.** Keep pending work and focus across transient failures. Distinguish revoked access, deleted channels and invalid settings from failures that may recover. Reconcile uncertain delivery using Holodeck's replay-safe operations before retrying outward writing. Do not turn permanent failures into infinite retries.

## What will make this prototype useful

Use a sustained writers' room task with a whole-project goal, several connected problems, a correction and an unfinished question. Inspect whether the person understands consequences, can question an assumption and resumes relevant work after a new conversation, compaction and restart. Supply a misleading old recollection and inspect whether it is treated as current truth. Repeat with distractions and competing channel tags.

Also demonstrate the boundaries directly: an unread tag reveals no message; ordinary notifications appear only on request; a tag in another channel does not redirect writing; private thoughts never become public messages; interrupted work and redelivery do not duplicate speech or lose saved experience.

No specific wording or personality score defines success. Preserve actual inputs, thoughts, recollections, actions and outputs so the user can judge what happened. Structural checks establish transport and persistence correctness; they do not prove naturalness or intelligence.

## Work deliberately deferred

Nested semantic organisation, a separate Logic or Attention Core, parallel cognition, full sleep behaviour and replacing page framing with pure generated context are in [EXPERIMENTS.md](EXPERIMENTS.md). They do not block the first prototype. New evidence can justify changing this blueprint; record the change in the next version instead of rewriting a released snapshot.
