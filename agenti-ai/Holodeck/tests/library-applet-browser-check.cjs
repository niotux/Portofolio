// Integration check for library applets. Creates only temporary projects; retains no user sessions.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const { execFileSync } = require('node:child_process');
const base = (process.env.ASTUDIO_URL || 'http://127.0.0.1:7077').replace(/\/$/, '');
const LIBRARY = 'holodeck.utilities';
const VERSION = '1.0.0';
const TYPE = `library:${LIBRARY}:${VERSION}:bash`;
const wait = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

async function poll(read, accepts, message) {
  let latest;
  for (let attempt = 0; attempt < 100; attempt++) {
    latest = await read();
    if (accepts(latest)) { return latest; }
    await wait(150);
  }
  throw new Error(`${message}: ${JSON.stringify(latest)}`);
}

(async () => {
  const browser = await chromium.launch({ executablePath: process.env.CHROME_EXECUTABLE || '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  // Inspect the public terminal buffer regardless of which renderer the browser supports.
  await context.addInitScript(() => {
    Object.defineProperty(window, 'Terminal', { configurable: true, set(value) {
      Object.defineProperty(window, 'Terminal', { configurable: true, writable: true, value: class extends value {
        constructor(options) { super(options); window.bashTerminal = this; }
      } });
    } });
  });
  let page = await context.newPage();
  const api = context.request;
  const projects = [];
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  let projectId;
  const service = (project, path) => `${base}/api/projects/${project}/libraries/${LIBRARY}/${VERSION}/services/bash${path}${path.includes('?') ? '&' : '?'}applet=bash`;
  const windows = () => page.locator(`.applet-window[data-applet="${TYPE}"]`);
  const sessions = async project => {
    const response = await api.get(service(project, '/sessions'));
    assert.equal(response.status(), 200, await response.text());
    return (await response.json()).sessions;
  };
  async function createProject() {
    const created = await api.post(`${base}/api/projects`, { data: { name: `Utilities check ${Date.now()}` } });
    assert.equal(created.status(), 201, await created.text());
    const project = await created.json(); projects.push(project.id);
    const attached = await api.post(`${base}/api/projects/${project.id}/libraries`, { data: { id: LIBRARY, version: VERSION } });
    assert.equal(attached.status(), 200, await attached.text());
    return project.id;
  }
  async function openBash() {
    const count = await windows().count();
    await page.locator('#start').click();
    await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: /^Bash$/ }) }).click();
    await poll(() => windows().count(), value => value === count + 1, 'Bash window did not open');
    const window = windows().nth(count);
    await window.locator('iframe').contentFrame().locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    return window;
  }
  async function closeWindow(window) {
    const instance = await window.getAttribute('data-instance');
    const task = page.locator(`#tasks .task[data-instance="${instance}"]`);
    if (!await window.evaluate(node => node.classList.contains('active'))) { await task.click(); }
    await window.locator('.window-controls button[aria-label="Close window"]').click();
    await page.locator(`.applet-window[data-instance="${instance}"]`).waitFor({ state: 'detached' });
  }
  async function workspace() { return (await (await api.get(`${base}/api/projects/${projectId}/workspace`)).json()).layout.windows; }
  async function readShell(id, command, pattern, project = projectId) {
    return page.evaluate(async ({ url, command, pattern }) => {
      return new Promise((resolve, reject) => {
        const socket = new WebSocket(url.replace(/^http/, 'ws'));
        let output = '';
        const timeout = setTimeout(() => { socket.close(); reject(new Error(`Terminal output timed out: ${output}`)); }, 12000);
        socket.addEventListener('open', () => { if (command) { socket.send(JSON.stringify({ type: 'input', data: command + '\r' })); } });
        socket.addEventListener('message', event => {
          const message = JSON.parse(event.data);
          if (message.type !== 'output') { return; }
          output += message.data;
          const match = output.match(new RegExp(pattern));
          if (match) { clearTimeout(timeout); socket.close(); resolve(match[1]); }
        });
        socket.addEventListener('error', () => { clearTimeout(timeout); reject(new Error('Terminal service connection failed.')); });
      });
    }, { url: service(project, `/sessions/${id}/terminal`), command, pattern });
  }

  try {
    projectId = await createProject();
    await page.goto(`${base}/?project=${projectId}`);
    await page.locator('#project-name').filter({ hasText: /Utilities check/ }).waitFor({ timeout: 90000 });
    await poll(() => page.locator('#start-menu .start-item strong').allTextContents(), names => names.includes('Bash'), 'Library applet was not discovered');
    assert.equal((await sessions(projectId)).length, 0, 'project activation starts the manager without creating a shell');
    console.log('PASS: project activation starts an applet-only library service and discovers Bash');

    const first = await openBash();
    const firstSession = (await poll(() => sessions(projectId), list => list.length === 1, 'First window did not create one shell'))[0];
    const input = first.locator('iframe').contentFrame().locator('.xterm-helper-textarea');
    await input.pressSequentially("export HD_APPLET_CHECK='kept-across-reload'", { delay: 5 });
    await input.press('Enter');
    const firstPid = await readShell(firstSession.id, "printf '\\nHD_PID:%s\\n' \"$$\"", 'HD_PID:(\\d+)');
    assert.notEqual(firstPid, '0');
    const shellUser = await readShell(firstSession.id, "printf '\\nHD_UID:%s\\n' \"$(id -u)\"", 'HD_UID:(\\d+)');
    assert.notEqual(shellUser, '0', 'the shell has no root access');
    assert.equal(await readShell(firstSession.id, "printf '\\nHD_VALUE:%s\\n' \"$HD_APPLET_CHECK\"", 'HD_VALUE:(kept-across-reload)'), 'kept-across-reload', 'terminal keystrokes reach the shell');

    const second = await openBash();
    let list = await poll(() => sessions(projectId), entries => entries.length === 2, 'Second window did not create a second shell');
    const secondSession = list.find(session => session.id !== firstSession.id);
    const secondPid = await readShell(secondSession.id, "printf '\\nHD_PID:%s\\n' \"$$\"", 'HD_PID:(\\d+)');
    assert.notEqual(firstPid, secondPid, 'each window owns a distinct shell process');
    assert.notEqual(firstSession.windowId, secondSession.windowId);
    const childPid = await readShell(secondSession.id, "sleep 600 & printf '\\nHD_CHILD:%s\\n' \"$!\"", 'HD_CHILD:(\\d+)');
    await poll(workspace, entries => entries.filter(entry => entry.type === TYPE && entry.state.sessionId).length === 2, 'Window ownership state was not saved');

    await page.screenshot({ path: '/tmp/holodeck-utilities-desktop.png' });
    const frame = second.locator('iframe').contentFrame();
    const restrictions = await frame.locator('body').evaluate(async () => {
      let parentBlocked = false;
      try { void parent.document.body; } catch { parentBlocked = true; }
      let networkBlocked = false;
      try { await fetch('/api/filesystem'); } catch { networkBlocked = true; }
      return { parentBlocked, networkBlocked };
    });
    assert.deepEqual(restrictions, { parentBlocked: true, networkBlocked: true }, 'iframe is isolated from shell DOM and direct network');
    const denied = await api.get(service(projectId, '/sessions').replace('applet=bash', 'applet=missing'));
    assert.ok([400, 403, 404].includes(denied.status()), 'unknown applets cannot use library services');

    console.log('PASS: separate non-root shells, real keyboard input and sandbox restrictions');
    await page.reload();
    await poll(() => windows().count(), count => count === 2, 'Window restore failed');
    await windows().first().locator('iframe').contentFrame().locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    await windows().last().locator('iframe').contentFrame().locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    list = await sessions(projectId);
    assert.deepEqual(list.map(session => session.id).sort(), [firstSession.id, secondSession.id].sort(), 'reload reconnects the same shells');
    assert.equal(await readShell(firstSession.id, "printf '\\nHD_AFTER:%s\\n' \"$HD_APPLET_CHECK\"", 'HD_AFTER:(kept-across-reload)'), 'kept-across-reload');

    console.log('PASS: browser reload restores both shell processes and environment state');
    await closeWindow(windows().last());
    list = await poll(() => sessions(projectId), entries => entries.length === 1, 'Explicit close did not release its shell');
    assert.equal(list[0].id, firstSession.id, 'closing one window leaves the other shell intact');
    assert.equal(await readShell(firstSession.id, `if kill -0 ${childPid} 2>/dev/null; then printf '\\nHD_CHILD_LIVE:1\\n'; else printf '\\nHD_CHILD_LIVE:0\\n'; fi`, '\\r?\\nHD_CHILD_LIVE:(0)\\r?\\n'), '0', 'explicit close also ends that shell’s background jobs');
    console.log('PASS: explicit window close ends only its shell and background jobs');
    await page.close();
    await wait(300);
    assert.equal((await sessions(projectId))[0].id, firstSession.id, 'browser closure does not end the shell');
    page = await context.newPage(); page.on('pageerror', error => errors.push(error.message));
    await page.goto(`${base}/?project=${projectId}`);
    await windows().first().locator('iframe').contentFrame().locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    await closeWindow(windows().first());
    await poll(() => sessions(projectId), entries => entries.length === 0, 'Last explicit window close left a shell');
    await openBash();
    list = await poll(() => sessions(projectId), entries => entries.length === 1, 'New Bash window failed');
    assert.notEqual(list[0].id, firstSession.id, 'a new window creates a new shell');

    console.log('PASS: browser closure preserves its shell; explicit close and new window replace it');
    const replaced = list[0].id;
    const environment = execFileSync('docker', ['--context', 'default', 'ps', '--filter', `label=holodeck.project=${projectId}`, '--filter', `label=holodeck.environment=${LIBRARY}/${VERSION}`, '--format', '{{.ID}}'], { encoding: 'utf8' }).trim();
    execFileSync('docker', ['--context', 'default', 'restart', environment], { stdio: 'ignore' });
    list = await poll(() => sessions(projectId).catch(() => []), entries => entries.length === 1 && entries[0].id !== replaced, 'An open window did not get a new shell after its environment restarted');
    await windows().last().locator('iframe').contentFrame().locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    assert.equal(await readShell(list[0].id, "printf '\\nHD_HOME:%s:%s\\n' \"$HOME\" \"$PWD\"", 'HD_HOME:(/[^\\r\\n]*)'), '/home/bash:/home/bash', 'the shell starts in its user\u2019s ordinary home');
    console.log('PASS: an open window starts a new shell in /home/bash after its environment restarts');
    await page.locator('#start').click();
    await page.locator('#start-menu .start-item').filter({ has: page.locator('strong', { hasText: /^Library Explorer$/ }) }).click();
    const explorer = page.locator('.applet-window[data-applet="library"]');
    await explorer.locator(`.library-row[data-library-key="${LIBRARY}/${VERSION}"]`).getByRole('button', { name: /^Explore / }).click();
    const appletsTab = explorer.getByRole('tab', { name: 'Applets', exact: true });
    assert.equal(await appletsTab.locator('.library-tab-count').textContent(), '1');
    await appletsTab.click();
    await explorer.locator('.library-definition').filter({ hasText: /^Bash/ }).click();
    const opened = await windows().count();
    await explorer.locator('.library-definition-detail').getByRole('button', { name: 'Open', exact: true }).click();
    await poll(() => windows().count(), value => value === opened + 1, 'Library Explorer did not open Bash');
    await windows().last().locator('iframe').contentFrame().locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    await closeWindow(windows().last());
    await closeWindow(explorer);
    await poll(() => sessions(projectId), entries => entries.length === 1, 'Closing the explored Bash window left its shell');
    console.log('PASS: Library Explorer lists the library\u2019s applets and opens Bash');

    // Two screens on one Bash window: the screen in use owns the size; the other shows the same grid unscaled.
    assert.equal(await windows().count(), 1);
    const desktop = windows().first();
    const deskFrame = desktop.locator('iframe').contentFrame();
    const phonePage = await context.newPage(); phonePage.on('pageerror', error => errors.push(error.message));
    await phonePage.setViewportSize({ width: 390, height: 844 });
    await phonePage.goto(`${base}/?project=${projectId}`);
    await phonePage.locator('body.phone').waitFor();
    await phonePage.locator('#start-menu .start-item strong', { hasText: /^Bash$/ }).waitFor({ state: 'attached', timeout: 90000 });
    await phonePage.locator('#phone-home').getByRole('button', { name: 'Bash', exact: true }).first().click();
    const phoneFrame = phonePage.locator(`.applet-window[data-applet="${TYPE}"]`).last().locator('iframe').contentFrame();
    await phoneFrame.locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    const shared = (await sessions(projectId))[0].id;
    const grid = frame => frame.locator('#terminal').evaluate(() => {
      const terminal = window.bashTerminal;
      const buffer = terminal.buffer.active;
      const text = [];
      for (let row = 0; row < terminal.rows; row++) { text.push(buffer.getLine(buffer.viewportY + row).translateToString(true)); }
      return { rows: terminal.rows, text };
    });
    const unscaled = frame => frame.locator('#terminal').evaluate(host => {
      const terminal = window.bashTerminal;
      const font = terminal.options.fontSize;
      const screen = host.querySelector('.xterm-screen');
      const whole = screen.offsetWidth <= host.clientWidth && screen.offsetHeight <= host.clientHeight;
      const box = host.getBoundingClientRect(); const bounds = screen.getBoundingClientRect();
      const width = screen.offsetWidth / terminal.cols; const height = screen.offsetHeight / terminal.rows;
      const left = bounds.left + terminal.buffer.active.cursorX * width; const top = bounds.top + terminal.buffer.active.cursorY * height;
      const cursorShown = top >= box.top - 1 && top + height <= box.bottom + 1 && left >= box.left - 1 && left + width <= box.right + 1;
      return font === 13 && (whole || cursorShown);
    });
    const size = () => readShell(shared, "printf '\\nHD_SIZE:%s\\n' \"$(stty size)\"", 'HD_SIZE:(\\d+ \\d+)');
    await deskFrame.locator('.xterm-helper-textarea').pressSequentially('echo desktop-owns', { delay: 5 });
    await deskFrame.locator('.xterm-helper-textarea').press('Enter');
    await deskFrame.locator('#elsewhere').waitFor({ state: 'hidden' });
    await phoneFrame.locator('#elsewhere').waitFor();
    const deskSize = await size();
    assert.equal((await grid(phoneFrame)).rows, Number(deskSize.split(' ')[0]), 'the phone shows the desktop’s grid');
    assert.ok(await unscaled(phoneFrame), 'the phone draws the desktop’s grid at its normal font and keeps the cursor in view');
    await phonePage.setViewportSize({ width: 844, height: 390 });
    await phonePage.waitForTimeout(500);
    assert.equal(await size(), deskSize, 'turning a screen that does not own the size leaves the shell alone');
    assert.ok(await unscaled(phoneFrame), 'the turned phone still draws the grid unscaled with its cursor in view');
    await phonePage.setViewportSize({ width: 390, height: 844 });
    await phoneFrame.locator('.xterm-helper-textarea').pressSequentially('echo phone-owns', { delay: 5 });
    await phoneFrame.locator('.xterm-helper-textarea').press('Enter');
    await phoneFrame.locator('#elsewhere').waitFor({ state: 'hidden' });
    await deskFrame.locator('#elsewhere').waitFor();
    const phoneSize = await size();
    assert.notEqual(phoneSize, deskSize, 'typing on the phone sizes the shell for the phone');
    assert.equal((await grid(deskFrame)).rows, Number(phoneSize.split(' ')[0]));
    await deskFrame.locator('.xterm-helper-textarea').pressSequentially('seq -s " " 1 60', { delay: 5 });
    await deskFrame.locator('.xterm-helper-textarea').press('Enter');
    await poll(async () => (await grid(phoneFrame)).text.join('\n'), text => text.includes('58 59 60'), 'Output did not reach the phone');
    await poll(async () => [await grid(deskFrame), await grid(phoneFrame)], ([left, right]) => left.text.join('\n') === right.text.join('\n'), 'The two screens show different text');
    await phonePage.reload(); await phonePage.locator('#phone-home').getByRole('button', { name: 'Bash', exact: true }).first().click();
    await phoneFrame.locator('#status').filter({ hasText: /^Connected$/ }).waitFor();
    await poll(async () => [await grid(deskFrame), await grid(phoneFrame)], ([left, right]) => left.text.join('\n') === right.text.join('\n') && right.text.join('\n').includes('58 59 60'), 'A reconnecting screen replayed different text');
    await deskFrame.locator('#elsewhere').waitFor({ state: 'hidden' });
    assert.equal(await size(), deskSize, 'when the owning screen leaves, the screen still open takes the size');
    await phoneFrame.locator('#elsewhere').click();
    await phoneFrame.locator('#elsewhere').waitFor({ state: 'hidden' });
    await deskFrame.locator('#elsewhere').waitFor();
    assert.equal(await size(), phoneSize, 'touching a screen sizes the shell for it');
    await phonePage.close();
    console.log('PASS: one Bash window on two screens keeps one size, follows the screen in use without scaling, and replays the same text');
    const otherProject = await createProject();
    const activated = await api.post(`${base}/api/projects/${otherProject}/activate`);
    assert.equal(activated.status(), 204, await activated.text());
    assert.equal((await sessions(otherProject)).length, 0, 'another project has an independent manager');
    assert.equal((await sessions(projectId)).length, 1, 'activating another project does not stop existing work');

    // Sudo for the bash service exists only while the user grants it.
    const servicesRoute = `${base}/api/projects/${otherProject}/services`;
    const bashService = async () => (await (await api.get(servicesRoute)).json()).find(entry => entry.libraryId === LIBRARY && entry.id === 'bash');
    const probeSudo = async () => {
      const created = await api.post(service(otherProject, '/sessions'), { data: { name: 'Sudo check' } });
      assert.equal(created.status(), 201, await created.text());
      return readShell((await created.json()).id, "printf 'HD_''SUDO=%s=\\n' \"$(sudo -n id -u 2>&1 | tail -1)\"", 'HD_SUDO=([^=\\r\\n]*)=', otherProject);
    };
    const listed = await bashService();
    assert.ok(listed.running && !listed.sudo && listed.uid >= 10000, 'Services lists the running bash service without sudo');
    assert.notEqual(await probeSudo(), '0', 'the bash service cannot use sudo before the user grants it');
    let grant = await api.put(`${servicesRoute}/${LIBRARY}/${VERSION}/bash/sudo`, { data: { enabled: true } });
    assert.equal(grant.status(), 200, await grant.text());
    assert.equal((await bashService()).sudo, true);
    assert.equal(await probeSudo(), '0', 'a granted bash service runs sudo as root');
    grant = await api.put(`${servicesRoute}/${LIBRARY}/${VERSION}/bash/sudo`, { data: { enabled: false } });
    assert.equal(grant.status(), 200, await grant.text());
    const log = await api.get(`${servicesRoute}/${LIBRARY}/${VERSION}/bash/log`);
    assert.equal(log.status(), 200, await log.text());
    assert.notEqual(await probeSudo(), '0', 'revoking sudo removes it');
    console.log('PASS: Services lists the bash service, reads its log, and grants sudo only while the user allows it');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.locator('body.phone').waitFor();
    await page.locator('#start').click();
    await page.locator('#phone-home').getByRole('button', { name: 'Bash', exact: true }).last().click();
    assert.ok(await windows().last().isVisible(), 'library applet fills the phone workspace');
    await page.screenshot({ path: '/tmp/holodeck-utilities-phone.png' });
    assert.deepEqual(errors, [], 'no uncaught browser exceptions');
    console.log('PASS: library discovery, isolated iframe, real terminal input, window-owned shells, reload/browser disconnect recovery, independent projects and phone layout');
  } catch (error) {
    if (!page.isClosed()) {
      await page.screenshot({ path: '/tmp/holodeck-utilities-failure.png' }).catch(() => { });
      console.error('Holodeck notices:', await page.locator('#notifications').innerText().catch(() => 'unavailable'));
    }
    throw error;
  } finally {
    for (const project of projects) {
      await api.post(`${base}/api/projects/${project}/stop`).catch(() => { });
      const response = await api.delete(`${base}/api/projects/${project}`);
      if (response.status() !== 204) { console.error(`Project cleanup failed (${response.status()})`); }
    }
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
