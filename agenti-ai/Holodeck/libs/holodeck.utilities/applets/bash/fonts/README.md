# Bash fonts

Cascadia Mono provides the terminal's fixed-width cells without programming ligatures. The bundled, unmodified TrueType face comes from the host's Cascadia distribution; its SIL Open Font License is in `Cascadia-OFL.txt`.

Antonio provides the LCARS-style footer and controls. The unmodified WOFF2 file matches Holodeck's `wwwroot/lcars/fonts/antonio.woff2`; its SIL Open Font License is in `Antonio-OFL.txt`.

Both fonts load locally. The terminal waits for its monospace font before measuring the grid and uses the monospace system fallback if loading fails. ANSI application colors remain xterm's defaults.
