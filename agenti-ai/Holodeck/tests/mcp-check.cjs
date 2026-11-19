// End-to-end check of the Holodeck MCP server over stdio against a running stack. Creates a project, places the
// fixture echo agent, exchanges a note with a file through MCP tools, then deletes the project.
// Build first: dotnet build src/Holodeck.Mcp
const { spawn } = require('node:child_process');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const readline = require('node:readline');
const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const server = path.join(__dirname, '..', 'src', 'Holodeck.Mcp', 'bin', 'Debug', 'net10.0', 'Holodeck.Mcp.dll');
const fixture = require('./fixture-library.cjs');
const echoFolder = `${fixture.Key}/agents/echo`;
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');

const child = spawn('dotnet', [server], { env: { ...process.env, Holodeck__Url: base }, stdio: ['pipe', 'pipe', 'inherit'] });
const pending = new Map();
let nextId = 0;
readline.createInterface({ input: child.stdout }).on('line', line => {
  const message = JSON.parse(line);
  const waiter = pending.get(message.id);
  if (waiter) { pending.delete(message.id); waiter(message); }
});

function request(method, params) {
  const id = ++nextId;
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  return new Promise(resolve => pending.set(id, resolve));
}

async function call(name, args, expectError = false) {
  const response = await request('tools/call', { name, arguments: args });
  assert.ok(response.result, `${name}: ${JSON.stringify(response.error)}`);
  const text = response.result.content[0].text;
  assert.equal(Boolean(response.result.isError), expectError, `${name}: ${text}`);
  return text;
}

async function main() {
  fixture.install();
  const init = await request('initialize', { protocolVersion: '2025-06-18', capabilities: {}, clientInfo: { name: 'mcp-check', version: '1' } });
  assert.equal(init.result.serverInfo.name, 'Holodeck.Mcp');
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const tools = (await request('tools/list', {})).result.tools;
  assert.ok(tools.length >= 30, 'tools listed');
  const wait = tools.find(tool => tool.name === 'wait_for_messages');
  assert.deepEqual(wait.inputSchema.required, ['projectId', 'channelId', 'timeoutSeconds'], 'optional parameters are optional');

  const refused = await call('room_view', { projectId: 'missing-project' }, true);
  assert.match(refused, /404/);

  const project = JSON.parse(await call('create_project', { name: `MCP check ${new Date().toISOString()}` }));
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'holodeck-mcp-check-'));
  try {
    await call('attach_library', { projectId: project.id, libraryId: fixture.Id, version: '1.0.0' });
    const echo = JSON.parse(await call('place_agent', { projectId: project.id, library: echoFolder }));
    assert.equal(echo.status, 'stopped');
    const room = JSON.parse(await call('room_view', { projectId: project.id }));
    const you = room.members.find(member => member.isYou);
    const channel = JSON.parse(await call('create_channel', { projectId: project.id, name: 'MCP check', memberIds: [you.id, echo.id] }));
    const started = JSON.parse(await call('start_agent', { projectId: project.id, agentId: echo.id }));
    assert.equal(started.status, 'running');

    const attachment = path.join(scratch, 'hello.txt');
    fs.writeFileSync(attachment, 'Hello from the MCP check.');
    const image = path.join(scratch, 'pixel.png');
    fs.writeFileSync(image, PNG);
    const sent = JSON.parse(await call('send_message', { projectId: project.id, channelId: channel.id, note: 'ping over MCP', files: [attachment, image] }));
    const reply = JSON.parse(await call('wait_for_messages', { projectId: project.id, channelId: channel.id, after: sent.id, timeoutSeconds: 60 }));
    assert.equal(reply.arrived, true, 'echo answered');
    const answer = reply.messages.find(message => message.senderId === echo.id);
    assert.match(answer.note, /ping over MCP/);
    assert.equal(answer.files.length, 2);
    const echoed = await call('read_message_file', { projectId: project.id, packetId: answer.id, name: 'hello.txt' });
    assert.equal(echoed, 'Hello from the MCP check.');
    // Binary content is saved locally rather than returned inline.
    const saved = await call('read_message_file', { projectId: project.id, packetId: answer.id, name: 'pixel.png' });
    const savedPath = saved.match(/ to (.+)$/)[1];
    assert.deepEqual(fs.readFileSync(savedPath), PNG);
    fs.rmSync(path.dirname(savedPath), { recursive: true, force: true });

    const quiet = JSON.parse(await call('wait_for_messages', { projectId: project.id, channelId: channel.id, after: reply.lastId, timeoutSeconds: 1 }));
    assert.equal(quiet.arrived, false);
    const history = JSON.parse(await call('read_channel', { projectId: project.id, channelId: channel.id }));
    assert.ok(history.length >= 2);
    assert.match(await call('agent_log', { projectId: project.id, agentId: echo.id }), /./s);
    JSON.parse(await call('read_agent_settings', { projectId: project.id, agentId: echo.id }));
    JSON.parse(await call('read_agent_computer', { projectId: project.id, agentId: echo.id }));
    const stopped = JSON.parse(await call('stop_agent', { projectId: project.id, agentId: echo.id }));
    assert.equal(stopped.status, 'stopped');
  } finally {
    await call('delete_project', { projectId: project.id });
    fs.rmSync(scratch, { recursive: true, force: true });
  }

  const projects = JSON.parse(await call('list_projects', {}));
  assert.ok(!projects.projects.some(entry => entry.id === project.id), 'project deleted');
  console.log('MCP check passed.');
}

main().then(() => { fixture.remove(); child.kill(); }, error => { fixture.remove(); console.error(error); child.kill(); process.exitCode = 1; });
