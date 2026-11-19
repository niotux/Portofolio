# Person prompts

Prompts live only in the database: one live set per HH version, shared by every person of that version across projects. There are no prompt files in the source tree. Each person's conversations, memories and settings stay in its own private database.

## Editing and publishing

The `humans-prompts` MCP server is the editing surface. Any MCP client on the machine can run it: Claude, Codex or another tool. It talks to Holodeck's user API (`POST /api/libraries/{id}/{version}/database`), which runs SQL as this library's shared database role. Holodeck itself knows nothing about prompts.

| Tool | What it does |
| --- | --- |
| `list_versions` | Every HH version with prompts, its live revision and whether it has unpublished changes |
| `list_prompts` | The live prompts of one version with their first lines |
| `get_prompt` | The full live text of one prompt, or its text in a given publication |
| `edit_prompt` | Replaces one prompt's live text, or adds a prompt; every person of that version uses it from their next cycle or memory |
| `remove_prompt` | Removes a prompt a version no longer reads |
| `publish_prompts` | Freezes the live set as the next numbered publication (v1, v2, …) with a label |
| `list_publications` | Publications of one version, newest first |
| `restore_publication` | Makes a publication live again, replacing unpublished edits; nothing is deleted |
| `start_version` | Gives a new HH version its prompts, copied from the latest publication of the version it grew from |
| `create_version` | Gives a new HH version a complete prompt set of its own, live and published as v1 |

The working rhythm: edit, talk to a person, edit again, and publish when something works. Publications are immutable (a database trigger refuses updates and deletes). Restoring is how you go back.

An edit is validated before it goes live: prompts are named `.txt` or `.json` files of bounded nonempty text, and a `.json` prompt is an object of text labels that keeps every label it had. Which prompts a version needs is that version's business: its runtime refuses a set missing one and keeps the set it was using. A refused edit leaves the live prompts unchanged.

Install or update the server with `bash scripts/install-prompt-tools.sh`; it publishes `humans-prompts` with its `appsettings.json` (Holodeck URL and the library's package identity) into `~/.local/share/humans-prompts/`. Register that program as a stdio MCP server in each client.

## Pinning and records

A person reads the live set at the start of each wake and again before each later cycle, and pins it for that cycle's noticing, recollection, thought, hands and writing. Background memory formation reads and pins its own. An edit never changes prompts halfway through a pass.

Each pin records what it ran with in the person's workspace, `last-prompts.json` and `last-memory-prompts.json`: the HH version, the live revision and the publication it matched, if any. These identifiers never enter model prose. The person also writes `page.txt`, the whole current page, after every cycle.

A database backup preserves the live sets and publication history. Git holds only code.

## HH-0.2.x prompts

| Name | Purpose | When used |
| --- | --- | --- |
| `page.json` | The page's prose: header, portrait, summary, thought and recollection openers, message lines, time marks, conversation names, the human's label, and the narration of every body event | Whenever the page or a stretch of it is rendered |
| `controls.json` | What each hands control means, as the hands see it | Every hands pass |
| `continue.txt` | Continue the page as the person: their thought or their message | Every thought and every message |
| `hands.txt` | Carry out what the last thought means to do, with one control | After every thought |
| `look.txt` | Name what memory might hold more about | When something new came from outside |
| `search.txt` | Query instruction for the embedding model | Each memory search |
| `remember.txt` | Turn retrieved memories into a first-person recollection, marked as past | For each search that returns memories |
| `form.txt` | Turn a stretch of history into up to three first-person memories | In the background, at the end of a visit or after enough moments |
| `compact.txt` | Fold older history into what the person remembers of it | When the page would outgrow its share of capacity |
| `prepare.txt` | Write the self-portrait from the personality prompt or a drawn life | First start, or after the personality prompt changes |
| `lives.txt` | List many different adult lives that fit what is given | Preparation with an empty personality |
| `work.txt` | Reason one question through carefully, as the person, and conclude | Twice, when the hands choose to work something out |
| `check.txt` | Check two workings against each other and write the checked conclusion | After the two workings |

`{name}` is the personality name or the agent's display name; `{portrait}` is the prepared self-portrait. Event labels in `page.json` also use `{channel}`, `{others}`, `{count}`, `{senders}`, `{list}`, `{contact}` and `{time}`; control descriptions use `{focus}` and `{unknown}`. The thought opener's first character marks thoughts on the page, and the message line (`{speaker} in {channel}:`) also identifies a message meant for another conversation, so changes to them need matching attention in `Person.cs`.

## Code entry points

- `src/Humans.Prompts/PromptStore.cs`: the shared catalog and its schema.
- `src/Humans.PromptTools/PromptTools.cs`: the MCP tools.
- `src/Humans/Person.cs`: the wake and its cycles.
- `src/Humans/Body.cs`, `Hands.cs`: the phone, tags and controls.
- `src/Humans/Page.cs`: rendering the page from the pinned set.
- `src/Humans/Recall.cs`, `MemoryKeeper.cs`, `Preparation.cs`: recollection, formation, the portrait.
