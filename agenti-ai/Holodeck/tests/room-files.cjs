// Agents' files belong to their own users in library environments, so the checks remove a project's room folder
// through a short-lived root process in the trusted runtime image.
const { execFileSync } = require('node:child_process');
const path = require('node:path');

const rooms = path.join(__dirname, '..', 'data', 'rooms');

function removeRoom(projectId) {
  execFileSync('docker', ['--context', 'default', 'run', '--rm', '--entrypoint', 'rm', '-v', `${rooms}:/rooms`, 'holodeck-library:1', '-rf', `/rooms/${projectId}`], { stdio: 'ignore' });
}

module.exports = { removeRoom };
