> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# Attention

Deferred as a separate Core. The user agreed that choosing what deserves consideration can remain a responsibility of the thinking loop. A separate Core or model call has not been justified. A possible later experiment is recorded in the [experiment register](experiments.md#a1-separate-attention-only-if-needed).

## Agreed behaviour

The thinking loop is responsible for considering what needs attention alongside what the person is already doing, including the routine of requesting ordinary notifications. Do not create a separate Attention Core merely to ask the model what to think about next.

The body-loop rules remain: tags are pushed and may be seen across channel focus; ordinary notifications are requestable in either mode and never pushed. A notification does not force reading, replying or switching channels.

Concrete situation: the person has an unfinished thought when a tag arrives. Their thinking can draw on memory, consider the interruption and decide whether to continue or visit the channel. This alone does not establish a need for a separate faculty implementation.

## Boundary

Attention describes the responsibility of selecting and returning to subjects of consideration. It is currently assigned to thinking rather than a separate Core boundary. The body handles notification visibility and explicit conversation focus; the hands perform chosen actions.

## Proposed mechanism

Make the responsibility explicit in the thinking prompts while designing Thought and Logic. Do not add a separate model request or a priority classifier by default. The actual thinking loop and its interaction with Memory are the next discussion, not an already specified mechanism.

Revisit a standalone Attention Core only if a concrete failure and a plausible mechanism justify testing it.

## Open questions

- How does the thinking loop retain unfinished matters and return to them?
- How does its routine include checking ordinary notifications?
- Would a demonstrated failure warrant a separate Attention operation? This is experimental and is not a prerequisite for continuing the design.
