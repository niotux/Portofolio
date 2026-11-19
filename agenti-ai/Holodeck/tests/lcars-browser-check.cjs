// Browser check for the LCARS appearance: it loads the shared library from Front, leaves nothing behind when
// switched off, and keeps window geometry identical to the default theme. Needs the ai stack (gateway) running.
// Windows are opened only in a throwaway project, deleted at the end.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const KEY = 'astudio.appearance.v1';

async function setTheme(page, theme) {
  await page.evaluate(([key, value]) => localStorage.setItem(key, JSON.stringify({ theme: value })), [KEY, theme]);
  await page.reload();
  await page.waitForLoadState('networkidle');
}

async function chooseTheme(page, value) {
  await page.locator('.boot-tab[data-section=appearance]').click();
  await page.locator('.boot-panel select').first().selectOption(value);
}

async function dragDistance(page, projectId) {
  await page.goto(`${base}/?project=${encodeURIComponent(projectId)}`);
  await page.waitForLoadState('networkidle');
  await page.click('#start');
  await page.click('.start-item[title="Open Appearance"]');
  const window = page.locator('.applet-window[data-applet=appearance]');
  const titlebar = window.locator('.window-titlebar');
  const before = await window.boundingBox();
  const grip = await titlebar.boundingBox();
  await page.mouse.move(grip.x + 60, grip.y + grip.height / 2);
  await page.mouse.down();
  await page.mouse.move(grip.x + 60 + 64, grip.y + grip.height / 2 + 32, { steps: 8 });
  await page.mouse.up();
  const after = await window.boundingBox();
  await window.locator('.window-controls button[aria-label="Close window"]').click();
  return { dx: Math.round(after.x - before.x), dy: Math.round(after.y - before.y) };
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  // Count synthesized tones: the check has no speakers, so it watches the page start oscillators.
  await page.addInitScript(() => {
    window.__tones = 0;
    const start = OscillatorNode.prototype.start;
    OscillatorNode.prototype.start = function (...args) { window.__tones++; return start.apply(this, args); };
  });
  const project = await (await page.request.post(`${base}/api/projects`, { data: { name: `LCARS check ${Date.now()}` } })).json();
  try {
    await page.goto(base);
    await setTheme(page, 'dark');
    const darkBackground = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);

    await chooseTheme(page, 'lcars');
    await page.waitForFunction(() => !!document.getElementById('lcars-library') && getComputedStyle(document.body).display === 'grid');
    const lcars = await page.evaluate(() => ({ theme: document.documentElement.dataset.theme, ui: document.documentElement.classList.contains('lcars-ui'), display: getComputedStyle(document.body).display, note: !document.querySelector('.lcars-palette-note').hidden }));
    assert.deepEqual(lcars, { theme: 'lcars', ui: true, display: 'grid', note: true }, 'LCARS loads the library and frames the page');
    await page.locator('#menu-sound-mute').waitFor({ state: 'visible' });
    assert.equal(await page.locator('.shell-menu-bar > #menu-sound-control > #menu-sound-volume').count(), 1, 'the main menu has a fixed top-strip volume slider');
    assert.equal(await page.locator('#menu-sound-control').evaluate(node => node.children.length), 2, 'the shared control is the original direct button/input markup');
    assert.equal(await page.locator('.taskbar > .sound-control').isVisible(), false, 'workspace audio stays hidden on the main menu');


    await setTheme(page, 'lcars');
    assert.equal(await page.evaluate(() => document.documentElement.dataset.theme), 'lcars', 'LCARS survives a reload');
    assert.ok(await page.evaluate(() => !!document.getElementById('lcars-library')), 'the library is loaded again after a reload');

    await chooseTheme(page, 'dark');
    const left = await page.evaluate(() => ({ link: !!document.getElementById('lcars-library'), ui: document.documentElement.classList.contains('lcars-ui'), background: getComputedStyle(document.body).backgroundColor }));
    assert.deepEqual(left, { link: false, ui: false, background: darkBackground }, 'switching back leaves nothing of LCARS behind');

    const projectId = project.id;
    const darkMove = await dragDistance(page, projectId);
    await setTheme(page, 'lcars');
    const lcarsMove = await dragDistance(page, projectId);
    assert.deepEqual(lcarsMove, darkMove, 'a dragged window moves by the same grid cells in LCARS');

    await page.goto(`${base}/?project=${encodeURIComponent(projectId)}`);
    await page.waitForLoadState('networkidle');
    for (let i = 0; i < 2; i++) { await page.click('#start'); await page.click('.start-item[title="Open Appearance"]'); }
    assert.equal(await page.locator('.applet-window[data-applet=appearance]').count(), 1, 'Appearance opens in one window');
    const appearance = page.locator('.applet-window[data-applet=appearance]');
    assert.match(await appearance.locator('.window-title').getAttribute('data-code'), /^\d{2}-\d{6}$/, 'the window carries a console code');
    await appearance.locator('.window-controls button[aria-label="Minimize window"]').click();
    assert.ok(await page.locator('.task.minimized').count() === 1, 'a minimized window is marked in the taskbar');
    await page.locator('.task.minimized').click();
    assert.equal(await page.locator('.task.minimized').count(), 0, 'restoring clears the mark');

    await page.waitForFunction(() => document.querySelector('#sound-mute') && getComputedStyle(document.querySelector('.sound-control')).display !== 'none');
    assert.equal(await page.locator('.taskbar > .sound-control > #sound-mute').count(), 1, 'audio keeps the original taskbar markup');
    assert.equal(await page.locator('.sound-control .lcars-sound, .lcars-sound-dock').count(), 0, 'audio adds no replacement wrapper or floating dock');
    const audioHeight = await page.locator('#sound-mute').evaluate(node => ({ button: node.getBoundingClientRect().height, parent: node.parentElement.getBoundingClientRect().height }));
    assert.equal(audioHeight.button, audioHeight.parent, 'the original Audio block fills the taskbar height');
    await page.click('#start');
    const appearanceIcon = page.locator('.start-item[title="Open Appearance"] .start-icon');
    assert.equal(await appearanceIcon.isVisible(), true, 'Appearance remains in the start menu with its icon');
    assert.ok(await appearanceIcon.evaluate(node => getComputedStyle(node, '::before').maskImage.includes('appearance.svg')), 'Appearance uses its loaded icon mask');
    await page.locator('.start-item[title="Open Appearance"]').click();

    let tones = await page.evaluate(() => window.__tones);
    await page.click('#start'); await page.click('.start-item[title="Open Team"]');
    await page.waitForFunction(count => window.__tones > count, tones);
    await page.click('#sound-mute');
    assert.equal(await page.getAttribute('#sound-mute', 'aria-pressed'), 'true', 'the sound control mutes');
    tones = await page.evaluate(() => window.__tones);
    await page.click('#arrange'); await page.click('#start'); await page.keyboard.press('Escape');
    await page.waitForTimeout(200);
    assert.equal(await page.evaluate(() => window.__tones), tones, 'muted LCARS makes no sound');
    await page.click('#sound-mute');
    assert.equal(await page.locator('#start-menu .start-item[title="Open Main menu"]').count(), 0, 'Main menu is not a start-menu applet');
    await page.locator('.shell-rail .rail-main-menu').click();
    await page.waitForFunction(() => document.body.classList.contains('booting') && !document.querySelector('#boot').hidden);
    assert.equal(await page.locator('.boot-tab[data-section=appearance]').isVisible(), true, 'Main menu returns to the real project menu');
    assert.equal(await page.locator('.lcars-sound-dock').count(), 0, 'returning to the menu does not move audio into a floating panel');
    assert.equal(await page.locator('#menu-sound-mute').isVisible(), true, 'the main menu retains working audio after actual navigation');
    assert.equal(await page.locator('#sound-mute').isVisible(), false, 'only the main-menu sound slot is visible');
    await page.locator('#menu-sound-volume').focus();
    await page.keyboard.press('Home'); await page.keyboard.press('ArrowRight');
    assert.equal(await page.locator('#menu-sound-volume').inputValue(), '5', 'the main-menu native slider works');
    assert.equal(await page.locator('#sound-volume').inputValue(), '5', 'both fixed slots share library state');
    await page.locator('#menu-sound-mute').click();
    assert.equal(await page.locator('#sound-mute').getAttribute('aria-pressed'), 'true', 'main-menu mute synchronizes the taskbar');

    await setTheme(page, 'dark');
    await page.goto(`${base}/?project=${encodeURIComponent(projectId)}`);
    await page.waitForLoadState('networkidle');
    tones = await page.evaluate(() => window.__tones);
    await page.click('#arrange'); await page.click('#start'); await page.keyboard.press('Escape');
    await page.waitForTimeout(200);
    assert.equal(await page.evaluate(() => window.__tones), tones, 'other themes are silent');
    assert.equal(await page.locator('.sound-control:visible').count(), 0, 'other themes hide both sound slots');

    assert.deepEqual(errors, [], 'no page errors');
    console.log('PASS: LCARS loads the shared library, frames the page, survives reload, switches off cleanly and keeps window geometry; Appearance is one window with a code; minimized windows are marked; LCARS plays sounds, mutes, and other themes stay silent');
  } finally {
    await page.evaluate(key => localStorage.removeItem(key), KEY).catch(() => { });
    await page.request.delete(`${base}/api/projects/${project.id}`);
    await browser.close();
  }
})().catch(error => { console.error(error); process.exit(1); });
