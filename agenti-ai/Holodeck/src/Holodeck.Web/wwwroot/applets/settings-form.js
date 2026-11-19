import { el, button, json, notify } from './shared.js';

let controlCount = 0;

// Management actions use the same bounded schema controls as stored settings, without coupling their submission.
export function schemaFields(schema, values, changes, errors, onChange) {
  const root = el('div', 'settings-form');
  appendFields(root, schema, '', values, { changes, errors, onChange }, () => {});
  root.append(errorList(errors.filter(error => !nodeAt(schema, error.path) || error.path === ''), false));
  return root;
}

export function schemaValues(values, changes) {
  const result = structuredClone(values);
  for (const [path, change] of changes) {
    if (change.invalid !== undefined) { throw new Error('Enter a valid number before running this action.'); }
    const names = path.slice(1).split('/').map(unescapePointer);
    let parent = result;
    for (const name of names.slice(0, -1)) {
      if (!Object.hasOwn(parent, name) || !isObject(parent[name])) {
        Object.defineProperty(parent, name, { value: {}, enumerable: true, writable: true, configurable: true });
      }
      parent = parent[name];
    }
    const name = names.at(-1);
    if (change.remove) { delete parent[name]; }
    else { Object.defineProperty(parent, name, { value: change.value, enumerable: true, writable: true, configurable: true }); }
  }
  return result;
}

// Settings for one agent instance. A definition with a settings schema gets a form; any other agent keeps its text
// editor. Drafts live in the map the caller keeps per instance, so they survive tab and agent switches and live
// updates. Saving is conditional on the revision the editor was opened with; the server validates every save.
export async function renderSettings({ id, path, drafts, pane, isCurrent }) {
  let draft = drafts.get(id);
  if (!draft || !isDirty(draft)) {
    const view = await read(path);
    if (!isCurrent()) { return; }
    draft = fresh(view);
    drafts.set(id, draft);
  }
  const render = () => { if (isCurrent()) { pane.replaceChildren(...build(draft, path, render)); } };
  render();
}

function fresh(view) {
  return { view, text: view.content, changes: new Map(), errors: view.errors || [], conflict: null, repairing: false, status: '' };
}

function isDirty(draft) { return draft.changes.size > 0 || draft.text !== draft.view.content; }
function isForm(draft) { return draft.view.mode === 'form' && !draft.view.needsRepair && !draft.repairing; }

function build(draft, path, render) {
  const parts = [];
  if (draft.view.mode === 'form') { parts.push(facts(draft.view)); }
  if (draft.view.notice) { parts.push(el('p', 'settings-notice', draft.view.notice)); }
  if (draft.conflict) { parts.push(conflictPanel(draft, render)); }
  if (isForm(draft)) { parts.push(...formParts(draft, render)); }
  else { parts.push(...textParts(draft)); }
  parts.push(actions(draft, path, render));
  return parts;
}

function facts(view) {
  const list = el('dl', 'settings-facts');
  list.append(el('dt', '', 'Definition'), el('dd', '', view.definition || 'Unavailable'));
  list.append(el('dt', '', 'Form revision'), el('dd', 'settings-revision', short(view.schemaRevision)));
  list.append(el('dt', '', 'Settings revision'), el('dd', 'settings-revision', short(view.revision)));
  return list;
}

function textParts(draft) {
  const parts = [];
  if (draft.view.mode === 'form') {
    parts.push(el('p', 'settings-notice', draft.view.needsRepair ? 'The stored settings are not a valid settings object, so the form cannot show them. Repair the text; saving checks it against the form’s rules.' : 'Edit the stored settings as text. Saving checks them against the form’s rules.'));
  } else {
    parts.push(el('p', 'muted', 'These settings belong to this agent instance.'));
  }
  const area = el('textarea', 'project-settings'); area.value = draft.text; area.spellcheck = false; area.setAttribute('aria-label', 'Settings');
  area.addEventListener('input', () => { draft.text = area.value; draft.status = 'Unsaved changes'; refreshState(area, draft); });
  parts.push(area, errorList(draft.errors, true));
  return parts;
}

