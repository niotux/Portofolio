// Browser check for tiled windows: windows that touch stay joined, without overlap, as the desktop is resized, and a
// window moved away from the others keeps its own size again. Runs in a throwaway project, deleted at the end.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';

function rects(page) {
  return page.$$eval('.applet-window:not([hidden])', nodes => nodes.map(node => { const r = node.getBoundingClientRect(); return { left: Math.round(r.left), top: Math.round(r.top), right: Math.round(r.right), bottom: Math.round(r.bottom) }; }));
}

function overlaps(a, b) { return a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom; }

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const project = await (await page.request.post(`${base}/api/projects`, { data: { name: `Tiling check ${Date.now()}` } })).json();
  try {
    await page.goto(`${base}/?project=${encodeURIComponent(project.id)}`);
    await page.waitForLoadState('networkidle');
    for (const title of ['Channels', 'Team', 'Library Explorer']) { await page.click('#start'); await page.click(`.start-item[title="Open ${title}"]`); }
    await page.click('#arrange');
    const arranged = await rects(page);
    const joins = [];
    for (let i = 0; i < arranged.length; i++) {
      for (let j = 0; j < arranged.length; j++) {
        if (i !== j && arranged[i].right === arranged[j].left) { joins.push([i, j]); }
      }
    }
    assert.ok(joins.length >= 2, 'Arrange leaves windows side by side');

    for (const [width, height] of [[1100, 760], [900, 700], [1600, 1000], [1250, 820]]) {
      await page.setViewportSize({ width, height });
      await page.waitForTimeout(150);
      const shown = await rects(page);
      for (const [i, j] of joins) { assert.equal(shown[i].right, shown[j].left, `windows ${i} and ${j} stay joined at ${width}x${height}`); }
      for (let i = 0; i < shown.length; i++) {
        for (let j = i + 1; j < shown.length; j++) { assert.ok(!overlaps(shown[i], shown[j]), `windows ${i} and ${j} do not overlap at ${width}x${height}`); }
      }
    }

    // Move the last window away from the others: it is anchored on its own again and keeps its size across resizes.
    const last = page.locator('.applet-window').last();
    await last.locator('.window-controls button[aria-label="Maximize window"]').click();
    await last.locator('.window-controls button[aria-label="Maximize window"]').click();
    const grip = await last.locator('.window-titlebar').boundingBox();
    await page.mouse.move(grip.x + 80, grip.y + grip.height / 2);
    await page.mouse.down();
    await page.mouse.move(grip.x + 80 - 48, grip.y + grip.height / 2 + 160, { steps: 8 });
    await page.mouse.up();
    const box = await last.boundingBox();
    const others = (await rects(page)).slice(0, -1);
    const alone = others.every(other => box.x + box.width < other.left || box.x > other.right || box.y + box.height < other.top || box.y > other.bottom || overlaps({ left: box.x, top: box.y, right: box.x + box.width, bottom: box.y + box.height }, other));
    if (alone) {
      await page.setViewportSize({ width: 1500, height: 950 });
      await page.waitForTimeout(150);
      const after = await last.boundingBox();
      assert.equal(Math.round(after.width), Math.round(box.width), 'a window on its own keeps its width across resizes');
    }

    assert.deepEqual(errors, [], 'no page errors');
    console.log('PASS: touching windows stay joined without overlap across desktop resizes');
  } finally {
    await page.request.delete(`${base}/api/projects/${project.id}`);
    await browser.close();
  }
})().catch(error => { console.error(error); process.exit(1); });
