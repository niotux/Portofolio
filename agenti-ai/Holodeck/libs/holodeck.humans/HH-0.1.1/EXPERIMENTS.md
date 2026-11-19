# Humans experiments

This is the backlog of questions beyond the simplest [prototype blueprint](BLUEPRINT.md). None of these mechanisms is established or required to begin testing. Actual runs and observations belong in version-associated artifacts; update this document with the resulting design decision, rather than keeping a journal here.

For each trial, identify the behaviour in question, compare against a tagged baseline, keep unrelated model/personality/prompt conditions stable, preserve the actual context and output, and decide what the result justifies. Mixed or inconclusive results remain inconclusive. Never reset existing people to manufacture a clean trial.

## E1 Preserve generated context in its original voice

Question: does retaining thought in its original generated voice improve continuity compared with the current named page framing?

Compare the existing representation with a clearly defined alternative while retaining necessary speaker and source distinctions. Inspect composed inputs as well as behaviour across private thinking, outward speech and compaction. Removing every boundary is not a prerequisite. This changes representation, not capacity management.

## E2 Give a substantial undertaking a navigable semantic structure

Question: can the person move between the whole undertaking, connected problems and supporting experiences more effectively than with prose context and flat semantic recall?

Start with a sustained undertaking that demonstrates a failure of the baseline. Compare a small model-developed arrangement of linked prose with ordinary recall. Nesting is one candidate; cross-connections may matter as much as parent/child relationships. Do not hardcode character, project, belief or concern categories, or require structured model output. Inspect whether a changed detail updates the person's understanding of the whole and whether they can return to supporting experience.

## E3 Let Memory participate while thought develops

Question: does Memory following a developing thought supply more useful experience than deliberate recall between serial stretches of thought?

First establish an observable serial cycle. Compare a bounded candidate that can introduce relevant recollection at safe context boundaries. Inspect relevance, repetition, stale recollection, latency and whether current understanding remains coherent. A simultaneous background worker editing thought is not an accepted design.

## E4 Separate Logic only for a demonstrated contribution

Question: does a distinct reasoning pass contribute something that continued ordinary thought does not?

Use a concrete missed implication, contradiction or unexamined assumption. Compare a focused prose reasoning pass against the same opportunity for continued thinking. Judge whether the person makes better grounded progress, rather than merely producing a longer explanation. Do not introduce a required proof format or model-authored classifications.

## E5 Separate Attention only if needed

Question: does a distinct attention operation help the person retain unfinished matters and check ordinary notifications?

Keep those responsibilities in thinking first. If live behaviour repeatedly loses them, compare a small explicit candidate. Tags still do not force a visit, ordinary notifications still require requests, and no priority classifier is assumed.

## E6 Find useful rhythm, rest and interruption boundaries

Question: how does the person continue useful work, pause without aimless generation, and return after an interruption?

Begin with the blueprint's opaque cancellable wait and explicit operating limits. Compare continuation and notification-checking routines on a long task with distractions. Inspect unfinished work, repeated thought and missed ordinary notifications. A technical budget stop must not be presented as a psychological choice. Full sleep behaviour is deferred.

## E7 Improve preparation and context implementation after the first prototype

Question: do alternative preparation guidance, compaction boundaries or counting methods improve behaviour and reliability?

Retain the fixed behavioural rules: only personality-prompt change triggers startup regeneration; unchanged prepared prompts are reused; application instructions remain unaffected; complete requests need generation room; compaction does not delete durable originals. Compare implementation alternatives within those boundaries. Provider-specific capability discovery is optional infrastructure, not a dependency silently imposed on every OpenAI-compatible model.
