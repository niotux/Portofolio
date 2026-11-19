// Live opt-in runtime check. Uses only temporary libraries/projects and read-only Docker inspection.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const root = path.join(__dirname, '..');
const libraryId = `holodeck.environment-check-${Date.now()}`;
const libraryPath = path.join(root, 'libs', libraryId);
const timeoutMs = 120000;

const probeSource = String.raw`
import hashlib, json, os, signal, sys, time, urllib.error, urllib.request
URL = os.environ['HOLODECK_URL'].rstrip('/')
KEY = os.environ['HOLODECK_KEY']

def call(method, route, body=None):
    request = urllib.request.Request(URL + route, data=None if body is None else json.dumps(body).encode(), method=method)
    request.add_header('Authorization', 'Bearer ' + KEY)
    request.add_header('Content-Type', 'application/json')
    with urllib.request.urlopen(request, timeout=40) as response:
        raw = response.read()
        return json.loads(raw) if raw else None

def read_allowed(file):
    try:
        with open(file, 'rb') as source:
            source.read(1)
        return True
    except OSError:
        return False

def report():
    home = os.environ['HOME']
    marker = os.path.join(home, 'private-marker')
    workspace = os.environ['HOLODECK_TABLE']
    table_marker = os.path.join(workspace, 'workspace-marker')
    with open(os.environ['HOLODECK_SETTINGS']) as source:
        settings = json.load(source)
    result = {'kind':'started', 'uid':os.getuid(), 'gid':os.getgid(), 'pid':os.getpid(), 'home':home, 'workspace':workspace,
              'homeBefore':os.path.exists(marker), 'tableBefore':os.path.exists(table_marker), 'settings':settings,
              'keyFingerprint':hashlib.sha256(KEY.encode()).hexdigest()}
    with open(marker, 'w') as output:
        output.write('private home survives restart')
    with open(table_marker, 'w') as output:
        output.write('workspace survives reset')
    with open(os.path.join(workspace, 'evidence.txt'), 'w') as output:
        output.write('platform can read shared-agent output')
    return result

me = call('GET', '/api/agent/me')
channel = me['channels'][0]['channelId']
call('POST', '/api/agent/send', {'channel':channel, 'note':json.dumps(report()), 'files':[]})
print('shared environment probe started', flush=True)
while True:
    try:
        packet = call('GET', '/api/agent/next?wait=30')
        if packet is None:
            continue
        operation = json.loads(packet['note'])
        result = {'kind':operation['op'], 'requestId':operation['requestId']}
        files = []
        if operation['op'] == 'probe':
            other = operation['other']
            result['otherHomeReadable'] = read_allowed(os.path.join(other['home'], 'private-marker'))
            result['otherSettingsReadable'] = read_allowed(os.path.join(os.path.dirname(other['home']), 'settings.json'))
            result['otherEnvironmentReadable'] = read_allowed('/proc/' + str(other['pid']) + '/environ')
            try:
                os.kill(other['pid'], 0)
                result['otherSignalAllowed'] = True
            except PermissionError:
                result['otherSignalAllowed'] = False
            with open('/proc/self/status') as source:
                result['processStatus'] = source.read()
        elif operation['op'] == 'echo':
            result['note'] = operation.get('text')
            files = packet['files']
            result['fileContents'] = []
            for file in files:
                with open(os.path.join(os.environ['HOLODECK_TABLE'], file)) as source:
                    result['fileContents'].append(source.read())
        elif operation['op'] == 'settings':
            with open(os.environ['HOLODECK_SETTINGS']) as source:
                result['settings'] = json.load(source)
        call('POST', '/api/agent/send', {'channel':packet['channelId'], 'note':json.dumps(result), 'files':files})
        print('handled ' + operation['op'], flush=True)
    except urllib.error.HTTPError as error:
        if error.code == 401:
            sys.exit(0)
        print('transport HTTP ' + str(error.code), flush=True)
        time.sleep(1)
    except (OSError, ValueError) as error:
        print(type(error).__name__ + ': ' + str(error), flush=True)
        time.sleep(1)
`;

function installLibrary() {
  const agent = path.join(libraryPath, 'agents', 'probe');
  fs.mkdirSync(agent, { recursive: true });
  fs.mkdirSync(path.join(libraryPath, 'teams'), { recursive: true });
  fs.writeFileSync(path.join(libraryPath, 'Library.json'), JSON.stringify({ FormatVersion: 1, Id: libraryId, Version: '1.0.0', Name: 'Environment Check', Dependencies: [], Runtime: { Image: 'holodeck-library:1', Processes: [] } }));
  fs.writeFileSync(path.join(agent, 'Agent.json'), JSON.stringify({ Id: 'probe', Name: 'Environment Probe', Launch: { Command: 'python3', Args: ['${Agent}/probe.py'] }, Settings: 'settings.json' }));
  fs.writeFileSync(path.join(agent, 'settings.json'), JSON.stringify({ value: 'initial' }));
  fs.writeFileSync(path.join(agent, 'probe.py'), probeSource);
}

