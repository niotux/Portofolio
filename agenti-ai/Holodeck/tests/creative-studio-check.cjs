// Exercises the real library, notebook, prompt revisions and ComfyUI on the default Docker engine.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const base = process.env.ASTUDIO_URL || 'http://127.0.0.1:7077';
const keep = process.env.CREATIVE_STUDIO_KEEP === '1';
const reference = process.env.CREATIVE_STUDIO_REFERENCE;
const timeout = Number(process.env.CREATIVE_STUDIO_TIMEOUT_MS || 900000);
const library = 'holodeck.creative-studio';
let project;
let room;
let studio;
let channel;

async function api(route, method = 'GET', body) {
  const response = await fetch(base + route, { method, headers: body === undefined ? {} : { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  assert.ok(response.ok, `${method} ${route}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

async function until(label, check) {
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const value = await check();
    if (value) { return value; }
    await new Promise(resolve => setTimeout(resolve, 2000));
  }
  throw new Error(`Timed out waiting for ${label}`);
}

async function status() {
  return (await api(room)).members.find(member => member.id === studio.id).status;
}

async function database(sql) {
  const result = await api(`${room}/agents/${studio.id}/database`, 'POST', { statements: [{ sql, parameters: [] }] });
  return result.results[0].rows;
}

async function post(note, image) {
  const form = new FormData();
  form.append('note', note);
  if (image) { form.append('files', new Blob([fs.readFileSync(image)], { type: 'image/png' }), path.basename(image)); }
  const response = await fetch(base + `${room}/channels/${channel.id}/packets`, { method: 'POST', body: form });
  const text = await response.text();
  assert.ok(response.ok, `share reference: ${response.status} ${text}`);
  return JSON.parse(text);
}

async function action(id, input) {
  const root = `${room}/agents/${studio.id}`;
  const definition = await api(root + '/actions');
  const job = await api(root + '/jobs', 'POST', { actionId: id, input, definitionRevision: definition.definitionRevision, requestId: crypto.randomUUID() });
  return until(id, async () => {
    const current = (await api(root + '/jobs')).find(item => item.id === job.id);
    if (current.status === 'failed') { throw new Error(current.error); }
    return current.status === 'succeeded' ? current.result : null;
  });
}

(async () => {
  try {
    project = await api('/api/projects', 'POST', { name: `Creative Studio check ${Date.now()}` });
    room = `/api/projects/${project.id}/room`;
    await api(`/api/projects/${project.id}/libraries`, 'POST', { id: library, version: '0.1.0' });
    studio = await api(room + '/agents', 'POST', { library: `${library}/0.1.0/agents/studio` });
    const you = (await api(room)).members.find(item => item.isYou);
    channel = await api(room + '/channels', 'POST', { name: 'Studio', memberIds: [you.id, studio.id] });
    assert.equal(await status(), 'stopped');
    await api(`${room}/agents/${studio.id}/start`, 'POST');
    await until('agent initialization', async () => {
      try { return (await database("SELECT count(*) FROM prompt_heads"))[0][0] > 0; }
      catch { return null; }
    });

    const active = await action('read-prompt', { purpose: 'delivery' });
    const draft = await action('save-prompt', { purpose: 'delivery', content: 'Here is the image. We can refine the light or composition next.' });
    assert.notEqual(active.revision, draft.revision);
    assert.equal((await action('read-prompt', { purpose: 'delivery' })).revision, active.revision);
    await action('select-prompt', { purpose: 'delivery', revision: draft.revision });
    assert.equal((await action('read-prompt', { purpose: 'delivery' })).revision, draft.revision);
    await action('select-prompt', { purpose: 'delivery', revision: active.revision });
    console.log('PASS: prompt drafts, publication and rollback work through agent actions');

    const first = await post('Our place is the Glasshouse Station: enormous amber windows, dark teal ironwork, a quiet empty atmosphere, and reflections on the floor. Please keep a notebook entry with these visual decisions for later. We are discussing it now; do not generate an image yet.', reference);
    await until('grounded notebook entry', async () => (await database('SELECT title, prose, files::text FROM notebook')).length > 0);
    const notes = await database('SELECT title, prose, files::text FROM notebook');
    if (reference) { assert.ok(notes.some(note => JSON.parse(note[2]).length > 0), 'the note retains a workspace reference path'); }
    await until('first reply', async () => (await api(`${room}/channels/${channel.id}/packets?after=${first.id}`)).some(packet => packet.senderId === studio.id));
    await api(`${room}/agents/${studio.id}/stop`, 'POST');
    assert.equal(await status(), 'stopped');
    await api(`${room}/agents/${studio.id}/start`, 'POST');
    assert.equal(await status(), 'running');
    console.log('PASS: a descriptive notebook entry and its linked file survive Stop/Start');

    const next = await post('Use what you kept about the Glasshouse Station to make one 512 by 512 anime establishing shot, no people. Search the notebook for the brief, then generate it now.');
    const output = await until('generated file delivery', async () => (await api(`${room}/channels/${channel.id}/packets?after=${next.id}`)).find(packet => packet.senderId === studio.id && packet.files.some(file => file.mediaType.startsWith('image/'))));
    const generations = await database('SELECT state, request_json::text FROM generations');
    assert.equal(generations.length, 1);
    assert.equal(generations[0][0], 'completed');
    assert.ok((await database("SELECT path FROM assets WHERE path LIKE 'generations/%'")).length > 0, 'delivered output remains addressable after outbox delivery');
    const saved = JSON.parse(generations[0][1]);
    assert.ok(saved.prompts.length > 0, 'generation keeps effective prompt revision snapshots');
    assert.ok(saved.plan.graphHash, 'generation keeps the compiled graph hash');
    const image = output.files.find(file => file.mediaType.startsWith('image/'));
    const response = await fetch(base + `${room}/packets/${output.id}/files/${encodeURIComponent(image.name)}`);
    assert.equal(response.status, 200);
    assert.ok((await response.arrayBuffer()).byteLength > 0);
    console.log('PASS: recalled prose drives a real ComfyUI render, saved provenance and one image delivery');

    if (keep) { await api(`/api/projects/${project.id}`, 'PUT', { name: 'Creative Studio' }); }
    fs.mkdirSync('artifacts', { recursive: true });
    fs.writeFileSync('artifacts/creative-studio-check.json', JSON.stringify({ projectId: project.id, agentId: studio.id, channelId: channel.id, kept: keep, url: `${base}/?project=${project.id}` }, null, 2));
    console.log(`creative studio check passed: ${base}/?project=${project.id}`);
  } finally {
    if (project && !keep) { await api(`/api/projects/${project.id}`, 'DELETE'); }
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
