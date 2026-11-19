// Channel timeline check: activity without a source message keeps its place in time across reloads, a new channel shows
// its first message, and a reader who scrolls up is not pulled back down by live work.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';

async function call(route, options = {}) {
  const response = await fetch(base + route, { headers: { 'Content-Type': 'application/json' }, ...options });
  const text = await response.text();
  assert.ok(response.ok, `${route}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

(async () => {
  const project = await call('/api/projects', { method: 'POST', body: JSON.stringify({ name: `Timeline check ${Date.now()}` }) });
  const browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  try {
    const room = await call(`/api/projects/${project.id}/room`);
    const you = room.members.find(member => member.isYou);
    const channel = await call(`/api/projects/${project.id}/room/channels`, { method: 'POST', body: JSON.stringify({ name: 'Many messages', memberIds: [you.id] }) });
    const send = note => { const body = new FormData(); body.append('note', note); return fetch(`${base}/api/projects/${project.id}/room/channels/${channel.id}/packets`, { method: 'POST', body }); };
    for (let index = 1; index <= 60; index++) { await send(`Message ${index}`); }

    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    // Applet icons are probed on purpose; a missing one falls back to its glyph.
    page.on('response', response => { if (response.status() >= 400 && !response.url().includes('/icons/')) { errors.push(`${response.status()} ${response.url()}`); } });
    const open = async () => {
      await page.goto(`${base}/?project=${project.id}`);
      await page.locator('#project-name').filter({ hasText: project.name }).waitFor();
      let window = page.locator('.applet-window[data-applet="conversations"]');
      if (!await window.count()) {
        await page.locator('#start').click();
        await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: /^Channels$/ }) }).click();
      }
      window = page.locator('.applet-window[data-applet="conversations"]');
      await window.locator('.conversations-app').waitFor();
      return window;
    };
    let chat = await open();
    await chat.locator('.conversation-entry').filter({ has: page.locator('strong', { hasText: /^Many messages$/ }) }).click();
    await chat.locator('.bubble-text').filter({ hasText: /^Message 60$/ }).waitFor();
    const order = () => chat.locator('.feed').evaluate(feed => [...feed.children].map(node => node.querySelector('.bubble-text')?.textContent || 'card'));
    const first = await order();
    assert.deepEqual(first, Array.from({ length: 60 }, (_, index) => `Message ${index + 1}`), 'messages are in sending order');
    assert.ok(await chat.locator('.feed').evaluate(feed => feed.scrollHeight - feed.scrollTop - feed.clientHeight < 80), 'opens at the end');

    // A reader who scrolls up stays there while new messages arrive, then follows again from the end.
    await chat.locator('.feed').evaluate(feed => { feed.scrollTop = 0; feed.dispatchEvent(new Event('scroll')); });
    await send('Arrives while reading');
    await chat.locator('.bubble-text').filter({ hasText: /^Arrives while reading$/ }).waitFor();
    assert.ok(await chat.locator('.feed').evaluate(feed => feed.scrollTop < 50), 'scrolling up is respected');
    await chat.locator('.feed').evaluate(feed => { feed.scrollTop = feed.scrollHeight; feed.dispatchEvent(new Event('scroll')); });
    await send('Arrives at the end');
    await chat.locator('.bubble-text').filter({ hasText: /^Arrives at the end$/ }).waitFor();
    await page.waitForFunction(() => { const feed = document.querySelector('.applet-window[data-applet="conversations"] .feed'); return feed.scrollHeight - feed.scrollTop - feed.clientHeight < 80; });

    // A channel created in the window shows the first message sent to it.
    await chat.locator('.channel-new').click();
    await page.locator('.channel-form input:not([type=checkbox])').fill('Fresh group');
    await page.locator('.channel-form button[type=submit]').click();
    await chat.locator('.chat-title').filter({ hasText: /^Fresh group$/ }).waitFor();
    await chat.getByLabel('Message', { exact: true }).fill('First words here');
    await chat.getByLabel('Message', { exact: true }).press('Enter');
    await chat.locator('.bubble-text').filter({ hasText: /^First words here$/ }).waitFor({ timeout: 5000 });
    assert.equal(await chat.locator('.bubble-text').count(), 1, 'only the new channel\'s message is shown');

    // The order survives a reload.
    chat = await open();
    await chat.locator('.conversation-entry').filter({ has: page.locator('strong', { hasText: /^Many messages$/ }) }).click();
    await chat.locator('.bubble-text').filter({ hasText: /^Arrives at the end$/ }).waitFor();
    const again = await order();
    assert.deepEqual(again.slice(0, 60), first, 'order is the same after a reload');
    assert.deepEqual(errors, []);
    console.log('PASS: messages keep their order across reloads, scrolling up is respected, a new channel shows its first message');

    // Many tabs of one workspace share the browser's few connections to a server. Live notices must not use them up,
    // or every tab stops loading new messages.
    const tabs = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    try {
      const chats = [];
      for (let index = 0; index < 4; index++) {
        const tab = await tabs.newPage();
        tab.on('pageerror', error => errors.push(error.message));
        await tab.goto(`${base}/?project=${project.id}`);
        await tab.locator('#project-name').filter({ hasText: project.name }).waitFor();
        for (const [title, type] of [['Team', 'room'], ['Channels', 'conversations']]) {
          if (await tab.locator(`.applet-window[data-applet="${type}"]`).count()) { continue; }
          await tab.locator('#start').click();
          await tab.locator('#start-menu .start-item').filter({ has: tab.locator('strong', { hasText: new RegExp(`^${title}$`) }) }).click();
        }
        const tabChat = tab.locator('.applet-window[data-applet="conversations"]');
        await tabChat.locator('.conversation-entry').filter({ has: tab.locator('strong', { hasText: /^Many messages$/ }) }).evaluate(entry => entry.click());
        await tabChat.locator('.bubble-text').filter({ hasText: /^Arrives at the end$/ }).waitFor({ timeout: 10000 });
        chats.push(tabChat);
      }
      await send('Seen by every tab');
      for (const tabChat of chats) { await tabChat.locator('.bubble-text').filter({ hasText: /^Seen by every tab$/ }).waitFor({ timeout: 10000 }); }
    } finally { await tabs.close(); }
    assert.deepEqual(errors, []);
    console.log('PASS: four tabs with Team and Channels open all load and receive new messages');
  } finally {
    await browser.close();
    await fetch(`${base}/api/projects/${project.id}`, { method: 'DELETE' });
  }
})().catch(error => { console.error(error); process.exit(1); });
