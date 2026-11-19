import { api, json, el, button, notify, listen, listBack, sudoRow } from './shared.js';

// The project's library services: whether each runs, as which user, its private log and its sudo grant. Team is the
// same for agents.
export const servicesApplet = { id: 'services', title: 'Services', icon: '⚙', description: 'Monitor library services and grant them sudo.', projectScoped: true, size: { width: 880, height: 600 }, mount };

const REFRESH_MS = 3000;
const TABS = [['Overview', 'overview'], ['Log', 'log']];
const keyOf = service => `${service.libraryId}/${service.libraryVersion}/${service.id}`;

async function mount(ctx) {
  const projectId = ctx.state.projectId;
  if (!projectId) {
    ctx.body.replaceChildren(el('div', 'empty', 'Open a project from the main menu to see its services.')); return;
  }
  const root = `api/projects/${encodeURIComponent(projectId)}/services`;
  let services = [];
  let selectedKey = ctx.state.service || null;
  let tab = ctx.state.serviceTab === 'log' ? 'log' : 'overview';
  // Phone: the chosen service replaces the list until its back pill is pressed.
  let detailShown = false;
  let paneKey = '';
  const host = el('div', 'project-app services-app');
  const heading = el('header', 'project-heading');
  const layout = el('div', 'project-layout');
  const side = el('aside', 'project-side'); side.setAttribute('aria-label', 'Library services');
  const main = el('section', 'project-main'); main.setAttribute('aria-label', 'Service');
  const header = el('header', 'project-agent-header');
  const tabs = el('nav', 'project-agent-tabs'); tabs.setAttribute('role', 'tablist'); tabs.setAttribute('aria-label', 'Service');
  const pane = el('div', 'project-agent-pane');
  layout.append(side, main); host.append(heading, layout); ctx.body.replaceChildren(host);

  const listing = await api('api/filesystem');
  const project = listing.projects.find(item => item.id === projectId);
  if (!project || ctx.isClosed()) { ctx.body.replaceChildren(el('div', 'empty', 'This project no longer exists.')); return; }
  const title = el('div', 'project-heading-copy'); title.append(el('span', 'eyebrow', 'Services'), el('h2', '', project.name));
  const shortcuts = el('div', 'project-shortcuts'); shortcuts.append(button('Add from library…', () => ctx.launch('library', { projectId })));
  heading.append(title, shortcuts);

  const selected = () => services.find(service => keyOf(service) === selectedKey) || null;
  const base = service => `${root}/${encodeURIComponent(service.libraryId)}/${encodeURIComponent(service.libraryVersion)}/${encodeURIComponent(service.id)}`;
  function saveState() { ctx.setState({ service: selectedKey, serviceTab: tab }); }

  let refreshing = null;
  function refresh() {
    if (refreshing) { return refreshing; }
    refreshing = api(root).then(next => {
      if (ctx.isClosed()) { return; }
      services = next;
      if (!selected()) { selectedKey = services.length ? keyOf(services[0]) : null; saveState(); }
      render();
    }).catch(error => notify(error.message, true)).finally(() => { refreshing = null; });
    return refreshing;
  }

  function render() { renderSide(); renderService(); }

  function renderSide() {
    const scrollTop = side.querySelector('.project-agent-list')?.scrollTop || 0;
    const list = el('div', 'project-agent-list');
    let library = '';
    for (const service of services) {
      const group = `${service.libraryId}/${service.libraryVersion}`;
      if (group !== library) {
        library = group;
        const caption = el('div', 'project-roster-heading'); caption.append(el('h3', '', service.libraryName), el('small', 'muted', service.libraryVersion));
        list.append(caption);
      }
      const row = button('', () => { selectedKey = keyOf(service); detailShown = true; saveState(); render(); }, 'project-agent');
      row.setAttribute('aria-label', `Inspect ${service.id}`); row.classList.toggle('selected', keyOf(service) === selectedKey);
      const copy = el('span', 'project-agent-copy'); copy.append(el('strong', '', service.id), el('small', 'muted', statusText(service)));
      const dot = el('span', 'status-dot'); dot.dataset.status = status(service);
      row.append(el('span', 'project-avatar', service.id.trim()[0]?.toUpperCase() || '?'), copy, dot); list.append(row);
    }
    if (!services.length) { list.append(el('p', 'project-side-empty muted', 'Services of this project’s libraries appear here.')); }
    side.replaceChildren(list); list.scrollTop = scrollTop;
  }

  function renderService() {
    const service = selected();
    host.classList.toggle('has-selection', detailShown && Boolean(service));
    if (!service) {
      paneKey = '';
      const empty = el('div', 'project-welcome');
      empty.append(el('h2', '', 'No services'), el('p', 'muted', 'Libraries that declare services list them here once attached to this project.'));
      main.replaceChildren(empty); return;
    }
    if (!main.contains(pane)) { main.replaceChildren(header, tabs, pane); }
    const copy = el('div', 'project-agent-heading'); copy.append(el('h2', 'project-agent-title', service.id), el('p', 'muted', statusText(service)));
    const action = button(service.running ? 'Stop service' : 'Start service', () => control(service, service.running ? 'stop' : 'start'), service.running ? '' : 'primary');
    header.replaceChildren(listBack('Services', () => { detailShown = false; host.classList.remove('has-selection'); }), copy, action);
    const tabButtons = [];
    for (const [label, target] of TABS) {
      const node = button(label, () => { tab = target; saveState(); renderService(); }, 'project-agent-tab');
      node.setAttribute('role', 'tab'); node.setAttribute('aria-selected', String(tab === target)); node.classList.toggle('selected', tab === target); tabButtons.push(node);
    }
    tabs.replaceChildren(...tabButtons);
    // The overview follows every refresh; the log changes only when asked, so reading it is never interrupted.
    const nextKey = tab === 'log' ? `${keyOf(service)}:log` : `${JSON.stringify(service)}:overview`;
    if (nextKey === paneKey) { return; }
    paneKey = nextKey;
    if (tab === 'log') { renderLog(service); } else { renderOverview(service); }
  }

  async function control(service, action) {
    try { services = await api(`${base(service)}/${action}`, { method: 'POST' }); paneKey = ''; render(); }
    catch (error) { notify(error.message, true); }
  }

  function renderOverview(service) {
    const process = section('Process', 'Runs as its own Linux user in this library’s project environment.');
    process.append(row(service.libraryName, service.libraryVersion));
    process.append(row(`User ${service.id}`, service.uid === null ? '' : `uid ${service.uid}`));
    if (service.port !== null) { process.append(row(`Port ${service.port}`, '')); }
    if (service.running && service.startedAt) { process.append(row('Started', new Date(service.startedAt).toLocaleString())); }
    const sudo = section('Sudo', 'Root inside this library’s environment, only when you allow it. Every applet window using this service shares it.');
    sudo.append(sudoRow(ctx, service.id, service.sudo, async wanted => {
      services = await api(`${base(service)}/sudo`, json('PUT', { enabled: wanted }));
      const current = services.find(item => keyOf(item) === keyOf(service));
      paneKey = ''; render();
      return current?.sudo === true;
    }));
    pane.replaceChildren(sudo, process);
  }

  async function renderLog(service) {
    const text = el('pre', 'project-log', 'Loading…');
    pane.replaceChildren(button('Refresh log', () => renderLog(service)), text);
    try {
      const response = await fetch(`${base(service)}/log`);
      if (!response.ok) { const body = await response.json().catch(() => ({})); throw new Error(body.error || `Could not read the service log (${response.status}).`); }
      const content = await response.text();
      if (!pane.contains(text)) { return; }
      text.textContent = content || 'Nothing logged yet.'; text.scrollTop = text.scrollHeight;
    }
    catch (error) { if (pane.contains(text)) { text.textContent = error.message; text.classList.add('error'); } }
  }

  listen(ctx, () => { refresh(); });
  const timer = setInterval(() => { if (!document.hidden) { refresh(); } }, REFRESH_MS);
  ctx.dispose(() => clearInterval(timer));
  await refresh();
}

function section(title, note) {
  const node = el('section', 'computer-section'); node.append(el('h3', '', title));
  if (note) { node.append(el('p', 'muted', note)); }
  return node;
}
function row(title, detail) {
  const node = el('div', 'computer-row'); const copy = el('div', 'computer-copy'); copy.append(el('strong', '', title));
  if (detail) { copy.append(el('small', 'muted', detail)); }
  node.append(copy); return node;
}
function status(service) {
  if (service.running) { return 'running'; }
  return service.exitCode === null ? 'stopped' : 'crashed';
}
function statusText(service) {
  if (service.running) { return 'Running'; }
  return service.exitCode === null ? 'Stopped' : `Exited with code ${service.exitCode}`;
}
