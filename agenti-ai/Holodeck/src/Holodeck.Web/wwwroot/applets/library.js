import { api, json, el, button, changed, listen, notify, confirm, newId, listBack } from './shared.js';

const TABS = [['Agents', 'agents'], ['Teams', 'teams'], ['Applets', 'applets']];

export const libraryApplet = { id: 'library', title: 'Library Explorer', icon: '▤', description: 'Explore local libraries and manage project references.', singleton: true, size: { width: 832, height: 624 }, mount };

async function mount(ctx) {
  let catalog = { libraries: [], agents: [], teams: [], applets: [], diagnostics: [] };
  let references = [];
  let projectId = ctx.state.projectId || null;
  let projectName = '';
  let libraryKey = ctx.state.libraryKey || null;
  let activeTab = TABS.some(([, kind]) => kind === ctx.state.libraryTab) ? ctx.state.libraryTab : 'agents';
  let selectedDefinition = null;
  let query = '';
  let generation = 0;
  const pending = new Set();
  const panelId = `library-contents-${newId()}`;

  const host = el('div', 'library-app');
  // Phone: the chosen library replaces the list until its back pill is pressed.
  let detailShown = false;
  const top = el('header', 'library-header');
  const layout = el('div', 'library-layout');
  const side = el('aside', 'library-sidebar'); side.setAttribute('aria-label', 'Libraries');
  const search = el('input', 'library-search'); search.type = 'search'; search.placeholder = 'Search libraries'; search.setAttribute('aria-label', 'Search library');
  const libraries = el('nav', 'library-list'); libraries.setAttribute('aria-label', 'Library references');
  side.append(search, libraries);
  const main = el('section', 'library-main'); main.setAttribute('aria-label', 'Library contents');
  layout.append(side, main); host.append(top, layout); ctx.body.replaceChildren(host);

  const keyOf = library => `${library.id}/${library.version}`;
  const matches = item => `${item.name} ${item.description || ''}`.toLowerCase().includes(query);
  const attached = key => references.some(reference => keyOf(reference) === key);

  function allLibraries() {
    const result = [];
    for (const library of catalog.libraries) {
      const key = keyOf(library);
      const prefix = `${key}/`;
      const reference = references.find(item => keyOf(item) === key);
      result.push({ ...library, key, isAvailable: reference?.isAvailable ?? true, error: reference?.error || '', agents: catalog.agents.filter(agent => agent.folder.startsWith(prefix)), teams: catalog.teams.filter(team => team.folder.startsWith(prefix)), applets: catalog.applets.filter(applet => applet.folder.startsWith(prefix)) });
    }
    for (const reference of references) {
      const key = keyOf(reference);
      if (!result.some(library => library.key === key)) { result.push({ ...reference, key, isAvailable: false, agents: [], teams: [], applets: [] }); }
    }
    return result;
  }

  function visibleLibraries() {
    return allLibraries().filter(library => matches(library) || library.agents.some(matches) || library.teams.some(matches) || library.applets.some(matches));
  }

  function render() {
    if (ctx.isClosed()) { return; }
    renderHeader(); renderLibraries(); renderMain();
  }

  function renderHeader() {
    const title = el('div', 'library-title'); title.append(el('h2', '', 'Library Explorer'));
    const tools = el('div', 'library-header-actions');
    if (projectId) {
      const context = el('div', 'library-project-context');
      context.append(el('small', 'muted', 'Project'), el('strong', '', projectName));
      title.append(context);
    }
    tools.append(button('Refresh', load, 'small'));
    top.replaceChildren(title, tools);
  }

  function renderLibraries() {
    libraries.replaceChildren();
    const visible = visibleLibraries();
    if (projectId) {
      appendGroup('Project libraries', visible.filter(library => attached(library.key)), true);
      appendGroup('Available libraries', visible.filter(library => !attached(library.key)), false);
    } else { appendGroup('Local libraries', visible, false); }
    if (catalog.diagnostics.length) {
      const issues = el('details', 'library-diagnostics'); issues.append(el('summary', '', 'Library errors'));
      for (const issue of catalog.diagnostics) {
        const row = el('div', 'library-issue'); row.append(el('strong', '', issue.source), el('p', 'muted', issue.message)); issues.append(row);
      }
      libraries.append(issues);
    }
  }

  function appendGroup(title, entries, isAttached) {
    const section = el('section', 'library-group'); section.append(el('h3', '', title));
    for (const library of entries) {
      const row = el('div', 'library-row'); row.classList.toggle('selected', library.key === libraryKey); row.dataset.libraryKey = library.key;
      const open = button('', () => { libraryKey = library.key; selectedDefinition = null; detailShown = true; ctx.setState({ libraryKey }); render(); }, 'library-entry');
      open.setAttribute('aria-label', `Explore ${library.name}${library.version ? ' ' + library.version : ''}`);
      open.setAttribute('aria-pressed', String(library.key === libraryKey));
      open.append(el('strong', 'library-entry-name', library.name));
      if (library.version) { open.append(el('span', 'library-entry-version', library.version)); }
      row.append(open);
      if (projectId) {
        const action = button(isAttached ? 'Remove' : 'Add', () => changeReference(library, isAttached), `library-reference-action${isAttached ? ' remove' : ''}`);
        action.setAttribute('aria-label', `${isAttached ? 'Remove' : 'Add'} ${library.name} ${library.version}`);
        action.disabled = pending.has(`${projectId}/${library.key}`); row.append(action);
      }
      if (!library.isAvailable) {
        const error = el('span', 'library-unavailable', 'Unavailable'); error.title = library.error || 'The local library is unavailable.'; row.append(error);
      }
      section.append(row);
    }
    if (!entries.length) { section.append(el('p', 'library-note muted', query ? 'No matches' : isAttached ? 'No libraries added' : 'No libraries available')); }
    libraries.append(section);
  }

  function renderMain() {
    const library = allLibraries().find(item => item.key === libraryKey);
    host.classList.toggle('has-selection', detailShown && Boolean(library));
    if (!library) { main.replaceChildren(el('div', 'library-empty', 'Select a library')); return; }
    const heading = el('header', 'library-content-heading');
    const copy = el('div', 'library-heading-copy');
    copy.append(el('h2', '', library.name));
    if (library.version) { copy.append(el('span', 'library-version', library.version)); }
    heading.append(listBack('Libraries', () => { detailShown = false; host.classList.remove('has-selection'); }), copy);
    if (projectId && attached(library.key) && library.isAvailable) {
      heading.append(button('Open Team', () => ctx.launch('room', { projectId, libraryId: library.id, libraryVersion: library.version, definitionKind: activeTab === 'teams' ? 'team' : 'agent' }), 'primary small'));
    }
    const tabs = el('nav', 'library-tabs'); tabs.setAttribute('role', 'tablist'); tabs.setAttribute('aria-label', 'Library definitions');
    for (const [index, [label, kind]] of TABS.entries()) {
      const tab = button('', () => selectTab(kind), 'library-tab');
      tab.setAttribute('role', 'tab'); tab.setAttribute('aria-label', label); tab.setAttribute('aria-selected', String(activeTab === kind)); tab.setAttribute('aria-controls', panelId);
      tab.id = `${panelId}-${kind}`; tab.tabIndex = activeTab === kind ? 0 : -1;
      const badge = el('span', 'library-tab-count', String(library[kind].length)); badge.setAttribute('aria-hidden', 'true');
      tab.append(el('span', 'library-tab-label', label), badge);
      tab.addEventListener('keydown', event => {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
          event.preventDefault(); selectTab(TABS[(index + (event.key === 'ArrowRight' ? 1 : TABS.length - 1)) % TABS.length][1]);
          main.querySelector('[role="tab"][aria-selected="true"]')?.focus();
        }
      });
      tabs.append(tab);
    }
    const panel = el('div', 'library-definition-list'); panel.id = panelId; panel.setAttribute('role', 'tabpanel'); panel.setAttribute('aria-labelledby', `${panelId}-${activeTab}`);
    const definitions = matches(library) ? library[activeTab] : library[activeTab].filter(matches);
    for (const item of definitions) {
      const entry = button('', () => { selectedDefinition = item; renderMain(); }, 'library-definition'); entry.classList.toggle('selected', selectedDefinition?.folder === item.folder);
      entry.append(el('strong', '', item.name));
      if (item.description) { entry.append(el('small', 'muted', item.description)); }
      panel.append(entry);
    }
    if (!definitions.length) { panel.append(el('p', 'library-note muted', library.isAvailable ? 'No items' : 'Library unavailable')); }
    main.replaceChildren(heading);
    if (library.description) { main.append(el('p', 'library-description muted', library.description)); }
    if (!library.isAvailable) { main.append(el('p', 'library-error', library.error || 'This library cannot be resolved.')); }
    main.append(tabs, panel);
    if (selectedDefinition && definitions.some(item => item.folder === selectedDefinition.folder)) {
      const detail = el('div', 'library-definition-detail');
      detail.append(el('strong', '', selectedDefinition.name));
      if (selectedDefinition.description) { detail.append(el('p', 'muted', selectedDefinition.description)); }
      const reference = el('details'); reference.append(el('summary', '', activeTab === 'applets' ? 'Entry point' : 'Definition'), el('code', '', selectedDefinition.folder)); detail.append(reference);
      if (activeTab === 'applets' && projectId && attached(library.key) && library.isAvailable) {
        const applet = selectedDefinition;
        detail.append(button('Open', () => openApplet(library, applet), 'primary small'));
      }
      main.append(detail);
    }
  }

  function selectTab(kind) {
    activeTab = kind; selectedDefinition = null; ctx.setState({ libraryTab: activeTab }); renderMain();
  }

  function openApplet(library, applet) {
    try { ctx.launch(`library:${library.id}:${library.version}:${applet.id}`); }
    catch (error) { notify(error.message, true); }
  }

  async function changeReference(library, remove) {
    const destination = projectId;
    if (!destination) { return; }
    const key = `${destination}/${library.key}`;
    if (pending.has(key)) { return; }
    if (remove && !await confirm(ctx, `Remove ${library.name}?`, 'Agents using this library will keep their settings, files, and history. Their definitions will be unresolved until you add a library again or replace them.', 'Remove')) { return; }
    pending.add(key); renderLibraries();
    try {
      const endpoint = `api/projects/${destination}/libraries`;
      if (remove) { await api(`${endpoint}/${encodeURIComponent(library.id)}/${encodeURIComponent(library.version)}`, { method: 'DELETE' }); }
      else { await api(endpoint, json('POST', { Id: library.id, Version: library.version })); }
      changed({ projectId: destination });
      await load();
    } finally { pending.delete(key); if (!ctx.isClosed()) { renderLibraries(); } }
  }

  async function load() {
    const turn = ++generation;
    const destination = projectId;
    try {
      const requests = [api('api/library/catalog'), api('api/filesystem')];
      if (destination) { requests.push(api(`api/projects/${destination}/libraries`)); }
      const [nextCatalog, listing, nextReferences] = await Promise.all(requests);
      if (turn !== generation || ctx.isClosed()) { return; }
      catalog = nextCatalog; references = nextReferences || [];
      const project = destination ? listing.projects.find(item => item.id === destination) : null;
      if (destination && !project) { projectId = null; references = []; ctx.setState({ projectId: null }); }
      projectName = project?.name || '';
      const entries = allLibraries();
      if (!entries.some(library => library.key === libraryKey)) { libraryKey = entries[0]?.key || null; selectedDefinition = null; }
      render();
    } catch (error) {
      if (turn !== generation || ctx.isClosed()) { return; }
      main.replaceChildren(el('div', 'library-empty', error.message), button('Try again', load)); notify(error.message, true);
    }
  }

  search.addEventListener('input', () => {
    query = search.value.trim().toLowerCase(); selectedDefinition = null;
    const entries = visibleLibraries();
    if (!entries.some(library => library.key === libraryKey)) { libraryKey = entries[0]?.key || null; }
    render();
  });
  ctx.onNavigate(state => {
    projectId = state.projectId || null; selectedDefinition = null; ctx.setState({ projectId }); return load();
  });
  listen(ctx, event => {
    if (event.detail.deletedProjectId === projectId) { projectId = null; references = []; ctx.setState({ projectId: null }); }
    load();
  });
  await load();
}
