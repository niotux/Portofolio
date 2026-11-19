# HH-0.2.1

The complete project for this Human version: HH-0.2.0's continuing person with a phone that buzzes for every message and conversations that can be muted. Source is in `src/`, tests in `tests/`, and the system blueprint is [BLUEPRINT.md](BLUEPRINT.md). The retained HH-0.1.0 project is a direct sibling, not contained here.

Read [REPORT.md](REPORT.md) for what was tried and found, [WORK_PLAN.md](WORK_PLAN.md) for the open failures, [WORKFLOW.md](WORKFLOW.md) for challenge–test–note–improve, and [EXPERIMENTS.md](EXPERIMENTS.md) for deferred ideas. The trial scripts are in `tests/trials/`.

```bash
dotnet test Humans.slnx
bash publish.sh                       # build this version only, into runtime/
bash scripts/install-prompt-tools.sh  # install the humans-prompts MCP server
```

Prompts live only in the shared database: one live set for this HH version, edited on the spot through the `humans-prompts` MCP server from any MCP client, with numbered immutable publications to restore. All people of this version share its live prompts; conversation and memory remain private. See [the prompt guide](docs/prompts.md).

This is a Git checkout of `git@github.com:HolodeckAI/holodeck.humans.git`. The outer library holds only host-facing definitions and compatibility assets for existing agents. Building here neither creates another nested project nor upgrades existing people. New version projects belong beside this folder.
