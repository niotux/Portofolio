import { el } from './applets/shared.js';
import { appletIcon } from './icons.js';
import { cue } from './sounds.js';

const toggle = document.querySelector('#start');
const menu = document.querySelector('#start-menu');
const search = menu.querySelector('.start-search');
const list = menu.querySelector('.start-list');
// Built-in applets first, then a divider, then the attached libraries' applets.
const builtins = list.querySelector('.start-builtin');
const libraries = list.querySelector('.start-library');
const divider = list.querySelector('.start-divider');
const empty = menu.querySelector('.start-empty');

// Every start-menu entry, in order, for views that list them another way (the phone project home).
export const startItems = [];
export function addStartItem(definition, open) {
  const entry = { definition, open };
  startItems.push(entry);
  const item = el('button', 'start-item'); item.type = 'button'; item.title = `Open ${definition.title}`;
  item.dataset.search = `${definition.title} ${definition.description || ''}`.toLowerCase();
  const copy = el('span', 'start-copy'); copy.append(el('strong', '', definition.title));
  if (definition.description) { copy.append(el('small', '', definition.description)); }
  item.append(appletIcon(definition, 'start-icon'), copy);
  item.addEventListener('click', () => { close(); open(); });
  (definition.libraryId ? libraries : builtins).append(item);
  showDivider();
  return () => {
    const index = startItems.indexOf(entry);
    if (index >= 0) { startItems.splice(index, 1); }
    item.remove();
    showDivider();
  };
}

function hasVisible(group) {
  for (const item of group.children) { if (!item.hidden) { return true; } }
  return false;
}
function showDivider() { divider.hidden = !hasVisible(builtins) || !hasVisible(libraries); }
function visibleItems() {
  const items = [];
  for (const group of [builtins, libraries]) {
    for (const item of group.children) { if (!item.hidden) { items.push(item); } }
  }
  return items;
}
function filter() {
  const query = search.value.trim().toLowerCase();
  for (const group of [builtins, libraries]) {
    for (const item of group.children) { item.hidden = !item.dataset.search.includes(query); }
  }
  showDivider();
  empty.hidden = visibleItems().length > 0;
}
function openMenu() { cue('menu'); menu.hidden = false; toggle.setAttribute('aria-expanded', 'true'); search.value = ''; filter(); search.focus(); }
function close() { if (menu.hidden) { return; } menu.hidden = true; toggle.setAttribute('aria-expanded', 'false'); }
function moveFocus(step) {
  const stops = [search, ...visibleItems()];
  const next = stops.indexOf(document.activeElement) + step;
  stops[Math.max(0, Math.min(stops.length - 1, next))].focus();
}
function onKey(event) {
  if (event.key === 'Escape') { close(); toggle.focus(); }
  else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); moveFocus(event.key === 'ArrowDown' ? 1 : -1); }
  else if (event.key === 'Enter' && event.target === search) { const first = visibleItems()[0]; if (first) { first.click(); } }
}

toggle.addEventListener('click', () => { if (menu.hidden) { openMenu(); } else { close(); } });
search.addEventListener('input', filter);
menu.addEventListener('keydown', onKey);
document.addEventListener('pointerdown', event => { if (!menu.contains(event.target) && !toggle.contains(event.target)) { close(); } });
