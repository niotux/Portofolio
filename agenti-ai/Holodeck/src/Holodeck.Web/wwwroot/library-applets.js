import { api, el, newId } from './applets/shared.js';

const CHANNEL = 'holodeck.applet.v1';
const MAX_REQUEST_BYTES = 1024 * 1024;
const MAX_SOCKETS = 8;
const REQUEST_TIMEOUT_MS = 30000;
const HTTP_METHODS = new Set(['GET', 'HEAD', 'POST', 'PUT', 'PATCH', 'DELETE']);
const THEME_NAMES = ['surface', 'text', 'bg', 'raised', 'border', 'muted', 'accent', 'accent-text', 'danger'];

export async function discoverLibraryApplets(projectId) {
  const entries = await api(`api/projects/${encodeURIComponent(projectId)}/applets`);
  const definitions = [];
  for (const entry of entries) {
    definitions.push({
      id: `library:${entry.libraryId}:${entry.libraryVersion}:${entry.id}`,
      title: entry.title, description: entry.description, icon: entry.icon || '▣',
      libraryId: entry.libraryId,
      size: { width: entry.width || 800, height: entry.height || 560 },
      mount: ctx => mountLibraryApplet(ctx, entry)
    });
  }
  return definitions;
}

function theme() {
  const computed = getComputedStyle(document.documentElement);
  const values = {};
  for (const name of THEME_NAMES) { values[name] = computed.getPropertyValue(`--${name}`).trim(); }
  return { name: document.documentElement.dataset.theme || 'dark', values };
}

function libraryBase(projectId, entry) {
  const segments = [projectId, entry.libraryId, entry.libraryVersion];
  return `api/projects/${encodeURIComponent(segments[0])}/libraries/${encodeURIComponent(segments[1])}/${encodeURIComponent(segments[2])}`;
}

function serviceUrl(base, entry, service, path) {
  if (!entry.services || !entry.services.includes(service)) { throw new Error('This applet cannot use that service.'); }
  if (typeof path !== 'string' || !path.startsWith('/') || path.startsWith('//') || path.length > 4096) { throw new Error('Invalid service path.'); }
  let decoded = path.split('?')[0];
  for (let pass = 0; pass < 3; pass++) { decoded = decodeURIComponent(decoded); }
  if (decoded.includes('\\') || decoded.includes('#') || decoded.split('/').some(part => part === '.' || part === '..')) { throw new Error('Invalid service path.'); }
  const url = new URL(`${base}/services/${encodeURIComponent(service)}${path}`, location.href);
  url.searchParams.set('applet', entry.id);
  return url;
}

