# Local agent libraries

Place a library folder or ZIP directly in `libs/`. Holodeck discovers only local packages and resolves dependencies only there. There is no registry, package-manager integration, or setup hook, and Holodeck downloads nothing for a library. Use Refresh in the Library applet after changing the folder.

```text
libs/
  my-library/
    Library.json
    agents/
      writer/
        Agent.json
        main.py
        settings.json
    teams/
      writing-room/
        Team.json
    ...anything else...
```

`Library.json`, lowercase `agents/` and `teams/`, and each definition's `Agent.json` or `Team.json` are the format. Both top-level directories must exist, even when empty; other contents are unrestricted. Keep the empty directory entries when creating a ZIP. A ZIP has `Library.json` at its root. JSON property names use PascalCase and are case-sensitive. Folder names do not determine identity; manifest IDs do.

## Library.json

```json
{
  "FormatVersion": 1,
  "Id": "example.writing-kit",
  "Version": "1.0.0",
  "Name": "Writing Kit",
  "Description": "Agents and teams for writing.",
  "Dependencies": [
    { "Id": "example.utilities", "Version": "1.2.0" }
  ],
  "Runtime": {
    "Image": "holodeck-library:1",
    "Processes": []
  }
}
```

Versions match exactly; no ranges or automatic upgrades. Different versions can coexist. IDs and versions contain ASCII letters, digits, dots, underscores, or hyphens and cannot begin with a dot. References are case-sensitive. Missing dependencies, cycles, duplicate identities, and invalid manifests make the affected library and its dependents unavailable. Library shows diagnostics. Unrelated valid libraries remain usable.

### Runtime

`Runtime` is required for a library that has agents, and optional for an applet-only library. Each project runs one persistent environment container per exact library version it references. `Image` must match the trusted runtime image configured by the installation (Debian with Python, uv, gcc/make, git, curl, Node, ffmpeg); a library cannot substitute an image that changes the privileged supervisor. Workloads install anything else into their own homes. The library is mounted read-only at `/library`, and each direct dependency at `/dependencies/<libraryId>`.

`Processes` declares the library's services, which may be empty: `Id`, literal `Command` argument array, optional unprivileged `Port` and optional readiness `HealthPath`. Service ports must be distinct within the environment. Each service runs as its own non-root user named by its `Id`, starting in its private home `/home/<Id>`, with a private `HOLODECK_SERVICE_TOKEN`. HTTP/WebSocket services must validate that token as a Bearer credential; Holodeck injects it when proxying applet requests, and browser applets never receive it. Service failures are surfaced rather than blindly submitting work again. Dependencies' services are not shared.

Loading a project activates its referenced runtime libraries, including libraries with no agents or open windows; activation is idempotent. Project execution intent survives Holodeck restart, and Holodeck reconnects to running environments without restarting their programs. Browser disconnect or switching projects does not stop execution. An explicit project stop ends its environments. On host reboot, a project whose intent was running is reactivated when Holodeck starts; services restart, while stopped agents stay stopped. Removing a reference stops that environment and unavailable dependents while retaining data and agent identities. Deleting a project removes its environments, including detached ones, while retaining disk files.

A `Services` declaration is refused with a diagnostic; services are `Runtime.Processes`.

Every library can also count on what Holodeck itself provides, without declaring anything: each agent instance has its own PostgreSQL schema with pgvector (`database-v1`).

## Agent.json

```json
{
  "Id": "writer",
  "Name": "Writer",
  "Description": "Drafts text from a brief.",
  "Launch": {
    "Command": "python3",
    "Args": ["${Agent}/main.py"]
  },
  "Settings": "settings.json",
  "Needs": { "Ports": [8188] }
}
```

