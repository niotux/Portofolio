# Person prompts

These are the canonical editable prompts for Person. Version them as ordinary text files in Git. There is no separate prompt registry, per-agent prompt revision selector or model process that rewrites these files. Writing roles use this same prompt set.

## Files and when they run

| File | Purpose | When used |
| --- | --- | --- |
| `page.json` | Labels and openers for the person's conversation page, memories, thoughts and speech | Whenever a page or conversation excerpt is assembled |
| `continue.txt` | Ask a chat model to continue the person's page from its open thought or speech | For each thought and reply |
| `recognise.txt` | Turn memories of previous conversations with this participant into a first-person recollection | At the start of a channel conversation, if previous memories of that participant exist |
| `look.txt` | Suggest subjects worth looking up in memory from the recent dialogue and incoming message | Before thinking about an incoming message |
| `search.txt` | Instruction prepended to semantic search queries sent to the embedding model | When embedding a memory-search query |
| `remember.txt` | Turn retrieved memories into a grounded first-person recollection | For each suggested subject that retrieves memories |
| `form.txt` | Turn an exchange into a first-person prose memory | In the background after an exchange is saved; a saved draft is reused on retry |
| `compact.txt` | Summarize the older dialogue while retaining recent turns verbatim | After a reply reaches the configured context threshold, if older turns can be removed |

`page.json` is application configuration. Its values become prose on the page; the JSON object is never a requested model output. Its labels also participate in opening and stopping generations, so changes to brackets and speaker boundaries need corresponding attention in `Self.cs`.

## Substitution and assembly

`Page.Fill` substitutes four literal placeholders in instructions and page labels:

| Placeholder | Source |
| --- | --- |
| `{name}` | Personality name from instance settings, falling back to the instance display name |
| `{personality}` | Personality description from instance settings |
| `{self}` | The selected role's `self.txt`, or empty for a generic Person |
| `{other}` | The selected role's `other.txt`, or the generic fallback |

For example, `[{name} thinks:` becomes `[Ana thinks:`. This substitution does not make a model request.

For each incoming message, code builds the page from the role, personality, compacted summary, retained conversation and new message. Earlier retained assistant turns include their recollections and private thoughts. Recognition and memory lookup can add new first-person recollections. The model continues the thought opener; code appends that thought and then asks the model to continue the speaker opener for the reply.

Recognition, lookup, remembering, memory formation and compaction are separate instructed model requests. Their outputs become contextual prose or stored memory; they do not change the authored prompt files. All generation uses OpenAI-compatible Chat Completions. Thinking and speech send the unchanged prose page as context with `continue.txt` as the instruction; standard stop sequences bound each continuation. Some context headings and the selection, ordering and stopping rules live in `Hearing.cs`, `Self.cs`, `MemoryKeeper.cs` and `Conversation.cs`.

## Editing and loading

Edit the canonical files here. Keep each meaningful prompt change as a focused Git revision with its reason; use Git diffs and history to compare versions. Prompts are tracked independently of generated runtime binaries. Do not edit the generated prompt copies under Show Writing.

For the development Person entry, `page.json` and the instructed-pass text files are reread on use. Released HH agents read their own frozen version directory; editing the development prompts does not change them. Direct Humans instances and new writing instances see subsequent file reads without a rebuild. Reads are not pinned to one snapshot per turn: an edit during a turn can affect a later pass in that same turn. There is currently no stored per-turn prompt revision identifier.

The embedding client reads `search.txt` at startup, so its changes require a restart. Personality, role text and runtime configuration are also loaded at startup. Changing prompt text does not rewrite already retained thoughts, summaries or memories.

Existing writing containers have their original library mount and use generated compatibility copies. Run `bash scripts/publish-legacy.sh /path/to/holodeck.show-writing` from this HH project to explicitly refresh those copies. Text changes then follow the same reread rules; restart when changing startup-loaded inputs or the executable. Publishing does not replace containers or reset memories.

## Inspecting a composed page

An instance writes `last-page.txt` in its `table/` workspace after a reply. It shows the assembled speech page and reply, including retained conversation, recollections and the current private thought. It does not contain the separate utility-request instructions, and it records only the latest page rather than a full request history. Channels shows the streamed prose of the individual activity steps.

## Code entry points

- `src/Humans/Page.cs`: file loading, substitutions and page rendering.
- `src/Humans/Hearing.cs`: recognition, lookup and remembering requests.
- `src/Humans/Self.cs`: page continuation for thoughts and replies; compaction trigger.
- `src/Humans/MemoryKeeper.cs`: background memory formation.
- `src/Humans/Conversation.cs`: compaction input and retained conversation.
- `src/Humans/Program.cs`: startup-loaded settings, role assets and search instruction.
