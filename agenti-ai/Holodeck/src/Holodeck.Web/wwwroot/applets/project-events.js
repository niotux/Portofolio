// Change notices for a project: one WebSocket per page and project, shared by every window that listens. Browsers
// allow only a few HTTP connections to a server, so a long-lived stream per window would leave ordinary requests
// waiting forever once a few windows or tabs were open; WebSockets do not count against that limit.
const RECONNECT_DELAYS_MS = [1000, 2000, 5000, 10000];
const connections = new Map();

// Calls onChange for every notice of this kind, and once each time the connection opens so the window can catch up
// on anything it missed. onLost is told when the connection drops. Returns the function that stops listening.
export function watchProject(projectId, kind, onChange, onLost = null) {
  let connection = connections.get(projectId);
  if (!connection) {
    connection = { projectId, listeners: new Set(), socket: null, attempt: 0, timer: 0 };
    connections.set(projectId, connection);
    connect(connection);
  }
  const listener = { kind, onChange, onLost };
  connection.listeners.add(listener);
  return () => {
    connection.listeners.delete(listener);
    if (connection.listeners.size) { return; }
    connections.delete(projectId);
    clearTimeout(connection.timer);
    if (connection.socket) { const socket = connection.socket; connection.socket = null; socket.close(); }
  };
}

function connect(connection) {
  const url = new URL(`api/projects/${encodeURIComponent(connection.projectId)}/events`, location.href);
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
  const socket = new WebSocket(url);
  connection.socket = socket;
  socket.addEventListener('open', () => {
    if (connection.socket !== socket) { return; }
    connection.attempt = 0;
    for (const listener of [...connection.listeners]) { listener.onChange(); }
  });
  socket.addEventListener('message', event => {
    if (connection.socket !== socket) { return; }
    let notice;
    try { notice = JSON.parse(event.data); } catch { return; }
    for (const listener of [...connection.listeners]) {
      if (listener.kind === notice.kind) { listener.onChange(); }
    }
  });
  socket.addEventListener('close', () => {
    if (connection.socket !== socket) { return; }
    connection.socket = null;
    for (const listener of [...connection.listeners]) { if (listener.onLost) { listener.onLost(); } }
    const delay = RECONNECT_DELAYS_MS[Math.min(connection.attempt, RECONNECT_DELAYS_MS.length - 1)];
    connection.attempt++;
    connection.timer = setTimeout(() => { if (connections.get(connection.projectId) === connection) { connect(connection); } }, delay);
  });
}
