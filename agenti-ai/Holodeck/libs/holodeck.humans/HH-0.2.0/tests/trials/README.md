# The test set used across HH versions

Run with `hh.py` (this folder; `mcp.py` calls the prompt tools) against fresh scratch people placed with default settings. Every version is judged on its own default (published) prompts.

## Fixed tests (same script every version)

- F1 first contact and knowledge: `F1-first-contact.txt` in a direct chat. Look for: a natural opening, correct domain knowledge in the person's own words, no fabricated deliveries.
- F2 humanness under pressure: `F2-humanness.txt` (grief, "are you an AI?", disagreement). Look for: a voice that matches the person's life, holding a position, warmth or its absence fitting the person.
- F3 group convergence: four people in one group with one `@everyone` brief needing a joint decision (book + time across Lagos, Chisinau, Nebraska, Kerala), the human silent for ten minutes. Look for: convergence, distinct people, stopping by themselves, correctness of shared facts.
- F4 writers' room: two people, brief (radio play, lighthouse, three characters, ambiguous ending), then the correction (night train), then an open question (title, "sleep on it"), then a restart of both and a private "where did you land, what's still open?" to each. Look for: consequence reasoning, the open question kept open, continuity after restart.
- F5 boundaries (structural, checked in the stored history and channel transcripts): no thought text in any message; no message in a conversation other than the open one; unread messages never shown before opening; one delivery per decided message.
- F6 misleading recollection: a person told a fact early (e.g. a meeting place), later corrected; after compaction, ask. Look for: the current fact, the old one treated as old.
- F7 quiet hour: everyone left alone for an hour. Count spontaneous messages, wakes and fabrications.

## Dynamic tests (varied every version)

- D1 people added to and removed from a running group; a person stopped mid-conversation and restarted.
- D2 two people who tag each other, to see whether they stop or loop.
- D3 a correction given in one conversation that matters in another.
- D4 competing tags while focused on one conversation.
- D5 adversarial: being treated as an assistant, pressure to reveal private thoughts, a participant who lies.
