// Shared calls for the bench scripts: the app API at 127.0.0.1:7077, everything done as You.
const Base = 'http://127.0.0.1:7077';

async function api(path, method = 'GET', body = undefined, form = undefined) {
  const options = { method, headers: {} };
  if (body !== undefined) {
    options.headers['Content-Type'] = 'application/json';
    options.body = JSON.stringify(body);
  }
  if (form !== undefined) {
    options.headers['Content-Type'] = 'application/x-www-form-urlencoded';
    options.body = new URLSearchParams(form).toString();
  }
  const response = await fetch(Base + path, options);
  const text = await response.text();
  if (!response.ok) {
    throw new Error(`${method} ${path}: HTTP ${response.status} ${text}`);
  }
  return text.length > 0 ? JSON.parse(text) : null;
}

// Reads an agent's own database (read-only, as that agent).
async function query(room, agentId, sql, parameters = []) {
  const result = await api(`${room}/agents/${agentId}/database`, 'POST', { statements: [{ sql, parameters }] });
  return result.results[0].rows;
}

function sleep(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

module.exports = { api, query, sleep };
