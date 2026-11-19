# Terminal renderer

Vendored browser assets from the published npm packages `@xterm/xterm` version `5.5.0`, `@xterm/addon-fit` version `0.10.0` and `@xterm/addon-webgl` version `0.18.0`. Their MIT licenses are included beside the assets. The applet loads these local files; no CDN is required.

WebGL renders the terminal when available; initialization failure or context loss restores the DOM renderer. Output bursts are batched without crossing terminal size changes.
