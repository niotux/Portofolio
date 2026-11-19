> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# Person component experiments

Planned experiments sit beside the component design. An experiment is a question to investigate, not an accepted mechanism or an implementation task. Keep observations and generated examples in separate artifacts; update the component design with any resulting accepted decision. Do not alter existing people or erase their memories to prepare a trial without explicit direction.

## Context and continuity

### C1 Preserve the generated context voice

State: planned; not performed.

Question: what happens when retained context stays as the AI generated it, without recasting it into third-person narration?

Candidate change: preserve generated thoughts and other contextual prose in their original wording and voice. Examine which added third-person framing, such as named thought openers, is necessary and which can be omitted. The exact meaning of pure context and the treatment of speaker boundaries remain to be defined before the experiment; this does not commit to removing all boundaries or instructions.

Comparison: compare the current page representation with the proposed representation using comparable situations and controlled starting conditions. Inspect both the actual composed inputs and the resulting conversation and thought. Keep model, personality and unrelated prompt behaviour stable enough to attribute differences to representation.

Observe whether the person retains continuity, ownership of its thoughts, speaker distinctions and natural conversation. Human review determines whether the result is preferable; no prescribed wording or personality score is an acceptance target.

Scope: context representation crosses Context, Thought/Logic and Response. It does not change the agreed responsibility of classic token budgeting and compaction. Keep one experiment here and cross-reference it from the other affected component documents when they are designed.

## DNA preparation

No experiments recorded yet.

## Senses and body loop

No experiments recorded yet.

## Attention

### A1 Separate Attention only if needed

State: deferred experiment candidate; not performed.

Start with choosing what deserves attention as a responsibility of thinking. If a concrete failure emerges, compare that approach with a separately justified Attention operation. Possible failures to investigate include repeatedly losing unfinished matters, following every tag or failing to check ordinary notifications. These are candidate observations, not claims about the existing agent.

Do not add a separate Core or model call unless the comparison has an explicit mechanism to test. The user's review of the actual thinking and behaviour determines whether it helps.

## Thought and Logic

Related experiment: [preserve the generated context voice](#c1-preserve-the-generated-context-voice). No separate experiments recorded yet.

## Memory

No experiments recorded yet.

## Hands and action

No experiments recorded yet.

## Response

Related experiment: [preserve the generated context voice](#c1-preserve-the-generated-context-voice). No separate experiments recorded yet.

## Rest

No experiments recorded yet.
