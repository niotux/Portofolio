// The only host capabilities available to a sandboxed library applet. No host DOM or credentials are exposed.
(() => {
  const channel = 'holodeck.applet.v1';
  const pending = new Map();
  const sockets = new Map();
  let nextId = 0;
  function call(operation, values = {}) {
    const id = String(++nextId);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { pending.delete(id); reject(new Error('Holodeck did not answer.')); }, 30000);
      pending.set(id, { resolve, reject, timer });
      parent.postMessage({ channel, id, operation, ...values }, '*');
    });
  }
  function applyTheme(theme) {
    document.documentElement.dataset.theme = theme.name;
    for (const [name, value] of Object.entries(theme.values)) { document.documentElement.style.setProperty(`--${name}`, value); }
    window.dispatchEvent(new CustomEvent('holodeck-theme', { detail: theme }));
  }
  window.addEventListener('message', event => {
    if (event.source !== parent || !event.data || event.data.channel !== channel) { return; }
    const message = event.data;
    if (message.event === 'theme') { applyTheme(message.theme); return; }
    if (message.event?.startsWith('socket-')) {
      const socket = sockets.get(message.socket);
      if (!socket) { return; }
      if (message.event === 'socket-open') { socket.readyState = 1; socket.dispatchEvent(new Event('open')); }
      if (message.event === 'socket-message') { socket.dispatchEvent(new MessageEvent('message', { data: message.data })); }
      if (message.event === 'socket-error') { socket.dispatchEvent(new Event('error')); }
      if (message.event === 'socket-close') { socket.readyState = 3; sockets.delete(message.socket); socket.dispatchEvent(new CloseEvent('close', { code: message.code })); }
      return;
    }
    const request = pending.get(message.id);
    if (!request) { return; }
    pending.delete(message.id); clearTimeout(request.timer);
    if (message.error) { request.reject(new Error(message.error)); } else { request.resolve(message.value); }
  });
  class ServiceSocket extends EventTarget {
    constructor(id) { super(); this.id = id; this.readyState = 0; }
    send(data) { return call('socket-send', { socket: this.id, data }); }
    close() { this.readyState = 2; return call('socket-close', { socket: this.id }); }
  }
  const focusWindow = () => { call('focus').catch(() => { }); };
  document.addEventListener('pointerdown', focusWindow);
  window.addEventListener('focus', focusWindow);
  window.holodeck = {
    ready: async () => { const value = await call('ready'); applyTheme(value.theme); return value; },
    setState: state => call('state', { state }),
    setTitle: title => call('title', { title }),
    ownResource: resource => call('own-resource', resource),
    async fetch(service, path, options = {}) {
      let body = options.body;
      let contentType = options.contentType || new Headers(options.headers).get('Content-Type');
      if (body != null && typeof body !== 'string') {
        const encoded = new Response(body);
        contentType = contentType || encoded.headers.get('content-type');
        body = await encoded.arrayBuffer();
      }
      const result = await call('fetch', { service, path, method: options.method || 'GET', body, contentType });
      const headers = new Headers();
      if (result.contentType) { headers.set('Content-Type', result.contentType); }
      const empty = result.status === 204 || result.status === 205 || result.status === 304 || String(options.method).toUpperCase() === 'HEAD';
      return new Response(empty ? null : result.body, { status: result.status, statusText: result.statusText, headers });
    },
    async request(service, path, options = {}) {
      const result = await call('request', { service, path, method: options.method || 'GET', body: options.body, contentType: options.contentType });
      if (result.status < 200 || result.status >= 300) {
        let error = `Request failed (${result.status}).`;
        try { error = JSON.parse(result.body).error || error; } catch { }
        throw new Error(error);
      }
      return result.body ? JSON.parse(result.body) : null;
    },
    async connect(service, path) {
      const id = `socket-${++nextId}`;
      const socket = new ServiceSocket(id); sockets.set(id, socket);
      try { await call('socket-open', { socket: id, service, path }); }
      catch (error) { sockets.delete(id); throw error; }
      return socket;
    }
  };
})();
