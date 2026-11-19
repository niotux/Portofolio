> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# Person component design

Design the person one component at a time. The person has one ongoing prose context through which experience, understanding and intentions develop. The body translates between that experience and the outside world. The current implementation in `agents/Humans/` is a working baseline, not a specification for the new design.

## Shared decisions

- At startup, regenerate the person's prepared prompts only if the personality prompt in settings changed; otherwise reuse them. Edits to default prompts or application instructions do not trigger regeneration. Application instructions are not themselves personality-prepared. The speaker's system prompt remains present. Preparation precedes the operating faculties and has no separate activation action.
- Keep authored defaults as directly editable files with Git history. The startup personality preparation is a separate concern from that authoring history.
- Channels are limited to those available to the agent. Channel notifications and tag notifications are distinct. A tag only says the person was tagged in a channel; it supplies no message content or sender. It interrupts sleep and starts internal consideration of whether to visit and read; it does not force reading or speech.
- Only tags are pushed to the person, including across channel focus. Ordinary notifications accumulate and are requestable through the hands in either internal or external mode; they are never automatically shown, including on waking. Checking them must be part of the person's routine. New events queue during ongoing work, tags have priority, and notifications never change conversation focus.
- The person decides when to leave a channel; the hands perform the exit to internal mode. Focus is application state changed by explicit hands operations.
- Keep sleep an opaque break until its component is discussed.
- The model develops the contents and relationships of its understanding. Examples of things it might remember are not a fixed taxonomy or output schema.
- Current understanding must reflect revisions promptly. Long-term retention may work asynchronously without restoring abandoned understanding as current.
- Internal and external modes belong to the same person. Opening a channel through the hands establishes external mode and that active conversation. The hands and their capabilities remain available in either mode.
- Each loop stage has its own prompt when needed; prompts that work unchanged across modes are shared. Mode switches use already prepared prompts and never trigger personality preparation.
- The hands interpret an intention, perform the precise tool operation and return the experience of the result. Tool-operation details do not become retained thought. Outward writing is ordinary model output routed by the body to the active conversation; the model does not call a send-message tool. Private stage output remains private.
- Choosing what deserves consideration is a responsibility of thinking for now. A separate Attention Core is deferred unless a concrete need justifies an experiment. The thinking loop must include the routine of checking ordinary notifications.
- A component may make multiple model calls. No fixed execution order or internal mechanism is established by naming the components.

## Design sequence

| Unit | Scope to establish | State | Document |
| --- | --- | --- | --- |
| 1 Context and continuity | Actual model capacity, token budgeting and classic compaction while retaining as much context as possible | Design agreed | [Context](01-context.md) |
| 2 DNA preparation | Personality-prompt change on startup, preparation and reuse | Design agreed | [DNA preparation](02-dna-preparation.md) |
| 3 Senses and body loop | Limited tag notification, internal/external mode, channel focus and outward writing | Design agreed | [Body loop](03-senses-and-body-loop.md) |
| 4 Attention | Responsibility retained in thinking; a separate Core must justify its need | Deferred as a separate Core | [Attention](04-attention.md) |
| 5 Thought and Logic | Ongoing thinking, revision, intentions and possible looping alongside Memory | Next discussion | [Thought and Logic](05-thought-and-logic.md) |
| 6 Memory | Emergent structure and connections, recollection, retention and consistency with current understanding | Awaiting discussion | Created after discussion |
| 7 Hands and action | Intention, precise execution and the resulting experience | Boundary partly agreed; mechanism pending | See body loop until its discussion |
| 8 Response | Outward writing on the active conversation; private stages remain private | Boundary partly agreed; mechanism pending | See body loop until its discussion |
| 9 Rest | Pausing, sleep, continued work and resumption | Awaiting discussion | Created after discussion |

## Experiments

Keep a per-component [experiment register](experiments.md) beside this plan. Record ideas when they arise, even if their final ownership crosses components. Planned experiments do not reopen an agreed component or become required mechanisms automatically. The first experiment considers preserving generated context in its original voice instead of recasting it as third-person narration.

## Component documents

Each discussion produces one document with four sections, using [the component template](component-template.md):

1. Agreed behaviour, illustrated by a concrete situation.
2. Boundary: what it receives, contributes, retains and can affect.
3. Proposed mechanism, kept provisional until examined and accepted.
4. Open questions, including dependencies on other components.

Record a cross-component question without designing the other component in passing. Distinguish an agreed requirement, a technical fact and a proposed solution. Existing code supplies possible pieces but does not settle the design. Do not fill undecided sections with invented mechanisms.

## Current milestone

Context management is agreed at the design level: use actual model capacity, count the complete input, allow room for generation, and compact older context when necessary while preserving as much as possible. Exact compaction boundaries and request handling are implementation details still to settle. DNA preparation is agreed at the design level. The senses/body behaviour is agreed: notifications queue, tags have priority and are visible across channel focus, ordinary notifications require a request in either mode and checking them belongs to the routine, explicit hands operations change focus or exit, and outward writing uses the active conversation. Retry handling remains provisional. These are body mechanisms rather than additional Cores. A separate Attention Core is deferred; its responsibility stays in thinking. Thought and Logic are next, including the user's idea that thinking may loop alongside Memory. That loop has not been designed. Continue the design discussion before changing runtime code. Implementation-level body-loop questions remain recorded without reopening the agreed behaviour. The pure-context representation idea remains a planned experiment, not a condition for completing Context management. No runtime replacement has been implemented by these design documents.

The earlier Person behaviour roadmap is retired in the host repository history. [The baseline implementation](../../../src/Humans/Self.cs) and [its prompt guide](../../../prompts/README.md) describe what runs today.
