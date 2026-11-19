# Humans handover

**Direction:** build and challenge a living prototype. The user replaced the discussion-first component sequence with an implementation-and-experiment workflow. Do not resume the old requirement to finish Thought/Logic, Memory and the other component discussions before building.

Read [BLUEPRINT.md](BLUEPRINT.md), [WORK_PLAN.md](WORK_PLAN.md) and [WORKFLOW.md](WORKFLOW.md). The blueprint carries agreed behaviour and the simplest provisional implementation choices. [EXPERIMENTS.md](EXPERIMENTS.md) carries optional mechanisms: nested semantic organisation, concurrent Memory participation, separate Logic/Attention, pure-context voice and richer rest.

HH-0.1.0 is the unchanged page-based baseline with file prompts. HH-0.1.1 keeps prompts only in the shared database: one live set per HH version, shared by all its people across projects, edited on the spot through the `humans-prompts` MCP server (installed in `~/.local/share/humans-prompts/`, registered in Claude Code and Codex). Publishing freezes numbered immutable copies; restoring makes one live again. There is no draft/preview step and no prompt editing through Settings or management jobs. Memory and conversation remain per-person. Each foreground turn and each background formation pins the live set it started with. See [docs/prompts.md](docs/prompts.md).

Full source checkouts are direct sibling folders under the outer library: `HH-0.1.0/` and `HH-0.1.1/`. Each contains its own source, tests and blueprint. Never nest one project inside another. The baseline implementation and original tag remain retained. Holodeck's origin is `git@github.com:HolodeckAI/Holodeck.git`; Humans' origin is `git@github.com:HolodeckAI/holodeck.humans.git`. The local bare repository remains a backup, not the authoritative external address. Do not push without an instruction to publish remotely.

HH-0.2.0 is still the next cognitive prototype: continuing private history, explicit internal/external focus, notifications and shared hands. Startup personality preparation and before-operation context management remain part of that target.

**Current state:** the flat sibling layout is complete. Holodeck is deployed with the generic user-side library database endpoint. HH-0.1.1 is built, placeable as `Human HH-0.1.1`, and its prompts were imported as v1; a scratch person in the `Scratch HH-0.1.1 prompts` project picked up a live edit without restarting. Next: prompt work on HH-0.1.1 through the MCP, an extended cognitive task, then the HH-0.2.0 prototype.

Keep model thought and speech natural prose. Only the focused hands pass may emit tool operations; discard their syntax from retained thought. Native model thinking remains disabled. Tags disclose only a channel and never change focus automatically; ordinary notifications require a request in either mode. Keep the notification-checking routine in thinking. Do not add a mind taxonomy or semantic hierarchy merely because it is an experiment candidate.

Preserve current people, settings, conversations, memories, workspaces and persistent containers. Use scratch instances for controlled trials. Publishing does not recreate agents. Exact package dependency changes must be deliberate so existing writing roles keep working.
