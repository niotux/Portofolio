// Window geometry on the 16 px grid. On each axis a window within STICK cells of an edge is stuck to it; stuck to both
// edges it stretches ('s') and keeps both gaps, so a full-screen window keeps every side. A window stuck to neither edge
// is stored relative to the anchor nearest its center (left, center, or right third; top or bottom half).
// A tiled window, one that touches another, is stored instead as its edges' fractions of the desktop: neighbours store
// a shared edge as the same fraction, so any desktop size rounds it to the same cell for both and they stay joined.
// All rects are whole cells; W and H are the desktop size in cells. Clamping is for display and is never stored.
export const CELL = 16;
const STICK = 2;
const HORIZONTAL = ['l', 'c', 'r', 's'];
const VERTICAL = ['t', 'b', 's'];

export function toStored(rect, W, H) {
  const right = W - (rect.x + rect.w);
  const bottom = H - (rect.y + rect.h);
  const ax = horizontalAnchor(rect, right, W);
  const ay = verticalAnchor(rect, bottom, H);
  let ox = rect.x;
  if (ax === 'r') { ox = right; }
  else if (ax === 'c') { ox = rect.x - Math.floor(W / 2); }
  const oy = ay === 'b' ? bottom : rect.y;
  const stored = { ax, ay, ox, oy, w: rect.w, h: rect.h };
  if (ax === 's') { stored.ex = right; }
  if (ay === 's') { stored.ey = bottom; }
  return stored;
}

function horizontalAnchor(rect, right, W) {
  const nearLeft = rect.x <= STICK; const nearRight = right <= STICK;
  if (nearLeft && nearRight) { return 's'; }
  if (nearLeft) { return 'l'; }
  if (nearRight) { return 'r'; }
  const centerX = rect.x + rect.w / 2;
  return centerX < W / 3 ? 'l' : centerX > 2 * W / 3 ? 'r' : 'c';
}

function verticalAnchor(rect, bottom, H) {
  const nearTop = rect.y <= STICK; const nearBottom = bottom <= STICK;
  if (nearTop && nearBottom) { return 's'; }
  if (nearTop) { return 't'; }
  if (nearBottom) { return 'b'; }
  return rect.y + rect.h / 2 < H / 2 ? 't' : 'b';
}

export function toTiled(rect, W, H) {
  return { tile: true, l: rect.x / W, t: rect.y / H, r: (rect.x + rect.w) / W, b: (rect.y + rect.h) / H };
}

// Two windows touch when an edge of one lies on an edge of the other along some shared length.
export function touches(a, b) {
  const overlapX = a.x < b.x + b.w && b.x < a.x + a.w;
  const overlapY = a.y < b.y + b.h && b.y < a.y + a.h;
  const sideBySide = (a.x + a.w === b.x || b.x + b.w === a.x) && overlapY;
  const stacked = (a.y + a.h === b.y || b.y + b.h === a.y) && overlapX;
  return sideBySide || stacked;
}

export function fromStored(stored, W, H) {
  if (stored.tile) {
    const x = Math.round(stored.l * W); const y = Math.round(stored.t * H);
    return { x, y, w: Math.round(stored.r * W) - x, h: Math.round(stored.b * H) - y };
  }
  let x = stored.ox; let w = stored.w;
  if (stored.ax === 'r') { x = W - stored.ox - stored.w; }
  else if (stored.ax === 'c') { x = Math.floor(W / 2) + stored.ox; }
  else if (stored.ax === 's') { w = W - stored.ox - stored.ex; }
  let y = stored.oy; let h = stored.h;
  if (stored.ay === 'b') { y = H - stored.oy - stored.h; }
  else if (stored.ay === 's') { h = H - stored.oy - stored.ey; }
  return { x, y, w, h };
}

export function clampRect(rect, min, W, H) {
  const w = Math.max(min.w, Math.min(rect.w, W));
  const h = Math.max(min.h, Math.min(rect.h, H));
  const x = Math.max(0, Math.min(rect.x, W - w));
  const y = Math.max(0, Math.min(rect.y, H - h));
  return { x, y, w, h };
}

export function isStored(value) {
  if (value === null || typeof value !== 'object') { return false; }
  if (value.tile === true) { return isFraction(value.l) && isFraction(value.t) && isFraction(value.r) && isFraction(value.b) && value.l < value.r && value.t < value.b; }
  if (!HORIZONTAL.includes(value.ax) || !VERTICAL.includes(value.ay)) { return false; }
  if (value.ax === 's' && !Number.isInteger(value.ex)) { return false; }
  if (value.ay === 's' && !Number.isInteger(value.ey)) { return false; }
  return Number.isInteger(value.ox) && Number.isInteger(value.oy) && Number.isInteger(value.w) && Number.isInteger(value.h) && value.w > 0 && value.h > 0;
}

function isFraction(value) { return typeof value === 'number' && value >= 0 && value <= 1; }
