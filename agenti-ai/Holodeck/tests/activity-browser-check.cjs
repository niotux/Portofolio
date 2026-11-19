// Focused live-chat check. A temporary deterministic agent exercises activity transport and UI without model calls.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { removeRoom } = require('./room-files.cjs');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const libraryId = `holodeck.activity-check-${Date.now()}`;
const directory = path.join(__dirname, '..', 'libs', libraryId);
const agentFolder = path.join(directory, 'agents', 'worker');
fs.mkdirSync(agentFolder, { recursive: true }); fs.mkdirSync(path.join(directory, 'teams'));
fs.writeFileSync(path.join(directory, 'Library.json'), JSON.stringify({ FormatVersion: 1, Id: libraryId, Version: '1.0.0', Name: 'Activity Check', Dependencies: [], Runtime: { Image: 'holodeck-library:1', Processes: [] } }));
fs.writeFileSync(path.join(agentFolder, 'Agent.json'), JSON.stringify({ Id: 'worker', Name: 'Thinking worker', Launch: { Command: 'python3', Args: ['${Agent}/worker.py'] } }));
fs.writeFileSync(path.join(agentFolder, 'worker.py'), `import json,os,time,uuid,urllib.request
base=os.environ['HOLODECK_URL'].rstrip('/')+'/api/agent/'
key=os.environ['HOLODECK_KEY']
def call(route,body=None):
 r=urllib.request.Request(base+route,data=None if body is None else json.dumps(body).encode(),headers={'Authorization':'Bearer '+key,'Content-Type':'application/json'})
 with urllib.request.urlopen(r,timeout=20) as response:
  raw=response.read()
  return json.loads(raw) if raw else None
while True:
 packet=call('next?wait=2')
 if not packet: continue
 run=uuid.uuid4().hex
 def report(ordinal,phase,label,text,typing=False,complete=False,state='working'):
  call('activity',{'id':run,'channelId':packet['channelId'],'packetId':packet['packetId'],'ordinal':ordinal,'phase':phase,'label':label,'text':text,'isTyping':typing,'isComplete':complete,'state':state})
 report(0,'feeling','Feeling','I feel')
 time.sleep(2)
 report(0,'feeling','Feeling','I feel curious. <img src=x onerror=alert(1)>',complete=True)
 report(1,'reason','Reasoning','I want to check the assumption.')
 time.sleep(2)
 report(1,'reason','Reasoning','I want to check the assumption before agreeing.',complete=True)
 report(2,'reply','Writing','',typing=True)
 time.sleep(3)
 call('send',{'channel':packet['channelId'],'note':'One ordinary public reply.','idempotencyKey':run})
 report(2,'reply','Writing','One ordinary public reply.',complete=True,state='completed')
`);

