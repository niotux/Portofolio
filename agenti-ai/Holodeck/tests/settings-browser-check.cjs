// Browser check for schema-defined Settings forms in Team. Creates a temporary local library and a project, then
// removes exactly those. The fixture library supplies an agent without a schema for the raw editor.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const libraryId = `holodeck.settings-check-${Date.now()}`;
const libraryPath = path.join(__dirname, '..', 'libs', libraryId);
const agentPath = path.join(libraryPath, 'agents', 'formed');
const fixture = require('./fixture-library.cjs');
const { removeRoom } = require('./room-files.cjs');
const largeNumber = '123456789012345678901234567890';

// A fixture schema; its field names belong to this temporary library, not to Holodeck.
function schema(maxLength) {
  return {
    type: 'object', title: 'Formed settings', additionalProperties: true, required: ['personality', 'model'],
    'x-ui': { order: ['personality', 'model'] },
    properties: {
      personality: { type: 'string', title: 'Personality', description: 'How this instance talks.', maxLength, 'x-ui': { control: 'multiline' } },
      model: {
        type: 'object', title: 'Model', additionalProperties: false, required: ['name'],
        properties: { name: { type: 'string', title: 'Model name', minLength: 1 }, key: { type: 'string', title: 'API key', 'x-ui': { control: 'password' } } }
      },
      temperature: { type: 'number', title: 'Temperature', minimum: 0, maximum: 2, default: 0.7, 'x-ui': { group: 'Generation' } },
      rounds: { type: 'integer', title: 'Rounds', minimum: 1, maximum: 20, 'x-ui': { group: 'Generation' } },
      tone: { type: 'string', title: 'Tone', enum: ['brief', 'detailed'] },
      thinking: { type: 'boolean', title: 'Thinking' }
    }
  };
}

function writeLibrary() {
  fs.mkdirSync(agentPath, { recursive: true });
  fs.mkdirSync(path.join(libraryPath, 'teams'), { recursive: true });
  fs.writeFileSync(path.join(libraryPath, 'Library.json'), JSON.stringify({ FormatVersion: 1, Id: libraryId, Version: '1.0.0', Name: 'Settings Check', Dependencies: [], Runtime: { Image: 'holodeck-library:1', Processes: [] } }));
  fs.writeFileSync(path.join(agentPath, 'Agent.json'), JSON.stringify({ Id: 'formed', Name: 'Formed', Launch: { Command: 'python3', Args: ['${Agent}/agent.py'] }, Settings: 'settings.json', SettingsSchema: 'settings.schema.json' }));
  fs.writeFileSync(path.join(agentPath, 'settings.json'), `{\n  "personality": "plain",\n  "model": { "name": "initial-model" },\n  "notes": { "large": ${largeNumber}, "text": "café" }\n}\n`);
  fs.writeFileSync(path.join(agentPath, 'settings.schema.json'), JSON.stringify(schema(400), null, 2));
  fs.copyFileSync(fixture.echoProgram, path.join(agentPath, 'agent.py'));
}

