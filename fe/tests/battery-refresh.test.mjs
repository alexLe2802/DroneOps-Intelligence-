import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const compile = source => ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const helper = compile(await readFile(new URL("../src/lib/portal/assessment-battery.ts", import.meta.url), "utf8"));
const { assessmentBatteryState } = await import(`data:text/javascript;base64,${Buffer.from(helper).toString("base64")}`);
const source = await readFile(new URL("../src/app/portal/ai-assessment/live-battery.tsx", import.meta.url), "utf8");
const effect = compile(source.slice(source.indexOf("  useEffect(() => {"), source.indexOf("  const state =")));
const initialTime = Date.parse("2026-11-14T08:00:00Z");
const sample = () => ({ missionId: "mission", uavId: "uav", sourceId: "fc", isValid: true, isStale: false,
  snapshotKind: "Latest", connectivityStatus: "Online", sourceTimestamp: new Date(initialTime).toISOString(),
  receivedAt: new Date(initialTime).toISOString(), batteryPercent: 70, armed: false });

function setup({ auto = false, read = () => Promise.resolve(sample()), lastRead = { current: { missionId: "", token: -1 } }, token = 0, uavId = "uav" } = {}) {
  let time = initialTime, sequence = 0, cleanup, observer;
  const timers = new Map();
  const document = Object.assign(new EventTarget(), { visibilityState: "visible" });
  const window = new EventTarget();
  const navigator = { onLine: true };
  const calls = { requests: 0, writes: 0, signals: [] };
  class Observer {
    constructor(callback) { this.callback = callback; observer = this.show.bind(this); }
    observe() {}
    disconnect() { this.disconnected = true; }
    show(value) { if (!this.disconnected) this.callback([{ isIntersecting: value }]); }
  }
  const write = () => calls.writes++;
  Function("useEffect", "missionId", "uavId", "autoRefresh", "refreshToken", "lastRead", "card", "IntersectionObserver", "document", "navigator", "window", "AbortController", "Date", "setTimeout", "clearTimeout", "setInterval", "clearInterval", "getLatestMissionTelemetry", "assessmentBatteryState", "setChecking", "setTelemetry", "setFailed", "setCheckedAt", "setNow", effect)(
    fn => { cleanup = fn(); }, "mission", uavId, auto, token, lastRead, { current: {} }, Observer, document, navigator, window, AbortController,
    { now: () => time }, (fn, delay) => { const id = ++sequence; timers.set(id, { fn, due: time + delay }); return id; }, id => timers.delete(id), () => 1, () => {},
    (id, signal) => { calls.requests++; calls.signals.push(signal); return read(id, signal); }, assessmentBatteryState,
    write, write, write, write, write);
  const flush = () => new Promise(resolve => setImmediate(resolve));
  return { document, window, navigator, calls, lastRead, flush, cleanup: () => cleanup?.(), show: value => observer?.(value),
    async advance(ms) {
      const target = time + ms;
      while (true) {
        const next = [...timers.entries()].filter(([, timer]) => timer.due <= target).sort((a, b) => a[1].due - b[1].due)[0];
        if (!next) break;
        timers.delete(next[0]); time = next[1].due; next[1].fn(); await flush();
      }
      time = target;
    } };
}

test("default battery check reads once, including for a connected disarmed aircraft", async () => {
  const app = setup(); app.show(true); await app.flush(); await app.advance(300000);
  assert.equal(app.calls.requests, 1); app.cleanup();
});

test("no requests before card visibility, or while tab is hidden/offline", async () => {
  const app = setup({ auto: true }); await app.advance(30000); assert.equal(app.calls.requests, 0);
  app.document.visibilityState = "hidden"; app.show(true); await app.advance(30000); assert.equal(app.calls.requests, 0);
  app.document.visibilityState = "visible"; app.navigator.onLine = false;
  app.document.dispatchEvent(new Event("visibilitychange")); await app.advance(30000); assert.equal(app.calls.requests, 0);
  app.navigator.onLine = true; app.window.dispatchEvent(new Event("online")); await app.flush();
  assert.equal(app.calls.requests, 1);
  app.show(false); await app.advance(300000); assert.equal(app.calls.requests, 1); app.cleanup();
});

test("opt-in automatic checks back off to 30, 60 and 120 seconds without telemetry", async () => {
  const app = setup({ auto: true, read: () => Promise.resolve(null) }); app.show(true); await app.flush();
  await app.advance(29999); assert.equal(app.calls.requests, 1);
  await app.advance(1); assert.equal(app.calls.requests, 2);
  await app.advance(59999); assert.equal(app.calls.requests, 2);
  await app.advance(1); assert.equal(app.calls.requests, 3);
  await app.advance(119999); assert.equal(app.calls.requests, 3);
  await app.advance(1); assert.equal(app.calls.requests, 4); app.cleanup();
});

test("slow reads never overlap; hiding aborts them and unmount prevents late writes", async () => {
  let finish;
  const app = setup({ auto: true, read: () => new Promise(resolve => { finish = resolve; }) });
  app.show(true); await app.advance(60000); assert.equal(app.calls.requests, 1);
  app.show(false); assert.equal(app.calls.signals[0].aborted, true);
  const writes = app.calls.writes; app.cleanup(); finish(sample()); await app.flush();
  assert.equal(app.calls.writes, writes);
});

test("switching auto off does not fetch again; an explicit new check does", async () => {
  const app = setup(); app.show(true); await app.flush(); app.cleanup();
  const stopped = setup({ lastRead: app.lastRead }); stopped.show(true); await stopped.flush();
  await stopped.advance(60000); assert.equal(stopped.calls.requests, 0); stopped.cleanup();
  const manual = setup({ lastRead: app.lastRead, token: 1 }); manual.show(true); await manual.flush();
  assert.equal(manual.calls.requests, 1); manual.cleanup();
  const unassigned = setup({ uavId: null }); await unassigned.advance(60000);
  assert.equal(unassigned.calls.requests, 0); unassigned.cleanup();
});
