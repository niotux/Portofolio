# When you return

**Every agent runs in its library's project environment:** one container per project and exact library version, with a separate non-root user for each agent and service. Per-agent containers and container-based `Services` are gone; the existing people were migrated with their ids, settings, tables and memories, and their old tables are backed up in `data/agent-migration-backup-2026-11-15.tar.gz`. Attach `holodeck.utilities` in Library Explorer and open **Bash** from the project's start menu: each new window owns a shell, explicit window Close ends it, browser reload/closure preserves it. Sudo is the user's per-process grant: an agent's in Team's Computer tab, a service's (such as `bash`) in the Services applet, which also shows each library service's state and log. An environment created before sudo is recreated once, when stopped or when a grant first needs it. [WORK_PLAN.md](WORK_PLAN.md#library-environments-and-applets) owns the remaining grant work.

**Resume Humans here:** [HH-0.1.1 handover](libs/holodeck.humans/HH-0.1.1/WHEN_YOU_RETURN.md). The user explicitly chose complete, flat sibling projects: `libs/holodeck.humans/HH-0.1.0/` and `HH-0.1.1/`. Every project directly owns `src/`, `tests/`, `prompts/` and `BLUEPRINT.md`; no project contains another HH version. The outer library holds host-facing definitions and compatibility assets only.

HH-0.1.0 retains the original application source and executable. Its layout-only source commit removes old nested packaging while preserving its historical tag. HH-0.1.1 holds the database prompt work saved in Git. Shared library database support and prompt draft/publication/preview/rollback/cleanup passed source-level database checks; they have not been deployed. Do not resume the integration trial or publish new agents as part of the layout correction.

The working cycle remains challenge–test–note–improve. The active [blueprint](libs/holodeck.humans/HH-0.1.1/BLUEPRINT.md), [roadmap](libs/holodeck.humans/HH-0.1.1/WORK_PLAN.md) and experiment backlog belong to HH-0.1.1. Each HH version's prompt catalog is shared across its agents; memories and conversation remain private. The new cognitive body loop remains planned.

Existing agents, data, source history and the outer library directory must be retained. Both source projects use `git@github.com:HolodeckAI/holodeck.humans.git`; the platform uses `git@github.com:HolodeckAI/Holodeck.git`. No external push has been made.

Creative Studio remains a separate library and project. Its first anime image workflow, private notebook, prompt revisions and bounded file inspection are implemented. Realistic images, direct reference conditioning and video families remain planned in [the creative-agent design](docs/creative-agent.md). Gateway status, richer rendering and cancellation controls remain pinned; cancellation is not an LLM tool during rendering.

## What runs

Bash uses GPU terminal rendering with a DOM fallback and batches redraw output while preserving size-change ordering. Refresh the browser to load the applet update into existing windows; their shells remain running.

The installation lives at `/Space/Servers/Holodeck`, beside the AI stack.

The stack runs on the default Docker context at http://127.0.0.1:7077, and on the LAN at http://192.168.2.2/holodeck/ through the PC's front server (Holodeck, its pgvector PostgreSQL, and the Docker socket proxy). Agents come only from libraries in `libs/`, including `holodeck.humans`, `holodeck.show-writing` and `holodeck.creative-studio`. Their agents reach the chat and embedding servers through the gateway of the `ai` stack at the addresses in their settings.

## Agent lifetime

An agent's computer is its user, home and table in its library's environment for the project, created at placement. Start and Stop change only its process; an exit is shown as crashed and not restarted. Holodeck restart reconnects to running agents and leaves stopped agents stopped; Holodeck shutdown leaves them running. Only agent or project deletion removes the computer. A host or engine shutdown leaves agents stopped until Start. The writing agents retry transport and memory-queue requests while Holodeck is unavailable, then continue saved work when it returns.

An agent's environment registration fixes its definition, so definition replacement on an existing agent is refused. Folder and port grants are refused until per-process grant support exists. See [WORK_PLAN.md](WORK_PLAN.md).

## Try it

Open http://127.0.0.1:7077, pick **Bench**, open Channels and talk to one of the four people. Each person's Thoughts card shows every step: recognising you, what to remember, remembering, thinking, writing, and the memory formed afterwards.

Publish a retained HH release through the Humans publisher. Only `bash agents/Humans/publish.sh` explicitly refreshes the development Person (`libs/holodeck.humans/runtime/Person`, which Show Writing launches) and generated compatibility assets. Restart only the agents whose program or settings should reload. `node tests/bench/replay.cjs <role> <name> <message>...` replays a conversation in a scratch project and prints each step. The destructive `tests/bench/reset-people.cjs` helper clears a bench’s memories and channels; use it only when a fresh bench is explicitly wanted.

## Checks

See [README.md](README.md#checks).
