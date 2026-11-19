# LCARS theme

## Intent

An Appearance choice that turns AStudio into an LCARS console. It is a full layout restyle, not a recolour: the main
menu and the workspace sit inside LCARS elbow frames, and every part of the UI takes the LCARS shape language. AStudio
keeps its structure and behaviour: the main menu, the taskbar, draggable and resizable windows, the start menu, the
status bar and every applet work as they do in the other themes.

The look comes from the shared LCARS library, the same one the PC's `http://localhost` service directory uses. AStudio
uses it as it is; whatever AStudio needs that the library lacks is added to the library, so the next consumer has it.

## Goals

- LCARS sits beside Dark, Light and System in Appearance and switches instantly, like the other themes.
- Main menu: the rail is an LCARS rail joined by an elbow to a top bar; Projects, Libraries and Appearance are rail
  blocks, the selected one marked; the section's panel sits in the frame's content area.
- Workspace: one frame around the desktop. The top bar is the taskbar: the start button sits at the elbow, open windows
  are pill segments, Arrange is a block. A rail runs down the left edge to a bottom elbow whose bar is the status bar.
- Windows: an LCARS title bar (coloured band with a rounded leading end) and pill window controls; moving, resizing,
  maximizing, keyboard movement and the grid behave as now.
- Applet content: buttons are pills, inputs and selects are LCARS fields, tabs, lists, rows, accordions, dialogs,
  notifications, the start menu and the chat thread follow the library's parts and palette.
- The interface stays fully readable and usable: focus is visible, text contrast holds, reduced motion is respected.

## Non-goals

- No change to applet behaviour, data, API or window geometry rules.
- No new layout for the other themes; Dark, Light and System look exactly as before.
- No sounds, no animated boot sequence.

## Anti-goals

- A recolour that keeps rectangular boxes and calls itself LCARS.
- LCARS that breaks usability: unreadable text, controls that look decorative, layouts that hide content at small sizes.
- Copying AStudio-specific styling into the library, or library parts into AStudio.

## Constraints

- AStudio code style (AGENTS.md); plain JavaScript modules and CSS, no build tools or frameworks.
- The accent and background settings do not apply in LCARS, which uses the library palette; Appearance says so while
  LCARS is selected, and those settings are kept for the other themes.
- Free assets only: the library's bundled OFL font, shapes drawn in CSS, no original LCARS artwork or typeface.
- Existing browser checks keep passing in the default theme.

## Decisions (builder's call, open to the user's correction)

- **Library home.** The library stays in the gateway's `www/lcars/` folder in `/Space/Servers/AI`, served at
  `/lcars/` on the gateway. AStudio's page loads it from the gateway on the host name AStudio was opened with; the
  gateway allows other origins to load its site files. LCARS therefore needs the `ai` stack running, which AStudio's
  agents already need. Library edits show in both on the next reload.
- **How AStudio applies it.** The library stylesheet loads only while LCARS is selected. An AStudio theme stylesheet,
  scoped to the LCARS theme, maps AStudio's own elements onto the library's look; AStudio markup keeps its own classes.
  The few elements a frame needs (elbow corners, rail fill blocks) are added to the page shell and shown only in LCARS.
- **What goes in the library.** Parts any console page could use: window frame and title bar, tabs, form fields and
  selects, menus, dialogs and notifications. AStudio-only arrangements (taskbar contents, window layer, applet layouts)
  stay in AStudio's theme stylesheet.

## Left to the builder

Exact colours per part, segment arrangement and numbering on bars and rails, the small-screen arrangement of the
frames, and how each applet's specific widgets (graph view, chat composer, model forms) adopt the look, judged against
the intent above.