An agent is any program that runs on its computer: its own non-root user in its library's project environment, created at placement and kept until deletion. It runs in its table (`HOLODECK_TABLE`), with its home in `HOME`. Start and Stop control only its process tree, and Holodeck restart reconnects without restarting it. A native binary can use `"Command": "${Agent}/bin/writer"`; a bare command is found on `PATH`, and a relative path resolves within the agent's folder. Arguments remain literal except for `${Agent}` (the agent's folder under `/library`, read-only), `${Library}` (`/library`, the library root, read-only), and `${Dependency:libraryId}` (the declared dependency root). Commands execute directly without an implicit shell. Anything else the agent needs it installs itself as a user (pip, uv, npm) into its private home, which survives restarts until **Reset environment**. The user may allow an agent or a service sudo; a library cannot request it. System-wide installs made through sudo live in the container and end when the environment is recreated.

Every directly declared dependency is mounted read-only at `/dependencies/<libraryId>`, resolved to the exact version in `Library.json`. `${Dependency:libraryId}` expands to that root in launch commands and arguments. An undeclared or malformed dependency placeholder makes the library unavailable. Only declared direct dependencies are exposed; dependencies do not give access to the whole local library folder or share their services. For example, Show Writing launches `${Dependency:holodeck.humans}/runtime/Person` and keeps its own role assets under `${Library}`. There is no implicit agent inheritance. An agent's environment registration fixes its library and launch command.

`Needs` is optional: inbound ports the agent would like. They are requests shown in Team's Computer tab. Published ports and host folders cannot be granted yet; placement with grants and later grant changes are refused. Agents have open network access, so the addresses of anything they use belong in their settings.

`Settings` is optional. When present it names a file relative to the agent directory; paths to shared files elsewhere inside the same library are allowed. Its contents are copied to each placed agent, independently editable in the room. Omitted settings produce `{}`. Existing room settings are never overwritten by catalog refreshes.

`SettingsSchema` is optional and follows the same path rules. With it, Team shows a form for each instance's settings and the server validates every save; the library's `Settings` must already satisfy it. Without it, settings stay free text, as before. `Actions` optionally names a management-action declaration. Holodeck validates and loads it, renders its forms in Team's Settings tab, and provides durable job enqueue, claim, progress, completion, failure and cancellation APIs. The agent executes the declared work; canceling a job must also be handled by the agent's worker.

### Settings schema profile (version 1)

A small, explicit part of [JSON Schema](https://json-schema.org/understanding-json-schema/). Anything outside it, including an unknown keyword, makes the library unavailable with a diagnostic, so a schema is never enforced only partly.

| Field type | Keywords |
| --- | --- |
| every field | `type` (exactly one of the types below), `title` (label), `description` (help), `$comment`, `x-ui` |
| `object` | `properties`, `required` (declared names), `additionalProperties` (`true` or `false`, always declared) |
| `string` | `minLength`, `maxLength` (counted in characters), `enum` (strings only), `default` |
| `number`, `integer` | `minimum`, `maximum` (inclusive), `default` |
| `boolean` | `default` |

The root is an `object` and may also carry `$schema`. Arrays, `null`, `pattern`, `format`, exclusive bounds, `$ref`, `$id`, and composition keywords (`allOf`, `oneOf`, ...) are not supported. `default` is shown as a suggestion and must satisfy its own field; it is never written into an instance's settings. `additionalProperties: true` keeps values the form does not show, exactly as stored; `false` refuses them on save. Limits: 64 KB per schema, 8 levels of nesting, 256 fields, 100 enum values, 2,000 characters of title or description.

`x-ui` holds presentation hints only: `order` (an object's property names, listed first in that order), `group` (a heading that gathers sibling fields), and `control` on strings (`multiline` or `password`; password only hides typing on screen, the file is not a secret store). Labels and help are shown as plain text. Hints cannot add scripts, HTML, links, requests, or commands.

The form edits only the fields it changes; other values, key order, and number text stay as stored. Each save names the settings revision and schema revision it was opened with, and a stale save is refused rather than replacing newer content. If the stored text is not a JSON object, Team offers a repair editor checked against the same rules. An instance whose definition is unavailable keeps its stored settings as text until the definition returns.

## Team.json

```json
{
  "Id": "writing-room",
  "Name": "Writing Room",
  "Description": "A writer and a researcher.",
  "Members": [
    { "Id": "writer", "Name": "Writer", "Agent": "example.writing-kit/agents/writer" },
    { "Id": "researcher", "Name": "Researcher", "Agent": "example.utilities/agents/researcher" }
  ],
  "Wires": [
    ["You", "writer"],
    ["writer", "researcher"]
  ],
  "Channels": [
    { "Name": "Writing desk", "Members": ["You", "writer", "researcher"] }
  ]
}
```

Teams are templates. Adding one creates ordinary, editable room members and channels; it does not create another running process. Each wire becomes a two-member channel, named after the agent when one end is `You` and `A & B` otherwise. Each entry in `Channels` creates a named channel with those members (at least two, `You` allowed). Taken channel names get a numeric suffix. All added agents start stopped. Members can reuse the same agent definition. Member IDs must be unique (ignoring case), and `You` is reserved for the room's human participant. Wire endpoints match member IDs exactly. Self-wires, duplicate wires, unknown endpoints, and repeated channel names are invalid.

Agent references use `libraryId/agents/agentId`. They can point to the containing library or a directly declared dependency; the dependency declaration selects the exact version. Room members store the resolved `libraryId/version/agents/agentId`, so installing another version does not switch existing instances. Repeated team placement creates independent instances, adding numeric suffixes to occupied display names. Placement validates and copies settings, then creates members and channels in one transaction; failed placement rolls back new records and directories.

## Discovery and storage

`Studio:LibrariesPath` configures the source folder (`libs` by default, mounted as `/libs` in Compose). ZIPs extract into a content-addressed cache under `data/library-cache/`; cached packages are never discovered independently of their source ZIP. Removing a source removes it from the catalog, and existing members cannot start until their referenced library returns. Do not edit a released version in place; use a new version for changed programs. Folder contents are read as installed, rather than snapshotted.

ZIP extraction rejects traversal, duplicate entries, and links, preserves ordinary Unix file permissions, and limits archive entries and expanded size (named in code). A copied ZIP can carry native binaries; compatibility remains the library author's responsibility.

Library Explorer lists libraries by name and version as distinct fields. Open one to switch between separate Agents, Teams and Applets tabs; their counts use separate badges. In project context an attached library's applet can be opened from its Applets tab. In project context Add/Remove changes a library reference, not agent instances. Team creates instances only from attached libraries. Removing a reference preserves affected instances as unresolved; reattach their original library to repair them. Replacing an existing agent's definition is refused because its environment registration fixes its launch.

## Environment

The working directory is the agent's own table. `HOLODECK_KEY` stays with the agent: Stop revokes its authorization and Start authorizes it again. Stopping or crashing clears the delivery session so unfinished work is redelivered after Start; reconnecting Holodeck to a running process preserves its current delivery. `HOLODECK_URL`, `HOLODECK_KEY`, `HOLODECK_TABLE`, `HOLODECK_SETTINGS`, and `HOLODECK_PROGRAM` keep their existing meanings; packaged agents also receive `HOLODECK_LIBRARY` with their library root. Details in [ARCHITECTURE.md](../ARCHITECTURE.md).

## The loop

`GET /api/agent/me` includes your stable `memberId`. Each channel retains its `members` display-name list and also includes `participants` with `{ memberId, name, isYou }` for every active participant, including you. Use IDs for identity and names for display; renaming an agent does not create a new person.

Every delivered packet includes `packetId`, `senderId`, and original `sentAt`, alongside `channelId`, `channel`, `from`, `note`, and `files`. A redelivery after restart retains that ID and timestamp; intentionally repeated messages have different IDs even when their text is identical. Store the packet durably before requesting the next one.

### Retrying a send

`POST /api/agent/send` accepts an optional `idempotencyKey`: 1 to 128 ASCII letters, digits, `-`, `_`, `.`, or `:`. Choose a new key for each message you mean to send, store it with the outgoing message before the first attempt, and reuse it when the outcome of an attempt is unknown (timeout, lost connection, 5xx, restart). Send keys belong to your agent within its project and survive process restarts.

- `201` is a new packet. `200` is a retry of a send already committed under that key; it returns the same packet, and nobody receives it again or gets another copy of its files.
- `409` with a key you already used means the retry differs from what was committed: another channel, note, attachment list or order, or attachment bytes. Attachments are compared with the bytes actually stored, so do not change a file before its send is confirmed.
- A retry reports a committed send even if the channel was paused or you left it since. A new key always needs the current permission; a refused attempt stores nothing.
- Without a key, every POST is a new send, as before.

This guarantees at most one accepted packet per key. It does not make your own model calls or tool work exactly-once; record what you decided to send before sending it.

```text
loop:
  GET  /api/agent/next?wait=30        (Authorization: Bearer $HOLODECK_KEY)
  204  → ask again
  200  → { channelId, channel, from, note, files }; files are already on your table
         do the work (or decide the packet needs no answer)
         POST /api/agent/send { channel: channelId, note: "...", files: ["result.png"], idempotencyKey: "optional" }
```

- Asking for the next packet means you finished the previous one. If you crash, you get it again after a restart.
- If Holodeck is unreachable or answers 5xx, wait and retry (back off up to ~15 s). Do not exit: Holodeck may be restarting.
- 401 means your key was revoked (you were stopped); exit.
- Print what you are doing. Standard output and error go to your log in the room.

The agent identity response lists `capabilities`: `idempotent-send-v1` when the optional send key is enforced, and `database-v1` when the agent database (`POST /api/agent/database`, see ARCHITECTURE.md) is available. A durable outbox may retry ambiguous outcomes with its original key only after seeing this capability; older servers may ignore unknown request properties. The Person foundation holds uncertain sends when the capability is absent and retries confirmed keyed sends through host backoff.

## Shared library database

Agents supporting `library-database-v1` can call `POST /api/agent/library-database/{libraryId}` with the same SQL request shape and transaction limits as their private database. Authentication and running-state authorization are unchanged. A caller may use its own exact library package or a directly declared dependency resolved to its exact version; arbitrary and transitive library access is refused. The user's own tools reach the same data through `POST /api/libraries/{libraryId}/{version}/database` on the user port, for an installed exact package; agents cannot use that route.

Every instance using the same exact library package shares that library's isolated schema, including across projects. Other library versions, private agent schemas and Holodeck's tables remain isolated. The library owns its schema and the meaning of its contents. Removing an agent, project or library reference does not delete shared library data. This is shared storage, so library code must handle concurrent edits and replay deliberately.

## Library runtime and applets

A library contributes browser windows through `Applets`, independently of agents and teams; retain the required directories even when empty.

```json
{
  "FormatVersion": 1,
  "Id": "example.utilities",
  "Version": "1.0.0",
  "Name": "Utilities",
  "Runtime": {
    "Image": "holodeck-library:1",
    "Processes": [
      {
        "Id": "backend",
        "Command": ["python3", "/library/services/server.py"],
        "Port": 8090,
        "HealthPath": "/health"
      }
    ]
  },
  "Applets": [
    {
      "Id": "tool",
      "Name": "Tool",
      "EntryPoint": "applets/tool/index.html",
      "Services": ["backend"]
    }
  ]
}
```

`Applets` declares `Id`, `Name`, library-relative HTML `EntryPoint`, optional `Description`, glyph `Icon`, `Width`, `Height`, and a list of allowed service IDs in `Services`. `Runtime` can be omitted for frontend-only applets. An applet's files must stay in its entry directory; symlinks and traversal are refused. Applets appear in the attached project's start menu without changing the built-in applet registry.

The native window contains a sandboxed iframe. It can run its own HTML, styles and scripts, but cannot access the shell's DOM or make direct network requests. Use the platform bridge for scoped backend access; Holodeck supplies the window, taskbar, theme values and saved state. Embed the bridge script from the applet asset root at `_holodeck/bridge.js` (for an entry under `applets/tool/`, use `../../_holodeck/bridge.js`).

Bridge operations:

- `holodeck.ready()` supplies saved state, stable window identity and theme.
- `holodeck.setState(value)` and `setTitle(text)` update that window.
- `holodeck.fetch(service, path, options)` returns a browser Response for a scoped HTTP request, including binary content.
- `holodeck.request(service, path, options)` is its JSON convenience form.
- `holodeck.connect(service, path)` opens a scoped text WebSocket.
- `holodeck.ownResource({ service, path, method: 'DELETE' })` optionally registers cleanup on explicit native window Close. Ownership is saved before resource creation; browser teardown does not run cleanup.

The Bash applet in `holodeck.utilities` is a working example. Its one-shell-per-window ownership is application behavior, not a rule for other applets. A conventional desktop GUI program needs a separate remote-display frontend; an iframe renders web content directly.
