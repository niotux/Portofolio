// Usage: node tests/bench/create-bench.cjs
// Creates project Bench with the four play people (Victor, Ana, Constantin, Dorin), opens
// one channel each with You and starts them.
const { api, sleep } = require('./bench.cjs');

const Roles = ['grump', 'curious', 'contrarian', 'believer'];

async function main() {
  const project = await api('/api/projects', 'POST', { name: 'Bench' });
  const room = `/api/projects/${project.id}/room`;
  await api(`/api/projects/${project.id}/libraries`, 'POST', { id: 'holodeck.show-writing', version: '0.1.0' });
  const you = (await api(room)).members.find(member => member.isYou).id;
  for (const role of Roles) {
    const person = await api(`${room}/agents`, 'POST', { library: `holodeck.show-writing/0.1.0/agents/${role}` });
    await api(`${room}/channels`, 'POST', { name: person.name, memberIds: [you, person.id] });
    await api(`${room}/agents/${person.id}/start`, 'POST');
    console.log('placed', person.name);
  }
  await sleep(15000);
  console.log('Bench', project.id);
}

main().catch(error => { console.error(error.message); process.exit(1); });
