import { api, json, el, button, empty, ask, confirm, changed, listen, notify, showOverlay } from './shared.js';

const DEFAULT_PROJECT_ID = '00000000000000000000000000000001';

// One folder or project: its name opens it, then its kind, a short detail and the Rename/Move/Delete pill.
function entry(kind, title, kindLabel, detail, action, tools) {
  const node = el('div', `explorer-row explorer-${kind}`);
  const open = button(title, action, 'explorer-name'); open.title = `Open ${title}`;
  const pair = el('div', 'explorer-tools lcars-split'); pair.append(...tools);
  node.append(open, el('span', 'explorer-kind', kindLabel), el('span', 'explorer-detail', detail), pair); return node;
}

// The folder path as a band: a segment for each level from All projects down to the current folder, each one
// opening that level, then the band runs on to the end of the row.
function pathBand(folders, folderId, enter) {
  const band = el('nav', 'explorer-path'); band.setAttribute('aria-label', 'Folder path');
  const levels = [];
  let folder = folders.find(item => item.id === folderId);
  while (folder) { levels.unshift(folder); folder = folders.find(item => item.id === folder.parentId); }
  levels.unshift({ id: null, name: 'All projects' });
  for (const level of levels) {
    const segment = button(level.name, () => enter(level.id), 'explorer-segment');
    if (level.id === folderId) { segment.setAttribute('aria-current', 'location'); }
    band.append(segment);
  }
  band.append(el('span', 'explorer-path-end'));
  return band;
}

// True when folder is target or lies inside it.
function isInside(folders, folder, targetId) {
  while (folder) { if (folder.id === targetId) { return true; } folder = folders.find(item => item.id === folder.parentId); }
  return false;
}

// A small explorer for choosing where an item goes: open folders through their pills or step back up the path band,
// then move the item into the folder on show. A moving folder is left out so it cannot go inside itself.
// Resolves the folder id, null for All projects, or undefined when cancelled.
function pickDestination(ctx, name, folders, currentId, movingFolderId) {
  return new Promise(resolve => {
    const overlay = el('div', 'window-overlay'); const form = el('div', 'dialog-form lcars-dialog explorer-move-dialog');
    const finish = result => { overlay.remove(); resolve(result); };
    const band = el('div'); const list = el('div', 'explorer-destinations');
    let shown = currentId;
    const here = button('Move here', () => finish(shown), 'primary');
    function show(id) {
      shown = id;
      band.replaceChildren(pathBand(folders, shown, show));
      list.replaceChildren();
      for (const folder of folders) {
        if (folder.parentId !== shown || folder.id === movingFolderId) { continue; }
        const row = el('div', 'explorer-folder'); const open = button(folder.name, () => show(folder.id), 'explorer-name'); open.title = `Open ${folder.name}`;
        row.append(open); list.append(row);
      }
      if (!list.children.length) { empty(list, 'No folders inside.'); }
      const unchanged = shown === currentId;
      here.textContent = unchanged ? 'Already here' : 'Move here'; here.disabled = unchanged;
    }
    const actions = el('div', 'actions'); actions.append(button('Cancel', () => finish(undefined)), here);
    form.append(el('h3', '', `Move ${name}`), el('p', 'subtitle', 'Open the folder it should go into, then move it here.'), band, list, actions);
    overlay.append(form); showOverlay(overlay); show(currentId);
    list.querySelector('button')?.focus(); ctx.dispose(() => finish(undefined));
  });
}

// What deleting a folder removes: every folder and project inside it. The Default project is kept at the top level.
function folderContents(listing, folder) {
  const contents = { folders: 0, projects: [], keepsDefault: false };
  for (const item of listing.folders) { if (item.id !== folder.id && isInside(listing.folders, item, folder.id)) { contents.folders++; } }
  for (const project of listing.projects) {
    const holder = listing.folders.find(item => item.id === project.folderId);
    if (!holder || !isInside(listing.folders, holder, folder.id)) { continue; }
    if (project.id === DEFAULT_PROJECT_ID) { contents.keepsDefault = true; } else { contents.projects.push(project.id); }
  }
  return contents;
}

function deleteFolderWarning(contents) {
  const parts = [];
  if (contents.folders) { parts.push(contents.folders === 1 ? '1 folder' : `${contents.folders} folders`); }
  if (contents.projects.length) { parts.push(contents.projects.length === 1 ? '1 project' : `${contents.projects.length} projects`); }
  let text = parts.length ? `This permanently deletes the folder and the ${parts.join(' and ')} inside it, with their rooms and agents.` : 'This deletes the empty folder.';
  if (contents.keepsDefault) { text += ' The Default project is kept and moves to All projects.'; }
  return text;
}

