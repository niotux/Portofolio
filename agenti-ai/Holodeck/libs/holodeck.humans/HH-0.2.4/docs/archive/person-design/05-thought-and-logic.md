> Historical component discussion. Superseded by the current [Humans blueprint](../../../BLUEPRINT.md), [roadmap](../../../WORK_PLAN.md), and [experiments](../../../EXPERIMENTS.md). This archive does not gate prototype implementation.

# Thought and Logic

Next discussion. No mechanism has been agreed. The user expects this may involve a loop alongside Memory; that possibility must be worked through rather than treated as a settled architecture. Related experiments belong in the [experiment register](experiments.md#thought-and-logic).

## Agreed behaviour

Choosing what deserves consideration belongs to thinking for now. A separate Attention Core is deferred. Thinking also needs a routine for requesting ordinary notifications, since only tags are pushed.

The person must be able to develop and revise understanding over time. Current understanding should reflect revisions promptly, while long-term retention may happen asynchronously without reviving an abandoned understanding as current. These are previously agreed requirements; their mechanism is still open.

Internal thought and reasoning remain natural prose. The model develops the contents and relationships of its understanding; example subjects must not become hardcoded object categories, field taxonomies or prescribed model output formats.

## Boundary

The relationship between Thought and Logic, whether they need separate Cores, what each contributes and what persists between operations are unresolved. Their interface to Memory is unresolved too. The existing page-based runtime is a reference implementation to inspect, not a constraint on this design.

Use the agreed body-loop contract: limited tag notifications, requestable ordinary notifications, shared hands, explicit channel focus and outward writing only from the writing stage. Do not redesign those decisions while exploring cognition.

## Proposed mechanism

None agreed. Begin with a concrete cycle and establish what changes during a moment of thinking, what remains available afterward, and how Memory can participate. The user's expectation of a loop alongside Memory is a starting idea, not a fixed sequence of calls or a background-worker design.

Keep sleep opaque. The internal/external perspective mechanism is agreed at the body level, but the amount and representation of thought carried between those views remains open and relates to the [context-voice experiment](experiments.md#c1-preserve-the-generated-context-voice).

## Open questions

- What is one useful operation of thinking, and what makes the person continue, act, wait or rest?
- What does Logic add, and does that require a distinct Core?
- What working understanding persists between operations, and how can the person revisit and revise it?
- How does Memory supply, form and retain material while reasoning continues, with current understanding kept consistent?
- How are unfinished intentions and the notification-checking routine kept available without adding a separate Attention Core by assumption?

Continue this discussion with the user before designing a mechanism or changing runtime code.