(async () => {
  fixture.install();
  writeLibrary();
  const browser = await chromium.launch({ executablePath: process.env.CHROME_EXECUTABLE || '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1400, height: 950 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  let projectId;
  const team = page.locator('.applet-window[data-applet="room"]');
  const api = async (method, url, data) => {
    const response = await page.request.fetch(`${base}${url}`, { method, data });
    assert.ok(response.ok(), `${method} ${url} failed: HTTP ${response.status()} ${await response.text()}`);
    return response.status() === 204 ? null : response.json();
  };
  const room = () => `/api/projects/${projectId}/room`;
  const form = id => `${room()}/agents/${id}/settings/form`;
  const stored = async id => JSON.parse((await api('GET', form(id))).content);
  const field = label => team.getByLabel(label, { exact: true });
  const status = () => team.locator('.settings-status');
  async function inspect(name) {
    await team.getByRole('button', { name: `Inspect ${name}`, exact: true }).click();
    await team.locator('.project-agent-title').filter({ hasText: new RegExp(`^${name}$`) }).waitFor();
  }
  async function settingsTab() {
    await team.getByRole('tab', { name: 'Settings', exact: true }).click();
    await team.locator('.project-settings-actions').waitFor();
  }
  async function save() { await team.getByRole('button', { name: 'Save settings', exact: true }).click(); }
  async function saveElsewhere(id, personality) {
    const view = await api('GET', form(id));
    await api('PUT', form(id), { baseRevision: view.revision, schemaRevision: view.schemaRevision, changes: [{ path: '/personality', value: personality }] });
  }

  try {
    const project = await api('POST', '/api/projects', { name: `Settings check ${Date.now()}` });
    projectId = project.id;
    await api('POST', `/api/projects/${projectId}/libraries`, { id: libraryId, version: '1.0.0' });
    await api('POST', `/api/projects/${projectId}/libraries`, { id: fixture.Id, version: '1.0.0' });
    const first = await api('POST', `${room()}/agents`, { library: `${libraryId}/1.0.0/agents/formed` });
    const second = await api('POST', `${room()}/agents`, { library: `${libraryId}/1.0.0/agents/formed` });
    const rawAgent = await api('POST', `${room()}/agents`, { library: `${fixture.Key}/agents/raw` });
    const firstInitial = await api('GET', form(first.id));

    await page.goto(`${base}/?project=${projectId}`);
    await page.locator('#project-name').filter({ hasText: project.name }).waitFor();
    await page.locator('#start').click();
    await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: /^Team$/ }) }).click();
    await team.locator('.team-app').waitFor();

    await inspect(first.name); await settingsTab();
    await team.locator('.settings-facts dd').filter({ hasText: `${libraryId}/1.0.0/agents/formed` }).waitFor();
    await team.locator('.settings-facts dd').filter({ hasText: firstInitial.schemaRevision.slice(0, 12) }).waitFor();
    assert.equal(await field('Personality').evaluate(node => node.tagName), 'TEXTAREA');
    assert.equal(await field('API key').getAttribute('type'), 'password');
    assert.equal(await field('Temperature').getAttribute('type'), 'number');
    assert.equal(await field('Thinking').getAttribute('type'), 'checkbox');
    assert.deepEqual(await field('Tone').locator('option').allTextContents(), ['Not set', 'brief', 'detailed']);
    await team.locator('legend').filter({ hasText: /^Generation$/ }).waitFor();
    await team.locator('legend').filter({ hasText: /^Model$/ }).waitFor();
    await team.getByText('Suggested: 0.7', { exact: true }).waitFor();
    await team.getByText('One stored value is not part of this form and is kept as stored.', { exact: true }).waitFor();
    assert.equal(await field('Temperature').inputValue(), '', 'a schema default is shown as a suggestion, not filled in');
    assert.equal(await field('Personality').inputValue(), 'plain');
    assert.equal(await team.getByRole('button', { name: 'Discard changes', exact: true }).isVisible(), false);
    await field('Personality').fill('temporary draft');
    await team.getByRole('button', { name: 'Discard changes', exact: true }).click();
    assert.equal(await field('Personality').inputValue(), 'plain', 'discard restores the stored field value');
    assert.equal(await team.getByRole('button', { name: 'Discard changes', exact: true }).isVisible(), false);
    console.log('PASS: a schema-defined form renders groups, help, limits, suggestions and proper controls while the agent is stopped');

    await field('Personality').fill('patient');
    await field('Model name').fill('model-one');
    await field('Tone').selectOption('brief');
    await field('Thinking').check();
    assert.equal(await status().textContent(), 'Unsaved changes');
    await api('PUT', `${room()}/agents/${rawAgent.id}`, { name: 'Raw renamed' });
    await team.getByRole('button', { name: 'Inspect Raw renamed', exact: true }).waitFor();
    assert.equal(await field('Personality').inputValue(), 'patient', 'live events keep unsaved form values');
    await inspect(second.name); await settingsTab();
    assert.equal(await field('Personality').inputValue(), 'plain', 'instances do not share drafts');
    await field('Personality').fill('blunt');
    await field('Model name').fill('model-two');
    await inspect(first.name); await settingsTab();
    assert.equal(await field('Personality').inputValue(), 'patient');
    assert.equal(await field('Model name').inputValue(), 'model-one');
    await team.getByRole('tab', { name: 'Overview', exact: true }).click(); await settingsTab();
    assert.equal(await field('Tone').inputValue(), 'brief', 'tab navigation keeps unsaved form values');
    await save(); await status().filter({ hasText: /^Saved$/ }).waitFor();
    await inspect(second.name); await settingsTab();
    assert.equal(await field('Personality').inputValue(), 'blunt');
    await save(); await status().filter({ hasText: /^Saved$/ }).waitFor();
    const one = await stored(first.id); const two = await stored(second.id);
    assert.deepEqual([one.personality, one.model.name, one.tone, one.thinking], ['patient', 'model-one', 'brief', true]);
    assert.deepEqual([two.personality, two.model.name], ['blunt', 'model-two']);
    assert.equal('temperature' in one, false, 'missing defaults are not written');
    const firstText = (await api('GET', form(first.id))).content;
    assert.ok(firstText.includes(largeNumber) && firstText.includes('café'), 'values outside the form keep their exact text');
    console.log('PASS: two instances keep different values across agent switches, tab switches and live events; unknown values survive');

    await inspect(first.name); await settingsTab();
    const beforeInvalid = (await api('GET', form(first.id))).revision;
    await field('Temperature').fill('9');
    await field('Model name').fill('');
    await save();
    await team.locator('.settings-field[data-path="/temperature"] .error').filter({ hasText: 'Use 2 or less.' }).waitFor();
    await team.locator('.settings-field[data-path="/model/name"] .error').filter({ hasText: 'Use at least 1 characters.' }).waitFor();
    assert.equal((await api('GET', form(first.id))).revision, beforeInvalid, 'refused values do not change the file');
    assert.equal(await field('Temperature').inputValue(), '9', 'refused values stay in the form for correction');
    await field('Temperature').fill('');
    await field('Model name').fill('model-one');
    await save(); await status().filter({ hasText: /^No changes to save$/ }).waitFor();
    console.log('PASS: invalid values are refused with field errors and leave the stored settings unchanged');

    await field('Personality').fill('mine');
    await saveElsewhere(first.id, 'changed elsewhere');
    await save();
    const conflict = team.getByRole('region', { name: 'Settings conflict' });
    await conflict.getByRole('heading', { name: 'These settings changed elsewhere', exact: true }).waitFor();
    await conflict.getByText('changed elsewhere', { exact: true }).waitFor();
    assert.equal(await field('Personality').inputValue(), 'mine', 'a conflict keeps the unsaved value');
    assert.equal((await stored(first.id)).personality, 'changed elsewhere', 'a stale save does not overwrite newer settings');
    await conflict.getByRole('button', { name: 'Keep my changes on the latest settings', exact: true }).click();
    await save(); await status().filter({ hasText: /^Saved$/ }).waitFor();
    assert.equal((await stored(first.id)).personality, 'mine');
    await field('Personality').fill('discarded');
    await saveElsewhere(first.id, 'kept elsewhere');
    await save();
    await conflict.getByRole('button', { name: 'Use the latest settings', exact: true }).click();
    assert.equal(await field('Personality').inputValue(), 'kept elsewhere');
    await page.screenshot({ path: '/tmp/holodeck-settings-form.png' });
    console.log('PASS: stale saves show both values and resolve only by an explicit choice');

    await field('Personality').fill('after form change');
    fs.writeFileSync(path.join(agentPath, 'settings.schema.json'), JSON.stringify(schema(300), null, 2));
    await save();
    await conflict.getByRole('heading', { name: 'The settings form changed', exact: true }).waitFor();
    await conflict.getByRole('button', { name: 'Keep my changes on the latest settings', exact: true }).click();
    await save(); await status().filter({ hasText: /^Saved$/ }).waitFor();
    assert.equal((await stored(first.id)).personality, 'after form change');
    console.log('PASS: a changed library schema is reconciled visibly instead of dropping values');

    const beforeRun = await api('GET', form(first.id));
    await api('POST', `${room()}/agents/${first.id}/start`);
    await team.locator('.project-agent-header').getByText('Running').waitFor({ timeout: 30000 });
    await api('POST', `${room()}/agents/${first.id}/stop`);
    await team.locator('.project-agent-header').getByText('Stopped').waitFor({ timeout: 30000 });
    assert.equal((await api('GET', form(first.id))).content, beforeRun.content, 'starting and stopping keeps settings');
    console.log('PASS: settings survive starting and stopping the agent');

    await inspect('Raw renamed'); await settingsTab();
    const raw = field('Settings');
    const rawText = await raw.inputValue();
    assert.ok(rawText.includes('"model"'), 'an agent without a schema keeps its raw settings text');
    const edited = rawText.replace('"history_messages": 40', '"history_messages": 41');
    await raw.fill(edited);
    const rawSaved = page.waitForResponse(response => response.url().endsWith(`/agents/${rawAgent.id}/settings/form`) && response.request().method() === 'PUT');
    await save();
    assert.ok((await rawSaved).ok(), 'the raw save request succeeds');
    await status().filter({ hasText: /^Saved$/ }).waitFor();
    assert.equal(await (await page.request.get(`${base}${room()}/agents/${rawAgent.id}/settings`)).text(), edited);
    console.log('PASS: a definition without a schema keeps its raw settings editor');

    await api('DELETE', `/api/projects/${projectId}/libraries/${libraryId}/1.0.0`);
    await inspect(first.name); await settingsTab();
    await team.locator('.settings-notice').filter({ hasText: 'Restore the definition' }).waitFor();
    assert.ok((await field('Settings').inputValue()).includes('after form change'), 'an unresolved definition keeps its stored settings');
    assert.equal(await team.locator('.settings-form').count(), 0, 'no form is invented without the definition');
    await api('POST', `/api/projects/${projectId}/libraries`, { id: libraryId, version: '1.0.0' });
    await team.getByRole('tab', { name: 'Overview', exact: true }).click(); await settingsTab();
    await field('Personality').waitFor();
    console.log('PASS: an unresolved definition shows its stored settings as text and the form returns with the library');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(200);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth);
    assert.ok(overflow <= 390, `page scrolls sideways at phone width (${overflow}px)`);
    const bodyOverflow = await team.locator('.window-body').evaluate(node => node.scrollWidth - node.clientWidth);
    assert.ok(bodyOverflow <= 1, `Team scrolls sideways at phone width (${bodyOverflow}px)`);
    const paneOverflow = await team.locator('.project-agent-pane').evaluate(node => node.scrollWidth - node.clientWidth);
    assert.ok(paneOverflow <= 1, `Settings scrolls sideways at phone width (${paneOverflow}px)`);
    await page.screenshot({ path: '/tmp/holodeck-settings-phone.png' });
    assert.deepEqual(errors, []);
    console.log('PASS: the form fits phone width without browser errors');
    console.log('settings browser check passed');
  } catch (error) {
    const notices = await page.locator('.notification, .settings-errors .error, .settings-field .error').allTextContents().catch(() => []);
    console.error('Visible notices:', JSON.stringify(notices));
    await page.screenshot({ path: '/tmp/holodeck-settings-failure.png' }).catch(() => null);
    throw error;
  } finally {
    try {
      if (projectId) {
        assert.match(projectId, /^[a-f0-9]{32}$/, 'cleanup only accepts the generated project id');
        const deleted = await page.request.delete(`${base}/api/projects/${projectId}`);
        assert.ok(deleted.ok(), `could not delete settings-check project ${projectId}: HTTP ${deleted.status()}`);
        removeRoom(projectId);
      }
    } finally {
      try { await browser.close(); }
      finally { fs.rmSync(libraryPath, { recursive: true, force: true }); fixture.remove(); }
    }
  }
})().catch(error => { fs.rmSync(libraryPath, { recursive: true, force: true }); console.error(error); process.exit(1); });