(async () => {
  const browser = await chromium.launch({ executablePath: '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1350, height: 950 } });
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  const api = async (route, method = 'GET', data) => {
    const response = await page.request.fetch(base + route, { method, data });
    assert.ok(response.ok(), `${route}: ${response.status()} ${await response.text()}`);
    return response.status() === 204 ? null : response.json();
  };
  let project; let agent;
  try {
    project = await api('/api/projects', 'POST', { name: 'Activity browser check ' + Date.now() });
    const room = `/api/projects/${project.id}/room`;
    const you = (await api(room)).members.find(member => member.isYou);
    await api(`/api/projects/${project.id}/libraries`, 'POST', { id: libraryId, version: '1.0.0' });
    agent = await api(room + '/agents', 'POST', { library: `${libraryId}/1.0.0/agents/worker` });
    const channel = await api(room + '/channels', 'POST', { name: 'Live thinking', memberIds: [you.id, agent.id] });
    const other = await api(room + '/channels', 'POST', { name: 'Other channel', memberIds: [you.id] });
    await api(room + `/agents/${agent.id}/start`, 'POST');
    async function open() {
      await page.goto(`${base}/?project=${project.id}`);
      await page.locator('#project-name').filter({ hasText: project.name }).waitFor();
      const window = page.locator('.applet-window[data-applet="conversations"]');
      if (!await window.count()) {
        await page.locator('#start').click();
        await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: /^Channels$/ }) }).click();
      }
      await window.locator('.conversations-app').waitFor();
      return window;
    }
    let chat = await open();
    await chat.locator('.conversation-entry').filter({ has: page.locator('strong', { hasText: /^Live thinking$/ }) }).click();
    await chat.getByLabel('Message', { exact: true }).fill('Please consider this.');
    await chat.getByRole('button', { name: 'Send', exact: true }).click();
    const thoughts = chat.locator('.agent-thoughts');
    await thoughts.locator('summary').first().waitFor();
    assert.equal(await thoughts.locator('details').first().evaluate(node => node.open), false, 'activity starts collapsed');
    await thoughts.locator('summary').first().click();
    await thoughts.getByText('I feel', { exact: true }).waitFor();
    await chat.getByLabel('Message', { exact: true }).fill('My unsent draft stays here.');
    await thoughts.getByText('I want to check the assumption before agreeing.', { exact: true }).waitFor();
    assert.equal(await chat.getByLabel('Message', { exact: true }).inputValue(), 'My unsent draft stays here.');
    assert.equal(await thoughts.locator('img').count(), 0, 'inner prose is rendered as text');
    await chat.locator('.agent-typing').filter({ hasText: 'is typing' }).waitFor();
    await page.screenshot({ path: '/tmp/holodeck-live-thoughts.png' });
    await chat.locator('.bubble-text').filter({ hasText: /^One ordinary public reply\.$/ }).waitFor();
    await page.waitForFunction(() => document.querySelector('.agent-typing')?.hidden === true);
    assert.equal(await chat.locator('.bubble-text').filter({ hasText: /^One ordinary public reply\.$/ }).count(), 1);
    console.log('PASS: live prose, typing, text-safe rendering, one public reply and preserved composer draft');
    await chat.locator('.conversation-entry').filter({ has: page.locator('strong', { hasText: /^Other channel$/ }) }).click();
    assert.equal(await chat.locator('.agent-thoughts').count(), 0);
    await chat.locator('.conversation-entry').filter({ has: page.locator('strong', { hasText: /^Live thinking$/ }) }).click();
    assert.equal(await chat.getByLabel('Message', { exact: true }).inputValue(), 'My unsent draft stays here.');
    await page.route('**/channels/*/packets?after=*', async route => { await new Promise(resolve => setTimeout(resolve, 400)); await route.continue(); });
    await page.reload();
    chat = page.locator('.applet-window[data-applet="conversations"]');
    await chat.locator('.agent-thoughts').waitFor();
    await chat.locator('.bubble-text').filter({ hasText: /^Please consider this\.$/ }).waitFor();
    await page.waitForFunction(() => { const card = document.querySelector('.agent-thoughts'); return card?.previousElementSibling?.dataset.packetId === card?.dataset.sourcePacketId; });
    await chat.locator('.agent-thoughts summary').click();
    await chat.getByText('I feel curious. <img src=x onerror=alert(1)>', { exact: true }).waitFor();
    console.log('PASS: thoughts survive channel switches and reload without entering other channels');
    await chat.getByLabel('Message', { exact: true }).fill('A second request to interrupt.');
    await chat.getByRole('button', { name: 'Send', exact: true }).click();
    await chat.locator('.agent-thoughts.active').waitFor();
    await api(room + `/agents/${agent.id}/stop`, 'POST');
    await page.waitForFunction(() => document.querySelector('.agent-typing')?.hidden === true);
    await chat.locator('.agent-thought-state').filter({ hasText: 'Interrupted' }).waitFor();
    await page.setViewportSize({ width: 430, height: 850 });
    await page.waitForTimeout(300);
    await page.screenshot({ path: '/tmp/holodeck-live-thoughts-phone.png' });
    assert.ok(await chat.locator('.window-body').evaluate(node => node.scrollWidth <= node.clientWidth + 1), 'chat body fits a phone viewport');
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= 430), 'page fits a phone viewport');
    await page.screenshot({ path: '/tmp/holodeck-live-thoughts-phone.png' });
    assert.deepEqual(errors, []);
    console.log('PASS: stopping the agent clears typing and marks interrupted work; phone layout fits');
  } finally {
    if (project) {
      if (agent) { await api(`/api/projects/${project.id}/room/agents/${agent.id}/stop`, 'POST'); }
      await api(`/api/projects/${project.id}`, 'DELETE');
      removeRoom(project.id);
    }
    fs.rmSync(directory, { recursive: true, force: true });
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
