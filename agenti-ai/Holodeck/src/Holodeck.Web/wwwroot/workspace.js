import { api, json, button, el, notify, newId, changes } from './applets/shared.js';
import { appletIcon, loadAppletIcon } from './icons.js';
import { addStartItem, startItems } from './start-menu.js';
import { CELL, toStored, toTiled, touches, fromStored, clampRect, isStored } from './layout.js';
import { cue } from './sounds.js';
import { discoverLibraryApplets } from './library-applets.js';

const GRID = CELL;
const MIN_CELLS = { w: 22, h: 16 };
// Tiled windows may shrink this far to stay joined with their neighbours on a small desktop.
const TILE_MIN_CELLS = { w: 10, h: 8 };
const MAX_RESTORED_WINDOWS = 24;
const LAYOUT_VERSION = 1;
const DISCARDED_STORAGE = ['astudio.applets.v2', 'astudio.applets.v3'];
const desktop = document.querySelector('#window-layer');
// Phone layout: one applet fills the screen over a project home that lists open and available applets. Back (the
// window's Back pill, the Home pill or the device's back gesture) returns to that home. The saved desktop layout is
// never changed by it.
const phone = matchMedia('(max-width: 700px)');
const phoneHome = document.querySelector('#phone-home');
const isPhone = () => phone.matches;
const tasks = document.querySelector('#tasks');
const types = new Map();
const windows = new Map();
const libraryEntries = new Map();
const libraryTypes = new Set();
let libraryRefresh = Promise.resolve();
let order = 0;
let activeId;
let restoring = false;
let project = null;
let saveQueued = false;
let saving = Promise.resolve();
let lastSaveError = null;
// Returning to the main menu belongs to the main menu; on a phone the project home offers it after the applets.
let mainMenuAction = null;
export function setMainMenuAction(action) { mainMenuAction = action; }
const snap = value => Math.round(value / GRID) * GRID;
const floor = value => Math.floor(value / GRID) * GRID;
const minWidth = () => Math.min(352, floor(desktop.clientWidth));
const minHeight = () => Math.min(256, floor(desktop.clientHeight));

// A window's console code, such as 03-482915: the applet's place in the start menu, then a number derived from the
// project, applet and what it shows (a file, say), so the same window keeps its code across reloads. Themes that show it
// read data-code.
function windowCode(type, projectId, subject = '') {
  let hash = 2166136261;
  for (const char of `${projectId}:${type}:${subject}`) { hash = Math.imul(hash ^ char.charCodeAt(0), 16777619) >>> 0; }
  const order = [...types.keys()].indexOf(type) + 1;
  return `${String(order).padStart(2, '0')}-${String(hash % 1000000).padStart(6, '0')}`;
}
// A minimized window stays in the taskbar, marked so themes can show it.
function setHidden(instance, hidden) { instance.node.hidden = hidden; instance.task.classList.toggle('minimized', hidden); }
export function registerApplet(definition) {
  if (types.has(definition.id)) { throw new Error(`Applet already registered: ${definition.id}`); }
  types.set(definition.id, definition);
  if (!definition.libraryId) { loadAppletIcon(definition); }
  // Hidden applets open from what they show (a file, say), not from the start menu.
  if (!definition.hidden) { return addStartItem(definition, () => launch(definition.id)); }
  return () => { };
}

