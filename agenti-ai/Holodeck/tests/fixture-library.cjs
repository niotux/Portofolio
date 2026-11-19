// The checks' fixture library (an echo agent, Echo Pair and a raw-settings agent) lives in tests/fixtures and is copied
// into libs/ only while a check runs.
const fs = require('node:fs');
const path = require('node:path');

const Id = 'holodeck.check';
const Key = `${Id}/1.0.0`;
const source = path.join(__dirname, 'fixtures', Id);
const target = path.join(__dirname, '..', 'libs', Id);

function install() {
  fs.cpSync(source, target, { recursive: true });
}

function remove() {
  fs.rmSync(target, { recursive: true, force: true });
}

const echoProgram = path.join(source, 'agents', 'echo', 'agent.py');

module.exports = { Id, Key, install, remove, echoProgram };
