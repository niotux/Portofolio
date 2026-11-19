> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# DNA preparation

Design agreed at the behaviour level. Prompt-file layout, initial preparation and failure handling remain implementation details. Related experiments belong in the [experiment register](experiments.md#dna-preparation).

## Agreed behaviour

On agent startup, read the personality prompt from settings and check whether it differs from the personality prompt used for the retained preparation. If it changed, regenerate the personality-shaped agent prompts and use the generated prompts. If it did not change, reuse the retained prompts.

Only a change to the personality prompt in settings triggers regeneration. Changes to default prompts, application instructions, model configuration or other settings do not trigger it. A settings edit is considered on startup, not as an automatic mid-conversation preparation.

Each process or stage has a separate prompt where needed. Prompts that work unchanged across internal and external modes can be shared; Memory and Logic do not automatically require separate copies. Mode changes reuse prepared prompts and do not cause regeneration.

Application prompts are the instructions that perform the personality preparation. They are not themselves affected by personality preparation. The prepared agent prompts are retained for ordinary operation; they are not remixed for each thought or message.

For example, an agent stopped and started with the same personality prompt reuses its prepared prompts. If the user changes that personality prompt in settings before starting it, startup regenerates them. Editing a default prompt alone leaves the retained preparation in use.

There is no separate activation action, user control or deployment workflow. Using the generated prompts is part of completing startup preparation.

## Boundary

Preparation precedes the operating faculties. It receives the personality prompt, the authored default prompts and the application instructions. It retains prepared agent prompts and the personality prompt they were prepared from, so the startup check can compare personality changes. Exact storage and comparison implementation are undecided.

This component shapes the person's operating prompts. It does not change their conversation, memories, current understanding or identity records. Those are not regeneration triggers. Learned experience belongs to the faculties being designed separately.

## Proposed mechanism

Startup follows the agreed check and reuse-or-regenerate path. The previously discussed application layout has three possibilities per agent prompt:

- Apply shared personality-application instructions.
- Supply guidance specific to that prompt's purpose.
- Leave the prompt unaffected and use its default unchanged.

For an affected prompt, a preparation call receives its default text, the personality prompt and the applicable instructions, and returns the prepared prompt as ordinary prose. The exact choice between shared instructions, per-prompt instructions and optional additional guidance remains to be settled. No application instruction is recursively personality-prepared.

These possibilities do not add regeneration triggers. Existing prepared prompts remain in use when only default files or application instructions are edited.

## Open questions

- The concrete application-instruction layout and how each default prompt declares whether personality affects it.
- First preparation for an agent that has no retained prompts yet.
- Startup behaviour if required preparation cannot finish, including retaining incomplete work safely. No separate activation workflow is implied.
