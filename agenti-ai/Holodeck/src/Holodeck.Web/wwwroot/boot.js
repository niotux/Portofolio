import { api, el, button, notify, changes } from './applets/shared.js';
import { browse } from './applets/browser.js';
import { libraryApplet } from './applets/library.js';
import { appearanceApplet } from './applets/appearance.js';
import { bootProject, leaveProject, currentProject, setMainMenuAction } from './workspace.js';

// The main menu. Nothing runs on the desktop here; choosing a project boots it into its own workspace.
const RECENT_KEY = 'astudio.recent.v1';
const MAX_RECENT = 6;
const screen = document.querySelector('#boot');
const nav = screen.querySelector('.boot-nav');
const panel = screen.querySelector('.boot-panel');
const title = panel.querySelector('.boot-title');
const projectName = document.querySelector('#project-name');
// The PC's landing page, served by its front server on the host name Holodeck was opened with.
screen.querySelector('#boot-home').href = `${location.protocol}//${location.hostname}/`;
const sections = [['projects', 'Projects', mountProjects], ['libraries', 'Libraries', ctx => libraryApplet.mount(ctx)], ['appearance', 'Appearance', ctx => appearanceApplet.mount(ctx)]];
let current = null;

function readRecent() { try { const value = JSON.parse(localStorage.getItem(RECENT_KEY)); return Array.isArray(value) ? value : []; } catch { return []; } }
function remember(projectId) {
  const recent = [projectId];
  for (const id of readRecent()) { if (id !== projectId && recent.length < MAX_RECENT) { recent.push(id); } }
  try { localStorage.setItem(RECENT_KEY, JSON.stringify(recent)); } catch { }
}

// Applets mounted on the main menu get a small inline context instead of a window.
function inlineContext(body) {
  const disposers = []; let closed = false;
  return {
    body, state: {}, setState() { }, setTitle() { }, setBusy() { }, onBeforeClose() { }, onNavigate() { }, launch() { return null; }, close() { }, isClosed: () => closed,
    dispose(callback) { disposers.push(callback); },
    teardown() { closed = true; for (const dispose of disposers) { dispose(); } }
  };
}

function show(id) {
  if (current) { current.teardown(); }
  for (const tab of nav.children) { tab.setAttribute('aria-selected', String(tab.dataset.section === id)); }
  const body = el('div', 'boot-panel-body'); panel.replaceChildren(title, body);
  current = inlineContext(body);
  const mount = sections.find(section => section[0] === id)[2];
  Promise.resolve(mount(current)).catch(error => notify(error.message, true));
}

async function mountProjects(ctx) {
  const recent = el('section', 'boot-recent'); recent.setAttribute('aria-label', 'Recent projects');
  const list = el('div', 'boot-projects');
  const layout = el('div', 'boot-start'); layout.append(recent, list); ctx.body.append(layout);
  async function renderRecent() {
    const listing = await api('api/filesystem');
    if (ctx.isClosed()) { return; }
    recent.replaceChildren();
    for (const id of readRecent()) {
      const project = listing.projects.find(item => item.id === id);
      if (!project) { continue; }
      const card = button('', () => open(project.id), 'boot-recent-card'); card.setAttribute('aria-label', `Open ${project.name}`);
      card.append(el('strong', '', project.name), el('small', '', project.description || 'Project'));
      recent.append(card);
    }
    recent.hidden = !recent.children.length; layout.classList.toggle('has-recent', !recent.hidden);
    if (recent.children.length) { recent.prepend(el('h3', '', 'Recent')); }
  }
  const refresh = () => { renderRecent().catch(error => notify(error.message, true)); };
  changes.addEventListener('change', refresh); ctx.dispose(() => changes.removeEventListener('change', refresh));
  await renderRecent();
  await browse(inlineChild(ctx, list), open);
}
function inlineChild(parent, body) { return { ...parent, body }; }

function showMenu() {
  document.body.classList.add('booting'); screen.hidden = false; projectName.textContent = '';
  history.replaceState(null, '', location.pathname);
  show('projects');
}

// The desktop is shown before booting so windows are placed against its real size.
export async function open(projectId) {
  screen.hidden = true; document.body.classList.remove('booting');
  try { await bootProject(projectId); }
  catch (error) { showMenu(); throw error; }
  remember(projectId);
  if (current) { current.teardown(); current = null; }
  panel.replaceChildren();
  projectName.textContent = currentProject().name;
  history.replaceState(null, '', `${location.pathname}?project=${encodeURIComponent(projectId)}`);
}

async function returnToMenu() { await leaveProject(); showMenu(); }

export async function startBoot() {
  for (const [id, label] of sections) {
    const tab = el('button', 'boot-tab', label); tab.type = 'button'; tab.dataset.section = id; tab.setAttribute('role', 'tab');
    tab.addEventListener('click', () => show(id)); nav.append(tab);
  }
  const toMenu = () => { returnToMenu().catch(error => notify(error.message, true)); };
  for (const node of document.querySelectorAll('.main-menu-button')) { node.addEventListener('click', toMenu); }
  setMainMenuAction(toMenu);
  const requested = new URLSearchParams(location.search).get('project');
  if (!requested) { showMenu(); return; }
  try { await open(requested); }
  catch (error) { showMenu(); notify(`Could not open the project. ${error.message}`, true); }
}
