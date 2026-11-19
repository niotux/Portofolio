import { el } from './applets/shared.js';

// Applet icons are plain SVG files in /icons/<applet-id>.svg. Their shape is used as a mask and drawn in the
// current text color, so any single-color drawing works. Without a file the applet's glyph is shown instead.
const ready = new Set();
const iconUrl = id => `icons/${id}.svg`;

export function loadAppletIcon(definition) {
  const probe = new Image();
  probe.onload = () => {
    ready.add(definition.id);
    for (const node of document.querySelectorAll(`.applet-icon[data-applet="${definition.id}"]`)) { useSvg(node, definition.id); }
  };
  probe.src = iconUrl(definition.id);
}

export function appletIcon(definition, className) {
  const node = el('span', `applet-icon ${className}`, definition.icon); node.dataset.applet = definition.id; node.setAttribute('aria-hidden', 'true');
  if (ready.has(definition.id)) { useSvg(node, definition.id); }
  return node;
}

function useSvg(node, id) { node.textContent = ''; node.classList.add('svg'); node.style.setProperty('--icon', `url("${iconUrl(id)}")`); }