function formParts(draft, render) {
  const view = draft.view;
  const values = JSON.parse(view.content);
  const parts = [];
  if (view.schema.description) { parts.push(el('p', 'muted', view.schema.description)); }
  const root = el('div', 'settings-form');
  appendFields(root, view.schema, '', values, draft, render);
  parts.push(root);
  const unknown = countUnknown(view.schema, values);
  if (unknown) { parts.push(el('p', 'muted', unknown === 1 ? 'One stored value is not part of this form and is kept as stored.' : `${unknown} stored values are not part of this form and are kept as stored.`)); }
  const structural = view.errors.filter(error => nodeAt(view.schema, error.path)?.type === 'object');
  if (structural.length) {
    parts.push(button('Repair settings text', () => { draft.repairing = true; render(); }));
  }
  parts.push(errorList(draft.errors.filter(error => !nodeAt(view.schema, error.path) || error.path === ''), false));
  return parts;
}

// Fields with the same group hint sit together; nested objects are groups of their own.
function appendFields(container, node, path, values, draft, render) {
  const groups = new Map();
  for (const property of node.properties || []) {
    let target = container;
    if (property.group) {
      if (!groups.has(property.group)) { groups.set(property.group, groupBox(property.group, null)); container.append(groups.get(property.group)); }
      target = groups.get(property.group);
    }
    const childPath = `${path}/${escapePointer(property.name)}`;
    const stored = isObject(values) ? values[property.name] : undefined;
    if (property.type === 'object') { target.append(objectGroup(property, childPath, stored, values, draft, render)); }
    else { target.append(fieldRow(property, childPath, stored, values, node, draft)); }
  }
}

function objectGroup(node, path, stored, parentValues, draft, render) {
  const box = groupBox(node.title || node.name, node.description);
  box.dataset.path = path;
  const present = isObject(parentValues) && Object.hasOwn(parentValues, node.name);
  if (present && !isObject(stored)) {
    box.append(el('p', 'error', 'The stored value here is not a group of settings. Repair the settings text to edit these fields.'));
    return box;
  }
  appendFields(box, node, path, stored || {}, draft, render);
  return box;
}

function groupBox(title, description) {
  const box = el('fieldset', 'settings-group');
  box.append(el('legend', '', title));
  if (description) { box.append(el('p', 'settings-help', description)); }
  return box;
}

function fieldRow(node, path, stored, parentValues, parent, draft) {
  const label = node.title || node.name;
  const present = isObject(parentValues) && Object.hasOwn(parentValues, node.name);
  const change = draft.changes.get(path);
  const current = change ? (change.remove ? undefined : change.value) : stored;
  const isSet = change ? !change.remove : present;
  const row = el('div', 'settings-field'); row.dataset.path = path;
  const caption = el('div', 'settings-caption');
  caption.append(el('label', '', label));
  if ((parent.required || []).includes(node.name)) { caption.append(el('span', 'settings-badge', 'Required')); }
  if (!isSet) { caption.append(el('span', 'settings-badge muted', 'Not set')); }
  const control = controlFor(node, current, isSet, value => { record(draft, path, value, present ? stored : undefined, present); refreshState(row, draft); });
  control.id = `settings-control-${++controlCount}`; caption.querySelector('label').htmlFor = control.id; control.setAttribute('aria-label', label);
  row.append(caption, control);
  if (node.description) { row.append(el('p', 'settings-help', node.description)); }
  const limits = limitText(node);
  if (limits) { row.append(el('p', 'settings-help', limits)); }
  if (node.default !== undefined && node.default !== null) { row.append(el('p', 'settings-help', `Suggested: ${display(node.default)}`)); }
  for (const error of draft.errors.filter(item => item.path === path)) { row.append(el('p', 'error', error.message)); }
  return row;
}

function controlFor(node, value, isSet, onChange) {
  if (node.type === 'boolean') {
    const box = el('input'); box.type = 'checkbox'; box.checked = value === true;
    box.addEventListener('change', () => onChange({ value: box.checked }));
    return box;
  }
  if (node.type === 'string' && node.enum) {
    const select = el('select');
    select.append(option('', 'Not set'));
    for (const entry of node.enum) { select.append(option(entry, entry)); }
    if (isSet && typeof value === 'string' && !node.enum.includes(value)) { select.append(option(value, value)); }
    select.value = isSet && typeof value === 'string' ? value : '';
    select.addEventListener('change', () => onChange(select.value === '' ? { remove: true } : { value: select.value }));
    return select;
  }
  if (node.type === 'number' || node.type === 'integer') {
    const input = el('input'); input.type = 'number'; input.step = node.type === 'integer' ? '1' : 'any';
    if (node.minimum !== null && node.minimum !== undefined) { input.min = node.minimum; }
    if (node.maximum !== null && node.maximum !== undefined) { input.max = node.maximum; }
    input.value = isSet && value !== undefined ? String(value) : '';
    input.addEventListener('input', () => {
      const text = input.value.trim();
      if (text === '') { onChange({ remove: true }); return; }
      const number = Number(text);
      onChange(Number.isFinite(number) ? { value: number } : { invalid: text });
    });
    return input;
  }
  const multiline = node.control === 'multiline';
  const input = el(multiline ? 'textarea' : 'input');
  if (!multiline) { input.type = node.control === 'password' ? 'password' : 'text'; }
  input.spellcheck = multiline; input.value = typeof value === 'string' ? value : isSet && value !== undefined ? JSON.stringify(value) : '';
  input.addEventListener('input', () => onChange({ value: input.value, text: true }));
  return input;
}

