# Humans development workflow

Build a useful prototype, challenge it and improve it. Unresolved nonessential mechanisms belong in [EXPERIMENTS.md](EXPERIMENTS.md); they do not require completing every component discussion before implementation. [BLUEPRINT.md](BLUEPRINT.md) is the current system direction and [WORK_PLAN.md](WORK_PLAN.md) is the single active task roadmap.

## Challenge, test, note, improve

1. **Challenge.** Select a concrete task or observed failure. State what useful behaviour would look like, the hypothesis being tested and what would count against it. Prefer sustained work with corrections and return visits over a single clever reply.
2. **Test.** Make the smallest reviewable change, publish it and exercise the actual person. Keep a tagged baseline for comparison. Inspect the composed context, recalled material, streamed thought, actions and speech. Use a scratch person when a controlled starting point is needed; preserve existing people and their files.
3. **Note.** Save the actual run and observations as artifacts associated with the version, including failures and unresolved interpretations. Record the exact revision, prompt/configuration inputs and model identity needed to understand the run. Keep secrets out of Git and shared artifacts. Documentation describes the resulting design and current state; it is not an evidence journal.
4. **Improve.** Address the most consequential observed failure, or compare a specific alternative through an experiment. Update the blueprint and roadmap to match. Preserve useful behaviour and retain the old version's blueprint. Repeat.

A failure is a result to investigate. A passing build establishes buildability; it does not establish intelligence. User inspection of live Thoughts and conversation determines whether the person is more natural and capable.

## Version contract

Name released versions **HH-X.X.X** and tag the independent Humans repository with that exact name. Use a minor increment for a new prototype capability and a patch increment for a focused correction or prompt refinement. Reserve a major increment for an incompatible boundary or data-contract change. The HH name is the release identity; `Library.json` may retain its compatible local package version until a deliberate dependency migration is made.

Each version is a complete direct sibling checkout, with `src/`, `tests/`, configuration and `BLUEPRINT.md` directly inside its HH folder. The outer library holds the host-facing agent definitions. Never put another HH checkout or a `versions/project` structure inside a version.

Git retains complete source history. Released source and blueprints stay fixed; new work happens in the next sibling checkout. Prompts live in the database: each version's live set stays editable and its publications are immutable. Older code versions and their prompt histories remain available.

## Build and retain a version

1. Work in the intended HH project and keep its `VERSION`, blueprint and roadmap aligned with actual behaviour. Run focused checks and challenge the implementation.
2. Run `bash publish.sh` in that project to build its own `runtime/Person`. This does not create a release folder, modify another project or update existing agents.
3. Once a code version is ready, mark it retained with a `RELEASED` file, review and commit its complete source/blueprint, and create the matching HH tag. Preserve the existing executable. A retained build with its executable present is kept unchanged.
4. New development goes in a direct sibling checkout of the same repository, on its own branch. For example, from `HH-0.1.1`, `git worktree add -b hh/HH-0.1.2 ../HH-0.1.2 HEAD` creates a sibling, never a child. Update that new project's version and remove its retained marker before development, then give it prompts with the `start_version` tool.
5. Record each source checkout's commit in Holodeck's submodule references. Keep actual runs in the relevant project's ignored `artifacts/` directory. External publication remains a separate action.

HH-0.1.0's historical tag is preserved. Its current checkout contains a layout-only correction, with the same application source and retained executable. Do not retarget that historical tag or mistake the layout correction for new cognitive behaviour.

## Prompt experiments and publication

Prompt work happens live, separately from HH code releases and Git. Use the `humans-prompts` MCP server from any MCP client (see the [prompt guide](docs/prompts.md)). Edit a prompt; every person of that HH version uses it from their next message or memory. Talk to a scratch person, inspect Thoughts, and edit again. When something works, publish it with a label saying what it is. To go back, restore a publication; nothing is ever deleted.

A controlled comparison identifies both its HH code version and the prompt revision each operation used, which the person records in its workspace. Database backups preserve prompt history; Git preserves code.

## Verification and integration

Keep source, tests, prompts, configuration and publishing in this library repository. Run focused checks for changed behaviour. Add automated tests when transport, persistence, replay or data could silently break; avoid tests that pretend to score personality or merely mirror an implementation. Platform changes run Holodeck's C# suite and room browser check as required by its own instructions.

For a substantial architectural change, inspect the working tree and create a local Git savepoint without including secrets or runtime data. Keep unrelated edits intact. Publishing new binaries does not authorize replacing persistent containers or clearing memories; restart only the intended prototype instances when program/settings reload is needed.

Keep Holodeck's library reference aligned with the chosen Humans revision. Keep the library-local handover current at useful milestones. When a plan changes, retire conflicting active guidance and retain useful history in version snapshots or an explicitly archived location.
