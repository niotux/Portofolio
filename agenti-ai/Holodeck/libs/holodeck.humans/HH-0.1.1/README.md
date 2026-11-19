# HH-0.1.1

The complete development project for this Human version. Source is in `src/`, tests in `tests/`, and the system blueprint is [BLUEPRINT.md](BLUEPRINT.md). The retained HH-0.1.0 project is a direct sibling, not contained here.

Read [WORK_PLAN.md](WORK_PLAN.md) for current tasks, [WORKFLOW.md](WORKFLOW.md) for challenge–test–note–improve, and [EXPERIMENTS.md](EXPERIMENTS.md) for deferred ideas. The new cognitive body loop remains planned.

```bash
dotnet test Humans.slnx
bash publish.sh                       # build this version only, into runtime/
bash scripts/install-prompt-tools.sh  # install the humans-prompts MCP server
```

Prompts live only in the shared database: one live set for this HH version, edited on the spot through the `humans-prompts` MCP server from any MCP client, with numbered immutable publications to restore. All people of this version share its live prompts; conversation and memory remain private. See [the prompt guide](docs/prompts.md).

This is a Git checkout of `git@github.com:HolodeckAI/holodeck.humans.git`. The outer library holds only host-facing definitions and compatibility assets for existing agents. Building here neither creates another nested project nor upgrades existing people. New version projects belong beside this folder.
