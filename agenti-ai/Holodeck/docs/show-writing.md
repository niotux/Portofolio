# Person and the writing roles

This describes the retained page-based baseline and writing integration. The independent Humans [blueprint](../libs/holodeck.humans/HH-0.1.1/BLUEPRINT.md) and [prototype roadmap](../libs/holodeck.humans/HH-0.1.1/WORK_PLAN.md) define the next direction through live experiments.

Person belongs to Holodeck Humans: source in `libs/holodeck.humans/HH-0.1.0/src/Humans/`, package in `libs/holodeck.humans/`. Humans owns the `Person` executable, core prompts and runtime allowances. Its `person` definition is a generic person with an editable personality and no authored role or backstory.

Show Writing (`libs/holodeck.show-writing/`) depends on Humans and owns the writing collaborator and bench roles: their agent definitions, settings, biographies and backstory memories. Each role launches Humans’ Person with its role name and the Humans asset root. Model behavior and persistence are shared; no agent inheritance is involved. Every value that tunes behaviour lives in Humans’ `runtime.json` or the instance settings, never here.

## Rules it keeps

- Every model input and output is prose. No JSON, schemas, fields, markers or tool calls go to or come from the model; JSON is only HTTP transport.
- Native model thinking is always off. The person's thinking is bracketed prose on the page.
- Only formed memory prose is embedded. Raw messages are never a search corpus; there is no keyword fallback.
- Identifiers, packet ids and other storage metadata stay in code and the database, never in what the model sees.

## Storage

Everything lives in the instance's own schema of Holodeck's PostgreSQL, reached through `POST /api/agent/database` (see ARCHITECTURE.md). Tables:

| Table | Holds |
| --- | --- |
| `received` | Incoming messages, stored before the next one is requested |
| `turns` | The chat per channel; each reply with the thought before it and what was recalled for it |
| `sessions` | Each channel's compaction summary and how many turns it replaced |
| `outbox` | Replies waiting to be sent, with their idempotency keys |
| `forms` | Exchanges waiting to become memories (durable queue with drafts and attempts) |
| `memories` | Prose memories with their source exchange and a pgvector embedding (HNSW cosine index). Backstory seeds from `roles/<role>/memories.txt` are memories too |

## Prompt files and assembly

Person uses ordinary editable files with Git history. [The prompt guide](../libs/holodeck.humans/prompts/README.md) explains every file, when each pass runs, which inputs are substituted, and when edits become visible. The application assembles fresh context for each request; model-generated recollections, thoughts and summaries never rewrite the prompt files.

## Startup

Read instance settings and Humans’ `runtime.json`, require Holodeck's send and database capabilities, create the tables, check the embedding service, re-embed memories made with a different embedding model, seed the backstory when its file changed, then run the conversation loop and the memory keeper side by side.

## One message

1. **Recognise.** Only on the first message of a new chat: memories formed in this sender's other chats are rewritten (`recognise.txt`) into what the person remembers about them.
2. **Look.** An instructed pass (`look.txt`) over the latest lines names what is worth trying to remember. (Pinned for review.)
3. **Recall.** Each thing is embedded as a query and searched with pgvector, leaving out memories whose exchange is still on the page and memories that came back recently.
4. **Remember.** What came back is rewritten (`remember.txt`) as the person's own remembering, or nothing if it is not about the thing.
5. **Think.** The page is continued on llama's completion endpoint after `[<name> thinks:` until the bracket closes.
6. **Speak.** The page is continued after `<name>:` until the next line of the page. A reply that copies a line already in the chat is spoken again; an empty reply means the person stays silent.
7. **Save.** One transaction stores both turns, the handled receipt, the outbox entry and the formation job; then the reply is sent.

The page (`prompts/page.json` labels): a header, who the person is (`roles/<role>/self.txt`), what they are like (personality setting), the compaction summary, then every kept turn with its recalled lines, thought and spoken line. There is no system prompt, user or assistant on it.

## After the reply

- **Memory keeper** (background): claims a formation job, writes the memory (`form.txt`) from the exchange and the turns before it, embeds it and stores it. Failures keep the draft and retry.
- **Compaction:** when the last call fills the configured share of the context, older turns are summarised (`compact.txt`) into the summary line; the most recent turns stay verbatim.

## What you can see

Each step streams into the Thoughts card for that message in Channels. The agent log shows every recall candidate with its similarity. `last-page.txt` in the workspace holds the last full page. The room API reads the agent's database read-only (`POST /api/projects/{p}/room/agents/{id}/database`).

## Publishing and existing instances

Run `bash agents/ShowWriting/publish.sh` to explicitly publish the development Person that writing agents launch through their Humans dependency. New HH releases are published independently and retained side by side. It also generates Show Writing's copies of the executable, prompts and runtime configuration. `agents/ShowWriting/publish.sh` forwards to the Humans publisher. Edit core assets in Humans; the writing compatibility copies are generated.

Writing instances use the declared Humans dependency directly. Existing instances keep their computer, identity, settings and memory; an explicit restart loads the newly published runtime. Moving Person into Humans does not reset conversations or change the thinking and memory design.
