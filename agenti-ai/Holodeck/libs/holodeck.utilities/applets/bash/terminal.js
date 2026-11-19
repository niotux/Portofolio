(async () => {
  const SERVICE = 'bash';
  const RECONNECT_DELAY_MS = 1500;
  const FONT_SIZE = 13;
  const OUTPUT_BATCH_CHARACTERS = 262144;
  const reconnectButton = document.querySelector('#reconnect');
  const elsewhereNote = document.querySelector('#elsewhere');
  const status = document.querySelector('#status');
  const fontFamily = '"Bash Mono", Consolas, monospace';
  // Measure the bundled monospace face before creating the terminal grid.
  await document.fonts.load(`${FONT_SIZE}px "Bash Mono"`).catch(() => {});
  const terminal = new Terminal({ cursorBlink: true, fontFamily, fontSize: FONT_SIZE, scrollback: 5000, theme: { background: '#000000', foreground: '#ffcc99', cursor: '#ff9c00', cursorAccent: '#000000', selectionBackground: '#4b3559', selectionForeground: '#fff1dd' } });
  const fit = new FitAddon.FitAddon();
  terminal.loadAddon(fit);
  const screen = document.querySelector('#terminal');
  terminal.open(screen);
  // Keep the DOM renderer when GPU rendering is unavailable or its context is lost.
  let webgl;
  try {
    webgl = new WebglAddon.WebglAddon();
    webgl.onContextLoss(() => webgl.dispose());
    terminal.loadAddon(webgl);
  } catch { if (webgl) { webgl.dispose(); } }
  // The shell has one size, owned by the screen in use: the one last touched or typed into. While nobody owns it, any
  // visible screen takes it. Every screen draws that grid at its normal font size; a smaller screen scrolls and
  // follows the cursor.
  let connectionId = null;
  let owner = null;
  let claimed = '';
  let windowId = null;
  let sessionId = null;
  let socket = null;
  let connectAttempt = 0;
  let reconnectTimer = 0;
  let disposed = false;
  let exited = false;

  function showStatus(text, error = false) { status.textContent = text; status.classList.toggle('error', error); }
  function report(error) { showStatus(error.message, true); }
  function send(message) {
    if (!socket || socket.readyState !== 1) { return; }
    socket.send(JSON.stringify(message)).catch(report);
  }
  // A minimized or hidden window has no screen to size for.
  function visible() { return screen.clientWidth > 0 && screen.clientHeight > 0; }
  function owned() { return connectionId !== null && owner === connectionId; }
  // The grid that fits this screen at the normal font size.
  // Null while the window is hidden and has nothing to measure.
  function ownSize() {
    if (!visible()) { return null; }
    const size = fit.proposeDimensions();
    if (!size || !Number.isFinite(size.cols) || !Number.isFinite(size.rows)) { return null; }
    return { cols: Math.max(2, size.cols), rows: Math.max(2, size.rows) };
  }
  function claim() {
    if (disposed || !connectionId) { return; }
    const size = ownSize();
    if (!size) { return; }
    const { cols, rows } = size;
    const key = `${cols}x${rows}`;
    // Set while this screen owns that size or has asked for it.
    if (key === claimed) { return; }
    claimed = key;
    send({ type: 'resize', cols, rows });
  }
  // A screen smaller than the grid keeps the cursor in view.
  function followCursor() {
    const grid = screen.querySelector('.xterm-screen');
    if (!grid || !visible()) { return; }
    const cellWidth = grid.offsetWidth / terminal.cols;
    const cellHeight = grid.offsetHeight / terminal.rows;
    const buffer = terminal.buffer.active;
    const left = buffer.cursorX * cellWidth;
    const top = buffer.cursorY * cellHeight;
    if (left < screen.scrollLeft || left + cellWidth > screen.scrollLeft + screen.clientWidth) { screen.scrollLeft = Math.max(0, left + cellWidth - screen.clientWidth / 2); }
    if (top < screen.scrollTop || top + cellHeight > screen.scrollTop + screen.clientHeight) { screen.scrollTop = Math.max(0, top + cellHeight - screen.clientHeight); }
  }
  function layout() {
    if (disposed || !visible()) { return; }
    const elsewhere = owner !== null && !owned();
    elsewhereNote.hidden = !elsewhere;
    document.body.classList.toggle('elsewhere', elsewhere);
    followCursor();
  }
  function screenChanged() {
    if (owned() || owner === null) { claim(); }
    layout();
  }
  const resizeObserver = new ResizeObserver(screenChanged);
  resizeObserver.observe(screen);
  terminal.onData(data => {
    if (!owned()) { claim(); }
    send({ type: 'input', data });
  });
  // Touching a screen makes it the one in use.
  screen.addEventListener('pointerdown', () => { if (!owned()) { claim(); } });
  elsewhereNote.addEventListener('click', () => { claim(); terminal.focus(); });

  terminal.onRender(followCursor);
  function write(data) { return new Promise(resolve => terminal.write(data, resolve)); }
  function setSize(cols, rows) {
    if (terminal.cols !== cols || terminal.rows !== rows) { terminal.resize(cols, rows); }
  }
  // Messages apply in order: a size change takes effect only after the output printed before it.
  async function handle(message) {
    if (message.type === 'replay') {
      connectionId = message.connection; owner = message.owner; claimed = '';
      terminal.reset();
      for (const chunk of message.chunks) { setSize(chunk.cols, chunk.rows); await write(chunk.data); }
      setSize(message.cols, message.rows); layout();
      if (owner === null) { claim(); }
    }
    if (message.type === 'size') {
      owner = message.owner; setSize(message.cols, message.rows);
      claimed = owned() ? `${message.cols}x${message.rows}` : '';
      layout();
      if (owner === null) { claim(); }
    }
    if (message.type === 'output') { await write(message.data); }
    if (message.type === 'exit') { exited = true; showStatus(`Shell exited with code ${message.code}.`); }
  }

  function disconnect() {
    ++connectAttempt; clearTimeout(reconnectTimer);
    if (socket) { socket.close().catch(() => { }); socket = null; }
    connectionId = null; owner = null;
    reconnectButton.hidden = true; elsewhereNote.hidden = true; document.body.classList.remove('elsewhere');
  }

  async function connectSession() {
    disconnect();
    if (!sessionId || disposed) { return; }
    const attempt = connectAttempt;
    showStatus('Connecting…');
    try {
      const connection = await holodeck.connect(SERVICE, `/sessions/${encodeURIComponent(sessionId)}/terminal`);
      if (disposed || attempt !== connectAttempt) { connection.close().catch(() => { }); return; }
      socket = connection;
      const pending = [];
      let receiving = false;
      // Combine adjacent output while the parser is busy. Size/replay messages remain barriers,
      // so the shell's output is always interpreted at the grid size it was printed at.
      async function receive(message) {
        pending.push(message);
        if (receiving) { return; }
        receiving = true;
        try {
          while (pending.length && connection === socket) {
            const next = pending.shift();
            if (next.type === 'output') {
              const parts = [next.data];
              let length = next.data.length;
              while (pending.length && pending[0].type === 'output' && length + pending[0].data.length <= OUTPUT_BATCH_CHARACTERS) {
                const output = pending.shift();
                parts.push(output.data); length += output.data.length;
              }
              next.data = parts.join('');
            }
            await handle(next);
          }
        } catch (error) { report(error); }
        finally { receiving = false; }
      }
      connection.addEventListener('open', () => {
        if (connection !== socket) { return; }
        showStatus(exited ? 'Shell ended.' : 'Connected');
        reconnectButton.hidden = true; terminal.focus();
      });
      connection.addEventListener('message', event => {
        if (connection !== socket) { return; }
        let message;
        try { message = JSON.parse(event.data); } catch { showStatus('Invalid terminal response.', true); return; }
        receive(message);
      });
      connection.addEventListener('close', () => {
        if (connection !== socket || disposed) { return; }
        socket = null;
        if (exited) { return; }
        showStatus('Disconnected. Reconnecting…'); reconnectButton.hidden = false;
        reconnectTimer = setTimeout(() => attach().catch(report), RECONNECT_DELAY_MS);
      });
      connection.addEventListener('error', () => {
        if (connection === socket) { showStatus('Terminal connection failed.', true); reconnectButton.hidden = false; }
      });
      if (connection.readyState === 1) { terminal.focus(); showStatus('Connected'); }
    } catch (error) {
      if (attempt !== connectAttempt || disposed) { return; }
      report(error); reconnectButton.hidden = false;
    }
  }

  reconnectButton.addEventListener('click', () => attach().catch(report));
  // Browser navigation disconnects this view. Only the native window's explicit Close releases its shell.
  window.addEventListener('pagehide', () => { disposed = true; disconnect(); resizeObserver.disconnect(); terminal.dispose(); });
  // The manager returns this window's shell, or starts a new one if the old one ended with its environment.
  async function attach() {
    const size = ownSize() || { cols: terminal.cols, rows: terminal.rows };
    const session = await holodeck.request(SERVICE, '/sessions', { method: 'POST', contentType: 'application/json', body: JSON.stringify({ windowId, cols: size.cols, rows: size.rows }) });
    if (session.id !== sessionId) {
      sessionId = session.id; exited = !session.running;
      await holodeck.setState({ sessionId });
    }
    await connectSession();
  }
  async function start() {
    const context = await holodeck.ready();
    windowId = context.windowId;
    await holodeck.ownResource({ service: SERVICE, method: 'DELETE', path: `/sessions/by-window/${encodeURIComponent(windowId)}` });
    await attach();
  }
  start().catch(report);
})();
