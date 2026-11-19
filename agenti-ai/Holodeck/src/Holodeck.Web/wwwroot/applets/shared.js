import { cue } from '../sounds.js';
export const changes = new EventTarget();
export function changed(detail = {}) { changes.dispatchEvent(new CustomEvent('change', { detail })); }
export async function api(path, options = {}) {
  const response = await fetch(path, options);
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.error || body.detail || `Request failed (${response.status}).`);
  }
  return response.status === 204 ? null : response.json();
}
// crypto.randomUUID exists only in secure contexts; plain HTTP over the LAN is not one.
export function newId() { return Array.from(crypto.getRandomValues(new Uint8Array(16)), byte => byte.toString(16).padStart(2, '0')).join(''); }
export const json = (method, body) => ({ method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
export function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) { node.className = className; }
  if (text != null) { node.textContent = text; }
  return node;
}
export function button(text, action, className = '') {
  const node = el('button', className, text); node.type = 'button';
  node.addEventListener('click', async () => {
    node.disabled = true;
    try { await action(); } catch (error) { notify(error.message, true); }
    finally { node.disabled = false; }
  });
  return node;
}
export function notify(text, error = false) {
  const node = el('div', `notification lcars-toast${error ? ' error' : ''}`, text);
  if (error) { cue('alert'); }
  document.querySelector('#notifications').append(node);
  setTimeout(() => node.remove(), error ? 9000 : 3500);
}
export function field(label, input) {
  input.setAttribute('aria-label', label);
  const wrapper = el('label', 'field'); wrapper.append(el('span', '', label), input); return wrapper;
}
export function input(value = '', placeholder = '') {
  const node = document.createElement('input'); node.value = value; node.placeholder = placeholder; return node;
}
export function listen(ctx, listener) {
  changes.addEventListener('change', listener); ctx.dispose(() => changes.removeEventListener('change', listener));
}
export function row(icon, title, detail, action, actions = []) {
  const node = el('div', 'file-row');
  const open = button('', action, 'file-open'); const copy = el('span', 'file-copy');
  copy.append(el('strong', '', title), el('small', '', detail)); open.append(el('span', 'file-icon', icon), copy);
  const tools = el('div', 'row-actions'); tools.append(...actions); node.append(open, tools); return node;
}
export function empty(host, text) { host.append(el('div', 'empty', text)); }
export function pickFile(accept) {
  return new Promise(resolve => {
    const picker = document.createElement('input'); picker.type = 'file'; picker.accept = accept; picker.hidden = true; document.body.append(picker);
    picker.addEventListener('change', () => { const file = picker.files[0]; picker.remove(); resolve(file); }, { once: true });
    picker.addEventListener('cancel', () => { picker.remove(); resolve(null); }, { once: true }); picker.click();
  });
}
// On a phone an applet shows its list or the chosen item, not both; this pill in the item returns to the list.
export function listBack(label, action) { const node = button(label, action, 'list-back'); node.setAttribute('aria-label', `Back to ${label}`); return node; }
// Dialogs sit over the whole page, centred in the browser, not inside the window that opened them.
export function showOverlay(overlay) { overlay.classList.add('page-overlay'); document.body.append(overlay); }
export async function ask(ctx, title, label, value = '') {
  return new Promise(resolve => {
    const overlay = el('div', 'window-overlay'); const form = el('form', 'dialog-form lcars-dialog');
    const entry = input(value); entry.required = true; entry.maxLength = 240;
    const actions = el('div', 'actions'); const submit = el('button', 'primary', 'Save'); submit.type = 'submit';
    const finish = result => { overlay.remove(); resolve(result); };
    actions.append(button('Cancel', () => finish(null)), submit); form.append(el('h3', '', title), field(label, entry), actions); overlay.append(form); showOverlay(overlay);
    form.addEventListener('submit', event => { event.preventDefault(); finish(entry.value.trim()); }); entry.focus(); entry.select();
    ctx.dispose(() => finish(null));
  });
}
// Sudo lets a process act as root inside its library's project environment, never on the PC itself.
export const SUDO_WARNING = 'It can then run any command as root in this library\u2019s project environment: install software system-wide and read or change every file there, including other agents\u2019 files and settings. It cannot reach this PC, Docker or other projects. It restarts now, so its open sessions end; the first time, the whole environment restarts.';

// One process's sudo grant: what it has and the single action that changes it. Allowing asks first. change(wanted)
// saves the grant and returns whether sudo is now allowed.
export function sudoRow(ctx, title, enabled, change) {
  const row = el('div', 'computer-row sudo-row'); const copy = el('div', 'computer-copy'); const state = el('small', 'muted');
  copy.append(el('strong', '', title), state);
  let current = enabled;
  const action = button('', async () => {
    const wanted = !current;
    if (wanted && !await confirm(ctx, `Allow sudo for ${title}?`, SUDO_WARNING, 'Allow sudo')) { return; }
    action.disabled = true;
    try { current = await change(wanted); }
    catch (error) { notify(error.message, true); }
    finally { action.disabled = false; show(); }
  }, 'small');
  function show() { state.textContent = current ? 'Can run commands as root with sudo' : 'No sudo'; action.textContent = current ? 'Remove sudo' : 'Allow sudo'; }
  show(); row.append(copy, action);
  return row;
}

export async function confirm(ctx, title, description, action = 'Delete') {
  return new Promise(resolve => {
    const overlay = el('div', 'window-overlay'); const form = el('div', 'dialog-form lcars-dialog');
    const finish = result => { overlay.remove(); resolve(result); };
    const actions = el('div', 'actions'); actions.append(button('Cancel', () => finish(false)), button(action, () => finish(true), action === 'Delete' ? 'danger' : 'primary'));
    form.append(el('h3', '', title), el('p', 'muted', description), actions); overlay.append(form); showOverlay(overlay); ctx.dispose(() => finish(false));
  });
}
