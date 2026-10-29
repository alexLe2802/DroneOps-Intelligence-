import test from "node:test";
import assert from "node:assert/strict";
import { percentile, summarize } from "./telemetry-performance-lib.mjs";

test("nearest-rank percentile is deterministic", () => {
  assert.equal(percentile([10, 40, 20, 30], 95), 40);
  assert.equal(percentile([], 95), null);
});

test("summary separates accepted, lost, late and duplicate", () => {
  const summary = summarize([
    { kind: "ingest", key: "a", accepted: true, duplicate: false, late: false, latencyMs: 10 },
    { kind: "ingest", key: "a", accepted: true, duplicate: true, late: false, latencyMs: 8 },
    { kind: "ingest", key: "b", accepted: true, duplicate: false, late: true, latencyMs: 20 },
    { kind: "sse-connected", session: 1 }, { kind: "sse-connected", session: 2 },
    { kind: "telemetry-rendered", latencyMs: 100 }, { kind: "sse-incident", latencyMs: 250 },
  ], 3, 2);
  assert.deepEqual({ accepted: summary.acceptedUnique, lost: summary.lost, late: summary.late, duplicate: summary.duplicates },
    { accepted: 2, lost: 1, late: 1, duplicate: 1 });
  assert.equal(summary.browserRenderP95Ms, 100);
  assert.equal(summary.connectedSessions, 2);
  assert.equal(summary.sseErrors, 0);
  assert.equal(summary.reconnectObserved, false);
});
