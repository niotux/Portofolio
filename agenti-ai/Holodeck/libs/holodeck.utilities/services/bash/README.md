# Bash manager

The manager runs as its own non-root user in the project's utilities environment. It creates interactive Bash processes attached to real pseudo-terminals. Shell children inherit that user's environment and permissions, including sudo when the user granted it to this service; sessions are not separate security boundaries.

Opening a browser connection subscribes to output and replays retained recent output. A session has one terminal size, owned by the connection that last asked for its own size; every connection draws that grid. Retained output is kept in chunks tagged with the size it was printed at, so a replay passes through the same sizes the live terminal did and reproduces its screen. Disconnecting a browser leaves the shell running. Each Bash applet window owns one session; its explicit Close action deletes that session. Reloading or closing the browser preserves it for restoration. Explicitly ending a session sends a hangup to its terminal jobs and then terminates remaining jobs in that terminal session. A command that deliberately detaches from the terminal remains a process owned by the manager user; restarting the library environment ends all its processes.

Shell sessions survive browser disconnection and Holodeck frontend reconnection while the manager process remains running. They do not survive manager or container restart. Home files and shell history persist in the manager's mounted data directory.

The service requires the platform-supplied bearer token for HTTP and WebSocket connections. The browser uses Holodeck's scoped bridge; it receives no service or supervisor token. The shell environment omits platform credential variables.

## Interface

- `GET /health` reports readiness.
- `GET /sessions` returns `{ "sessions": [...] }`.
- `POST /sessions` creates a shell, optionally accepting `windowId`, `name`, `cols` and `rows`. Repeating a creation request with the same `windowId` returns the existing session, so retries cannot create a second shell for that window.
- `DELETE /sessions/{id}` ends and removes that shell.
- `DELETE /sessions/by-window/{windowId}` idempotently ends the window's shell, including when the frontend never received the creation response.
- `GET /sessions/{id}/terminal` upgrades to a WebSocket.

WebSocket client messages are JSON: `input` with `data`, or `resize` with `cols` and `rows`, which sizes the shell for that connection and makes it the owner. The first server message is `replay` with this `connection`'s id, the current `owner` (or null), `cols`, `rows` and `chunks` of retained output, each with the `cols` and `rows` it was printed at and its `data`. Later server messages are `output` with `data`; `size` with `cols`, `rows` and `owner` whenever the size or its owner changes, including null when the owner disconnects; `exit` with `code`; `error` or `disconnected` with `message`; and `closed`. A client applies messages in order, so a size change takes effect after the output printed before it. A completed session remains inspectable until explicitly removed.
