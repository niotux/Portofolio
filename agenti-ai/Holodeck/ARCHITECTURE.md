# Holodeck: agents in a room

A project is a room. You pick agents from a library, put them in the room, gather them (and yourself) into channels, and start them. They talk like people in a chat app and hand each other real files. You can read every conversation and talk wherever you are wired in.

## Concepts

| Concept | What it is |
| --- | --- |
| Library | A local folder or ZIP in `libs/` containing `Library.json`, `agents/`, and `teams/`. Dependencies reference exact versions of other libraries present there. |
| Library agent | A program described by `agents/<folder>/Agent.json` within a library. Any runnable implementation; the app reads launch instructions only. |
| Team | A reusable template that places stopped agents and their channels into a room. |
| Agent | A placed library program with its own process, name, table, settings and running state. It runs as its own private user in its library's environment for the project. The same definition can be placed several times. |
| You | A member of every room. No program; the Conversations applet is how you speak. |
| Channel | A named conversation with an explicit member set, which may include you. Everything posted reaches every other member. Wires from before channels became legacy channels with the same ids. |
| Packet | One message in a channel: a note (plain text) plus any number of files. |
| Table | An agent's own directory. Nothing appears there unless the agent made it or someone sent it. |

Holodeck never knows what an agent is made of. An LLM is something an agent may use inside itself, configured in its own settings.

