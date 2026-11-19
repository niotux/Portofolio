// Renderer/stream regression check with a local bridge; no running shells are touched.
const { chromium } = require(process.env.PLAYWRIGHT_PACKAGE || 'playwright');
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
const root = path.resolve(__dirname, '../libs/holodeck.utilities/applets/bash');

(async () => {
  const server = http.createServer(async (request, response) => {
    try {
      const file = path.join(root, request.url === '/' ? 'index.html' : request.url);
      response.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : 'text/html');
      response.end(await fs.readFile(file));
    } catch { response.writeHead(404); response.end(); }
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const browser = await chromium.launch({ executablePath: process.env.CHROME_EXECUTABLE || '/usr/bin/google-chrome', headless: true, args: ['--no-sandbox', '--enable-unsafe-swiftshader'] });
  try {
    for (const fallback of [false, true]) {
      const page = await browser.newPage();
      const errors = [];
      page.on('pageerror', error => errors.push(error.message));
      await page.addInitScript(fallback => {
        if (fallback) {
          const getContext = HTMLCanvasElement.prototype.getContext;
          HTMLCanvasElement.prototype.getContext = function (type, ...args) {
            return type.startsWith('webgl') ? null : getContext.call(this, type, ...args);
          };
        }
        Object.defineProperty(window, 'Terminal', { configurable: true, set(value) {
          Object.defineProperty(window, 'Terminal', { value: class extends value {
            constructor(options) { super(options); window.testTerminal = this; window.writeCount = 0; }
            write(data, callback) { window.writeCount++; super.write(data, callback); }
          } });
        } });
        const events = new EventTarget();
        window.emit = message => events.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(message) }));
        window.holodeck = {
          ready: async () => ({ windowId: 'test' }), ownResource: async () => {}, setState: async () => {},
          request: async () => ({ id: 'test', running: true }),
          connect: async () => ({ readyState: 1, addEventListener: (...args) => events.addEventListener(...args), send: async () => {}, close: async () => {} })
        };
      }, fallback);
      await page.goto(`http://127.0.0.1:${server.address().port}/`);
      await page.waitForFunction(() => document.querySelector('#status').textContent === 'Connected');
      if (!fallback) { assert.ok(await page.locator('.xterm-screen canvas').count(), 'WebGL renderer loaded'); }
      else {
        assert.equal(await page.locator('.xterm-screen canvas:not(.xterm-link-layer)').count(), 0, 'unavailable WebGL falls back');
        assert.ok(await page.locator('.xterm-rows').count(), 'DOM renderer stays available');
      }
      const result = await page.evaluate(async () => {
        const start = performance.now();
        window.emit({ type: 'replay', connection: 'test', owner: 'test', cols: 80, rows: 24, chunks: [] });
        // Fragmented cursor addressing, color, line drawing and text like a full-screen IDE.
        for (let row = 1; row <= 24; row++) {
          for (const data of ['\x1b[', String(row), ';1H', '\x1b[44;37m', '│', ' Pascal editor '.repeat(5), '│']) {
            window.emit({ type: 'output', data });
          }
        }
        window.emit({ type: 'size', owner: 'test', cols: 100, rows: 30 });
        for (let index = 0; index < 2000; index++) { window.emit({ type: 'output', data: '\x1b[1;1H' + (index % 10) }); }
        window.emit({ type: 'output', data: '\x1b[30;1HFINISHED' });
        await new Promise((resolve, reject) => {
          const timeout = setTimeout(() => { clearInterval(timer); reject(new Error('Output stalled')); }, 10000);
          const timer = setInterval(() => {
            if (window.testTerminal.buffer.active.getLine(29)?.translateToString(true) === 'FINISHED') {
              clearInterval(timer); clearTimeout(timeout); resolve();
            }
          }, 10);
        });
        return { writes: window.writeCount, milliseconds: performance.now() - start, cols: window.testTerminal.cols, rows: window.testTerminal.rows, first: window.testTerminal.buffer.active.getLine(0).translateToString(true) };
      });
      assert.ok(result.writes < 20, `fragmented redraw batched: ${JSON.stringify(result)}`);
      assert.equal(result.cols, 100); assert.equal(result.rows, 30);
      assert.ok(result.first.startsWith('9 Pascal editor'), result.first);
      if (!fallback) {
        await page.locator('.xterm-screen canvas:not(.xterm-link-layer)').evaluate(canvas => canvas.dispatchEvent(new Event('webglcontextlost', { cancelable: true })));
        await page.waitForFunction(() => !document.querySelector('.xterm-screen canvas'));
        assert.ok(await page.locator('.xterm-rows').count(), 'context loss restores DOM rendering');
        assert.equal(await page.evaluate(() => window.testTerminal.buffer.active.getLine(29).translateToString(true)), 'FINISHED');
      }
      assert.deepEqual(errors, []);
      console.log(`PASS: ${fallback ? 'DOM fallback' : 'WebGL and context loss'}, ordered redraw/resize: ${result.writes} writes, ${Math.round(result.milliseconds)}ms`);
      await page.close();
    }
  } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(error => { console.error(error); process.exitCode = 1; });
