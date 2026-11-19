// Browser regression for the main menu -> project workspace (Channels, Team, Library Explorer).
// Creates a project and a temporary local replacement library, then removes both.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const fixture = require('./fixture-library.cjs');
const { removeRoom } = require('./room-files.cjs');
const sampleKey = fixture.Key;
const replacementId = `holodeck.browser-check-${Date.now()}`;
const replacementKey = `${replacementId}/1.0.0`;
const replacementPath = path.join(__dirname, '..', 'libs', replacementId);
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');

function writeReplacementLibrary() {
  const agent = path.join(replacementPath, 'agents', 'echo');
  fs.mkdirSync(agent, { recursive: true });
  fs.mkdirSync(path.join(replacementPath, 'teams'), { recursive: true });
  fs.writeFileSync(path.join(replacementPath, 'Library.json'), JSON.stringify({ FormatVersion: 1, Id: replacementId, Version: '1.0.0', Name: 'Browser Replacement', Dependencies: [], Runtime: { Image: 'holodeck-library:1', Processes: [] } }));
  fs.writeFileSync(path.join(agent, 'Agent.json'), JSON.stringify({ Id: 'echo', Name: 'Replacement Echo', Launch: { Command: 'python3', Args: ['${Agent}/agent.py'] } }));
  fs.copyFileSync(fixture.echoProgram, path.join(agent, 'agent.py'));
}