What the agents are for: [the writers' room](docs/writers-room.md), a team of Base Persons with specializations working a shared board with the user.

## Working in a project

The page boots into a **main menu** under the Holodeck title: projects and folders, recent projects, global libraries, and Appearance, with a rail link back to the PC's landing page (`/` on the same host name). Choosing a project boots it: its windows (anchored positions, applet state) come back from the server (`project_workspaces`), and `?project=<id>` makes reload return to it. One appearance (theme, accent, backgrounds, backdrop) covers the main menu and every project. The server stores it (`appearance`, `/api/appearance`) so every browser shares it; each browser keeps a copy only to paint the theme before the server answers, and a browser holding an appearance from before the server stored one hands it over once. The LCARS theme restyles the whole shell with the shared LCARS library, which the page loads from the PC's front server (`/lcars/` on the same host name) only while LCARS is selected; `lcars-theme.css` maps AStudio's elements onto it. In LCARS the library's synthesized console cues sound for presses, windows, the start menu and errors (`sounds.js`), with a mute toggle and volume slider on the taskbar kept in the browser; other themes are silent. Inside a project, the start menu lists Channels, Team, Services, Library Explorer and Appearance, then the attached libraries' applets below a divider; the LCARS rail's top block (the taskbar in other themes) returns to the main menu, and the taskbar shows that project's windows. Services shows each declared library service of the project, its state, user and log, and holds its sudo grant, as Team does for agents. On a phone (narrow screens) the workspace becomes one applet at a time: a project home lists the open applets, every start-menu entry and Main menu, an applet fills the screen with Back and Close in its title band, and Back, the Home pill or the device's back gesture returns to the project home. Applets with a list and a detail show one of them at a time there, with a pill back to the list. The phone layout never changes the saved desktop layout. A project opens with exactly the windows it had when last used (none for a new project); every change is saved immediately.

Windows sit on a 16 px grid. A window touching another (edge to edge, as Arrange leaves them) is tiled: it and the windows it touches store their edges as fractions of the desktop, so desktop size changes scale the group and shared edges stay shared; tiled windows may shrink below the usual minimum to stay joined. A window on its own is stored anchored: on each axis a window within two cells of an edge sticks to it, a window touching both edges stretches between them, and any other window is anchored to the nearest third (horizontally) or half (vertically). Desktop size changes, from the browser or a theme, only redraw; only a mouse or keyboard move, resize, Arrange, or maximize saves.

Windows of a project share a selection. Selecting an agent in Team highlights its channels in Channels and switches to one that includes it (its channel with just you first); selecting a channel highlights its members in Team.

**Channels** lists every channel. You can read any of them and post only where you are a member (Join adds you). A channel's header edits its members, renames, deletes, and pauses it. **Team** owns each agent's Overview, Channels (the channels it is in), Settings, Workspace, and Log. **Library Explorer** in project context owns library references. Removing an agent removes it from every channel; history and files stay. Drafts stay with their channel while the window is open, not across reloads. Windows learn of changes through one WebSocket per tab and project (`/api/projects/{id}/events`, notices `room` and `activity`), then reload what they show. Nothing holds a long-lived HTTP request: browsers allow only a few HTTP/1.1 connections to a server, and once open streams fill them every request in every tab waits.

## How agents run

Every library that has agents declares `Runtime`. Each project gets one persistent environment container per exact library version it references (`holodeck-library-<id>`, from the trusted image `holodeck-library:1`: Debian with Python, uv, gcc/make, git, curl, Node, ffmpeg). A trusted supervisor inside it is the only root process. Every declared service process and every placed agent has its own non-root user with a private home and credentials; it cannot read another user's files or credentials, signal another user's processes or gain privileges. The one exception is sudo, which the user grants to a single agent or service: that process may then run commands as root inside its environment, never on the host, in Docker or in another project. Ordinary child processes inherit their owner's user. The layout is ordinary Linux:

| Inside | What it is |
| --- | --- |
| `/home/<user>` | Each user's private home, kept on disk. A service starts there. |
| `/srv/<agent>` | An agent's table, where it starts. Root owns `/srv`, so an agent cannot swap its table for a link to someone else's files. |
| `/var/lib/holodeck` | The supervisor's state: registrations, logs and agents' read-only settings. |
| `/etc/holodeck/config.json` | The environment's configuration and private service tokens, root only. |
| `/tmp` | Ordinary shared temporary files. |
| `/library`, `/dependencies/<libraryId>` | The library and each direct dependency, read-only. |

An agent gets its computer when it is placed: its user `agent-<memberId>`, its home, its table and its settings. Holodeck's own `table/` and `home/` paths for the agent link into that storage, which grants Holodeck's group access for its file APIs. The agent runs `Launch.Command` with `Launch.Args` in its table (`${Agent}` is its folder under `/library`, `${Library}` is `/library`, `${Dependency:libraryId}` names a declared dependency root). Relative launch programs such as `./run.sh` resolve within its folder; other arguments are literal.

Environment: `HOLODECK_URL` (`http://holodeck:8081`), `HOLODECK_KEY` (its private key, authorized while running and revoked on stop), `HOLODECK_TABLE`, `HOLODECK_SETTINGS`, `HOLODECK_PROGRAM`, `HOLODECK_LIBRARY`, `HOME`. Nothing of Holodeck's own environment is passed on. User-level installs (pip, uv, npm) live in the home and survive restarts; **Reset environment** in Team empties the home and keeps the table.

Start, Stop, an exit and Delete affect only that agent's process tree. Output goes to the agent's log, including what it wrote just before exiting. An agent that exits on its own is shown as crashed and is not restarted. Create, Start, Stop, Delete, Reset environment and exit handling are serialized per agent; Stop waits for a pending start, and shutdown waits for pending lifecycle work without stopping agents. Deleting the last agent keeps the environment.

Project activation starts the declared services of every referenced runtime library, independently of agents and windows. Activation intent persists across platform restarts. Reopening a project reconnects rather than duplicating processes; browser closure and switching projects do not stop execution. Holodeck shutdown leaves environments and their processes running; startup reconnects and leaves stopped agents stopped. Explicit project stop ends its environments; on a later host startup services can restart while agents stay stopped until started. One library's failure is reported without blocking the project. Removing a library reference stops its environment without deleting agent identities or data. Project deletion removes its environments, including detached ones, and keeps files on disk.

Resource budgets (memory, CPU, processes) apply to each environment container; workload users also have process and open-file limits. Users in one environment share its network namespace, so service endpoints authenticate with the per-process `HOLODECK_SERVICE_TOKEN` and use distinct ports.

**Network.** Agents have open network access: the internet, the LAN and anything else their network can route to. Environments are on `holodeck-network` for that and on `holodeck-agents`, a network with no route out, to reach Holodeck's agent port. Port 8081 serves only the agent API, and the user port refuses the agent network. Holodeck's database and the socket proxy that is its only way to Docker are on networks agents never join, and everything Holodeck publishes is on `127.0.0.1`, which a container cannot reach. What Holodeck itself provides to every library goes through the agent API, so far only its pgvector database.

**Grants.** Team's **Computer** tab shows an agent's library services, the ports its `Agent.json` `Needs` requests, and its disk use. Published ports and host folders (`room_member_grants`, validated against `AGENT_FOLDER_ROOT`) are recorded but cannot be granted yet: placement with grants and later grant changes are refused. Per-process grants are planned in [the roadmap](WORK_PLAN.md#library-environments-and-applets).

## Library applets

An `Applets` declaration contributes its own HTML/JavaScript content inside a native Holodeck window. The content is sandboxed with an opaque origin; a small capability bridge supplies theme, saved window state, scoped HTTP and WebSocket connections to the library's own services, and optional explicit-close cleanup. Per-service private credentials remain in platform/backend code. Browser cross-origin mutation requests cannot operate the user API. Built-in applets remain on their existing implementation.

`holodeck.utilities` is the first applet library. Its Bash manager creates one shell for each Bash window. Closing that window explicitly ends the shell and its terminal jobs. Reloading or closing the browser disconnects the view and preserves the saved window's shell. A new window creates a different shell. This ownership belongs to the Bash application; other applets choose their own backend and lifecycle. Shells execute inside the project's Utilities environment, not on the host. See [the contract](agents/README.md#library-runtime-and-applets).

## The agent contract

Every call carries `Authorization: Bearer <key>`. Agent identity includes its stable `memberId`, and each channel includes `participants` with stable member IDs and display names while retaining the older name lists. Deliveries include `packetId`, `senderId`, and original `sentAt` as well as channel/name/content/file fields. Renames affect display labels; restart redelivery preserves the original packet identity and time. These transport identifiers support attributed agent memory without adding cognitive concepts to Holodeck. An optional sender-chosen idempotency key makes an uncertain send safe to retry.

- `GET /api/agent/me` → `{ name, projectId, peers, channels: [{ channelId, channel, members }] }`. `peers` are everyone it shares a channel with; `members` are the other members' names.
- `GET /api/agent/next?wait=<0..60>` → the next packet `{ channelId, channel, from, note, files }`, waiting up to `wait` seconds, or 204. `files` are paths on its table where the files were placed (`received/<sender>/<name>`).
- `POST /api/agent/send` with `{ channel, note, files }` → 201. `channel` is a channel id (stable across renames) or its current name; the agent must be a member. `files` are paths on its own table; paths outside the table and symlinks are refused. A paused channel refuses agent posts with 409. The legacy form `{ to, note, files }` posts to the channel the agent shares with just that member. An optional `idempotencyKey` (up to 128 characters, scoped to the sender in its project and kept across restarts) is stored with the packet and a fingerprint of its resolved channel, note, and stored attachment bytes. A retry with the same key and content returns the committed packet with 200 and enqueues nothing; different content under that key is 409. A replay may report a send made before a pause or membership change; new keys need current permission.

- `POST /api/agent/database` with `{ statements: [{ sql, parameters }] }` → `{ results: [{ columns, rows, affected }] }`. The agent's own database: each agent instance has its own schema and login role in Holodeck's PostgreSQL (pgvector installed), created on first use, so it sees only its own tables, never Holodeck's or another agent's. Statements run in order as one transaction (all or nothing), within the statement, row and time limits named in `AgentDatabases.cs`. Parameters are positional (`$1`); strings are sent untyped so PostgreSQL infers them, and number arrays arrive as `double precision[]` (cast `$n::vector` for embeddings, `$n::bigint[]` for ids). Vector columns come back as number arrays. A refused statement is 400 with the error. `/me` lists `database-v1`. Room side: `POST /api/projects/{p}/room/agents/{id}/database` runs read-only SQL as that agent (inspection), `DELETE` erases it (agent stopped). Deleting the project drops every agent's schema and role.

Agents decide whether a packet needs an answer; nothing forces a reply. Pausing a channel is how you stop two agents that keep answering each other.

Delivery rule: asking for the next packet means the previous one is done. Stopping or crashing clears its delivery session, so starting the same agent receives its unfinished packet again. Reconnecting Holodeck to a still-running agent preserves its active delivery. Packets to one agent arrive in the order they were sent to it, across all its channels. A sent file is a copy: later edits on either side do not leak across.

## Storage

PostgreSQL holds projects, folders, room members, channels and their members, packets, per-recipient deliveries, and packet file records (`room_*` tables), plus each project's saved workspace (`project_workspaces`). Each agent instance's own data lives in its own schema `agent_<memberId>` (owned by a role of the same name, logins in `agent_databases`), reached only through the agent database API. Files live under `data/`:

```text
libs/<folder>/ or libs/<name>.zip               local libraries (sole package/dependency source)
data/library-cache/<hash>/                     ZIP extraction cache
data/rooms/<project>/environments/<id>/control/  one library environment's configuration and Holodeck's registrations
data/rooms/<project>/environments/<id>/data/     its home/, srv/ and state/ (/home, /srv and /var/lib/holodeck inside)
data/rooms/<project>/agents/<agent>/table/      link to the agent's table in its environment
data/rooms/<project>/agents/<agent>/home/       link to the agent's home in its environment
data/rooms/<project>/agents/<agent>/settings.json
data/rooms/<project>/agents/<agent>/log.txt
data/rooms/<project>/packets/<packet>/<file>    the copy of each sent file
```

Deleting a project removes its environments and room records and drops its agents' schemas and roles; its files on disk stay.

## Limits

Named in `src/Holodeck.Web/Rooms/RoomLimits.cs` (notes, files, waits, history pages, logs, names, settings) and `src/Holodeck.Web/AgentData/AgentDatabases.cs` (agent database calls).

## Not yet

Agents on other machines (the contract allows it; uploading files over HTTP does not exist yet), GPU grants, disk quotas, export/import of rooms, Claude Code and ComfyUI agents.

## Local library packages

Holodeck Humans owns the reusable Person implementation, core prompts and runtime allowances. Show Writing depends on Humans and supplies writing and bench role assets. This is ordinary package composition: Holodeck mounts declared dependency files without knowing what the program implements.

Format and examples: [agents/README.md](agents/README.md). Manifests use PascalCase properties; `agents/` and `teams/` are lowercase. Contents outside required manifests/directories are unrestricted. There are no downloads, registries, setup hooks, or runtime installers. Room members pin the library ID/version and agent ID in their stored reference; existing legacy references remain valid. Teams place ordinary agents and channels transactionally, with independent settings and all processes stopped. The catalog reports unavailable libraries and dependency errors while retaining unrelated valid packages.

## Person design

Humans is an independent library repository at `libs/holodeck.humans/`, owning its implementation, tests, prompts and [system blueprint](libs/holodeck.humans/HH-0.1.1/BLUEPRINT.md). Its [prototype roadmap](libs/holodeck.humans/HH-0.1.1/WORK_PLAN.md) follows challenge–test–note–improve. Each HH-X.X.X agent version remains runnable with frozen operating assets and a matching source tag; existing instances are never automatically upgraded. Optional cognitive mechanisms belong to its experiment backlog. Holodeck supplies generic agent capabilities and remains unaware of those internals. [The page-based implementation](docs/show-writing.md) remains the retained baseline while the new body loop is built.

## Application direction

The [creative agent](docs/creative-agent.md) owns its notebook, prompt/workflow revisions and ComfyUI jobs in its library and private agent storage. It uses the standalone AI gateway for GPU phases. Generic media presentation and permissioned reference sharing can extend Holodeck; inference and creative decisions remain outside the platform. Its first anime image workflow is implemented; realistic generation, direct reference conditioning and video remain planned.

The main menu boots projects (see Working in a project). Library Explorer is a global local-library browser or a contextual reference manager: attached libraries appear first, other available libraries second. Adding a reference does not create agent instances. Removing a reference does not delete instances or their data. Team creates instances from attached definitions and owns agent lifecycle, settings, workspace, logs, and the list of the agent's channels. The earlier unified room and three-applet split are superseded as UI direction.

`project_libraries` stores exact project references, with library name/description snapshots. Attachment includes the selected local dependency closure. Removal stops newly unresolved agents and revokes their sessions. Room views expose an error while retaining the member ID, settings, workspace, channels, and packets. Reattachment resolves those instances without starting them. Changing the definition of an existing agent is refused because its environment registration fixes its library and launch; reattaching the original library repairs the instance. Unresolved members can be explicitly removed individually or together, which also removes their environment registrations. Existing packaged members are backfilled once per project, including their available dependencies. A migration marker prevents reattaching a deliberately removed library on subsequent startup.

Library Explorer uses separate name/version elements, never inline agent/team summaries. Agents, Teams and Applets have distinct tabs with separate count badges; in project context an attached library's applet opens from its Applets tab as well as the start menu. No duplicate library entry points, duplicate connection shortcuts, or metadata separators using centered dots or long dashes belong in the active UI.

## Channels

A post is stored once with its files and enqueued for every current member except the sender, in each recipient's single ordered queue. Removing a member stops later deliveries; packets already queued are still delivered. Received files go to `received/<channelId>/<sender>/`; legacy channels keep `received/<sender>/`. The one-time migration (`room_migrations`) turned each wire into a legacy channel with the same id, its history, queue positions and delivered paths; a disconnected wire became a channel with no members. `room_wires` is kept but no longer written.

## Shared library storage

Holodeck provides an isolated PostgreSQL schema and role for each exact library package, alongside private per-agent schemas. The generic library-database endpoint authorizes a running agent against its own package or directly declared exact dependencies. The library owns its tables and business behaviour; Holodeck supplies transaction execution and isolation only. Shared data survives instance/project deletion and reference removal. Humans uses it for version-scoped prompt catalogs while retaining conversations and memory in private instance schemas.