// Clearing a text field that was never stored returns it to unset instead of storing an empty value.
function record(draft, path, change, stored, present) {
  if (change.text && change.value === '' && !present) { draft.changes.delete(path); }
  else if (change.remove && !present) { draft.changes.delete(path); }
  else if (!change.remove && !change.invalid && present && JSON.stringify(change.value) === JSON.stringify(stored)) { draft.changes.delete(path); }
  else { draft.changes.set(path, change.text ? { value: change.value } : change); }
}

function refreshState(row, draft) {
  if (draft.onChange) { draft.onChange(); return; }
  const pane = row.closest('.project-agent-pane');
  pane?.querySelector('.settings-status')?.replaceChildren(isDirty(draft) ? 'Unsaved changes' : '');
  const discard = pane?.querySelector('.settings-discard');
  if (discard) { discard.hidden = !isDirty(draft); }
}

function actions(draft, path, render) {
  const bar = el('div', 'project-settings-actions');
  const status = el('span', 'muted settings-status', isDirty(draft) ? 'Unsaved changes' : draft.status);
  const saveButton = button('Save settings', () => save(draft, path, render), 'primary');
  saveButton.disabled = Boolean(draft.conflict);
  bar.append(saveButton);
  const discard = button('Discard changes', () => { const view = draft.view; Object.assign(draft, fresh(view)); render(); }, 'settings-discard');
  discard.hidden = !isDirty(draft);
  bar.append(discard);
  if (draft.repairing && !draft.view.needsRepair) { bar.append(button('Back to form', () => { draft.repairing = false; draft.text = draft.view.content; render(); })); }
  bar.append(status);
  return bar;
}

async function save(draft, path, render) {
  const body = { baseRevision: draft.view.revision, schemaRevision: draft.view.schemaRevision };
  if (isForm(draft) && !draft.changes.size) { draft.status = 'No changes to save'; render(); return; }
  if (isForm(draft)) {
    const invalid = [...draft.changes].filter(([, change]) => change.invalid !== undefined);
    if (invalid.length) { draft.errors = invalid.map(([field]) => ({ path: field, message: 'Enter a number.' })); render(); return; }
    body.changes = [...draft.changes].map(([field, change]) => change.remove ? { path: field, remove: true } : { path: field, value: change.value });
  } else {
    body.content = draft.text;
  }
  const response = await fetch(path, json('PUT', body));
  const result = await response.json().catch(() => ({}));
  if (response.ok) {
    Object.assign(draft, fresh(result)); draft.status = 'Saved'; render();
    notify('Settings saved. Restart the agent to use them.'); return;
  }
  if (response.status === 409 && result.current) { draft.conflict = result.current; draft.errors = []; render(); return; }
  if (response.status === 400 && result.fieldErrors) { draft.errors = result.fieldErrors; draft.status = ''; render(); throw new Error(result.error); }
  throw new Error(result.error || `Request failed (${response.status}).`);
}

