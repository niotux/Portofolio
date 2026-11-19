// Containment check against the running stack: a probe agent reports what its computer, its own user in its
// library's project environment, can see and reach. Creates a temporary library and project, then removes both.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
const path = require('node:path');
const { removeRoom } = require('./room-files.cjs');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const libraryId = `holodeck.computer-check-${Date.now()}`;
const projectRoot = path.join(__dirname, '..');
const libraryPath = path.join(projectRoot, 'libs', libraryId);
const LIMIT = 90 * 1000;

const PROBE = String.raw`
import json, os, shutil, subprocess, sys, urllib.error, urllib.request

URL = os.environ["HOLODECK_URL"]
KEY = os.environ["HOLODECK_KEY"]
with open(os.environ["HOLODECK_SETTINGS"]) as settings_file:
    settings = json.load(settings_file)
if settings.get("crash"):
    print("intentional computer-check crash", flush=True)
    sys.exit(23)

def call(method, route, body=None):
    request = urllib.request.Request(URL + route, data=None if body is None else json.dumps(body).encode(), method=method)
    request.add_header("Authorization", "Bearer " + KEY)
    request.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(request, timeout=20) as response:
        raw = response.read()
        return json.loads(raw) if raw else None

def status(url, proxied):
    handler = urllib.request.ProxyHandler() if proxied else urllib.request.ProxyHandler({})
    try:
        with urllib.request.build_opener(handler).open(url, timeout=15) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code
    except Exception as error:
        return type(error).__name__

def default_route():
    with open("/proc/net/route") as routes:
        return any(line.split()[1] == "00000000" for line in list(routes)[1:])

home = os.environ["HOME"]
marker = os.path.join(home, "marker")
workspace_marker = os.path.join(os.getcwd(), "workspace-marker")
tool_name = "computer-check-tool"
tool_before = subprocess.check_output([tool_name], text=True).strip() if shutil.which(tool_name) else None
report = {
    "uid": os.getuid(),
    "home": home,
    "workspace": os.getcwd(),
    "dataVisible": os.path.exists("/data"),
    "libsVisible": os.path.exists("/libs"),
    "secretsInEnv": any("Password=" in value for value in os.environ.values()),
    "markerBefore": os.path.exists(marker),
    "toolBefore": tool_before,
    "workspaceBefore": os.path.exists(workspace_marker),
    "userApi": status("http://holodeck:8080/api/filesystem", False),
    "agentApiOnUserPortOnly": status("http://holodeck:8081/api/filesystem", False),
    "internet": status("https://pypi.org/simple/", True),
    "defaultRoute": default_route(),
}
open(marker, "w").write("installed")
open(workspace_marker, "w").write("workspace survives reset")
tool_directory = os.path.join(home, ".local", "bin")
os.makedirs(tool_directory, exist_ok=True)
tool = os.path.join(tool_directory, tool_name)
with open(tool, "w") as executable:
    executable.write("#!/bin/sh\necho computer-check-installed\n")
os.chmod(tool, 0o755)
report["installedTool"] = subprocess.check_output([tool_name], text=True).strip()
me = call("GET", "/api/agent/me")
call("POST", "/api/agent/send", {"channel": me["channels"][0]["channelId"], "note": json.dumps(report), "files": []})
while True:
    if call("GET", "/api/agent/next?wait=30"):
        print("probe received a delivery", flush=True)
`;

