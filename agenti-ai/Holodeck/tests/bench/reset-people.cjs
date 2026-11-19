// Usage: node tests/bench/reset-people.cjs [project-id]   (default: the project named Bench)
// Wipes every person's memory of past chats (their database is erased; backstory seeds come back on start), deletes all
// channels and opens one fresh channel per person. No backup: these are test people.
const fs = require('fs');
const path = require('path');
const { api, sleep } = require('./bench.cjs');

async function benchId() {
  const listing = await api('/api/filesystem');
  const projects = listing.projects;
  const bench = projects.find(project => project.name === 'Bench');
  if (!bench) {
    throw new Error('No project named Bench; run tests/bench/create-bench.cjs.');
  }
  return bench.id;
}

async function main() {
  const projectId = process.argv[2] || await benchId();
  const room = `/api/projects/${projectId}/room`;
  const data = path.join(__dirname, '..', '..', 'data', 'rooms', projectId, 'agents');
  const state = await api(room);
  const you = state.members.find(member => member.isYou).id;
  const people = state.members.filter(member => !member.isYou && !member.isRemoved);
  for (const person of people) {
    await api(`${room}/agents/${person.id}/stop`, 'POST');
  }
  for (const channel of state.channels) {
    await api(`${room}/channels/${channel.id}`, 'DELETE');
  }
  for (const person of people) {
    await api(`${room}/agents/${person.id}/database`, 'DELETE');
    fs.rmSync(path.join(data, person.id, 'table', 'last-page.txt'), { force: true });
    await api(`${room}/channels`, 'POST', { name: person.name, memberIds: [you, person.id] });
    await api(`${room}/agents/${person.id}/start`, 'POST');
    console.log('reset', person.name);
  }
  await sleep(15000);
  const after = await api(room);
  console.log(after.channels.map(channel => `${channel.name}: ${channel.packetCount}`).join(', '));
}

main().catch(error => { console.error(error.message); process.exit(1); });