export function launch(type, state = {}) {
  const definition = types.get(type);
  if (!definition) { throw new Error(`Unknown applet: ${type}`); }
  if (!project) { return null; }
  state = { ...state, projectId: project.id };
  if (!restoring && (definition.singleton || (definition.projectScoped && state.projectId))) {
    for (const instance of windows.values()) {
      const sameProject = instance.state.projectId === state.projectId;
      if (instance.type === type && (definition.singleton || sameProject)) {
        Object.assign(instance.state, state);
        setHidden(instance, false); focus(instance.id); persist();
        if (instance.navigate) { Promise.resolve(instance.navigate(state)).catch(error => notify(error.message, true)); }
        else { instance.pendingNavigation = state; }
        return instance.ctx;
      }
    }
  }
  const id = newId();
  const node = el('section', 'applet-window lcars-window'); node.dataset.instance = id; node.dataset.applet = type;
  node.setAttribute('aria-label', definition.title);
  const header = el('header', 'window-titlebar lcars-titlebar'); header.tabIndex = 0; header.title = 'Drag to move. Double click to maximize. Arrow keys move. Shift + arrows resize.';
  const title = el('strong', 'window-title', definition.title); title.dataset.code = windowCode(type, state.projectId, state.url); const controls = el('div', 'window-controls lcars-window-controls');
  // The applet's name inside the window, shown by themes that keep the title bar for the code.
  const heading = el('div', 'window-heading'); heading.append(el('h2', '', definition.title), el('span', 'window-heading-band')); heading.setAttribute('aria-hidden', 'true');
  const body = el('div', 'window-body lcars-window-body');
  const task = button('', () => { if (activeId === id && !node.hidden) { minimize(); } else { show(); } }, 'task');
  task.dataset.instance = id; task.append(appletIcon(definition, 'task-icon'), el('span', 'task-title', definition.title));
  const disposers = [];
  let beforeClose = null;
  let closing = false;
  const defaults = definition.size || { width: 640, height: 544 };
  const instance = { id, type, state: { ...state }, node, task, rect: { x: 16 + (windows.size % 5) * 32, y: 32 + (windows.size % 5) * 32, ...defaults }, stored: null, maximized: false };
  apply(instance); anchor(instance);
  const ctx = {
    body, state: instance.state,
    setTitle(value) { title.textContent = value; task.querySelector('.task-title').textContent = value; node.setAttribute('aria-label', `${definition.title}: ${value}`); },
    setState(value) { Object.assign(instance.state, value); persist(); },
    async flushState() { await Promise.resolve(); await saving; if (lastSaveError) { throw lastSaveError; } },
    setBusy(value) { task.classList.toggle('busy', value); node.classList.toggle('busy', value); },
    dispose(callback) { disposers.push(callback); },
    onBeforeClose(callback) { beforeClose = callback; },
    onNavigate(callback) {
      instance.navigate = callback;
      if (instance.pendingNavigation) {
        const state = instance.pendingNavigation; instance.pendingNavigation = null;
        Promise.resolve(callback(state)).catch(error => notify(error.message, true));
      }
    },
    launch, close,
    isClosed: () => !windows.has(id)
  };
  instance.ctx = ctx;
  function minimize() { cue('close'); setHidden(instance, true); task.classList.remove('active'); if (activeId === id) { activeId = null; focusNext(); } persist(); }
  instance.show = show;
  function show() { if (node.hidden) { cue('open'); } setHidden(instance, false); focus(id); persist(); }
  function maximize() { cue('press'); instance.maximized = !instance.maximized; place(instance); persist(); }
  async function close() {
    if (!windows.has(id) || closing) { return; }
    closing = true;
    try {
      if (beforeClose && await beforeClose() === false) { return; }
    } finally {
      closing = false;
    }
    if (!windows.has(id)) { return; }
    cue('close'); teardown(); if (activeId === id || isPhone()) { activeId = null; if (isPhone()) { leaveApplet(); } else { focusNext(); } } persist();
  }
  function teardown() { for (const dispose of disposers) { dispose(); } windows.delete(id); node.remove(); task.remove(); }
  instance.teardown = teardown;
  const minimizeButton = button('−', minimize); minimizeButton.setAttribute('aria-label', 'Minimize window');
  const maximizeButton = button('□', maximize); maximizeButton.setAttribute('aria-label', 'Maximize window');
  const closeButton = button('×', close); closeButton.setAttribute('aria-label', 'Close window');
  const backButton = button('Back', goBack, 'window-back'); backButton.setAttribute('aria-label', 'Back to project home');
  controls.append(minimizeButton, maximizeButton, closeButton); header.append(backButton, appletIcon(definition, 'window-icon'), title, controls); node.append(header, heading, body);
  header.addEventListener('dblclick', event => { if (!isPhone() && !event.target.closest('button')) { maximize(); } });
  header.addEventListener('pointerdown', event => { if (!event.target.closest('button')) { drag(event, instance, 'move'); } });
  header.addEventListener('keydown', event => {
    if (isPhone() || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || event.target !== header) { return; }
    event.preventDefault(); const dx = event.key === 'ArrowLeft' ? -GRID : event.key === 'ArrowRight' ? GRID : 0; const dy = event.key === 'ArrowUp' ? -GRID : event.key === 'ArrowDown' ? GRID : 0;
    if (instance.maximized) { instance.maximized = false; place(instance); }
    if (event.shiftKey) { instance.rect.width += dx; instance.rect.height += dy; } else { instance.rect.x += dx; instance.rect.y += dy; }
    apply(instance); anchor(instance); persist();
  });
  for (const edge of ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw']) {
    const handle = el('div', `resize-handle resize-${edge}`); handle.dataset.edge = edge; handle.addEventListener('pointerdown', event => drag(event, instance, edge)); node.append(handle);
  }
  node.addEventListener('pointerdown', () => focus(id));
  windows.set(id, instance); desktop.append(node); tasks.append(task); place(instance); focus(id); persist();
  if (!restoring) { cue('open'); }
  Promise.resolve().then(() => definition.mount(ctx)).catch(error => { if (!ctx.isClosed()) { body.append(el('p', 'error', error.message)); notify(error.message, true); } });
  return ctx;
}
function focus(id) {
  const instance = windows.get(id); if (!instance) { return; }
  activeId = id; instance.node.style.zIndex = ++order;
  for (const item of windows.values()) { const active = item.id === id; item.node.classList.toggle('active', active); item.task.classList.toggle('active', active); item.task.setAttribute('aria-pressed', String(active)); }
  document.querySelector('#status').textContent = types.get(instance.type).title;
  if (isPhone() && !restoring) { enterApplet(); }
}
function focusNext() {
  if (isPhone()) { showHome(); return; }
  const visible = [...windows.values()].filter(item => !item.node.hidden);
  if (visible.length) { focus(visible.at(-1).id); }
  else { activeId = null; for (const item of windows.values()) { item.node.classList.remove('active'); item.task.classList.remove('active'); item.task.setAttribute('aria-pressed', 'false'); } document.querySelector('#status').textContent = 'Ready'; }
}
function desktopCells() { return { W: Math.floor(desktop.clientWidth / CELL), H: Math.floor(desktop.clientHeight / CELL) }; }
// Display only: derive the pixel rect from the stored anchored form. Never persisted, so resizes cannot damage the layout.
function place(instance) {
  if (isPhone()) { Object.assign(instance.node.style, { left: '', top: '', width: '', height: '' }); return; }
  const { W, H } = desktopCells();
  const floorCells = instance.stored?.tile ? TILE_MIN_CELLS : MIN_CELLS;
  const min = { w: Math.min(floorCells.w, W), h: Math.min(floorCells.h, H) };
  const cells = instance.maximized ? { x: 0, y: 0, w: W, h: H } : clampRect(fromStored(instance.stored, W, H), min, W, H);
  instance.rect = { x: cells.x * CELL, y: cells.y * CELL, width: cells.w * CELL, height: cells.h * CELL };
  apply(instance, { width: min.w * CELL, height: min.h * CELL });
}
function cellsOf(instance) { const rect = instance.rect; return { x: Math.round(rect.x / CELL), y: Math.round(rect.y / CELL), w: Math.round(rect.width / CELL), h: Math.round(rect.height / CELL) }; }
// After a user move or resize, as the window is now shown: a window touching another is tiled together with the windows
// it touches, so desktop resizes keep them joined; a window on its own is anchored to the edge nearest it.
function anchor(instance) {
  const { W, H } = desktopCells(); const cells = cellsOf(instance);
  let touching = false;
  for (const other of windows.values()) {
    if (other === instance || other.node.hidden || other.maximized) { continue; }
    const near = cellsOf(other);
    if (touches(cells, near)) { touching = true; other.stored = toTiled(near, W, H); }
  }
  instance.stored = touching ? toTiled(cells, W, H) : toStored(cells, W, H);
}
function apply(instance, min = { width: minWidth(), height: minHeight() }) {
  const rect = instance.rect;
  rect.width = Math.max(min.width, Math.min(floor(desktop.clientWidth), snap(rect.width)));
  rect.height = Math.max(min.height, Math.min(floor(desktop.clientHeight), snap(rect.height)));
  rect.x = Math.max(0, Math.min(floor(desktop.clientWidth - rect.width), snap(rect.x)));
  rect.y = Math.max(0, Math.min(floor(desktop.clientHeight - rect.height), snap(rect.y)));
  Object.assign(instance.node.style, { left: `${rect.x}px`, top: `${rect.y}px`, width: `${rect.width}px`, height: `${rect.height}px` });
}
function drag(event, instance, edge) {
  if (event.button !== 0 || instance.maximized || isPhone()) { return; }
  event.preventDefault(); focus(instance.id); const target = event.currentTarget; target.setPointerCapture(event.pointerId);
  const initial = { ...instance.rect }; const startX = event.clientX; const startY = event.clientY;
  instance.node.classList.add('dragging'); document.body.classList.add('moving-window');
  function move(pointer) {
    const dx = snap(pointer.clientX - startX); const dy = snap(pointer.clientY - startY); const right = initial.x + initial.width; const bottom = initial.y + initial.height;
    const rect = { ...initial };
    if (edge === 'move') { rect.x += dx; rect.y += dy; }
    else {
      if (edge.includes('e')) { rect.width = Math.max(minWidth(), Math.min(floor(desktop.clientWidth) - rect.x, initial.width + dx)); }
      if (edge.includes('s')) { rect.height = Math.max(minHeight(), Math.min(floor(desktop.clientHeight) - rect.y, initial.height + dy)); }
      if (edge.includes('w')) { rect.x = Math.max(0, Math.min(right - minWidth(), initial.x + dx)); rect.width = right - rect.x; }
      if (edge.includes('n')) { rect.y = Math.max(0, Math.min(bottom - minHeight(), initial.y + dy)); rect.height = bottom - rect.y; }
    }
    instance.rect = rect; apply(instance);
  }
  function end() {
    target.removeEventListener('pointermove', move); target.removeEventListener('pointerup', end); target.removeEventListener('pointercancel', end); target.removeEventListener('lostpointercapture', end);
    instance.node.classList.remove('dragging'); document.body.classList.remove('moving-window'); anchor(instance); persist();
  }
  target.addEventListener('pointermove', move); target.addEventListener('pointerup', end); target.addEventListener('pointercancel', end); target.addEventListener('lostpointercapture', end);
}
// Every change saves right away (changes in one task go out as one save), in order, as keepalive requests that
// survive a reload or a closed tab.
function persist() {
  if (restoring || !project || saveQueued) { return; }
  saveQueued = true;
  queueMicrotask(() => {
    saveQueued = false;
    const path = `api/projects/${project.id}/workspace`;
    const saved = [...windows.values()].map(({ type, state, stored, maximized, node }) => ({ type, state, stored, maximized, hidden: node.hidden }));
    const request = { ...json('PUT', { layout: { version: LAYOUT_VERSION, windows: saved } }), keepalive: true };
    saving = saving.then(async () => { await api(path, request); lastSaveError = null; }).catch(error => { lastSaveError = error; notify(error.message, true); });
  });
}
export function currentProject() { return project; }
// Boot a project: close whatever is open, then bring back its windows as they were saved.
export async function bootProject(projectId) {
  const listing = await api('api/filesystem');
  const found = listing.projects.find(item => item.id === projectId);
  if (!found) { throw new Error('That project no longer exists.'); }
  const activation = await api(`api/projects/${projectId}/activate`, { method: 'POST' });
  for (const failure of activation?.failures || []) { notify(`${failure.libraryId}: ${failure.message}`, true); }
  const definitions = await discoverLibraryApplets(projectId);
  const workspace = await api(`api/projects/${projectId}/workspace`);
  await leaveProject();
  project = { id: found.id, name: found.name };
  lastSaveError = null;
  installLibraryApplets(definitions);
  restoreLayout(workspace.layout);
}
export async function leaveProject() {
  if (!project) { return; }
  await Promise.resolve(); await saving;
  for (const instance of [...windows.values()]) { instance.teardown(); }
  clearLibraryApplets();
  activeId = null; project = null; document.querySelector('#status').textContent = 'Ready';
  document.body.classList.remove('phone-applet'); phoneHome.replaceChildren();
}
function clearLibraryApplets() {
  for (const remove of libraryEntries.values()) { remove(); }
  for (const id of libraryTypes) { types.delete(id); }
  libraryEntries.clear(); libraryTypes.clear();
}
function installLibraryApplets(definitions) {
  for (const definition of definitions) {
    const remove = libraryEntries.get(definition.id);
    if (remove) { remove(); libraryEntries.delete(definition.id); }
    types.delete(definition.id);
    libraryTypes.add(definition.id);
    libraryEntries.set(definition.id, registerApplet(definition));
  }
  const available = new Set(definitions.map(definition => definition.id));
  for (const [id, remove] of libraryEntries) {
    if (!available.has(id)) { remove(); libraryEntries.delete(id); }
  }
}
changes.addEventListener('change', () => {
  if (!project) { return; }
  const projectId = project.id;
  libraryRefresh = libraryRefresh.then(async () => {
    const definitions = await discoverLibraryApplets(projectId);
    if (!project || project.id !== projectId) { return; }
    installLibraryApplets(definitions);
    if (isPhone() && !activeId) { renderHome(); }
  }).catch(error => notify(error.message, true));
});