function mountLibraryApplet(ctx, entry) {
  const base = libraryBase(ctx.state.projectId, entry);
  const frame = el('iframe', 'library-applet-frame');
  frame.title = entry.title;
  frame.setAttribute('sandbox', 'allow-scripts');
  frame.setAttribute('referrerpolicy', 'no-referrer');
  frame.src = `${base}/applets/${encodeURIComponent(entry.id)}/assets/${entry.entryPoint.split('/').map(encodeURIComponent).join('/')}`;
  if (!ctx.state._libraryWindowId) { ctx.setState({ _libraryWindowId: newId() }); }
  ctx.body.classList.add('library-applet-body');
  ctx.body.append(frame);
  const sockets = new Map();
  const requests = new Set();
  const inFlight = new Set();
  const resources = Array.isArray(ctx.state._libraryResources) ? [...ctx.state._libraryResources] : [];
  let disposed = false;
  let closing = false;
  const post = message => { if (!disposed) { frame.contentWindow?.postMessage({ channel: CHANNEL, ...message }, '*'); } };
  const reply = (id, value) => post({ id, value });
  const reject = (id, error) => post({ id, error: error.message });
  const observer = new MutationObserver(() => post({ event: 'theme', theme: theme() }));
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['style', 'class', 'data-theme'] });

  async function responseBody(response) {
    if (!response.body) { return new ArrayBuffer(0); }
    const reader = response.body.getReader();
    const chunks = [];
    let length = 0;
    try {
      while (true) {
        const result = await reader.read();
        if (result.done) { break; }
        length += result.value.byteLength;
        if (length > MAX_REQUEST_BYTES) { await reader.cancel(); throw new Error('Response is too large.'); }
        chunks.push(result.value);
      }
    } finally { reader.releaseLock(); }
    const bytes = new Uint8Array(length);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    return bytes.buffer;
  }

  async function request(message, binary = false) {
    if (requests.size >= 16) { throw new Error('Too many pending service requests.'); }
    const method = String(message.method || 'GET').toUpperCase();
    if (!HTTP_METHODS.has(method)) { throw new Error('Unsupported request method.'); }
    if (message.body != null) {
      const bytes = typeof message.body === 'string' ? new TextEncoder().encode(message.body).byteLength : binary && message.body instanceof ArrayBuffer ? message.body.byteLength : -1;
      if (bytes < 0 || bytes > MAX_REQUEST_BYTES) { throw new Error('Invalid or excessive request body.'); }
    }
    const url = serviceUrl(base, entry, message.service, message.path);
    const controller = new AbortController(); requests.add(controller);
    const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
    try {
      const headers = {};
      if (message.contentType) { headers['Content-Type'] = String(message.contentType); }
      const response = await fetch(url, { method, headers, body: message.body, signal: controller.signal });
      const bytes = await responseBody(response);
      return { status: response.status, statusText: response.statusText, contentType: response.headers.get('content-type'), body: binary ? bytes : new TextDecoder().decode(bytes) };
    } finally { clearTimeout(timeout); requests.delete(controller); }
  }

  function openSocket(message) {
    if (typeof message.socket !== 'string' || sockets.has(message.socket) || sockets.size >= MAX_SOCKETS) { throw new Error('Invalid or excessive service connections.'); }
    const url = serviceUrl(base, entry, message.service, message.path);
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(url);
    sockets.set(message.socket, socket);
    socket.addEventListener('open', () => post({ event: 'socket-open', socket: message.socket }));
    socket.addEventListener('message', event => {
      if (typeof event.data === 'string') { post({ event: 'socket-message', socket: message.socket, data: event.data }); }
      else { socket.close(1003, 'Text messages required'); }
    });
    socket.addEventListener('close', event => {
      if (sockets.get(message.socket) === socket) { sockets.delete(message.socket); }
      post({ event: 'socket-close', socket: message.socket, code: event.code });
    });
    socket.addEventListener('error', () => post({ event: 'socket-error', socket: message.socket }));
  }

  async function receive(event) {
    if (disposed || event.source !== frame.contentWindow || event.origin !== 'null') { return; }
    const message = event.data;
    if (!message || message.channel !== CHANNEL || typeof message.id !== 'string') { return; }
    try {
      if (closing) { throw new Error('This applet window is closing.'); }
      switch (message.operation) {
        case 'ready': reply(message.id, { state: ctx.state, windowId: ctx.state._libraryWindowId, theme: theme() }); break;
        case 'state': {
          if (!message.state || Array.isArray(message.state) || typeof message.state !== 'object' || JSON.stringify(message.state).length > 65536) { throw new Error('Invalid applet state.'); }
          const state = { ...message.state };
          for (const key of Object.keys(state)) { if (key === 'projectId' || key.startsWith('_library') || key === '__proto__' || key === 'constructor') { delete state[key]; } }
          ctx.setState(state); reply(message.id, null); break;
        }
        case 'title': ctx.setTitle(String(message.title || entry.title).slice(0, 160)); reply(message.id, null); break;
        case 'request':
        case 'fetch': {
          const operation = request(message, message.operation === 'fetch'); inFlight.add(operation);
          try { reply(message.id, await operation); } finally { inFlight.delete(operation); }
          break;
        }
        case 'own-resource': {
          if (message.method !== 'DELETE') { throw new Error('Invalid window resource cleanup.'); }
          serviceUrl(base, entry, message.service, message.path);
          if (!resources.some(resource => resource.service === message.service && resource.path === message.path)) {
            if (resources.length >= 8) { throw new Error('Too many window resources.'); }
            resources.push({ service: message.service, path: message.path, method: 'DELETE' });
            ctx.setState({ _libraryResources: resources });
          }
          if (ctx.flushState) { await ctx.flushState(); }
          reply(message.id, null); break;
        }
        case 'focus': ctx.body.dispatchEvent(new Event('pointerdown', { bubbles: true })); reply(message.id, null); break;
        case 'socket-open': openSocket(message); reply(message.id, null); break;
        case 'socket-send': {
          const socket = sockets.get(message.socket);
          if (!socket || socket.readyState !== WebSocket.OPEN) { throw new Error('Service connection is closed.'); }
          if (typeof message.data !== 'string' || message.data.length > MAX_REQUEST_BYTES) { throw new Error('Invalid service message.'); }
          if (socket.bufferedAmount > MAX_REQUEST_BYTES) { throw new Error('Service connection is busy.'); }
          socket.send(message.data); reply(message.id, null); break;
        }
        case 'socket-close': sockets.get(message.socket)?.close(); reply(message.id, null); break;
        default: throw new Error('Unsupported applet operation.');
      }
    } catch (error) { reject(message.id, error); }
  }
  window.addEventListener('message', receive);
  ctx.onBeforeClose(async () => {
    closing = true;
    try {
      await Promise.allSettled([...inFlight]);
      for (const resource of resources) {
        const result = await request(resource);
        if (result.status !== 404 && (result.status < 200 || result.status >= 300)) { throw new Error('Could not release this window’s resources. Try closing it again.'); }
      }
      return true;
    } catch (error) { closing = false; throw error; }
  });
  ctx.dispose(() => {
    disposed = true; observer.disconnect(); window.removeEventListener('message', receive);
    for (const controller of requests) { controller.abort(); }
    for (const socket of sockets.values()) { socket.close(); }
    sockets.clear(); frame.remove();
  });
}