// Neither draft is replaced silently: the editor keeps its values beside what is stored now until the user chooses.
function conflictPanel(draft, render) {
  const latest = draft.conflict;
  const panel = el('section', 'settings-conflict'); panel.setAttribute('aria-label', 'Settings conflict');
  const formChanged = latest.schemaRevision !== draft.view.schemaRevision;
  panel.append(el('h3', '', formChanged ? 'The settings form changed' : 'These settings changed elsewhere'));
  panel.append(el('p', '', formChanged ? 'The definition now has a different settings form. Your unsaved values are still here.' : 'Someone saved these settings after you opened them. Your unsaved values are still here.'));
  const dropped = [];
  const usable = latest.mode === 'form' && !latest.needsRepair && isForm(draft);
  if (isForm(draft)) {
    const stored = safeParse(latest.content);
    const list = el('dl', 'settings-facts');
    for (const [field, change] of draft.changes) {
      if (!usable || !nodeAt(latest.schema, field) || nodeAt(latest.schema, field).type === 'object') { dropped.push(field); continue; }
      const hidden = nodeAt(latest.schema, field).control === 'password';
      const mine = change.remove ? 'Not set' : hidden ? 'Hidden' : display(change.value);
      const theirs = hidden ? 'Hidden' : display(valueAt(stored, field));
      list.append(el('dt', '', `Yours: ${field}`), el('dd', '', mine), el('dt', 'muted', `Saved now: ${field}`), el('dd', '', theirs));
    }
    if (list.children.length) { panel.append(list); }
  } else {
    const saved = el('textarea', 'project-settings settings-latest'); saved.readOnly = true; saved.value = latest.content; saved.setAttribute('aria-label', 'Latest saved settings');
    panel.append(el('p', 'muted', 'Latest saved settings'), saved);
  }
  if (dropped.length) { panel.append(el('p', 'error', `Not in the new form, so these will be left out if you keep your changes: ${dropped.join(', ')}`)); }
  const choices = el('div', 'project-settings-actions');
  choices.append(button('Keep my changes on the latest settings', () => {
    const keep = draft.changes; const text = draft.text; const repairing = draft.repairing;
    Object.assign(draft, fresh(latest));
    if (isForm(draft) && !repairing) { for (const [field, change] of keep) { if (!dropped.includes(field)) { draft.changes.set(field, change); } } }
    else { draft.text = text; draft.repairing = repairing && latest.mode === 'form'; }
    draft.status = 'Review, then save again'; render();
  }, 'primary'), button('Use the latest settings', () => { Object.assign(draft, fresh(latest)); render(); }));
  panel.append(choices);
  return panel;
}

function errorList(errors, includeAll) {
  const list = el('div', 'settings-errors');
  for (const error of errors) { if (includeAll || error.path === '' || error.path) { list.append(el('p', 'error', error.path ? `${error.path}: ${error.message}` : error.message)); } }
  return list;
}

function countUnknown(node, values) {
  if (!isObject(values)) { return 0; }
  let count = 0;
  for (const name of Object.keys(values)) {
    const property = (node.properties || []).find(item => item.name === name);
    if (!property) { count++; }
    else if (property.type === 'object') { count += countUnknown(property, values[name]); }
  }
  return count;
}

function nodeAt(schema, path) {
  if (!schema) { return null; }
  if (path === '') { return schema; }
  let node = schema;
  for (const name of path.slice(1).split('/').map(unescapePointer)) {
    node = (node.properties || []).find(item => item.name === name);
    if (!node) { return null; }
  }
  return node;
}

function valueAt(values, path) {
  let current = values;
  for (const name of path.slice(1).split('/').map(unescapePointer)) {
    if (!isObject(current) || !Object.hasOwn(current, name)) { return undefined; }
    current = current[name];
  }
  return current;
}

function limitText(node) {
  const parts = [];
  if (node.minLength !== null && node.minLength !== undefined) { parts.push(`At least ${node.minLength} characters.`); }
  if (node.maxLength !== null && node.maxLength !== undefined) { parts.push(`At most ${node.maxLength} characters.`); }
  if (node.minimum !== null && node.minimum !== undefined) { parts.push(`Minimum ${node.minimum}.`); }
  if (node.maximum !== null && node.maximum !== undefined) { parts.push(`Maximum ${node.maximum}.`); }
  return parts.join(' ');
}

async function read(path) {
  const response = await fetch(path);
  const body = await response.json().catch(() => ({}));
  if (!response.ok) { throw new Error(body.error || `Could not read settings (${response.status}).`); }
  return body;
}

function option(value, text) { const node = el('option', '', text); node.value = value; return node; }
function display(value) { return value === undefined ? 'Not set' : typeof value === 'string' ? value : JSON.stringify(value); }
function short(revision) { return revision ? revision.slice(0, 12) : 'None'; }
function isObject(value) { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function safeParse(text) { try { return JSON.parse(text); } catch { return undefined; } }
function escapePointer(name) { return name.replaceAll('~', '~0').replaceAll('/', '~1'); }
function unescapePointer(name) { return name.replaceAll('~1', '/').replaceAll('~0', '~'); }