function restoreLayout(layout) {
  const saved = layout && layout.version === LAYOUT_VERSION && Array.isArray(layout.windows) ? layout.windows : [];
  restoring = true;
  for (const item of saved.slice(0, MAX_RESTORED_WINDOWS)) {
    if (!types.has(item.type)) { continue; }
    launch(item.type, item.state || {}); const instance = [...windows.values()].at(-1);
    if (isStored(item.stored)) { instance.stored = item.stored; }
    instance.maximized = Boolean(item.maximized); place(instance);
    setHidden(instance, Boolean(item.hidden));
  }
  restoring = false;
  focusNext();
}

// Phone: the active applet fills the screen; with none active the project home shows. Entering an applet adds one
// history entry so the device's back gesture returns home instead of leaving Holodeck.
function enterApplet() {
  document.body.classList.add('phone-applet');
  if (!history.state?.phoneApplet) { history.pushState({ ...history.state, phoneApplet: true }, ''); }
}
function leaveApplet() {
  if (history.state?.phoneApplet) { history.back(); return; }
  showHome();
}
function goBack() { cue('close'); leaveApplet(); }
function showHome() {
  activeId = null;
  document.body.classList.remove('phone-applet');
  for (const item of windows.values()) { item.node.classList.remove('active'); item.task.classList.remove('active'); item.task.setAttribute('aria-pressed', 'false'); }
  document.querySelector('#status').textContent = 'Ready';
  renderHome();
}
function homeTile(label, detail, kind, open) {
  const tile = button('', open, `phone-tile phone-tile-${kind}`);
  tile.append(el('strong', '', label));
  if (detail) { tile.append(el('small', '', detail)); }
  return tile;
}
function renderHome() {
  if (!project) { phoneHome.replaceChildren(); return; }
  const parts = [el('h2', 'phone-home-title', project.name)];
  if (windows.size) {
    parts.push(el('h3', 'phone-band', 'Open'));
    const open = el('div', 'phone-open');
    for (const instance of windows.values()) {
      const name = instance.task.querySelector('.task-title').textContent;
      const row = el('div', 'phone-open-row');
      const close = button('Close', () => instance.ctx.close(), 'phone-close'); close.setAttribute('aria-label', `Close ${name}`);
      row.append(homeTile(name, '', 'open', () => { cue('open'); instance.show(); }), close);
      open.append(row);
    }
    parts.push(open);
  }
  parts.push(el('h3', 'phone-band', 'Applets'));
  const grid = el('div', 'phone-applets');
  for (const item of startItems) { grid.append(homeTile(item.definition.title, item.definition.description, item.definition.id, item.open)); }
  if (mainMenuAction) { grid.append(homeTile('Main menu', 'Save this project and return to the main menu.', 'main-menu', mainMenuAction)); }
  parts.push(grid);
  phoneHome.replaceChildren(...parts);
}
window.addEventListener('popstate', () => { if (project && isPhone() && activeId) { showHome(); } });
// The Home pill: on a phone the start button returns to the project home instead of opening the start menu.
document.querySelector('#start').addEventListener('click', event => {
  if (!isPhone()) { return; }
  event.stopImmediatePropagation(); if (activeId) { goBack(); } else { renderHome(); }
}, true);
function applyPhone() {
  document.body.classList.toggle('phone', isPhone());
  for (const item of windows.values()) { place(item); }
  if (!project) { return; }
  if (isPhone()) { if (activeId) { enterApplet(); } else { showHome(); } }
  else { document.body.classList.remove('phone-applet'); if (!activeId) { focusNext(); } }
}
phone.addEventListener('change', applyPhone);
applyPhone();
for (const key of DISCARDED_STORAGE) { try { localStorage.removeItem(key); } catch { } }
// Split total cells into parts whose sizes differ by at most one cell and add up to total; part index starts where returned.
function share(total, parts, index) { const base = Math.floor(total / parts); const extra = total % parts; return { start: index * base + Math.min(index, extra), size: base + (index < extra ? 1 : 0) }; }
// Tile the visible windows over the whole desktop: no leftover cells, and a shorter last row shares the full width.
document.querySelector('#arrange').addEventListener('click', () => {
  const visible = [...windows.values()].filter(item => !item.node.hidden); if (!visible.length) { return; }
  const { W, H } = desktopCells();
  const columns = Math.max(1, Math.min(visible.length, Math.floor(desktop.clientWidth / 352)));
  const rows = Math.ceil(visible.length / columns);
  visible.forEach((instance, index) => {
    const row = Math.floor(index / columns); const inRow = row === rows - 1 ? visible.length - row * columns : columns;
    const across = share(W, inRow, index % columns); const down = share(H, rows, row);
    instance.maximized = false;
    instance.rect = { x: across.start * CELL, y: down.start * CELL, width: across.size * CELL, height: Math.max(minHeight(), down.size * CELL) };
    apply(instance);
  });
  // Anchored once every window is in place, so the whole arrangement is tiled together.
  for (const instance of visible) { anchor(instance); }
  persist();
});
// Re-place on any desktop size change: browser resizes and themes that frame the desktop differently. Hidden desktops are skipped.
new ResizeObserver(() => { if (desktop.clientWidth === 0) { return; } for (const item of windows.values()) { place(item); } }).observe(desktop);
