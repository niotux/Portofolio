// node tests/layout.test.mjs: anchored grid math for workspace windows (rounding trips, anchors, clamping).
import { CELL, toStored, toTiled, touches, fromStored, clampRect, isStored } from '../src/Holodeck.Web/wwwroot/layout.js';

let failures = 0;
function check(name, condition) { if (!condition) { failures++; console.log('FAIL', name); } }
const eq = (a, b) => a.x === b.x && a.y === b.y && a.w === b.w && a.h === b.h;

check('cell is 16 px', CELL === 16);

const W = 100, H = 60;
for (const r of [{ x: 1, y: 1, w: 16, h: 9 }, { x: 34, y: 1, w: 16, h: 9 }, { x: 84, y: 46, w: 15, h: 14 }, { x: 40, y: 50, w: 20, h: 6 }]) {
  check(`round trip ${JSON.stringify(r)}`, eq(fromStored(toStored(r, W, H), W, H), r));
}
{
  const s = toStored({ x: 84, y: 46, w: 15, h: 14 }, W, H);
  check('right/bottom anchors chosen', s.ax === 'r' && s.ay === 'b');
  const wide = fromStored(s, 140, 80);
  check('keeps right distance', 140 - (wide.x + wide.w) === 1);
  check('keeps bottom distance', 80 - (wide.y + wide.h) === 0);
}
{
  const s = toStored({ x: 34, y: 1, w: 16, h: 9 }, W, H);
  check('center anchor chosen', s.ax === 'c' && s.ay === 't');
  check('keeps center offset', fromStored(s, 140, 80).x - 70 === 34 - 50);
}
{
  const min = { w: 10, h: 5 };
  check('clamp to minimum', eq(clampRect({ x: 5, y: 5, w: 2, h: 1 }, min, W, H), { x: 5, y: 5, w: 10, h: 5 }));
  check('clamp into view', eq(clampRect({ x: 95, y: 58, w: 20, h: 10 }, min, W, H), { x: 80, y: 50, w: 20, h: 10 }));
  check('clamp negative', eq(clampRect({ x: -4, y: -2, w: 20, h: 10 }, min, W, H), { x: 0, y: 0, w: 20, h: 10 }));
  check('clamp oversize', eq(clampRect({ x: 0, y: 0, w: 500, h: 500 }, min, W, H), { x: 0, y: 0, w: 100, h: 60 }));
}
{
  const s = toStored({ x: 120, y: 70, w: 20, h: 10 }, 160, 90);
  const small = clampRect(fromStored(s, 60, 40), { w: 10, h: 5 }, 60, 40);
  check('fits a small screen', small.x >= 0 && small.y >= 0 && small.x + small.w <= 60 && small.y + small.h <= 40);
  check('stored form survives the small screen', eq(fromStored(s, 160, 90), { x: 120, y: 70, w: 20, h: 10 }));
}
{
  const s = toStored({ x: 26, y: 2, w: 60, h: 40 }, 87, 56);
  check('wide window flush right sticks to the right', s.ax === 'r' && s.ox === 1);
  check('wide window keeps right distance', 100 - (fromStored(s, 100, 70).x + 60) === 1);
  const b = toStored({ x: 30, y: 30, w: 20, h: 25 }, 100, 56);
  check('window flush bottom sticks to the bottom', b.ay === 'b' && b.oy === 1);
  }
{
  const s = toStored({ x: 40, y: 0, w: 47, h: 56 }, 87, 56);
  check('right, top and bottom: right anchor, vertical stretch', s.ax === 'r' && s.ay === 's');
  const tall = fromStored(s, 100, 70);
  check('stretch keeps top and bottom contact', tall.y === 0 && tall.h === 70 && 100 - (tall.x + tall.w) === 0 && tall.w === 47);
  const short = clampRect(fromStored(s, 60, 30), { w: 22, h: 16 }, 60, 30);
  check('stretch follows a smaller screen', short.y === 0 && short.h === 30);
  const full = toStored({ x: 0, y: 0, w: 87, h: 56 }, 87, 56);
  check('full screen stretches both ways', full.ax === 's' && full.ay === 's');
  check('full screen keeps every side', eq(fromStored(full, 120, 80), { x: 0, y: 0, w: 120, h: 80 }));
  check('stretch with gaps keeps the gaps', eq(fromStored(toStored({ x: 1, y: 2, w: 85, h: 53 }, 87, 56), 100, 70), { x: 1, y: 2, w: 98, h: 67 }));
}
check('valid stretch form', isStored({ ax: 's', ay: 's', ox: 0, oy: 0, ex: 0, ey: 0, w: 87, h: 56 }));
check('stretch needs its far gap', !isStored({ ax: 's', ay: 't', ox: 0, oy: 0, w: 87, h: 56 }));
check('valid stored form', isStored({ ax: 'r', ay: 'b', ox: 1, oy: 0, w: 15, h: 14 }));
check('null is not stored', !isStored(null));
check('bad anchor is not stored', !isStored({ ax: 'x', ay: 't', ox: 1, oy: 0, w: 15, h: 14 }));
check('fractional offset is not stored', !isStored({ ax: 'l', ay: 't', ox: 1.5, oy: 0, w: 15, h: 14 }));

// Tiled windows: edges stored as fractions, so neighbours keep shared edges across desktop sizes.
const left = { x: 0, y: 0, w: 40, h: 30 }, right = { x: 40, y: 0, w: 60, h: 30 }, below = { x: 0, y: 30, w: 100, h: 30 };
check('side by side windows touch', touches(left, right));
check('stacked windows touch', touches(left, below));
check('windows meeting only at a corner do not touch', !touches({ x: 0, y: 0, w: 10, h: 10 }, { x: 10, y: 10, w: 10, h: 10 }));
check('separate windows do not touch', !touches({ x: 0, y: 0, w: 10, h: 10 }, { x: 12, y: 0, w: 10, h: 10 }));
check('overlapping windows do not touch', !touches({ x: 0, y: 0, w: 10, h: 10 }, { x: 5, y: 0, w: 10, h: 10 }));
check('tiled round trip', eq(fromStored(toTiled(right, W, H), W, H), right));
let joined = true, filled = true;
for (const size of [[37, 21], [61, 44], [99, 59], [143, 80], [200, 111]]) {
  const a = fromStored(toTiled(left, W, H), size[0], size[1]), b = fromStored(toTiled(right, W, H), size[0], size[1]), c = fromStored(toTiled(below, W, H), size[0], size[1]);
  if (a.x + a.w !== b.x || a.y + a.h !== c.y) { joined = false; }
  if (b.x + b.w !== size[0] || c.y + c.h !== size[1]) { filled = false; }
}
check('shared edges stay shared at every desktop size', joined);
check('tiles that fill the desktop keep filling it', filled);
check('valid tiled form', isStored(toTiled(right, W, H)));
check('tiled edges must be ordered', !isStored({ tile: true, l: 0.5, t: 0, r: 0.4, b: 1 }));
check('tiled edges stay on the desktop', !isStored({ tile: true, l: -0.1, t: 0, r: 0.4, b: 1 }));

console.log(failures === 0 ? 'layout: all checks passed' : `layout: ${failures} failures`);
process.exit(failures === 0 ? 0 : 1);
