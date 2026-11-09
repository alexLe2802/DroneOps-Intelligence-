import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';

// Run the component's polling effect with a controlled browser and clock.
const source = await readFile(new URL('../src/app/portal/drone-connection/drone-connection-client.tsx', import.meta.url), 'utf8');
const effect = source.slice(source.indexOf('  useEffect(() => {'), source.indexOf('  const eligible='));
const js = ts.transpileModule(effect, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText;
function setup(sessions = () => Promise.resolve([])) {
  const document = Object.assign(new EventTarget(), { visibilityState: 'visible' });
  const window = Object.assign(new EventTarget(), { setInterval(fn) { this.tick = fn; return 1; } });
  const navigator = { onLine: true };
  let now = 0, cleanup;
  const calls = { sessions: 0, bindings: 0, writes: 0 };
  Function('useEffect', 'loading', 'Date', 'window', 'document', 'navigator', 'clearInterval',
    'getDroneConnections', 'getDroneBindings', 'setNow', 'setConnections', 'setBindings', 'setStatusError', js)(
    fn => { cleanup = fn(); }, false, { now: () => now }, window, document, navigator, () => {},
    () => { calls.sessions++; return sessions(); },
    () => { calls.bindings++; return Promise.resolve([]); }, () => {},
    () => calls.writes++, () => calls.writes++, () => calls.writes++);
  return { document, window, navigator, calls, cleanup, setNow: value => { now = value; },
    tick: async () => { window.tick(); await new Promise(resolve => setImmediate(resolve)); } };
}
test('hidden and offline tabs stop polling; returning online refreshes bindings', async () => {
  const app = setup();
  app.document.visibilityState = 'hidden'; await app.tick();
  app.document.visibilityState = 'visible'; app.navigator.onLine = false; await app.tick();
  assert.equal(app.calls.sessions, 0);
  app.navigator.onLine = true; app.window.dispatchEvent(new Event('online'));
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(app.calls.sessions, 1); assert.equal(app.calls.bindings, 1);
  app.cleanup(); await app.tick(); assert.equal(app.calls.sessions, 1);
});
test('session polling keeps 5s cadence while bindings refresh every 30s', async () => {
  const app = setup();
  for (let now = 5000; now <= 30000; now += 5000) { app.setNow(now); await app.tick(); }
  assert.equal(app.calls.sessions, 6); assert.equal(app.calls.bindings, 1);
  app.cleanup();
});
test('slow requests never overlap and unmount prevents late state updates', async () => {
  let finish;
  const app = setup(() => new Promise(resolve => { finish = resolve; }));
  await app.tick(); await app.tick(); assert.equal(app.calls.sessions, 1);
  app.cleanup(); finish([]); await new Promise(resolve => setImmediate(resolve));
  assert.equal(app.calls.writes, 0);
});
