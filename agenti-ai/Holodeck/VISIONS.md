# Visions

A growing list of far-off ideas: things worth remembering but not yet planned. Add new ones at the bottom and keep each to a few lines. When one becomes real work, move it into [the work plan](WORK_PLAN.md) and leave a link here. The current direction is in [ARCHITECTURE.md](ARCHITECTURE.md).

## Learning from corrections

Each time the user corrects the graph's structure ("the storm should be its own event", "that's not the same character"), store the before/after and the reason as an example. pgvector retrieves similar past decisions into the integration prompt, so the system improves without fine-tuning. Replay (E4) measures whether a prompt or example library actually does better on recorded conversations. *(2026-10-22)*

## Lenses and specializations

A small universal core (things, events, places, times, ideas, relations), with lenses on top for each kind of work. A lens adds its own atom kinds, relations and checks. Libraries of lenses could be shared between projects. *(2026-10-22)*

- **Film:** two orderings of events, *fabula* (when it happens in the story world) and *syuzhet* (the order the audience sees it). A flashback is a different position in the second ordering. Characters, objects and places are shared between both.
- **Novel, game, music, visuals:** each would need its own shape (chapters and viewpoint; branching state; motifs and sections; shots and palettes).

## Open atom kinds

Don't hard-code the ontology. Let the model propose atom and relation kinds, observe which ones get used and corrected, and promote the stable ones into a lens. *(2026-10-22)*
