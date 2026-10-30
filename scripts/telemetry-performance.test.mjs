import test from "node:test";
import assert from "node:assert/strict";
import { distribution, percentile, summarize } from "./telemetry-performance-lib.mjs";

test("nearest-rank percentile is deterministic", () => {
  assert.equal(percentile([10, 40, 20, 30], 95), 40);
  assert.equal(percentile([], 95), null);
});

test("latency distribution reports p50, p95 and max", () => {
  assert.deepEqual(distribution([40, 10, 30, 20]), { count: 4, p50Ms: 20, p95Ms: 40, maxMs: 40 });
});

test("summary separates accepted, lost, late and duplicate", () => {
  const summary = summarize([
    { kind: "ingest", key: "a", accepted: true, duplicate: false, late: false, latencyMs: 10 },
    { kind: "ingest", key: "a", accepted: true, duplicate: true, late: false, latencyMs: 8 },
    { kind: "ingest", key: "b", accepted: true, duplicate: false, late: true, latencyMs: 20 },
    { kind: "sse-connected", session: 1 }, { kind: "sse-connected", session: 2 },
    { kind: "sse-telemetry", missionId: "m1", sourceTimestamp: "t1", latencyMs: 50 },
    { kind: "telemetry-rendered", missionId: "m1", sourceTimestamp: "t1", browserSession: 1, latencyMs: 100 },
    { kind: "sse-incident", latencyMs: 250 },
  ], 3, 2);
  assert.deepEqual({ accepted: summary.acceptedUnique, lost: summary.lost, late: summary.late, duplicate: summary.duplicates },
    { accepted: 2, lost: 1, late: 1, duplicate: 1 });
  assert.equal(summary.browserRenderP95Ms, 100);
  assert.equal(summary.connectedSessions, 2);
  assert.equal(summary.sseErrors, 0);
  assert.equal(summary.reconnectObserved, false);
});

test("stage correlation reports publish, render and browser-session gaps", () => {
  const events = [
    { kind: "ingest", key: "a", missionId: "m1", sourceTimestamp: "t1", accepted: true, duplicate: false, latencyMs: 10 },
    { kind: "ingest", key: "b", missionId: "m2", sourceTimestamp: "t2", accepted: true, duplicate: false, latencyMs: 12 },
    { kind: "sse-telemetry", missionId: "m1", sourceTimestamp: "t1", latencyMs: 20 },
    { kind: "telemetry-rendered", missionId: "m1", sourceTimestamp: "t1", browserSession: 1, latencyMs: 30 },
  ];
  const summary = summarize(events, 2, 10);
  assert.equal(summary.acknowledgedRecords, 2);
  assert.equal(summary.publishLost, 1);
  assert.equal(summary.browserRenderLost, 1);
  assert.equal(summary.browserSessions, 1);
});

test("late historical records are not misreported as publish loss", () => {
  const summary = summarize([
    { kind: "ingest", key: "late", missionId: "m1", sourceTimestamp: "old", accepted: true, duplicate: false, late: true, latencyMs: 10 },
  ], 1, 10);
  assert.equal(summary.acknowledgedRecords, 1);
  assert.equal(summary.publishLost, 0);
  assert.equal(summary.browserRenderLost, 0);
});

test("diagnostics report queue, memory and subscriber cleanup", () => {
  const summary = summarize([
    { kind: "server-diagnostics", activeSubscribers: 2, pendingThreadPoolWorkItems: 3, managedMemoryBytes: 100 },
    { kind: "server-diagnostics", activeSubscribers: 12, pendingThreadPoolWorkItems: 9, managedMemoryBytes: 180 },
    { kind: "server-diagnostics", activeSubscribers: 2, pendingThreadPoolWorkItems: 4, managedMemoryBytes: 130 },
  ], 0, 10);
  assert.equal(summary.peakActiveSubscribers, 12);
  assert.equal(summary.peakPendingThreadPoolWorkItems, 9);
  assert.equal(summary.managedMemoryGrowthBytes, 30);
  assert.equal(summary.subscriberCleanupDelta, 0);
});
