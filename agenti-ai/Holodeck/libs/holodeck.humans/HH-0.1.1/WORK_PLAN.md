# Humans work plan

Direction: [BLUEPRINT.md](BLUEPRINT.md). Method: [challenge, test, note, improve](WORKFLOW.md). Deferred mechanisms: [EXPERIMENTS.md](EXPERIMENTS.md).

HH-0.1.0 is the extracted baseline. HH-0.1.1 corrects the repository layout and keeps prompts live in the shared database. HH-0.2.0 is the first target with a continuing person and the agreed body loop. Release names distinguish snapshots; they do not mean every long-term capability is finished. Implementation status below must change only when code and observable behaviour justify it.

## Immediate milestone: work on prompts live, then challenge a retained version

| Task | Dependency | Completion observation | State |
| --- | --- | --- | --- |
| Configure the separate Holodeck and Humans GitHub origins | Repository extraction | Holodeck references its platform origin and submodule points to the Humans origin; both addresses are accessible | Configured; no remote push |
| Keep complete flat version projects | Existing source/history | HH-0.1.0 and HH-0.1.1 are direct siblings; each owns source/tests/blueprint; neither contains another project | Done |
| Provide generic shared library storage in Holodeck | Existing isolated agent database | Same exact library package shares storage across instances/projects for agents and the user's tools; undeclared libraries and private/host data stay isolated | Deployed |
| Keep prompts only in the database, live per HH version | Library storage | Edits are live from the next operation; invalid sets never go live; publications are numbered and immutable; restore brings one back; a new version starts from the latest publication | Deployed; HH-0.1.1 imported as v1 |
| Edit prompts through the `humans-prompts` MCP server | Prompt store | Any MCP client lists, reads, edits, publishes and restores prompts without a running person | Installed for Claude Code and Codex |
| Challenge HH-0.1.1 prompts live | Deployed store and MCP | Prompt changes are judged through a scratch person's Thoughts and published when they work | In progress |
| Run an extended cognitive task against a retained version | Published version and available services | Retained input/thought/output shows a whole undertaking, a correction, unfinished work and a later return | Planned |

HH-0.1.0 remains installed and unchanged. Use a separate scratch person/project for trials; never reset existing people. Prompt publication and Git/code publication are separate actions. Live prompts and their publications are shared by HH version; conversation and memory are not.

## HH-0.2.0 increments

Each row is a reviewable increment. Challenge it live before broadening the architecture. Continue using the baseline's functioning transport, semantic recall and memory queue.

| Task | Dependency | Completion observation | State |
| --- | --- | --- | --- |
| Retain one continuing private prose history with explicit public channel views | Baseline snapshot | An unfinished thought survives leaving a channel, visiting another and restarting, while public transcripts remain separate | Planned |
| Add startup personality preparation and retained reuse | Private state ownership | First preparation completes; unchanged personality reuses it; only a changed personality prompt regenerates on startup; interrupted preparation retains recoverable work | Planned |
| Budget each next operation and compact before overflow | Explicit request assembly | A long task continues within configured capacity; recent experience and grounding remain; durable originals survive; counting limits are explicit | Planned |
| Implement queued notifications and explicit body focus | Continuing history and Holodeck capability audit | An unread tag gives only the channel; ordinary notifications require a request; pending events never switch focus | Planned |
| Add focused hands for available body controls | Body focus | The person can open/read/check/leave in either mode; failures do not claim success; tool syntax is excluded from retained thought | Planned |
| Continue private thinking between experiences and route outward writing | History, budgeting and hands | Several stretches of thought build on one another; only the writing stage posts to the selected conversation; the person can leave or wait | Planned |
| Reuse recall and asynchronous prose formation through the cycle | Continuing thinking and existing memory queue | Recall contributes relevant experience; a correction remains current after old material is recalled; restart and redelivery preserve work | Planned |
| Publish and challenge the integrated prototype | All preceding increments | Extended task, distractions, correction, compaction, restart and uncertain delivery can be inspected; limitations accompany the version | Planned |

Before implementing notifications and hands, inspect the actual Holodeck agent API for tag metadata, requested channel reads and notification access. Add only missing generic platform capabilities; do not infer that the old inbox already fulfils the new contract.

Use the simplest configurable serial cycle first. Separate faculties or semantic structures are added only when an [experiment](EXPERIMENTS.md) gives them a specific purpose. Scheduling, cancellation, token budgeting and delivery reconciliation are necessary engineering choices; make them explicit during their increment rather than blocking on a complete theory of thought.

## Completion and next decision

HH-0.2.0 is ready to tag when its implemented contract is represented accurately by its version blueprint, focused safety checks pass, it is published, and its actual live behaviour and remaining failures are reviewable. User judgement determines whether the behaviour is promising. A build alone does not finish the milestone.

After the integrated challenge, select the most consequential observed failure. Improve that failure in the next small version; use an experiment only where competing mechanisms need comparison. Keep this plan current rather than adding a development journal.
