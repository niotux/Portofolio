# Person prompts

Prompts live only in the database: one live set per HH version, shared by every person of that version across projects. There are no prompt files in the source tree. Each person's conversations, memories and settings stay in its own private database.

## Editing and publishing

The `humans-prompts` MCP server is the editing surface. Any MCP client on the machine can run it: Claude, Codex or another tool. It talks to Holodeck's user API (`POST /api/libraries/{id}/{version}/database`), which runs SQL as this library's shared database role. Holodeck itself knows nothing about prompts.

| Tool | What it does |
| --- | --- |
| `list_versions` | Every HH version with prompts, its live revision and whether it has unpublished changes |
| `list_prompts` | The live prompts of one version with their first lines |
| `get_prompt` | The full live text of one prompt, or its text in a given publication |
| `edit_prompt` | Replaces one prompt's live text; every person of that version uses it from their next message or memory |
| `publish_prompts` | Freezes the live set as the next numbered publication (v1, v2, …) with a label |
| `list_publications` | Publications of one version, newest first |
| `restore_publication` | Makes a publication live again, replacing unpublished edits; nothing is deleted |
| `start_version` | Gives a new HH version its prompts, copied from the latest publication of the version it grew from |

The working rhythm: edit, talk to a person, edit again, and publish when something works. Publications are immutable (a database trigger refuses updates and deletes). Restoring is how you go back.

An edit is validated as a whole set before it goes live: every required prompt must exist and `page.json` must hold every page label. A refused edit leaves the live prompts unchanged.

Install or update the server with `bash scripts/install-prompt-tools.sh`; it publishes `humans-prompts` with its `appsettings.json` (Holodeck URL and the library's package identity) into `~/.local/share/humans-prompts/`. Register that program as a stdio MCP server in each client.

## Pinning and records

A person reads the live set at the start of each foreground response and pins it for recognition, searches, recollections, thought, reply and the compaction that follows. Background memory formation reads and pins its own. An edit never changes prompts halfway through an operation.

Each operation records what it ran with in the person's workspace, `last-prompts.json` and `last-memory-prompts.json`: the HH version, the live revision and the publication it matched, if any. These identifiers never enter model prose.

A database backup preserves the live sets and publication history. Git holds only code.

## Prompts and when they run

| Name | Purpose | When used |
| --- | --- | --- |
| `page.json` | Labels and openers for the person's conversation page, memories, thoughts and speech | Whenever a page or conversation excerpt is assembled |
| `continue.txt` | Ask a chat model to continue the person's page from its open thought or speech | For each thought and reply |
| `recognise.txt` | Turn memories of previous conversations with this participant into a first-person recollection | At the start of a channel conversation, if previous memories of that participant exist |
| `look.txt` | Suggest subjects worth looking up in memory from the recent dialogue and incoming message | Before thinking about an incoming message |
| `search.txt` | Instruction prepended to semantic search queries sent to the embedding model | When embedding a memory-search query |
| `remember.txt` | Turn retrieved memories into a grounded first-person recollection | For each suggested subject that retrieves memories |
| `form.txt` | Turn an exchange into a first-person prose memory | In the background after an exchange is saved; a saved draft memory is reused on retry |
| `compact.txt` | Summarize the older dialogue while retaining recent turns verbatim | After a reply reaches the configured context threshold, if older turns can be removed |

`page.json` is application configuration. Its values become prose on the page; the JSON object is never a requested model output. Its labels also take part in opening and stopping generations, so changes to brackets and speaker boundaries need matching attention in `Self.cs`.

## Substitution and assembly

`Page.Fill` substitutes four literal placeholders in instructions and page labels:

| Placeholder | Source |
| --- | --- |
| `{name}` | Personality name from instance settings, falling back to the instance display name |
| `{personality}` | Personality description from instance settings |
| `{self}` | The selected role's `self.txt`, or empty for a generic Person |
| `{other}` | The selected role's `other.txt`, or the generic fallback |

For each incoming message, code builds the page from the role, personality, compacted summary, retained conversation and new message. Earlier retained assistant turns include their recollections and private thoughts. Recognition and memory lookup can add new first-person recollections. The model continues the thought opener; code appends that thought and then asks the model to continue the speaker opener for the reply.

Recognition, lookup, remembering, memory formation and compaction are separate instructed model requests. Their outputs become contextual prose or stored memory; they never change the prompts. All generation uses OpenAI-compatible Chat Completions. Some context headings and the selection, ordering and stopping rules live in `Hearing.cs`, `Self.cs`, `MemoryKeeper.cs` and `Conversation.cs`.

Personality, role text, model settings and runtime allowances load at startup. Prompt edits do not change the agreed personality-preparation trigger and do not rewrite earlier conversations, thoughts, summaries or memories.

An instance writes `last-page.txt` in its workspace after a reply: the assembled speech page and reply. Channels shows the streamed prose of the individual steps.

## Code entry points

- `src/Humans.Prompts/PromptStore.cs`: the shared catalog (live sets, publications, restore, new versions) and its schema.
- `src/Humans.PromptTools/PromptTools.cs`: the MCP tools.
- `src/Humans/LivePrompts.cs`: a person reading its version's live set and recording what it used.
- `src/Humans/Page.cs`: substitutions and page rendering from the pinned set.
