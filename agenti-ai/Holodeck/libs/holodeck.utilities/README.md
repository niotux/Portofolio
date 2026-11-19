# Holodeck Utilities

A local library of project utilities. Attach it with Library Explorer, then open **Bash** from the project's start menu. It does not place any agents.

The library uses one shared environment container for each project and this exact library version. Its Bash manager starts when the project activates and runs as the non-root service user `bash`; shells start in its home, `/home/bash`. Bash runs inside that environment, with its tools and files; it is not a shell on the host machine. To install software system-wide, open **Services** from the project's start menu, select the `bash` service and allow sudo: every Bash window in that project can then run `sudo`. Changing the grant restarts the manager, so open shells end. System-wide installs stay until the environment is recreated; files in `/home` persist.

Each Bash window owns one shell. Opening another window creates another shell. Closing a window ends its shell and terminal jobs. Reloading or closing the browser, minimizing a window, or navigating away disconnects the view without ending the shell. Restoring the saved window reconnects it. Bash variables and current directory remain until the shell exits or its environment stops. Files remain in the library's persistent service workspace.

A window can be open on several screens at once, such as a laptop and a phone. A running program has exactly one terminal size, so the shell takes the size of the screen in use: the one last touched or typed into. Full-screen programs redraw for it. Every screen draws that same grid at its normal font size, never scaled; a screen that does not own the size shows it under a grey veil, without scrollbars, saying the size belongs to another screen; touching it or typing takes the size. Turning or resizing a screen changes the shell only when that screen owns the size. When the owning screen disconnects, a remaining open screen takes it.

The manager owns terminal sessions, real pseudo-terminals, output replay and the shared size. Holodeck supplies the native window, saved window identity, sandboxed content and scoped backend connection. The Bash window registers an optional resource cleanup with the shell; other applets are free to choose their own lifecycle.

Stopping the project/environment ends its processes. Service restart or host reboot cannot preserve a running shell; an open or reopened window starts a replacement when its old session no longer exists.

Source: `applets/bash/` contains the frontend and vendored terminal renderer; `services/bash/` contains the manager. The runtime image and trusted user launcher belong to Holodeck, not this library.
