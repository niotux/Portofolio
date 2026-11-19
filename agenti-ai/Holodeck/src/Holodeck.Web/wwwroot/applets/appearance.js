import { el, field, button, api, json } from './shared.js';
import { setSoundsActive } from '../sounds.js';
const KEY = 'astudio.appearance.v1';
const DEFAULTS = { theme: 'dark', accent: '#b8cf76', darkSurface: '#191d22', lightSurface: '#ffffff', grid: 'subtle' };
// One appearance for the whole app, the main menu and every project alike, stored by the server so every browser
// shares it. This browser keeps a copy only to paint the right theme before the server answers.
const SAVE_DELAY_MS = 300;
let appearance = {};
let saveTimer = 0;
const views = new Set();
try { appearance = JSON.parse(localStorage.getItem(KEY)) || {}; } catch { }
function keepLocal() { try { localStorage.setItem(KEY, JSON.stringify(appearance)); } catch { } }
function upload() { api('api/appearance', json('PUT', { settings: appearance })).catch(() => { }); }
function save() { keepLocal(); clearTimeout(saveTimer); saveTimer = setTimeout(upload, SAVE_DELAY_MS); }
// A browser that saved an appearance before the server kept one hands it over once.
async function loadShared() {
  try {
    const shared = await api('api/appearance');
    if (!shared.settings) { if (Object.keys(appearance).length) { upload(); } return; }
    appearance = shared.settings; keepLocal(); apply(); for (const sync of views) { sync(); }
  } catch { }
}
function setting(name) { return appearance[name] || DEFAULTS[name]; }
function gridStyle() { return appearance.showGrid === false && !appearance.grid ? 'none' : setting('grid'); }
function readableText(hex) {
  const value = parseInt(hex.slice(1), 16);
  const luminance = (0.299 * (value >> 16) + 0.587 * ((value >> 8) & 255) + 0.114 * (value & 255)) / 255;
  return luminance > 0.55 ? '#101510' : '#f6f8f2';
}
// LCARS draws with the shared LCARS library, served by the PC's front server on the host name Holodeck was opened with.
// When the front server is unreachable, Holodeck's bundled copy stands in.
// It is loaded only while LCARS is selected; the library classes in AStudio's markup do nothing without it.
const LCARS_LIBRARY_PATH = '/lcars/lcars.css';
const LCARS_FALLBACK_PATH = new URL('../lcars/lcars.css', import.meta.url).href;
function setLcarsLibrary(enabled) {
  let link = document.getElementById('lcars-library');
  if (enabled && !link) {
    link = el('link'); link.id = 'lcars-library'; link.rel = 'stylesheet';
    link.addEventListener('error', () => { if (link.href !== LCARS_FALLBACK_PATH) { link.href = LCARS_FALLBACK_PATH; } });
    link.href = `${location.protocol}//${location.hostname}${LCARS_LIBRARY_PATH}`; document.head.append(link);
  }
  if (!enabled && link) { link.remove(); }
}
function apply() {
  const requested = setting('theme');
  const theme = requested === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : requested;
  const root = document.documentElement; root.dataset.theme = theme; root.dataset.grid = gridStyle();
  const lcars = theme === 'lcars';
  root.classList.toggle('lcars-ui', lcars); setLcarsLibrary(lcars); setSoundsActive(lcars);
  // LCARS uses the library palette; the accent and backgrounds stay saved for the other themes.
  if (lcars) { for (const name of ['--accent', '--accent-text', '--surface']) { root.style.removeProperty(name); } return; }
  root.style.setProperty('--accent', setting('accent'));
  root.style.setProperty('--accent-text', readableText(setting('accent')));
  root.style.setProperty('--surface', setting(`${theme}Surface`));
}
apply();
loadShared();
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', apply);
function colorInput(name) { const input = el('input', 'setting-color'); input.type = 'color'; input.value = setting(name); return input; }
function select(options, value) { const input = el('select'); for (const [optionValue, label] of options) { input.append(new Option(label, optionValue)); } input.value = value; return input; }
export const appearanceApplet = { id: 'appearance', title: 'Appearance', icon: '◐', description: 'Theme, colors, and workspace backdrop.', singleton: true, size: { width: 400, height: 470 }, mount(ctx) {
  const host = el('div', 'content'); host.append(el('div', 'eyebrow', 'Workspace preferences'), el('h2', '', 'Appearance'), el('p', 'subtitle', 'Pick a background and an accent. Panels, bars and borders follow.'));
  const theme = select([['dark', 'Dark'], ['light', 'Light'], ['system', 'System'], ['lcars', 'LCARS']], setting('theme'));
  const accent = colorInput('accent'); const dark = colorInput('darkSurface'); const light = colorInput('lightSurface');
  const grid = select([['subtle', 'Subtle grid'], ['holodeck', 'Holodeck'], ['none', 'None']], gridStyle());
  const palette = el('p', 'info-note lcars-palette-note', 'LCARS uses its own palette and frame. Accent, backgrounds and backdrop apply to the other themes.');
  const controls = el('div'); controls.style.marginTop = '24px'; controls.append(field('Theme', theme), palette, field('Accent color', accent), field('Dark theme background', dark), field('Light theme background', light), field('Workspace backdrop', grid));
  const reflectTheme = () => { const lcars = theme.value === 'lcars'; palette.hidden = !lcars; for (const input of [accent, dark, light, grid]) { input.disabled = lcars; } };
  const sync = () => { theme.value = setting('theme'); accent.value = setting('accent'); dark.value = setting('darkSurface'); light.value = setting('lightSurface'); grid.value = gridStyle(); reflectTheme(); };
  host.append(controls, button('Reset appearance', () => { appearance = {}; save(); apply(); sync(); }));
  controls.addEventListener('input', () => { appearance = { theme: theme.value, accent: accent.value, darkSurface: dark.value, lightSurface: light.value, grid: grid.value }; save(); apply(); reflectTheme(); });
  views.add(sync); ctx.dispose(() => views.delete(sync));
  reflectTheme();
  ctx.body.append(host);
} };
