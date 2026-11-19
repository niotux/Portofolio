// Usage: node tests/bench/replay.cjs <role> <name> [--personality TEXT] <message>...
// A message '--new' waits for memories to form and opens a new channel. Creates a scratch project, sends the messages
// one by one, prints each step of thinking, the reply and what was looked for, then the memories formed. Deletes the
// project (and with it the person's database) at the end.
const fs = require('fs');
const path = require('path');
const { api, query, sleep } = require('./bench.cjs');

async function packets(room, channel) {
  const history = await api(`${room}/channels/${channel.id}/packets`);
  return Array.isArray(history) ? history : history.packets;
}

async function main() {
  let [role, name, ...messages] = process.argv.slice(2);
  let personality = null;
  if (messages[0] === '--personality') {
    personality = messages[1];
    messages = messages.slice(2);
  }
  const project = await api('/api/projects', 'POST', { name: 'Scratch ' + name });
  const room = `/api/projects/${project.id}/room`;
  const you = (await api(room)).members.find(member => member.isYou);
  let agent = null;
  try {
    await api(`/api/projects/${project.id}/libraries`, 'POST', { id: 'holodeck.show-writing', version: '0.1.0' });
    agent = await api(`${room}/agents`, 'POST', { library: `holodeck.show-writing/0.1.0/agents/${role}` });
    let channel = await api(`${room}/channels`, 'POST', { name: 'Scratch', memberIds: [you.id, agent.id] });
    if (personality !== null) {
      const settings = await api(`${room}/agents/${agent.id}/settings`);
      settings.Personality.Description = personality;
      await api(`${room}/agents/${agent.id}/settings`, 'PUT', settings);
      console.log('personality:', personality);
    }
    await api(`${room}/agents/${agent.id}/start`, 'POST');
    await sleep(5000);
    const log = path.join(__dirname, '..', '..', 'data', 'rooms', project.id, 'agents', agent.id, 'log.txt');
    let seen = 0;
    for (const message of messages) {
      if (message === '--new') {
        await sleep(25000);
        channel = await api(`${room}/channels`, 'POST', { name: 'Scratch 2', memberIds: [you.id, agent.id] });
        console.log('\n======== NEW CHANNEL');
        continue;
      }
      const before = (await packets(room, channel)).length;
      await api(`${room}/channels/${channel.id}/packets`, 'POST', undefined, { note: message });
      for (let attempt = 0; attempt < 120 && (await packets(room, channel)).length <= before + 1; attempt++) {
        await sleep(3000);
      }
      const runs = await api(`${room}/channels/${channel.id}/activity`);
      const replied = runs.filter(run => run.steps.some(step => step.phase === 'reply'));
      console.log('\n################ YOU:', message);
      if (replied.length > 0) {
        for (const step of replied[replied.length - 1].steps) {
          if (step.phase !== 'reply') {
            console.log(`  [${step.label}] ${step.text.trim().replaceAll('\n', ' / ')}`);
          }
        }
      }
      const history = await packets(room, channel);
      console.log(`>>> ${name.toUpperCase()}:`, history[history.length - 1].note);
      const lines = fs.readFileSync(log, 'utf8').split('\n');
      for (const line of lines.slice(seen)) {
        if (line.includes('Looked for') || line.includes('Recall candidate')) {
          console.log('    ' + line.slice(0, 160));
        }
      }
      seen = lines.length;
    }
    await sleep(20000);
    console.log('-- memories formed:');
    for (const [text] of await query(room, agent.id, 'SELECT text FROM memories WHERE packet_id > 0 ORDER BY id')) {
      console.log('  *', text);
    }
  } finally {
    if (agent !== null) {
      await api(`${room}/agents/${agent.id}/stop`, 'POST');
    }
    await api(`/api/projects/${project.id}`, 'DELETE');
    fs.rmSync(path.join(__dirname, '..', '..', 'data', 'rooms', project.id), { recursive: true, force: true });
  }
}

main().catch(error => { console.error(error.message); process.exit(1); });
