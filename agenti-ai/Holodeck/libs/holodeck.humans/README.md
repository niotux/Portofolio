# Holodeck Humans

Human versions are complete, flat sibling projects:

```text
holodeck.humans/
  HH-0.1.0/       retained baseline: source, tests, prompts, blueprint, executable
  HH-0.1.1/       per-channel replies with prompts in the database
  HH-0.2.0/ ...   the long run's versions, each with BLUEPRINT.md and REPORT.md
  HH-0.4.1/       latest: continuing person, standing mind, alarms, clock of their own
  REPORTS.md     overview of the long run: versions, highlights, findings
  Library.json   Holodeck package metadata
  agents/        host-facing agent definitions
```

Start with [REPORTS.md](REPORTS.md), the overview of the long run from HH-0.2.0 to HH-0.4.1. Open [HH-0.4.1](HH-0.4.1/README.md) for the latest version or [HH-0.1.0](HH-0.1.0/README.md) for the retained baseline. Each folder is a separate Git checkout of `git@github.com:HolodeckAI/holodeck.humans.git`. Git history is shared internally; no version contains another version's project. Source for a version is directly in its `src/`, tests in `tests/`, and its blueprint is directly in `BLUEPRINT.md`.

The latest [blueprint](HH-0.4.1/BLUEPRINT.md), [roadmap](HH-0.4.1/WORK_PLAN.md), [workflow](HH-0.4.1/WORKFLOW.md), and [experiments](HH-0.4.1/EXPERIMENTS.md) belong to HH-0.4.1. Every earlier version keeps its own. HH-0.1.0 is retained and unchanged in behaviour.

`Library.json`, `agents/`, and `teams/` expose these projects to Holodeck. Outer `runtime/`, `prompts/`, and `runtime.json` are the development Person that the Person agent and Show Writing launch. They are published assets, not another source project. Build within the chosen HH folder; builds do not upgrade existing people.

From HH-0.1.1 on, prompts live only in Holodeck's library database: one live set per HH version, edited on the spot through the `humans-prompts` MCP server from any MCP client, with numbered immutable publications. See [the prompt guide](HH-0.4.1/docs/prompts.md). Conversations and memories stay private to each person. The old HH-0.1.0 program retains its original file-based prompts.
