import { api, json, el, button, field, ask, confirm, notify, changed, listen, showOverlay, listBack, sudoRow } from './shared.js';
import { renderSettings } from './settings-form.js';
import { renderManagementActions } from './management-actions.js';
import { watchProject } from './project-events.js';

export const projectApplet = { id: 'room', title: 'Team', icon: '◎', description: 'Manage the agents in a project team.', projectScoped: true, size: { width: 960, height: 680 }, mount };

async function mount(ctx) {
  const projectId = ctx.state.projectId;
  if (!projectId) {
    ctx.body.replaceChildren(el('div', 'empty', 'Open a project from the main menu to manage its team.')); return;
  }
  const root = `api/projects/${projectId}/room`;
  let view = { members: [], channels: [] };
  let selectedId = ctx.state.memberId || null;
  let agentTab = ['overview', 'channels', 'settings', 'files', 'computer', 'log'].includes(ctx.state.agentTab) ? ctx.state.agentTab : 'overview';
  let refreshing = null;
  let refreshAgain = false;
  let layoutKey = '';
  let contentKey = '';
  let contentGeneration = 0;
  let agentHeader = null;
  let agentTabs = null;
  let pane = null;
  let definitionDialog = null;
  const settings = new Map();
  const management = new Map();
  const host = el('div', 'project-app team-app');
  // Phone: the chosen agent replaces the roster until its back pill is pressed.
  let detailShown = false;
  const heading = el('header', 'project-heading');
  const body = el('div', 'project-layout');
  const side = el('aside', 'project-side'); side.setAttribute('aria-label', 'Team agents');
  const main = el('section', 'project-main'); main.setAttribute('aria-label', 'Agent workspace');
  body.append(side, main); host.append(heading, body); ctx.body.replaceChildren(host);

  const listing = await api('api/filesystem');
  const project = listing.projects.find(item => item.id === projectId);
  if (!project || ctx.isClosed()) { ctx.body.replaceChildren(el('div', 'empty', 'This project no longer exists.')); return; }
  const title = el('div', 'project-heading-copy'); title.append(el('span', 'eyebrow', 'Team'), el('h2', '', project.name));
  const shortcuts = el('div', 'project-shortcuts');
  const unresolvedActions = el('span', 'project-unresolved-actions');
  shortcuts.append(button('Add agent', () => openDefinitionPicker('agent'), 'primary'), button('Add team', () => openDefinitionPicker('team')), button('Open channels', () => ctx.launch('conversations', { projectId })), button('Add from library…', () => ctx.launch('library', { projectId })), unresolvedActions);
  heading.append(title, shortcuts);

  const member = id => view.members.find(item => item.id === id);
  const activeMembers = () => view.members.filter(item => !item.isRemoved);
  const agents = () => activeMembers().filter(item => !item.isYou);
  const nameOf = id => member(id)?.name || 'Unknown';
  const channelsFor = id => view.channels.filter(channel => channel.memberIds.includes(id));
  const relationshipKey = () => JSON.stringify([view.members, view.channels.map(channel => [channel.id, channel.name, channel.memberIds])]);

  function saveState() {
    ctx.setState({ memberId: selectedId, agentTab });
  }

  function refresh() {
    if (refreshing) { refreshAgain = true; return refreshing; }
    refreshing = (async () => {
      do {
        refreshAgain = false;
        view = await api(root);
        if (ctx.isClosed()) { return; }
        if (!selectedId || !member(selectedId) || member(selectedId).isRemoved || member(selectedId).isYou) { selectedId = agents()[0]?.id || null; }
        saveState(); render();
      } while (refreshAgain && !ctx.isClosed());
    })().catch(error => notify(error.message, true)).finally(() => { refreshing = null; });
    return refreshing;
  }

  function selectAgent(id, tab = 'overview') {
    selectedId = id; agentTab = tab; detailShown = true; saveState(); render();
  }

  function render() {
    renderSide();
    renderUnresolvedActions(); renderAgent();
  }

  function renderSide() {
    const focusLabel = side.contains(document.activeElement) ? document.activeElement.getAttribute('aria-label') : null;
    const scrollTop = side.querySelector('.project-agent-list')?.scrollTop || 0;
    const caption = el('div', 'project-roster-heading'); caption.append(el('h3', '', 'Agents'));
    const list = el('div', 'project-agent-list');
    for (const item of agents()) {
      const row = button('', () => selectAgent(item.id), 'project-agent'); row.dataset.memberId = item.id;
      row.setAttribute('aria-label', `Inspect ${item.name}`); row.classList.toggle('selected', selectedId === item.id);
      const mark = el('span', 'project-avatar', item.name.trim()[0]?.toUpperCase() || '?');
      const copy = el('span', 'project-agent-copy'); copy.append(el('strong', '', item.name), el('small', 'muted', statusLabel(item.status)));
      const dot = el('span', 'status-dot'); dot.dataset.status = item.status;
      row.append(mark, copy, dot); list.append(row);
    }
    if (!agents().length) { list.append(el('p', 'project-side-empty muted', 'Your team agents will appear here.')); }
    side.replaceChildren(caption, list); list.scrollTop = scrollTop;
    if (focusLabel) { for (const control of side.querySelectorAll('[aria-label]')) { if (control.getAttribute('aria-label') === focusLabel) { control.focus({ preventScroll: true }); break; } } }
  }

  function renderAgent() {
    const item = member(selectedId);
    host.classList.toggle('has-selection', detailShown && Boolean(item));
    if (!item) {
      layoutKey = ''; contentKey = ''; ++contentGeneration;
      const empty = el('div', 'project-welcome');
      empty.append(el('h2', '', 'Your project team'), el('p', 'muted', 'Use Add agent or Add team to choose from this project’s libraries.'));
      main.replaceChildren(empty); return;
    }
    const nextLayout = `agent:${item.id}`;
    if (layoutKey !== nextLayout) {
      layoutKey = nextLayout; contentKey = '';
      agentHeader = el('header', 'project-agent-header'); agentTabs = el('nav', 'project-agent-tabs'); agentTabs.setAttribute('aria-label', 'Agent workspace'); agentTabs.setAttribute('role', 'tablist');
      pane = el('div', 'project-agent-pane'); main.replaceChildren(agentHeader, agentTabs, pane);
    }
    renderAgentHeader(item); renderAgentTabs();
    const nextContent = `${item.id}:${agentTab}:${agentTab === 'overview' || agentTab === 'channels' ? relationshipKey() : ''}`;
    if (contentKey === nextContent) { return; }
    contentKey = nextContent;
    const generation = ++contentGeneration;
    if (agentTab === 'overview') { renderOverview(item); }
    else if (agentTab === 'channels') { renderAgentChannels(item); }
    else if (agentTab === 'computer') { loadComputer(item, generation).catch(error => { if (generation === contentGeneration && !ctx.isClosed()) { pane.replaceChildren(el('p', 'error', error.message)); } }); }
    else { loadAgentContent(item, generation).catch(error => { if (generation === contentGeneration && !ctx.isClosed()) { pane.replaceChildren(el('p', 'error', error.message)); } }); }
  }

  function renderAgentHeader(item) {
    const copy = el('div', 'project-agent-heading');
    copy.append(el('h2', 'project-agent-title', item.name), el('p', 'muted', statusLabel(item.status)));
    const running = item.status === 'running';
    const action = button(running ? 'Stop agent' : 'Start agent', async () => { await api(`${root}/agents/${item.id}/${running ? 'stop' : 'start'}`, { method: 'POST' }); await update(); }, running ? '' : 'primary');
    action.disabled = unresolved(item);
    agentHeader.replaceChildren(listBack('Agents', () => { detailShown = false; host.classList.remove('has-selection'); }), copy, action);
  }

  function renderAgentTabs() {
    const tabs = [];
    for (const [label, target] of [['Overview', 'overview'], ['Channels', 'channels'], ['Settings', 'settings'], ['Workspace', 'files'], ['Computer', 'computer'], ['Log', 'log']]) {
      const tab = button(label, () => { agentTab = target; saveState(); renderAgent(); }, 'project-agent-tab');
      tab.setAttribute('role', 'tab'); tab.setAttribute('aria-selected', String(agentTab === target)); tab.classList.toggle('selected', agentTab === target); tabs.push(tab);
    }
    agentTabs.replaceChildren(...tabs);
  }

  function renderOverview(item) {
    const info = el('div', 'project-overview');
    info.append(el('h3', '', 'Agent instance'), el('p', 'muted', 'This agent has its own settings, files, and running process in this project.'));
    const origin = el('dl', 'project-facts');
    origin.append(el('dt', '', 'Definition'), el('dd', '', item.library || 'Unavailable'));
    const actions = el('div', 'project-overview-actions');
    actions.append(button('Rename', async () => {
      const name = await ask(ctx, `Rename ${item.name}`, 'Name', item.name); if (!name) { return; }
      await api(`${root}/agents/${item.id}`, json('PUT', { name })); await update();
    }), button('Remove agent', async () => {
      if (!await confirm(ctx, `Remove ${item.name}?`, 'The agent stops and leaves its channels. Channel history and files are kept.', 'Remove')) { return; }
      await api(`${root}/agents/${item.id}`, { method: 'DELETE' }); settings.delete(item.id); management.delete(item.id); await update();
    }, 'danger'));
    if (unresolved(item)) {
      const issue = el('div', 'project-definition-error');
      issue.append(el('h3', '', 'Definition unavailable'), el('p', '', item.error || 'The agent definition could not be found in the project libraries.'), button('Replace definition', () => openDefinitionPicker('agent', item)));
      info.append(issue);
    }
    info.append(origin, actions); pane.replaceChildren(info);
  }

  // Membership is edited in Channels; here the agent's channels are listed so one is a click away.
  function renderAgentChannels(item) {
    const toolbar = el('div', 'project-pane-heading');
    toolbar.append(el('div', '', `${item.name} reads and posts in these channels.`));
    const list = el('div', 'project-connection-list');
    for (const channel of channelsFor(item.id)) {
      const row = el('div', 'project-connection-row'); row.dataset.channelId = channel.id;
      const copy = el('div', 'project-connection-copy');
      const others = channel.memberIds.filter(id => id !== item.id).map(nameOf);
      copy.append(el('strong', '', channel.name), el('small', 'muted', others.length ? `With ${others.join(', ')}` : 'No one else'));
      const actions = el('div', 'project-connection-actions');
      actions.append(button('Open channel', () => openConversation(channel)));
      row.append(copy, actions); list.append(row);
    }
    if (!list.children.length) { list.append(el('div', 'empty', 'Not in any channel yet. Add this agent to a channel in Channels.')); }
    pane.replaceChildren(toolbar, list);
  }

  // The agent's computer: its user, home and workspace in its library's project environment, the services that
  // environment runs, and its disk.
  async function loadComputer(item, generation) {
    const base = `${root}/agents/${item.id}/computer`;
    const currentPane = pane;
    const isCurrent = () => generation === contentGeneration && currentPane === pane && !ctx.isClosed();
    let view = await api(base);
    if (!isCurrent()) { return; }
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
    function render() {
      if (!isCurrent()) { return; }
      const services = section('Library services', 'Processes this agent\u2019s library runs in the project environment. They need no grant.');
      for (const service of view.libraryServices) { services.append(row(service.name, service.image)); }
      const sudo = section('Sudo', 'Root inside this agent\u2019s library environment, only when you allow it.');
      sudo.append(sudoRow(ctx, item.name, view.sudo, async wanted => { view = await api(`${base}/sudo`, json('PUT', { enabled: wanted })); return view.sudo; }));
      const access = section('Ports and folders', 'Agents cannot be given published ports or host folders yet.');
      for (const number of view.needs.ports) { access.append(row(`Port ${number}`, 'Requested by this agent')); }
      const disk = section('Disk');
      const usage = el('div', 'computer-row');
      usage.append(el('span', '', `Workspace ${formatSize(view.workspaceBytes)}`), el('span', '', `Installed ${formatSize(view.homeBytes)}`));
      disk.append(usage);
      if (view.diskWarning) { disk.append(el('p', 'error', 'This agent uses a lot of disk.')); }
      disk.append(button('Reset environment', async () => {
        if (!await confirm(ctx, `Reset ${item.name}\u2019s environment?`, 'Everything it installed is removed. Its workspace stays. Stop the agent first.', 'Reset')) { return; }
        await api(`${base}/reset`, { method: 'POST' }); view = await api(base); render(); notify('Environment reset.');
      }, 'danger'));
      currentPane.replaceChildren(...[view.libraryServices.length ? services : null, sudo, access, disk].filter(Boolean));
    }
    render();
  }

  async function loadAgentContent(item, generation) {
    const currentPane = pane;
    const currentTab = agentTab;
    const base = `${root}/agents/${item.id}`;
    currentPane.replaceChildren(el('p', 'muted', 'Loading…'));
    const isCurrent = () => generation === contentGeneration && currentPane === pane && !ctx.isClosed();
    if (currentTab === 'settings') {
      const settingsPane = el('section', 'agent-settings'); settingsPane.setAttribute('aria-label', 'Agent settings');
      const actionsPane = el('div'); currentPane.replaceChildren(settingsPane, actionsPane);
      await renderSettings({ id: item.id, path: `${base}/settings/form`, drafts: settings, pane: settingsPane, isCurrent });
      if (isCurrent()) { await renderManagementActions({ id: item.id, path: base, drafts: management, pane: actionsPane, isCurrent, getStatus: () => member(item.id)?.status }); }
    } else if (currentTab === 'log') {
      const text = await readText(`${base}/log`); if (!isCurrent()) { return; }
      const pre = el('pre', 'project-log', text || 'Nothing logged yet.');
      currentPane.replaceChildren(button('Refresh log', () => { contentKey = ''; renderAgent(); }), pre); pre.scrollTop = pre.scrollHeight;
    } else {
      const entries = await api(`${base}/table`); if (!isCurrent()) { return; }
      const list = el('div', 'project-files');
      for (const entry of entries) {
        const row = el('div', 'project-file'); const link = el('a', '', entry.path);
        link.href = `${base}/table/file?path=${encodeURIComponent(entry.path)}`; link.download = '';
        row.append(link, el('small', 'muted', formatSize(entry.size))); list.append(row);
      }
      if (!entries.length) { list.append(el('div', 'empty', 'This agent’s workspace is empty.')); }
      currentPane.replaceChildren(el('p', 'muted', 'Files received or created by this agent.'), button('Refresh files', () => { contentKey = ''; renderAgent(); }), list);
    }
  }

  function unresolved(item) { return item.status === 'error' || Boolean(item.error); }

  function renderUnresolvedActions() {
    const missing = agents().filter(unresolved);
    if (Boolean(unresolvedActions.firstChild) === Boolean(missing.length)) { return; }
    unresolvedActions.replaceChildren();
    if (!missing.length) { return; }
    unresolvedActions.append(button('Remove unresolved agents', async () => {
      if (!await confirm(ctx, 'Remove unresolved agents?', 'Unresolved agents will be removed from the team. Their channel history and workspace files are kept.', 'Remove')) { return; }
      await api(`${root}/unresolved`, { method: 'DELETE' }); await update();
    }, 'danger'));
  }

  async function openDefinitionPicker(kind, replacing = null, libraryId = null, libraryVersion = null) {
    if (definitionDialog) { definitionDialog.close(); }
    const overlay = el('div', 'window-overlay');
    const panel = el('div', 'dialog-form lcars-dialog definition-picker');
    const title = replacing ? 'Replace definition' : kind === 'team' ? 'Add team' : 'Add agent';
    panel.setAttribute('role', 'dialog'); panel.setAttribute('aria-label', title);
    const restoreFocus = document.activeElement;
    let active = true;
    let submitting = false;
    function close() {
      active = false; overlay.remove();
      if (definitionDialog === dialog) { definitionDialog = null; }
      if (restoreFocus?.isConnected) { restoreFocus.focus(); }
    }
    const dialog = { close };
    definitionDialog = dialog;
    const heading = el('div', 'definition-picker-heading'); heading.append(el('h3', '', title), button('Close', close));
    const list = el('div', 'definition-picker-list'); list.append(el('p', 'muted', 'Loading definitions...'));
    panel.append(heading);
    if (replacing) { panel.append(el('p', 'muted', 'Choose a definition for this instance. Its settings, workspace, connections, and history are preserved.')); }
    panel.append(list); overlay.append(panel); showOverlay(overlay);
    overlay.addEventListener('keydown', event => {
      if (event.key === 'Escape') { event.preventDefault(); close(); }
      if (event.key === 'Tab') {
        const controls = [...panel.querySelectorAll('button')].filter(node => !node.disabled);
        const first = controls[0]; const last = controls[controls.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
      }
    });
    try {
      const [catalog, attached] = await Promise.all([api('api/library/catalog'), api(`api/projects/${projectId}/libraries`)]);
      if (!active || ctx.isClosed()) { return; }
      list.replaceChildren();
      const definitions = kind === 'team' ? catalog.teams : catalog.agents;
      let available = 0;
      for (const library of attached) {
        if (!library.isAvailable || (libraryId && (library.id !== libraryId || (libraryVersion && library.version !== libraryVersion)))) { continue; }
        const prefix = `${library.id}/${library.version}/`;
        const entries = definitions.filter(item => item.folder.startsWith(prefix));
        if (!entries.length) { continue; }
        const section = el('section', 'definition-picker-section');
        const caption = el('div', 'definition-library-heading'); caption.append(el('h4', '', library.name), el('small', 'muted', library.version)); section.append(caption);
        for (const definition of entries) {
          const choice = button('', async () => {
            if (submitting) { return; }
            submitting = true;
            const choices = list.querySelectorAll('button'); for (const entry of choices) { entry.disabled = true; }
            try {
              if (replacing) {
                await api(`${root}/agents/${replacing.id}/definition`, json('POST', { library: definition.folder }));
                selectedId = replacing.id; agentTab = 'overview';
              } else if (kind === 'team') {
                const members = await api(`${root}/teams`, json('POST', { team: definition.folder }));
                selectedId = members[0]?.id || selectedId; agentTab = 'overview';
              } else {
                const placed = await api(`${root}/agents`, json('POST', { library: definition.folder }));
                selectedId = placed.id; agentTab = 'overview';
              }
              saveState(); await update(); close();
            } finally {
              submitting = false; for (const entry of choices) { entry.disabled = false; }
            }
          }, 'definition-choice');
          choice.dataset.reference = definition.folder;
          choice.append(el('strong', '', definition.name));
          if (definition.description) { choice.append(el('small', 'muted', definition.description)); }
          section.append(choice); available++;
        }
        list.append(section);
      }
      if (!available) {
        list.append(el('p', 'muted', kind === 'team' ? 'No teams are available from this project’s libraries.' : 'No agents are available from this project’s libraries.'), button('Add from library…', () => { close(); ctx.launch('library', { projectId }); }));
      }
      panel.querySelector('button')?.focus();
    } catch (error) {
      if (active && !ctx.isClosed()) { list.replaceChildren(el('p', 'error', error.message), button('Add from library…', () => { close(); ctx.launch('library', { projectId }); })); }
    }
  }

  function pickFromNavigation(state) {
    const kind = state.definitionKind === 'team' ? 'team' : 'agent';
    const libraryId = state.libraryId; const version = state.libraryVersion;
    ctx.setState({ libraryId: null, libraryVersion: null, definitionKind: null });
    return openDefinitionPicker(kind, null, libraryId, version);
  }

  function openConversation(channel) { ctx.launch('conversations', { projectId, channelId: channel.id }); }
  async function update() { changed({ projectId }); await refresh(); }

  const stopWatching = watchProject(projectId, 'room', () => refresh());
  ctx.dispose(() => { stopWatching(); if (definitionDialog) { definitionDialog.close(); } });
  listen(ctx, event => {
    if (event.detail.deletedProjectId === projectId) { ctx.close(); return; }
    if (!event.detail.projectId || event.detail.projectId === projectId) { refresh(); }
  });
  ctx.onNavigate(state => {
    if (Object.hasOwn(state, 'memberId')) { selectedId = state.memberId || null; agentTab = state.agentTab || 'overview'; }
    saveState(); refresh();
    if (state.libraryId) { pickFromNavigation(state).catch(error => notify(error.message, true)); }
  });
  await refresh();
  if (ctx.state.libraryId) { await pickFromNavigation(ctx.state); }
}

async function readText(path) {
  const response = await fetch(path);
  if (!response.ok) { throw new Error(`Could not read agent data (${response.status}).`); }
  return response.text();
}
function statusLabel(status) { return { running: 'Running', stopped: 'Stopped', crashed: 'Crashed', removed: 'Removed', error: 'Error', you: 'You' }[status] || status; }
function formatSize(bytes) {
  if (bytes < 1024) { return `${bytes} B`; }
  if (bytes < 1024 * 1024) { return `${Math.round(bytes / 1024)} KB`; }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
