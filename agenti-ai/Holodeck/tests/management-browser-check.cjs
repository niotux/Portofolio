// A temporary library proves generic private actions through the real API and a real agent container.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { removeRoom } = require('./room-files.cjs');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const libraryId = `holodeck.management-check-${Date.now()}`;
const libraryPath = path.join(__dirname, '..', 'libs', libraryId);
const agentPath = path.join(libraryPath, 'agents', 'worker');

const inputSchema = {
  type: 'object', additionalProperties: false, required: ['brief'],
  properties: { brief: { type: 'string', title: 'Brief', minLength: 1, maxLength: 200, 'x-ui': { control: 'multiline' } } }
};
const resultSchema = {
  type: 'object', additionalProperties: false, required: ['text', 'revision'],
  properties: {
    text: { type: 'string', title: 'Draft text', minLength: 1, maxLength: 1000, 'x-ui': { control: 'multiline' } },
    revision: { type: 'string', title: 'Revision', maxLength: 100 }
  }
};

function writeLibrary() {
  fs.mkdirSync(agentPath, { recursive: true }); fs.mkdirSync(path.join(libraryPath, 'teams'), { recursive: true });
  const write = (name, value) => fs.writeFileSync(path.join(agentPath, name), JSON.stringify(value, null, 2));
  fs.writeFileSync(path.join(libraryPath, 'Library.json'), JSON.stringify({ FormatVersion: 1, Id: libraryId, Version: '1.0.0', Name: 'Management Check', Dependencies: [], Runtime: { Image: 'holodeck-library:1', Processes: [] } }));
  write('Agent.json', { Id: 'worker', Name: 'Worker', Launch: { Command: 'python3', Args: ['${Agent}/worker.py'] }, Settings: 'settings.json', Actions: 'actions.json' });
  write('settings.json', { sample: 'original' });
  write('input.schema.json', inputSchema); write('result.schema.json', resultSchema);
  write('actions.json', { FormatVersion: 1, Actions: [
    { Id: 'draft', Title: 'Create draft', Description: 'Create a test draft privately.', InputSchema: 'input.schema.json', ResultSchema: 'result.schema.json', FollowUpActions: ['apply'] },
    { Id: 'apply', Title: 'Apply draft', Description: 'Apply the edited test result.', InputSchema: 'result.schema.json', ResultSchema: 'result.schema.json' }
  ] });
  fs.writeFileSync(path.join(agentPath, 'worker.py'), `import json, os, time, uuid, urllib.request, urllib.error
url = os.environ['HOLODECK_URL'].rstrip('/')
key = os.environ['HOLODECK_KEY']
def call(route, body):
    request = urllib.request.Request(url + '/api/agent' + route, data=json.dumps(body).encode(), headers={'Authorization': 'Bearer ' + key, 'Content-Type': 'application/json'}, method='POST')
    with urllib.request.urlopen(request, timeout=15) as response:
        data = response.read()
        return json.loads(data) if data else None
while True:
    try:
        claim = call('/jobs/claim', {'claimId': uuid.uuid4().hex})
        if claim is None:
            time.sleep(0.2)
            continue
        job = claim['job']
        token = claim['attemptToken']
        route = '/jobs/' + str(job['id'])
        call(route + '/progress', {'attemptToken': token, 'progress': 25, 'message': 'Checking the source material'})
        if job['actionId'] == 'draft':
            if job['input']['brief'] == 'hold':
                time.sleep(15)
            else:
                time.sleep(3)
            result = {'text': 'Draft: ' + job['input']['brief'], 'revision': 'preview'}
        else:
            result = {'text': job['input']['text'], 'revision': 'applied'}
        call(route + '/complete', {'attemptToken': token, 'result': result})
    except urllib.error.HTTPError as error:
        if error.code == 401:
            break
        print('HTTP', error.code, flush=True)
        time.sleep(0.2)
    except (OSError, ValueError) as error:
        print(type(error).__name__, flush=True)
        time.sleep(0.2)
`);
}