(async () => {
  fixture.install();
  writeReplacementLibrary();
  const browser = await chromium.launch({ executablePath: process.env.CHROME_EXECUTABLE || '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1400, height: 950 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const originalAppearance = await (await page.request.get(`${base}/api/appearance`)).json();
  let projectId;
  let otherProjectId;
  const applet = id => page.locator(`.applet-window[data-applet="${id}"]`);
  const team = applet('room');
  const library = applet('library');
  const channels = applet('conversations');
  async function show(window) {
    await window.waitFor({ state: 'attached' });
    if (await window.evaluate(node => node.classList.contains('active') && !node.hidden)) { return; }
    const id = await window.getAttribute('data-instance');
    const task = page.locator(`#tasks .task[data-instance="${id}"]`);
    if (await page.locator('body.phone').count()) {
      const title = await task.locator('.task-title').textContent();
      if (await page.locator('body.phone-applet').count()) { await page.locator('#start').click(); }
      await page.locator('#phone-home').getByRole('button', { name: title, exact: true }).click();
    } else {
      await task.click();
    }
    await window.waitFor();
  }
  async function view() { return (await page.request.get(`${base}/api/projects/${projectId}/room`)).json(); }
  async function inspect(name) {
    await show(team);
    await team.getByRole('button', { name: `Inspect ${name}`, exact: true }).click();
    await team.locator('.project-agent-title').filter({ hasText: new RegExp(`^${name}$`) }).waitFor();
  }
  async function tab(name) { await team.getByRole('tab', { name, exact: true }).click(); }
  function channelRow(text) { return team.locator('.project-connection-row').filter({ hasText: text }); }
  function libraryRow(key) { return library.locator(`.library-row[data-library-key="${key}"]`); }
  async function startMenu(title) {
    await page.locator('#start').click();
    await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: new RegExp(`^${title}$`) }) }).click();
  }
  async function openLibrary() {
    await startMenu('Library Explorer');
    await library.locator('.library-app').waitFor();
    await library.locator('.library-project-context').waitFor({ state: 'attached' });
  }
  async function accent() { return page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--accent').trim()); }
  async function changeReference(key, remove = false) {
    await openLibrary();
    await libraryRow(key).getByRole('button', { name: remove ? /^Remove / : /^Add / }).click();
    if (remove) { await page.locator('.dialog-form').getByRole('button', { name: 'Remove', exact: true }).click(); }
    await libraryRow(key).getByRole('button', { name: remove ? /^Add / : /^Remove / }).waitFor();
  }
  // The agent's channel with just you, opened from Team's Channels tab.
  async function openPersonal(name) {
    await inspect(name); await tab('Channels');
    const row = team.locator('.project-connection-row').filter({ has: page.locator('small', { hasText: /^With You$/ }) }).filter({ hasNotText: 'Studio' });
    const title = await row.locator('strong').textContent();
    await row.getByRole('button', { name: 'Open channel', exact: true }).click();
    await channels.locator('.chat-title').filter({ hasText: new RegExp(`^${title}$`) }).waitFor();
    await channels.locator('.composer').waitFor();
  }
  async function post(note) {
    await channels.locator('.composer').getByLabel('Message', { exact: true }).fill(note);
    await channels.locator('.composer').getByRole('button', { name: 'Send', exact: true }).click();
  }
  const bubbles = text => channels.locator('.bubble').filter({ hasText: text });
  async function send(note) {
    await channels.locator('.composer').getByLabel('Message', { exact: true }).fill(note);
    await channels.locator('.composer').getByRole('button', { name: 'Send', exact: true }).click();
    await channels.locator('.bubble').filter({ hasText: `echo: ${note}` }).waitFor({ timeout: 15000 });
  }
  async function expectGhost(name) {
    await team.getByRole('button', { name: `Inspect ${name}`, exact: true }).locator('.status-dot[data-status="error"]').waitFor();
  }
  try {
    await page.goto(base);
    await page.evaluate(() => localStorage.clear());
    await page.reload();
    await page.locator('#boot').waitFor();
    assert.equal(await page.locator('.taskbar').isVisible(), false, 'the main menu hides the taskbar');
    await page.locator('#boot').getByRole('button', { name: 'New project', exact: true }).click();
    const created = page.waitForResponse(response => response.url().endsWith('/api/projects') && response.request().method() === 'POST');
    const projectTitle = `Hierarchy check ${Date.now()}`;
    await page.locator('.dialog-form input').fill(projectTitle);
    await page.locator('.dialog-form').getByRole('button', { name: 'Save', exact: true }).click();
    projectId = (await (await created).json()).id;
    await page.locator('#project-name').filter({ hasText: projectTitle }).waitFor();
    assert.equal(await page.locator('#boot').isVisible(), false);
    assert.ok(page.url().includes(`?project=${projectId}`), 'the booted project is in the URL');
    assert.equal(await page.locator('.applet-window').count(), 0, 'a new project opens with no windows');
    await page.locator('#start').click();
    assert.deepEqual(await page.locator('#start-menu .start-item strong').allTextContents(), ['Channels', 'Team', 'Services', 'Library Explorer', 'Appearance']);
    await page.keyboard.press('Escape');
    await startMenu('Team'); await team.locator('.team-app').waitFor();
    await team.locator('.window-controls button[aria-label="Close window"]').click();
    await page.waitForTimeout(700);
    await page.reload(); await page.locator('#project-name').filter({ hasText: projectTitle }).waitFor();
    await page.waitForTimeout(500);
    assert.equal(await page.locator('.applet-window').count(), 0, 'closing every window is saved as no windows');
    await startMenu('Channels'); await channels.waitFor();
    await startMenu('Team'); await team.locator('.team-app').waitFor();
    console.log('PASS: the main menu boots a project with exactly its saved windows (none at first), with a project-scoped start menu');

    await openLibrary();
    await library.getByRole('heading', { name: 'Project libraries', exact: true }).waitFor();
    await library.getByRole('heading', { name: 'Available libraries', exact: true }).waitFor();
    await libraryRow(sampleKey).getByRole('button', { name: /^Explore / }).click();
    assert.equal(await libraryRow(sampleKey).locator('.library-entry-name').textContent(), 'Holodeck Check');
    assert.equal(await libraryRow(sampleKey).locator('.library-entry-version').textContent(), '1.0.0');
    assert.ok(!/[·•–—]|\d+ (agents|teams)/.test(await libraryRow(sampleKey).innerText()), 'library row has separate name/version without inline counts or metadata separators');
    const agentsTab = library.getByRole('tab', { name: 'Agents', exact: true });
    const teamsTab = library.getByRole('tab', { name: 'Teams', exact: true });
    assert.equal(await agentsTab.locator('.library-tab-label').textContent(), 'Agents');
    assert.equal(await agentsTab.locator('.library-tab-count').textContent(), '2');
    assert.equal(await teamsTab.locator('.library-tab-label').textContent(), 'Teams');
    assert.equal(await teamsTab.locator('.library-tab-count').textContent(), '1');
    assert.equal(await library.getByRole('tab', { name: 'Applets', exact: true }).locator('.library-tab-count').textContent(), '0');
    assert.equal(await library.getByRole('button', { name: /Echo Pair/ }).count(), 0, 'Teams are hidden while Agents is selected');
    await teamsTab.click();
    await library.getByRole('button', { name: /Echo Pair/ }).waitFor();
    assert.equal(await library.getByRole('button', { name: /Library Echo/ }).count(), 0, 'Agents are hidden while Teams is selected');
    await libraryRow(sampleKey).getByRole('button', { name: /^Add / }).click();
    await libraryRow(sampleKey).getByRole('button', { name: /^Remove / }).waitFor();
    assert.equal((await view()).members.filter(member => !member.isYou).length, 0, 'attaching a library does not instantiate agents');
    assert.equal((await page.request.get(`${base}/api/projects/${projectId}/libraries`)).status(), 200);
    await page.screenshot({ path: '/tmp/holodeck-library-context.png' });
    console.log('PASS: Library Explorer separates project references from available libraries and shows Agents/Teams/Applets tabs with separate count badges');

    await show(team);
    assert.equal(await team.getByRole('button', { name: 'Library', exact: true }).count(), 0);
    assert.equal(await team.getByRole('button', { name: 'Map', exact: true }).count(), 0);
    await team.getByRole('button', { name: 'Add team', exact: true }).click();
    const picker = page.getByRole('dialog', { name: 'Add team', exact: true });
    await picker.locator(`.definition-choice[data-reference="${sampleKey}/teams/echo-pair"]`).click();
    await picker.waitFor({ state: 'detached' });
    await team.getByRole('button', { name: 'Inspect First Echo', exact: true }).waitFor();
    await team.getByRole('button', { name: 'Inspect Second Echo', exact: true }).waitFor();
    let state = await view();
    assert.equal(state.members.filter(member => !member.isYou).length, 2);
    assert.equal(state.channels.length, 2);
    for (const member of state.members) { if (!member.isYou) { assert.equal(member.status, 'stopped'); } }
    await inspect('First Echo');
    await team.getByRole('button', { name: 'Start agent', exact: true }).click();
    await team.getByRole('button', { name: 'Stop agent', exact: true }).waitFor();
    await inspect('Second Echo');
    await team.getByRole('button', { name: 'Start agent', exact: true }).click();
    await team.getByRole('button', { name: 'Stop agent', exact: true }).waitFor();
    await tab('Overview');
    await team.getByRole('button', { name: 'Rename', exact: true }).click();
    await page.locator('.dialog-form input').fill('Mirror');
    await page.locator('.dialog-form').getByRole('button', { name: 'Save', exact: true }).click();
    await team.getByRole('button', { name: 'Inspect Mirror', exact: true }).waitFor();
    const first = (await view()).members.find(member => member.name === 'First Echo');
    console.log('PASS: Team instantiates attached definitions as stopped agents with their channels, then starts and renames them');
    const teamRow = name => team.getByRole('button', { name: `Inspect ${name}`, exact: true });
    await show(channels);
    await channels.locator('.conversation-entry').filter({ hasText: 'First Echo' }).filter({ hasNotText: 'Mirror' }).click();
    await channels.locator('.chat-title').filter({ hasText: /^First Echo$/ }).waitFor();
    await show(team); await teamRow('Mirror').click();
    await team.locator('.project-agent.selected').filter({ hasText: 'Mirror' }).waitFor();
    assert.equal((await channels.locator('.chat-title').textContent()).trim(), 'First Echo', 'selecting an agent in Team leaves Channels on its channel');
    await show(channels);
    await channels.locator('.conversation-entry').filter({ hasText: 'First Echo' }).filter({ hasNotText: 'Mirror' }).click();
    assert.deepEqual(await team.locator('.project-agent.selected strong').allTextContents(), ['Mirror'], 'selecting a channel leaves Team on its agent');
    assert.equal(await page.locator('.linked').count(), 0, 'neither window highlights the other\'s selection');
    console.log('PASS: Channels and Team keep their own selections');

    await show(channels);
    await channels.locator('.conversation-side-heading').getByRole('button', { name: 'New channel', exact: true }).click();
    const createForm = page.locator('.channel-form');
    await createForm.getByLabel('Channel name', { exact: true }).fill('Studio');
    for (const name of ['First Echo', 'Mirror']) { await createForm.locator('label.check-row', { hasText: name }).locator('input').check(); }
    await createForm.getByRole('button', { name: 'Create', exact: true }).click();
    await channels.locator('.chat-title').filter({ hasText: /^Studio$/ }).waitFor();
    assert.equal(await channels.locator('.channel-member').count(), 3);
    await post('group hello');
    await bubbles('echo: group hello').nth(1).waitFor({ timeout: 15000 });
    await page.waitForTimeout(1500);
    assert.equal(await bubbles('echo: group hello').count(), 2, 'both echoes answer a group post once');
    assert.equal(await bubbles('echo: echo:').count(), 0, 'echoes in one channel do not answer each other');
    await channels.getByRole('button', { name: 'Pause', exact: true }).click();
    await channels.getByRole('button', { name: 'Resume', exact: true }).waitFor();
    await post('while paused');
    await bubbles('while paused').first().waitFor();
    await page.waitForTimeout(2500);
    assert.equal(await bubbles('echo: while paused').count(), 0, 'a paused channel refuses agent posts');
    await channels.getByRole('button', { name: 'Resume', exact: true }).click();
    await channels.getByRole('button', { name: 'Remove Mirror from Studio', exact: true }).click();
    await channels.getByRole('button', { name: 'Remove Mirror from Studio', exact: true }).waitFor({ state: 'detached' });
    await post('after removal');
    await bubbles('echo: after removal').first().waitFor({ timeout: 15000 });
    await page.waitForTimeout(1500);
    assert.equal(await bubbles('echo: after removal').count(), 1, 'a removed member no longer receives the channel');
    await inspect('First Echo'); await tab('Channels');
    await channelRow('Studio').getByRole('button', { name: 'Open channel', exact: true }).click();
    await channels.locator('.chat-title').filter({ hasText: /^Studio$/ }).waitFor();
    await channels.locator('.conversation-entry', { hasText: 'Second Echo' }).click();
    assert.equal(await channels.locator('.composer').count(), 1);
    console.log('PASS: a group channel fans out to every member, pauses agents, drops removed members, and opens from Team');

    await openPersonal('First Echo');
    const composer = channels.locator('.composer');
    await composer.getByLabel('Message', { exact: true }).fill('hello');
    const choosing = page.waitForEvent('filechooser');
    await composer.getByRole('button', { name: 'Attach files', exact: true }).click();
    await (await choosing).setFiles({ name: 'dot.png', mimeType: 'image/png', buffer: PNG });
    await startMenu('Team');
    assert.equal(await team.count(), 1);
    await startMenu('Channels');
    assert.equal(await channels.count(), 1);
    assert.equal(await composer.getByLabel('Message', { exact: true }).inputValue(), 'hello');
    await composer.locator('.attachment').filter({ hasText: 'dot.png' }).waitFor();
    await openPersonal('Mirror');
    assert.equal(await composer.getByLabel('Message', { exact: true }).inputValue(), '');
    assert.equal(await channels.locator('.attachment').count(), 0);
    await openPersonal('First Echo');
    assert.equal(await composer.getByLabel('Message', { exact: true }).inputValue(), 'hello');
    await composer.locator('.attachment').filter({ hasText: 'dot.png' }).waitFor();
    await composer.getByRole('button', { name: 'Send', exact: true }).click();
    await channels.locator('.bubble').filter({ hasText: 'echo: hello' }).locator('.file-chip').filter({ hasText: 'dot.png' }).waitFor({ timeout: 15000 });
    await channels.locator('.bubble').filter({ hasText: 'echo: hello' }).locator('.file-chip').filter({ hasText: 'dot.png' }).click();
    const viewer = page.locator('.applet-window[data-applet=viewer]');
    await viewer.locator('.viewer-image').waitFor();
    assert.equal(await viewer.locator('.viewer-name').textContent(), 'dot.png', 'a file in a message opens in the File Viewer');
    await viewer.locator('.window-controls button[aria-label="Close window"]').click();
    await show(channels);
    for (let index = 0; index < 10; index++) {
      await composer.getByLabel('Message', { exact: true }).fill(`burst ${index}`);
      await composer.getByLabel('Message', { exact: true }).press('Enter');
    }
    await channels.locator('.bubble').filter({ hasText: 'echo: burst 9' }).waitFor({ timeout: 15000 });
    const ids = await channels.locator('.bubble').evaluateAll(nodes => nodes.map(node => Number(node.dataset.packetId)));
    assert.equal(ids.length, 22);
    assert.equal(new Set(ids).size, ids.length);
    assert.deepEqual([...ids].sort((a, b) => a - b), ids);
    await page.screenshot({ path: '/tmp/holodeck-channels.png' });
    console.log('PASS: Channels preserves contextual windows and per-channel drafts and renders note/file bursts once in order');

    await channels.getByRole('button', { name: 'Open agent First Echo', exact: true }).click();
    await team.locator('.project-agent-title').filter({ hasText: /^First Echo$/ }).waitFor();
    await tab('Settings');
    const settingsText = '{"browserCheck":true}';
    const settings = team.getByLabel('Settings', { exact: true });
    await settings.fill(settingsText);
    state = await view();
    const mirror = state.members.find(member => member.name === 'Mirror');
    await page.request.post(`${base}/api/projects/${projectId}/room/agents/${mirror.id}/stop`);
    await page.waitForTimeout(150);
    assert.equal(await settings.inputValue(), settingsText, 'live events retain unsaved instance settings');
    await team.getByRole('button', { name: 'Save settings', exact: true }).click();
    await tab('Log');
    await team.locator('pre').filter({ hasText: 'from You' }).waitFor();
    await tab('Workspace');
    await team.getByRole('link', { name: /dot.png/ }).waitFor();
    await page.screenshot({ path: '/tmp/holodeck-team-workspace.png' });
    await tab('Computer');
    await team.locator('.project-agent-header').getByText('Running').waitFor({ timeout: 30000 });
    await team.getByText('Agents cannot be given published ports or host folders yet.').waitFor();
    assert.equal(await team.getByLabel('Folder name', { exact: true }).count(), 0, 'the Computer tab offers no grant controls');
    const computerUrl = `${base}/api/projects/${projectId}/room/agents/${first.id}/computer`;
    const computer = await (await page.request.get(computerUrl)).json();
    const refused = await page.request.put(computerUrl, { data: { ports: [8188], folders: [] } });
    assert.equal(refused.status(), 409, 'a grant change is refused once the agent has its computer');
    assert.deepEqual((await (await page.request.get(computerUrl)).json()).grants, computer.grants, 'a refused grant edit saves nothing');
    const unchanged = await page.request.put(computerUrl, { data: { ports: computer.grants.ports.map(port => port.containerPort), folders: computer.grants.folders } });
    assert.equal(unchanged.status(), 200, 'saving unchanged grants is allowed');
    assert.equal(await team.locator('.project-agent-header').getByText('Running').count(), 1, 'a refused grant edit does not restart the agent');
    await page.screenshot({ path: '/tmp/holodeck-team-computer.png' });
    console.log('PASS: the Computer tab shows the environment without grant controls; grant changes are refused without saving or restarting');
    await openPersonal('First Echo');
    await channels.getByRole('button', { name: 'Remove You from First Echo', exact: true }).click();
    await channels.getByRole('button', { name: 'Join', exact: true }).waitFor();
    assert.equal(await channels.locator('.composer').count(), 0);
    assert.equal(await channels.locator('.bubble').count(), 22);
    await channels.getByRole('button', { name: 'Join', exact: true }).click();
    await channels.locator('.composer').waitFor();
    assert.equal(await channels.locator('.bubble').count(), 22);
    console.log('PASS: Team owns Settings, Log and Workspace; leaving and rejoining a channel keeps its history');

    const channelIds = (await view()).channels.map(channel => channel.id).sort();
    await changeReference(sampleKey, true);
    await expectGhost('First Echo'); await expectGhost('Mirror');
    await inspect('First Echo'); await tab('Overview');
    await team.getByRole('heading', { name: 'Definition unavailable', exact: true }).waitFor();
    assert.equal(await team.getByRole('button', { name: 'Start agent', exact: true }).isDisabled(), true);
    state = await view();
    assert.equal(state.members.filter(member => !member.isYou && !member.isRemoved).length, 2);
    assert.deepEqual(state.channels.map(channel => channel.id).sort(), channelIds);
    const ghost = state.members.find(member => member.id === first.id);
    assert.equal(ghost.name, 'First Echo');
    assert.equal(ghost.status, 'error');
    assert.ok(ghost.error);
    await tab('Settings');
    assert.equal(await settings.inputValue(), settingsText);
    await tab('Workspace');
    await team.getByRole('link', { name: /dot.png/ }).waitFor();
    await page.screenshot({ path: '/tmp/holodeck-unresolved-agent.png' });
    await changeReference(sampleKey);
    await team.getByRole('button', { name: 'Inspect First Echo', exact: true }).locator('.status-dot[data-status="stopped"]').waitFor();
    await team.getByRole('button', { name: 'Inspect Mirror', exact: true }).locator('.status-dot[data-status="stopped"]').waitFor();
    state = await view();
    assert.equal(state.members.find(member => member.id === first.id).error, null);
    assert.equal(state.members.find(member => member.id === first.id).name, 'First Echo');
    console.log('PASS: removing a library retains unresolved agents, instance data and channels; reattaching repairs them without restarting processes');

    await changeReference(sampleKey, true);
    await changeReference(replacementKey);
    await expectGhost('First Echo');
    await inspect('First Echo'); await tab('Overview');
    await team.getByRole('button', { name: 'Replace definition', exact: true }).click();
    const replacePicker = page.getByRole('dialog', { name: 'Replace definition', exact: true });
    await replacePicker.locator(`.definition-choice[data-reference="${replacementKey}/agents/echo"]`).click();
    await page.locator('.notification.error').filter({ hasText: /already has its computer/ }).waitFor();
    await replacePicker.getByRole('button', { name: 'Close', exact: true }).click();
    state = await view();
    const retained = state.members.find(member => member.id === first.id);
    assert.equal(retained.library, `${sampleKey}/agents/echo`);
    assert.equal(retained.name, 'First Echo');
    assert.equal(retained.status, 'error');
    assert.deepEqual(state.channels.map(channel => channel.id).sort(), channelIds);
    assert.equal(await (await page.request.get(`${base}/api/projects/${projectId}/room/agents/${first.id}/settings`)).text(), settingsText);
    await changeReference(sampleKey);
    await inspect('First Echo');
    await tab('Workspace');
    await team.getByRole('link', { name: /dot.png/ }).waitFor();
    await team.getByRole('button', { name: 'Start agent', exact: true }).click();
    await team.getByRole('button', { name: 'Stop agent', exact: true }).waitFor();
    await openPersonal('First Echo');
    await send('retained container proof');
    assert.equal(await channels.locator('.bubble').count(), 24);
    console.log('PASS: a refused definition replacement preserves identity, settings, workspace and history; reattaching its library repairs the existing agent');

    const orphanResponse = await page.request.post(`${base}/api/projects/${projectId}/room/agents`, { data: { library: `${replacementKey}/agents/echo` } });
    assert.equal(orphanResponse.status(), 201);
    const orphan = await orphanResponse.json();
    await page.request.delete(`${base}/api/projects/${projectId}/room/agents/${mirror.id}`);
    await changeReference(replacementKey, true);
    await show(team);
    await team.getByRole('button', { name: 'Remove unresolved agents', exact: true }).click();
    await page.locator('.dialog-form').getByRole('button', { name: 'Remove', exact: true }).click();
    await team.getByRole('button', { name: 'Inspect Replacement Echo', exact: true }).waitFor({ state: 'detached' });
    state = await view();
    assert.equal(state.members.find(member => member.id === orphan.id).isRemoved, true);
    assert.equal(state.members.find(member => member.id === mirror.id).isRemoved, true);
    assert.equal(state.members.find(member => member.id === first.id).isRemoved, false);
    await show(channels);
    await channels.locator('.conversation-entry', { hasText: 'Second Echo' }).click();
    await channels.locator('.chat-title').filter({ hasText: /^Second Echo$/ }).waitFor();
    assert.equal(await channels.getByRole('button', { name: /Remove Mirror/ }).count(), 0, 'a removed agent leaves its channels');
    await openPersonal('First Echo');
    await channels.locator('.bubble').filter({ hasText: 'echo: retained container proof' }).waitFor();
    assert.equal(await channels.locator('.bubble').count(), 24);
    console.log('PASS: bulk unresolved cleanup removes only broken instances and retains readable channel history');

    await page.reload();
    await channels.locator('.bubble').filter({ hasText: 'echo: retained container proof' }).waitFor();
    assert.equal(await team.count(), 1); assert.equal(await channels.count(), 1); assert.equal(await library.count(), 1);
    async function frame(window) {
      return window.evaluate(node => {
        const desktop = document.querySelector('#window-layer').getBoundingClientRect(); const rect = node.getBoundingClientRect();
        return { left: rect.left - desktop.left, top: rect.top - desktop.top, width: rect.width, height: rect.height, right: desktop.right - rect.right, bottom: desktop.bottom - rect.bottom, desktopWidth: desktop.width };
      });
    }
    async function resizeTo(width, height) { await page.setViewportSize({ width, height }); await page.waitForTimeout(120); }
    await show(channels);
    if ((await frame(channels)).width >= (await frame(channels)).desktopWidth) { await channels.locator('.window-titlebar').dblclick(); }
    const before = await frame(channels);
    const titlebar = await channels.locator('.window-titlebar .window-title').boundingBox();
    const targetLeft = Math.floor(before.desktopWidth / 16) * 16 - 16 - before.width;
    await page.mouse.move(titlebar.x + 10, titlebar.y + 10); await page.mouse.down();
    await page.mouse.move(titlebar.x + 10 + targetLeft - before.left, titlebar.y + 10, { steps: 6 }); await page.mouse.up();
    const pinned = await frame(channels);
    await resizeTo(900, 700); await resizeTo(1600, 1000);
    let moved = await frame(channels);
    assert.ok(Math.abs(moved.right - pinned.right) <= 16, `right-anchored window drifted from the right edge (${pinned.right}px then ${moved.right}px)`);
    assert.equal(moved.width, pinned.width);
    await resizeTo(500, 600); await resizeTo(1400, 950);
    moved = await frame(channels);
    assert.deepEqual([moved.left, moved.top, moved.width, moved.height], [pinned.left, pinned.top, pinned.width, pinned.height], 'shrinking and growing the viewport damaged the window layout');
    await channels.locator('.window-titlebar').dblclick(); await resizeTo(1200, 800); await channels.locator('.window-titlebar').dblclick();
    moved = await frame(channels);
    assert.ok(Math.abs(moved.right - pinned.right) <= 16, `restored window lost its right anchor (${pinned.right}px then ${moved.right}px)`);
    await resizeTo(1400, 950);
    async function dragEdge(edge, dx, dy) {
      const box = await channels.locator(`.resize-${edge}`).boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await page.mouse.down();
      await page.mouse.move(box.x + box.width / 2 + dx, box.y + box.height / 2 + dy, { steps: 6 }); await page.mouse.up();
    }
    await dragEdge('n', 0, -2000); await dragEdge('s', 0, 2000);
    for (const [width, height] of [[900, 600], [1600, 1100], [1400, 950]]) {
      await resizeTo(width, height); const sides = await frame(channels);
      assert.ok(sides.top === 0 && sides.bottom < 16 && sides.right < 32, `window touching top, bottom and right lost contact at ${width}x${height}: ${JSON.stringify(sides)}`);
    }
    await dragEdge('w', -2000, 0);
    for (const [width, height] of [[1000, 700], [1700, 1100], [1400, 950]]) {
      await resizeTo(width, height); const sides = await frame(channels);
      assert.ok(sides.left === 0 && sides.top === 0 && sides.right < 32 && sides.bottom < 16, `window touching every side lost contact at ${width}x${height}: ${JSON.stringify(sides)}`);
    }
    console.log('PASS: windows keep their anchored edges across viewport resizes and maximize');

    await show(team);
    const teamTitle = await team.locator('.window-titlebar .window-title').boundingBox();
    await page.mouse.move(teamTitle.x + 10, teamTitle.y + 10); await page.mouse.down();
    await page.mouse.move(teamTitle.x + 74, teamTitle.y + 42, { steps: 4 }); await page.mouse.up();
    const movedTeam = await frame(team);
    await page.reload(); await team.locator('.team-app').waitFor();
    const reloadedTeam = await frame(team);
    assert.deepEqual([reloadedTeam.left, reloadedTeam.top, reloadedTeam.width, reloadedTeam.height], [movedTeam.left, movedTeam.top, movedTeam.width, movedTeam.height], 'an immediate reload lost the last window move');
    console.log('PASS: the project workspace is saved to the server, even right before a reload');

    await startMenu('Appearance');
    await applet('appearance').getByLabel('Theme', { exact: true }).selectOption('dark');
    const appearanceSaved = page.waitForResponse(response => response.url().endsWith('/api/appearance') && response.request().method() === 'PUT');
    await applet('appearance').locator('.setting-color').first().evaluate(input => { input.value = '#ff0000'; input.dispatchEvent(new Event('input', { bubbles: true })); });
    await appearanceSaved;
    assert.equal(await accent(), '#ff0000');
    await page.locator('.main-menu-button:visible').click();
    await page.locator('#boot').waitFor();
    assert.equal(await accent(), '#ff0000', 'the main menu uses the same appearance as projects');
    const other = await (await page.request.post(`${base}/api/projects`, { data: { name: `Other ${Date.now()}` } })).json();
    otherProjectId = other.id;
    await page.goto(`${base}/?project=${other.id}`); await page.locator('#project-name').filter({ hasText: other.name }).waitFor();
    assert.equal(await accent(), '#ff0000', 'every project uses the same appearance');
    await page.goto(`${base}/?project=${projectId}`); await team.locator('.team-app').waitFor();
    await applet('appearance').locator('.window-controls button[aria-label="Close window"]').click();
    await page.goto(`${base}/?project=missing`);
    await page.locator('#boot').waitFor(); await page.locator('.notification.error').waitFor();
    await page.goto(`${base}/?project=${projectId}`); await team.locator('.team-app').waitFor();
    console.log('PASS: one appearance covers the main menu and every project; an unknown project falls back to the main menu');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(150);
    for (const window of [team, channels, library]) {
      await show(window);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth);
      assert.ok(overflow <= 390, `page scrolls sideways at phone width (${overflow}px)`);
      const bodyOverflow = await window.locator('.window-body').evaluate(node => node.scrollWidth - node.clientWidth);
      assert.ok(bodyOverflow <= 1, `applet body scrolls sideways (${bodyOverflow}px)`);
    }
    await page.screenshot({ path: '/tmp/holodeck-hierarchy-mobile.png' });
    assert.deepEqual(errors, []);
    console.log('PASS: contextual applets restore once and fit phone width without browser errors');
    console.log('room browser check passed');
  } finally {
    try {
      for (const id of [projectId, otherProjectId]) {
        if (!id) { continue; }
        assert.match(id, /^[a-f0-9]{32}$/, 'cleanup only accepts the generated project id');
        const deleted = await page.request.delete(`${base}/api/projects/${id}`);
        assert.ok(deleted.ok(), `could not delete browser-check project ${id}: HTTP ${deleted.status()}`);
        removeRoom(id);
      }
    } finally {
      try {
        await page.waitForTimeout(400);
        const restored = await page.request.put(`${base}/api/appearance`, { data: { settings: originalAppearance.settings || {} } });
        assert.ok(restored.ok(), 'the original shared appearance is restored');
      }
      finally {
        await browser.close();
        fs.rmSync(replacementPath, { recursive: true, force: true });
        fixture.remove();
      }
    }
  }
})().catch(error => { fs.rmSync(replacementPath, { recursive: true, force: true }); fixture.remove(); console.error(error); process.exit(1); });