// The project browser on the main menu. openProject boots the chosen project.
export async function browse(ctx, openProject) {
  let folderId = ctx.state.folderId || null;
  let generation = 0;
  async function render() {
    if (ctx.isClosed()) { return; }
    const turn = ++generation;
    const listing = await api('api/filesystem');
    if (turn !== generation || ctx.isClosed()) { return; }
    if (folderId && !listing.folders.some(folder => folder.id === folderId)) { folderId = null; }
    const host = el('div', 'content picker');
    const head = el('div', 'explorer-head'); host.append(head);
    const heading = el('div', 'page-heading'); const copy = el('div');
    copy.append(el('h2', '', 'Projects'), el('p', 'subtitle', 'Open a project to work with its team.')); heading.append(copy); head.append(heading);
    const actions = el('div', 'explorer-bar');
    const search = el('input', 'explorer-search'); search.type = 'search'; search.placeholder = 'Filter folders and projects'; search.setAttribute('aria-label', 'Filter projects');
    actions.append(button('New project', async () => {
      const name = await ask(ctx, 'Create a project', 'Project name'); if (!name) { return; }
      const project = await api('api/projects', json('POST', { name, description: '', folderId }));
      changed(); await openProject(project.id);
    }, 'primary explorer-new'), button('New folder', async () => {
      const name = await ask(ctx, 'Create a folder', 'Folder name'); if (!name) { return; }
      await api('api/folders', json('POST', { name, parentId: folderId })); changed();
    }, 'explorer-new explorer-new-folder'), search);
    head.append(actions);
    head.append(pathBand(listing.folders, folderId, enter));
    const list = el('div', 'explorer-list'); const items = [];
    for (const folder of listing.folders.filter(item => item.parentId === folderId)) {
      const count = listing.folders.filter(item => item.parentId === folder.id).length + listing.projects.filter(item => item.folderId === folder.id).length;
      const tools = [button('Rename', async () => {
        const name = await ask(ctx, 'Rename folder', 'Folder name', folder.name); if (!name) { return; }
        await api(`api/folders/${folder.id}`, json('PUT', { name, parentId: folder.parentId })); changed();
      }), button('Move', async () => {
        const parentId = await pickDestination(ctx, folder.name, listing.folders, folder.parentId, folder.id); if (parentId === undefined) { return; }
        await api(`api/folders/${folder.id}`, json('PUT', { name: folder.name, parentId })); changed();
      }), button('Delete', async () => {
        if (!await confirm(ctx, `Delete ${folder.name}?`, deleteFolderWarning(folderContents(listing, folder)))) { return; }
        const deleted = await api(`api/folders/${folder.id}`, { method: 'DELETE' });
        for (const id of deleted) { changed({ deletedProjectId: id }); }
        changed();
      }, 'danger')];
      const item = entry('folder', folder.name, 'Folder', count === 1 ? '1 item' : `${count} items`, () => enter(folder.id), tools); items.push({ node: item, title: folder.name }); list.append(item);
    }
    for (const project of listing.projects.filter(item => item.folderId === folderId)) {
      const tools = [button('Rename', async () => {
        const name = await ask(ctx, 'Rename project', 'Project name', project.name); if (!name) { return; }
        await api(`api/projects/${project.id}`, json('PUT', { name, description: project.description, folderId: project.folderId })); changed();
      }), button('Move', async () => {
        const destination = await pickDestination(ctx, project.name, listing.folders, project.folderId, null); if (destination === undefined) { return; }
        await api(`api/projects/${project.id}`, json('PUT', { name: project.name, description: project.description, folderId: destination })); changed();
      }), button('Delete', async () => {
        if (!await confirm(ctx, `Delete ${project.name}?`, 'This permanently deletes this project and its room.')) { return; }
        await api(`api/projects/${project.id}`, { method: 'DELETE' }); changed({ deletedProjectId: project.id });
      }, 'danger')];
      const item = entry('project', project.name, 'Project', project.description || '', () => openProject(project.id), tools); items.push({ node: item, title: project.name }); list.append(item);
    }
    search.addEventListener('input', () => { for (const item of items) { item.node.hidden = !item.title.toLowerCase().includes(search.value.toLowerCase()); } });
    if (!items.length) { empty(list, 'This folder is empty.'); }
    host.append(list);
    ctx.body.replaceChildren(host);
  }
  function enter(id) { folderId = id; ctx.setState({ folderId }); return render(); }
  listen(ctx, () => { render().catch(error => notify(error.message, true)); });
  await render();
}