async function response(route, method = 'GET', body) {
  const options = { method, headers: {}, signal: AbortSignal.timeout(timeoutMs) };
  if (body !== undefined) {
    options.headers['Content-Type'] = 'application/json';
    options.body = JSON.stringify(body);
  }
  return fetch(base + route, options);
}

async function api(route, method = 'GET', body) {
  const result = await response(route, method, body);
  const text = await result.text();
  assert.ok(result.ok, `${method} ${route}: ${result.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function until(label, check) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const result = await check();
      if (result) {
        return result;
      }
    } catch (error) {
      lastError = error;
    }
    await new Promise(resolve => setTimeout(resolve, 400));
  }
  throw new Error(`Timed out waiting for ${label}${lastError ? ': ' + lastError.message : ''}`);
}

function docker(...args) {
  return execFileSync('docker', ['--context', 'default', ...args], { encoding: 'utf8' }).trim();
}

function environmentIds(projectId) {
  return docker('ps', '--all', '--filter', `label=holodeck.project=${projectId}`, '--filter', `label=holodeck.environment=${libraryId}/1.0.0`, '--format', '{{.ID}}').split('\n').filter(Boolean);
}

(async () => {
  let project;
  let otherProject;
  let deleted = false;
  try {
    installLibrary();
    project = await api('/api/projects', 'POST', { name: `Library environment check ${Date.now()}` });
    const projectRoute = `/api/projects/${project.id}`;
    const room = projectRoute + '/room';
    await api(projectRoute + '/libraries', 'POST', { id: libraryId, version: '1.0.0' });
    await api(projectRoute + '/activate', 'POST');
    const environment = environmentIds(project.id);
    assert.equal(environment.length, 1, 'one project/library environment exists without agents');
    const environmentId = environment[0];
    otherProject = await api('/api/projects', 'POST', { name: `Separate environment check ${Date.now()}` });
    await api(`/api/projects/${otherProject.id}/libraries`, 'POST', { id: libraryId, version: '1.0.0' });
    await api(`/api/projects/${otherProject.id}/activate`, 'POST');
    const otherEnvironment = environmentIds(otherProject.id);
    assert.equal(otherEnvironment.length, 1);
    assert.notEqual(otherEnvironment[0], environmentId, 'the same library has separate containers in separate projects');
    await api(`/api/projects/${otherProject.id}`, 'DELETE');
    assert.deepEqual(environmentIds(otherProject.id), []);
    otherProject = null;
    assert.equal(environmentIds(project.id)[0], environmentId, 'deleting another project leaves this environment intact');
    const you = (await api(room)).members.find(member => member.isYou);
    const first = await api(room + '/agents', 'POST', { library: `${libraryId}/1.0.0/agents/probe` });
    const second = await api(room + '/agents', 'POST', { library: `${libraryId}/1.0.0/agents/probe` });
    const firstChannel = await api(room + '/channels', 'POST', { name: 'First probe', memberIds: [you.id, first.id] });
    const secondChannel = await api(room + '/channels', 'POST', { name: 'Second probe', memberIds: [you.id, second.id] });
    const seen = new Map();
    async function nextPacket(agent, channel, predicate) {
      return until('agent report ' + agent.id, async () => {
        const packets = await api(`${room}/channels/${channel.id}/packets?after=0`);
        for (const packet of packets) {
          if (packet.senderId !== agent.id || packet.id <= (seen.get(agent.id) || 0)) {
            continue;
          }
          const report = JSON.parse(packet.note);
          if (predicate(report)) {
            seen.set(agent.id, packet.id);
            return { packet, report };
          }
        }
        return null;
      });
    }
    async function start(agent, channel) {
      await api(`${room}/agents/${agent.id}/start`, 'POST');
      return (await nextPacket(agent, channel, report => report.kind === 'started')).report;
    }
    async function stop(agent) {
      await api(`${room}/agents/${agent.id}/stop`, 'POST');
      await until('stopped agent', async () => (await api(room)).members.find(member => member.id === agent.id).status === 'stopped');
    }
    async function ask(agent, channel, command, file) {
      const requestId = crypto.randomUUID();
      const form = new FormData();
      form.append('note', JSON.stringify({ ...command, requestId }));
      if (file) {
        form.append('files', new Blob([file.contents], { type: 'text/plain' }), file.name);
      }
      const sent = await fetch(`${base}${room}/channels/${channel.id}/packets`, { method: 'POST', body: form, signal: AbortSignal.timeout(timeoutMs) });
      assert.ok(sent.ok, await sent.text());
      return nextPacket(agent, channel, report => report.requestId === requestId);
    }

    const one = await start(first, firstChannel);
    const two = await start(second, secondChannel);
    assert.notEqual(one.uid, 0);
    assert.notEqual(two.uid, 0);
    assert.notEqual(one.uid, two.uid, 'agents have distinct users');
    assert.notEqual(one.home, two.home);
    assert.notEqual(one.workspace, two.workspace);
    assert.equal(environmentIds(project.id).length, 1);
    assert.equal(environmentIds(project.id)[0], environmentId);
    const isolation = (await ask(first, firstChannel, { op: 'probe', other: two })).report;
    assert.equal(isolation.otherHomeReadable, false);
    assert.equal(isolation.otherSettingsReadable, false);
    assert.equal(isolation.otherEnvironmentReadable, false);
    assert.equal(isolation.otherSignalAllowed, false);
    assert.match(isolation.processStatus, /NoNewPrivs:\s+1/);
    assert.match(isolation.processStatus, /CapEff:\s+0000000000000000/);
    console.log('PASS: one shared container, separate non-root users, private files and process credentials');

    const fileContent = 'file roundtrip through platform and shared-agent user';
    const echoed = await ask(first, firstChannel, { op: 'echo', text: 'shared transport' }, { name: 'roundtrip.txt', contents: fileContent });
    assert.equal(echoed.report.note, 'shared transport');
    assert.deepEqual(echoed.report.fileContents, [fileContent]);
    assert.equal(echoed.packet.files[0].name, 'roundtrip.txt');
    const download = await fetch(`${base}${room}/packets/${echoed.packet.id}/files/roundtrip.txt`);
    assert.ok(download.ok);
    assert.equal(await download.text(), fileContent);
    const evidence = await fetch(`${base}${room}/agents/${first.id}/table/file?path=evidence.txt`);
    assert.ok(evidence.ok);
    assert.equal(await evidence.text(), 'platform can read shared-agent output');
    console.log('PASS: received files, agent files and packet downloads respect shared storage permissions');

    const grant = await response(`${room}/agents/${first.id}/computer`, 'PUT', { ports: [8188], folders: [] });
    assert.equal(grant.status, 409, 'unsupported shared-agent port grants are refused');
    assert.deepEqual((await api(`${room}/agents/${first.id}/computer`)).grants, { ports: [], folders: [] });
    await stop(first);
    assert.equal((await api(room)).members.find(member => member.id === second.id).status, 'running');
    const secondReply = await ask(second, secondChannel, { op: 'echo', text: 'other agent still running' });
    assert.equal(secondReply.report.note, 'other agent still running');
    const restarted = await start(first, firstChannel);
    assert.equal(restarted.uid, one.uid);
    assert.equal(restarted.keyFingerprint, one.keyFingerprint, 'private credential remains stable');
    assert.equal(restarted.homeBefore, true);
    assert.equal(restarted.tableBefore, true);
    await stop(first);
    await api(`${room}/agents/${first.id}/settings`, 'PUT', { value: 'changed' });
    const changed = await start(first, firstChannel);
    assert.equal(changed.settings.value, 'changed');
    await stop(first);
    await api(`${room}/agents/${first.id}/computer/reset`, 'POST');
    const reset = await start(first, firstChannel);
    assert.equal(reset.uid, one.uid);
    assert.equal(reset.keyFingerprint, one.keyFingerprint);
    assert.equal(reset.homeBefore, false);
    assert.equal(reset.tableBefore, true);
    assert.equal(reset.settings.value, 'changed');
    await until('shared agent logs', async () => (await (await fetch(`${base}${room}/agents/${first.id}/log`)).text()).includes('shared environment probe started'));
    console.log('PASS: independent stop/start, persistent identity/settings/files, home reset and agent logs');

    await stop(first);
    await stop(second);
    await api(`${room}/agents/${first.id}`, 'DELETE');
    await api(`${room}/agents/${second.id}`, 'DELETE');
    assert.equal(environmentIds(project.id)[0], environmentId, 'deleting last agent retains project/library environment');
    assert.equal(docker('inspect', '--format', '{{.State.Running}}', environmentId), 'true');
    await api(projectRoute, 'DELETE');
    deleted = true;
    assert.deepEqual(environmentIds(project.id), [], 'project deletion removes shared environment');
    assert.ok(fs.existsSync(path.join(root, 'data', 'rooms', project.id)), 'project deletion retains files for inspection');
    console.log('PASS: agent deletion retains environment; project deletion removes container and retains data');
    console.log('library environment check passed');
  } finally {
    if (otherProject) {
      try {
        await api(`/api/projects/${otherProject.id}`, 'DELETE');
      } catch (error) {
        console.error('Secondary temporary project cleanup failed:', error.message);
      }
    }
    if (project && !deleted) {
      try {
        await api(`/api/projects/${project.id}`, 'DELETE');
      } catch (error) {
        console.error('Temporary project cleanup failed:', error.message);
      }
    }
    fs.rmSync(libraryPath, { recursive: true, force: true });
  }
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