(async () => {
  writeLibrary();
  const browser = await chromium.launch({ executablePath: process.env.CHROME_EXECUTABLE || '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  let projectId;
  const team = page.locator('.applet-window[data-applet="room"]');
  const actions = team.getByRole('region', { name: 'Agent actions', exact: true });
  const api = async (method, url, data) => {
    const response = await page.request.fetch(base + url, { method, data });
    assert.ok(response.ok(), `${method} ${url}: HTTP ${response.status()} ${await response.text()}`);
    return response.status() === 204 ? null : response.json();
  };
  async function settings() { await team.getByRole('tab', { name: 'Settings', exact: true }).click(); await actions.getByRole('heading', { name: 'Actions', exact: true }).waitFor(); }
  async function choose(name) { await team.getByRole('button', { name: `Inspect ${name}`, exact: true }).click(); await settings(); }
  async function openTeam(project) {
    await page.goto(`${base}/?project=${projectId}`);
    await page.locator('#project-name').filter({ hasText: project.name }).waitFor();
    if (!await team.count()) {
      await page.locator('#start').click();
      await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: /^Team$/ }) }).click();
    }
    await team.locator('.team-app').waitFor();
  }
  try {
    const project = await api('POST', '/api/projects', { name: `Management check ${Date.now()}` }); projectId = project.id;
    await api('POST', `/api/projects/${projectId}/libraries`, { id: libraryId, version: '1.0.0' });
    const room = `/api/projects/${projectId}/room`;
    const first = await api('POST', room + '/agents', { library: `${libraryId}/1.0.0/agents/worker` });
    const second = await api('POST', room + '/agents', { library: `${libraryId}/1.0.0/agents/worker` });
    const endpoint = id => `${room}/agents/${id}`;
    await openTeam(project); await choose(first.name);
    const input = () => actions.getByRole('region', { name: 'Create draft', exact: true });
    await input().getByLabel('Brief', { exact: true }).fill('first idea');
    assert.equal(await input().getByRole('button', { name: 'Create draft', exact: true }).isDisabled(), true);
    await actions.getByText('Start agent to run an action. Settings and previous results remain available.', { exact: true }).waitFor();
    assert.deepEqual(await api('GET', endpoint(first.id) + '/jobs'), []);
    console.log('PASS: stopped agents show action forms without starting or queuing work');

    await choose(second.name); await input().getByLabel('Brief', { exact: true }).fill('second idea');
    await choose(first.name); assert.equal(await input().getByLabel('Brief', { exact: true }).inputValue(), 'first idea');
    const changedSchema = structuredClone(inputSchema); changedSchema.properties.brief.maxLength = 180;
    fs.writeFileSync(path.join(agentPath, 'input.schema.json'), JSON.stringify(changedSchema, null, 2));
    const definitionConflict = input().getByRole('region', { name: 'Action definition conflict', exact: true });
    await definitionConflict.getByRole('heading', { name: 'The action definition changed', exact: true }).waitFor();
    assert.equal(await input().getByLabel('Brief', { exact: true }).last().inputValue(), 'first idea', 'a definition change retains the previous draft');
    await definitionConflict.getByRole('button', { name: 'Keep my draft in the updated form', exact: true }).click();
    assert.equal(await input().getByLabel('Brief', { exact: true }).inputValue(), 'first idea');
    assert.equal(await definitionConflict.count(), 0);
    console.log('PASS: a changed action definition retains the draft until explicit review and reconciliation');
    await api('POST', endpoint(first.id) + '/start');
    await input().getByRole('button', { name: 'Create draft', exact: true }).waitFor();
    await page.waitForFunction(() => !document.querySelector('.management-action-form button')?.disabled);
    await page.waitForTimeout(2300);
    assert.equal(await input().getByLabel('Brief', { exact: true }).inputValue(), 'first idea', 'polling retains action input');
    await input().getByLabel('Brief', { exact: true }).fill('');
    await input().getByRole('button', { name: 'Create draft', exact: true }).click();
    await input().locator('.error').filter({ hasText: 'at least 1' }).waitFor();
    assert.equal((await api('GET', endpoint(first.id) + '/jobs')).length, 0);
    await input().getByLabel('Brief', { exact: true }).fill('<script>window.fixtureInjected = true</script>');
    await input().getByRole('button', { name: 'Create draft', exact: true }).click();
    await actions.getByRole('region', { name: 'Selected work', exact: true }).getByText('Checking the source material', { exact: true }).waitFor({ timeout: 20000 });
    assert.equal(await actions.getByRole('progressbar', { name: 'Work progress' }).getAttribute('value'), '25');
    const result = actions.getByRole('region', { name: 'Job result', exact: true });
    await result.getByLabel('Draft text', { exact: true }).waitFor({ timeout: 20000 });
    assert.equal(await page.evaluate(() => window.fixtureInjected), undefined);
    assert.equal(await result.getByLabel('Draft text', { exact: true }).inputValue(), 'Draft: <script>window.fixtureInjected = true</script>');
    console.log('PASS: schema errors leave no job; a real agent reports private progress and safe results');

    await actions.getByRole('region', { name: 'Selected work', exact: true }).getByRole('button', { name: 'Apply draft', exact: true }).click();
    const edit = actions.locator('.management-editor').getByRole('region', { name: 'Apply draft', exact: true });
    await edit.getByLabel('Draft text', { exact: true }).fill('my edited draft');
    await page.waitForTimeout(2300);
    assert.equal(await edit.getByLabel('Draft text', { exact: true }).inputValue(), 'my edited draft');
    await team.getByRole('tab', { name: 'Overview', exact: true }).click(); await settings();
    assert.equal(await edit.getByLabel('Draft text', { exact: true }).inputValue(), 'my edited draft', 'navigation retains edited results');
    const settingsText = team.getByRole('region', { name: 'Agent settings', exact: true }).getByLabel('Settings', { exact: true });
    await settingsText.fill('{"sample":"changed"}');
    await team.getByRole('button', { name: 'Save settings', exact: true }).click();
    assert.equal(await edit.getByLabel('Draft text', { exact: true }).inputValue(), 'my edited draft', 'settings save keeps the separate action editor');
    await edit.getByRole('button', { name: 'Apply draft', exact: true }).click();
    await result.getByLabel('Revision', { exact: true }).waitFor();
    await page.waitForFunction(() => document.querySelector('.management-result input[aria-label="Revision"]')?.value === 'applied');
    assert.equal(await result.getByLabel('Draft text', { exact: true }).inputValue(), 'my edited draft');
    const jobs = await api('GET', endpoint(first.id) + '/jobs');
    assert.equal(jobs.length, 2); assert.equal(jobs[0].input.text, 'my edited draft');
    console.log('PASS: advertised follow-ups use editable schema forms; input survives polling/navigation/settings saves');

    await openTeam(project); await choose(first.name);
    assert.equal(await actions.locator('.management-job').count(), 2, 'reload recovers durable work');
    await actions.locator('.management-job').first().getByRole('button', { name: 'Apply draft', exact: true }).click();
    assert.equal(await result.getByLabel('Draft text', { exact: true }).inputValue(), 'my edited draft');
    await input().getByLabel('Brief', { exact: true }).fill('hold');
    await input().getByRole('button', { name: 'Create draft', exact: true }).click();
    await actions.getByRole('region', { name: 'Selected work', exact: true }).getByText('Checking the source material', { exact: true }).waitFor();
    await actions.locator('.management-job').first().getByRole('button', { name: 'Cancel', exact: true }).click();
    await actions.getByRole('region', { name: 'Selected work', exact: true }).getByText('canceled', { exact: true }).waitFor();
    await api('POST', endpoint(first.id) + '/stop');
    await actions.getByText('Start agent to run an action. Settings and previous results remain available.', { exact: true }).waitFor();
    assert.equal((await api('GET', endpoint(first.id) + '/jobs')).length, 3);
    assert.deepEqual(await api('GET', endpoint(second.id) + '/jobs'), [], 'actions never route through another agent');
    assert.equal((await api('GET', room)).channels.length, 0, 'management traffic creates no public channel');
    console.log('PASS: reload, cancellation and stopping preserve durable work; instances stay isolated');

    await page.setViewportSize({ width: 390, height: 844 }); await page.waitForTimeout(100);
    assert.ok(await team.locator('.project-agent-pane').evaluate(node => node.scrollWidth - node.clientWidth) <= 1);
    assert.equal(await page.evaluate(() => window.fixtureInjected), undefined);
    assert.deepEqual(errors, []); await page.screenshot({ path: '/tmp/holodeck-management-phone.png' });
    console.log('management browser check passed');
  } catch (error) {
    console.error('Visible notices:', await page.locator('.notification, .management-actions .error').allTextContents().catch(() => []));
    await page.screenshot({ path: '/tmp/holodeck-management-failure.png' }).catch(() => null); throw error;
  } finally {
    try {
      if (projectId) {
        assert.match(projectId, /^[a-f0-9]{32}$/);
        await api('DELETE', `/api/projects/${projectId}`);
        removeRoom(projectId);
      }
    } finally { await browser.close(); fs.rmSync(libraryPath, { recursive: true, force: true }); }
  }
})().catch(error => { fs.rmSync(libraryPath, { recursive: true, force: true }); console.error(error); process.exit(1); });
