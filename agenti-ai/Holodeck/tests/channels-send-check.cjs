// Check acknowledged messages without live events, and password-field persistence, in disposable project data.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';

(async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROME_EXECUTABLE || '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  let project;
  async function api(route, method = 'GET', data) {
    const response = await page.request.fetch(base + route, { method, data });
    assert.ok(response.ok(), `${method} ${route}: ${response.status()}`);
    return response.status() === 204 ? null : response.json();
  }
  try {
    project = await api('/api/projects', 'POST', { name: `Channels send check ${Date.now()}` });
    const root = `/api/projects/${project.id}/room`;
    await api(`/api/projects/${project.id}/libraries`, 'POST', { id: 'holodeck.humans', version: '0.1.0' });
    const agent = await api(`${root}/agents`, 'POST', { library: 'holodeck.humans/0.1.0/agents/person' });
    const you = (await api(root)).members.find(member => member.isYou);
    const channel = await api(`${root}/channels`, 'POST', { name: 'First message', memberIds: [you.id] });
    await page.route('**/channels-send-check', route => route.fulfill({ contentType: 'text/html', body: '<div id="notifications"></div><main id="settings"></main><main id="channels"></main>' }));
    await page.goto(base + '/channels-send-check');
    await page.evaluate(async ({ projectId, agentId }) => {
      const { renderSettings } = await import('/applets/settings-form.js');
      window.settingsOptions = { id: agentId, path: `api/projects/${projectId}/room/agents/${agentId}/settings/form`, drafts: new Map(), pane: document.querySelector('#settings'), isCurrent: () => true };
      await renderSettings(window.settingsOptions);
    }, { projectId: project.id, agentId: agent.id });
    const testKey = 'dummy-key-for-password-save-test';
    await page.getByLabel('API key', { exact: true }).fill(testKey);
    await page.getByRole('button', { name: 'Save settings', exact: true }).click();
    await page.getByText('Settings saved. Restart the agent to use them.', { exact: true }).waitFor();
    const settings = await api(`${root}/agents/${agent.id}/settings/form`);
    assert.equal(JSON.parse(settings.content).Model.ApiKey, testKey);
    await page.evaluate(async () => {
      window.settingsOptions.drafts.clear();
      await (await import('/applets/settings-form.js')).renderSettings(window.settingsOptions);
    });
    assert.equal(await page.getByLabel('API key', { exact: true }).inputValue(), testKey);
    console.log('PASS: the password field saves and reloads the entered API key');

    await page.evaluate(async ({ projectId, channelId }) => {
      // Live notifications may be delayed or disconnected. A successful POST must still become visible.
      window.WebSocket = class { addEventListener() {} close() {} };
      const state = { projectId, channelId };
      await (await import('/applets/conversations.js')).conversationsApplet.mount({
        state, body: document.querySelector('#channels'), isClosed: () => false,
        setState: value => Object.assign(state, value), dispose: () => {}, onNavigate: () => {}, launch: () => {}, close: () => {}
      });
    }, { projectId: project.id, channelId: channel.id });
    const channels = page.locator('#channels');
    for (const note of ['first message without events', 'second message without events']) {
      await channels.getByLabel('Message', { exact: true }).fill(note);
      await channels.getByRole('button', { name: 'Send', exact: true }).click();
      await channels.locator('.bubble-text').getByText(note, { exact: true }).waitFor({ timeout: 3000 });
      assert.equal(await channels.locator('.bubble-text').getByText(note, { exact: true }).count(), 1);
    }
    console.log('PASS: acknowledged messages appear once without events or a refresh');
    assert.deepEqual(errors, []);
  } finally {
    if (project) { await page.request.delete(`${base}/api/projects/${project.id}`); }
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