function writeLibrary() {
  const agent = path.join(libraryPath, 'agents', 'probe');
  fs.mkdirSync(agent, { recursive: true });
  fs.mkdirSync(path.join(libraryPath, 'teams'), { recursive: true });
  fs.writeFileSync(path.join(libraryPath, 'Library.json'), JSON.stringify({ FormatVersion: 1, Id: libraryId, Version: '1.0.0', Name: 'Computer Check', Dependencies: [], Runtime: { Image: 'holodeck-library:1', Processes: [] } }));
  fs.writeFileSync(path.join(agent, 'Agent.json'), JSON.stringify({ Id: 'probe', Name: 'Probe', Launch: { Command: './run.sh', Args: [] } }));
  fs.writeFileSync(path.join(agent, 'run.sh'), '#!/bin/sh\nexec python3 "$HOLODECK_PROGRAM/probe.py"\n', { mode: 0o755 });
  const broken = path.join(libraryPath, 'agents', 'broken');
  fs.mkdirSync(broken);
  fs.writeFileSync(path.join(broken, 'Agent.json'), JSON.stringify({ Id: 'broken', Name: 'Broken launch', Launch: { Command: '/missing-computer-check-program', Args: [] } }));
  fs.writeFileSync(path.join(agent, 'probe.py'), PROBE);
}
async function api(route, method = 'GET', body) {
  const options = { method, headers: {} };
  if (body !== undefined) { options.headers['Content-Type'] = 'application/json'; options.body = JSON.stringify(body); }
  const response = await fetch(base + route, options);
  const raw = await response.text();
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${raw}`);
  return raw ? JSON.parse(raw) : null;
}
async function until(what, check) {
  const end = Date.now() + LIMIT;
  while (Date.now() < end) {
    let value;
    try { value = await check(); } catch { value = null; }
    if (value) { return value; }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error(`Timed out waiting for ${what}`);
}

function environmentIds(projectId) {
  const ids = execFileSync('docker', ['--context', 'default', 'ps', '--all', '--filter', `label=holodeck.project=${projectId}`, '--filter', `label=holodeck.environment=${libraryId}/1.0.0`, '--format', '{{.ID}}'], { encoding: 'utf8' });
  return ids.split('\n').filter(Boolean);
}

(async () => {
  let project;
  let room;
  try {
    writeLibrary();
    project = await api('/api/projects', 'POST', { name: `Computer check ${Date.now()}` });
    room = `/api/projects/${project.id}/room`;
    await api(`/api/projects/${project.id}/libraries`, 'POST', { id: libraryId, version: '1.0.0' });
    const you = (await api(room)).members[0];
    const probe = await api(`${room}/agents`, 'POST', { library: `${libraryId}/1.0.0/agents/probe` });
    const channel = await api(`${room}/channels`, 'POST', { name: 'Probe', memberIds: [you.id, probe.id] });
    const environment = environmentIds(project.id);
    assert.equal(environment.length, 1, 'the project has one environment for the library');
    let seen = 0;
    const memberStatus = async () => (await api(room)).members.find(member => member.id === probe.id).status;
    async function stopProbe() {
      await api(`${room}/agents/${probe.id}/stop`, 'POST');
      await until('the probe to stop', async () => await memberStatus() === 'stopped');
      assert.deepEqual(environmentIds(project.id), environment, 'stop retains the environment');
    }
    async function report() {
      await api(`${room}/agents/${probe.id}/start`, 'POST');
      const packet = await until('the probe report', async () => {
        const packets = (await api(`${room}/channels/${channel.id}/packets?after=0`)).filter(item => item.senderId === probe.id);
        return packets.length > seen ? packets[packets.length - 1] : null;
      });
      seen++;
      return JSON.parse(packet.note);
    }

    const first = await report();
    console.log(JSON.stringify(first));
    assert.notEqual(first.uid, 0, 'the agent does not run as root');
    assert.equal(first.home, `/home/agent-${probe.id}`);
    assert.equal(first.workspace, `/srv/agent-${probe.id}`);
    assert.equal(first.dataVisible, false, 'Holodeck data is not visible');
    assert.equal(first.libsVisible, false, 'the library folder is not visible');
    assert.equal(first.secretsInEnv, false, 'no Holodeck secrets in the environment');
    assert.equal(first.markerBefore, false);
    assert.equal(first.toolBefore, null, 'a fresh home has no installed test tool');
    assert.equal(first.workspaceBefore, false);
    assert.equal(first.installedTool, 'computer-check-installed', 'a user-installed executable runs through PATH');
    assert.equal(first.userApi, 403, 'the user API refuses agents');
    assert.equal(first.agentApiOnUserPortOnly, 404, 'the agent port serves only the agent API');
    assert.equal(first.internet, 200, 'the internet is reachable');
    assert.equal(first.defaultRoute, true, 'agents have a direct route out');
    console.log('PASS: the probe sees only its own files, has open network access, and cannot use the user API');

    await stopProbe();
    const second = await report();
    assert.equal(second.uid, first.uid, 'the agent keeps its user');
    assert.equal(second.markerBefore, true, 'files in the home survive a restart');
    assert.equal(second.toolBefore, 'computer-check-installed', 'the user-installed executable survives a restart');
    assert.equal(second.workspaceBefore, true, 'workspace files survive a restart');
    console.log('PASS: Start and Stop retain the user, installed tool and workspace');
    await stopProbe();
    await api(`${room}/agents/${probe.id}/computer/reset`, 'POST');
    const third = await report();
    assert.equal(third.markerBefore, false, 'Reset environment clears the home');
    assert.equal(third.toolBefore, null, 'Reset environment removes the installed executable');
    assert.equal(third.workspaceBefore, true, 'Reset environment keeps workspace files');
    console.log('PASS: Reset environment clears installs while preserving the workspace');
    // The Docker proxy closes quiet connections; restarting it cuts them at once. Holodeck must keep observing the agent.
    execFileSync('docker', ['--context', 'default', 'compose', 'restart', 'docker-proxy'], { cwd: projectRoot, stdio: 'ignore' });
    await new Promise(resolve => setTimeout(resolve, 5000));
    assert.equal(await memberStatus(), 'running', 'a cut Docker connection is not an exit');
    const message = new FormData();
    message.append('note', 'after the proxy restart');
    assert.ok((await fetch(`${base}${room}/channels/${channel.id}/packets`, { method: 'POST', body: message })).ok);
    await until('output after the proxy restart to reach the log', async () => /probe received a delivery/.test(await (await fetch(`${base}${room}/agents/${probe.id}/log`)).text()));
    await stopProbe();
    console.log('PASS: a running agent survives its Docker connections being cut, and its log continues');

    await api(`${room}/agents/${probe.id}/settings`, 'PUT', { crash: true });
    await api(`${room}/agents/${probe.id}/start`, 'POST');
    await until('the real crash to be reported', async () => await memberStatus() === 'crashed');
    const log = await (await fetch(`${base}${room}/agents/${probe.id}/log`)).text();
    assert.match(log, /intentional computer-check crash/);
    assert.match(log, /exited with code 23/);
    await new Promise(resolve => setTimeout(resolve, 2000));
    assert.equal(await memberStatus(), 'crashed', 'a crashed agent stays crashed and is not restarted');
    console.log('PASS: a real crash records its exit and log and does not restart');
    const broken = await api(`${room}/agents`, 'POST', { library: `${libraryId}/1.0.0/agents/broken` });
    const changedComputer = await fetch(`${base}${room}/agents/${broken.id}/computer`, {
      method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ports: [8188], folders: [] })
    });
    assert.equal(changedComputer.status, 409, 'grants are refused once the agent has its computer');
    assert.deepEqual((await api(`${room}/agents/${broken.id}/computer`)).grants, { ports: [], folders: [] }, 'refused edits are not saved');
    await api(`${room}/agents/${broken.id}/start`, 'POST');
    await until('the failed launch to be reported', async () => (await api(room)).members.find(member => member.id === broken.id).status === 'crashed');
    await api(`${room}/agents/${broken.id}`, 'DELETE');
    assert.deepEqual(environmentIds(project.id), environment, 'deleting an agent leaves the environment running');
    console.log('PASS: grant edits are refused; a failed launch is reported; deleting an agent keeps the environment');
    console.log('computer check passed');
  } finally {
    if (project) {
      const members = (await api(room)).members;
      for (const member of members) {
        if (!member.isYou) {
          if (!member.isRemoved) { await api(`${room}/agents/${member.id}/stop`, 'POST'); }
        }
      }
      await api(`/api/projects/${project.id}`, 'DELETE');
      assert.deepEqual(environmentIds(project.id), [], 'project deletion removes its environments');
      removeRoom(project.id);
    }
    fs.rmSync(libraryPath, { recursive: true, force: true });
  }
})().catch(error => { fs.rmSync(libraryPath, { recursive: true, force: true }); console.error(error); process.exit(1); });
