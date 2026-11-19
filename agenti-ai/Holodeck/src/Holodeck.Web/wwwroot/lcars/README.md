# LCARS library fallback

A copy of the LCARS library from the PC's front server (`/Space/Servers/Front/www/lcars/`), which remains the source.
Holodeck loads the library from the front server first and uses this copy only when that fails, so Holodeck keeps
its LCARS appearance on a machine without the front server. Edit the library in Front, then copy `lcars.css`,
`lcars.js` and `fonts/` here.
