import fs from "node:fs";
import path from "node:path";
import { summarize } from "./telemetry-performance-lib.mjs";

const arg = name => { const i = process.argv.indexOf(name); return i < 0 ? null : process.argv[i + 1]; };
const configPath = arg("--config");
if (!configPath) throw new Error("Usage: node scripts/telemetry-performance.mjs --config <private-json> [--duration-seconds 3600] [--output directory]");
const config = JSON.parse(fs.readFileSync(configPath, "utf8"));
if (!Array.isArray(config.vehicles) || config.vehicles.length !== 5) throw new Error("Config must contain exactly five vehicles.");
const durationSeconds = Number(arg("--duration-seconds") ?? 3600);
const sessionCount = Number(arg("--sessions") ?? 10);
if (!Number.isInteger(durationSeconds) || durationSeconds < 2) throw new Error("Duration must be an integer of at least two seconds.");
if (sessionCount !== 10) throw new Error("KAN-80 evidence requires exactly ten sessions.");
if (!config.apiBase || !config.integrationKey || !config.viewerToken) throw new Error("apiBase, integrationKey and viewerToken are required.");
const profile = config.profile ?? {};
const profileApproved = profile.approved === true;
for (const field of ["sourceId", "uavId", "missionId"])
  if (new Set(config.vehicles.map(x => x[field])).size !== 5) throw new Error(`Each vehicle must have a distinct ${field}.`);
const output = path.resolve(arg("--output") ?? path.join("artifacts", `telemetry-performance-${new Date().toISOString().replaceAll(":", "-")}`));
fs.mkdirSync(output, { recursive: true });
const rawPath = path.join(output, "raw-evidence.jsonl");
const events = [];
const startedAt = new Date();
const abort = new AbortController();
const incidentTriggers = new Map();
const write = event => {
  const value = { observedAt: new Date().toISOString(), ...event };
  events.push(value); fs.appendFileSync(rawPath, JSON.stringify(value) + "\n");
};
const headers = token => ({ Authorization: `Bearer ${token}`, Accept: "text/event-stream" });
async function sampleDiagnostics() {
  try {
    const response = await fetch(`${config.apiBase.replace(/\/$/, "")}/api/live/stream/diagnostics`, { headers: headers(config.viewerToken) });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    write({ kind: "server-diagnostics", ...await response.json() });
  } catch (error) { write({ kind: "diagnostics-error", error: String(error) }); }
}

async function consumeSse(session, vehicle) {
  try {
    const response = await fetch(`${config.apiBase.replace(/\/$/, "")}/api/live/stream?missionId=${vehicle.missionId}`,
      { headers: headers(config.viewerToken), signal: abort.signal });
    if (!response.ok || !response.body) throw new Error(`SSE HTTP ${response.status}`);
    write({ kind: "sse-connected", session, missionId: vehicle.missionId });
    const reader = response.body.getReader(); const decoder = new TextDecoder(); let buffer = "";
    while (!abort.signal.aborted) {
      const { value, done } = await reader.read(); if (done) break;
      buffer += decoder.decode(value, { stream: true });
      let boundary;
      while ((boundary = buffer.indexOf("\n\n")) >= 0) {
        const frame = buffer.slice(0, boundary); buffer = buffer.slice(boundary + 2);
        const type = frame.split("\n").find(x => x.startsWith("event:"))?.slice(6).trim();
        const data = frame.split("\n").find(x => x.startsWith("data:"))?.slice(5).trim();
        if (!type || !data) continue;
        const payload = JSON.parse(data); const now = Date.now();
        if (type === "telemetryUpdated" && payload.sourceTimestamp) write({ kind: "sse-telemetry", session,
          missionId: vehicle.missionId, sourceTimestamp: payload.sourceTimestamp,
          backendPublishLatencyMs: payload.receivedAt ? now - Date.parse(payload.receivedAt) : null,
          latencyMs: now - Date.parse(payload.sourceTimestamp) });
        if (type === "incidentRaised") { const trigger = incidentTriggers.get(vehicle.missionId);
          write({ kind: "sse-incident", session, missionId: vehicle.missionId, incidentId: payload.id,
            latencyMs: trigger ? now - trigger : now - Date.parse(payload.createdAt) }); }
      }
    }
  } catch (error) { if (!abort.signal.aborted) write({ kind: "sse-error", session, missionId: vehicle.missionId, error: String(error) }); }
}

async function send(vehicle, second, options = {}) {
  const sourceTimestamp = new Date(Date.now() - (options.late ? 5000 : 0)).toISOString();
  const key = `${vehicle.sourceId}:${second}:${startedAt.getTime()}`;
  const payload = { sourceId: vehicle.sourceId, uavId: vehicle.uavId, missionId: vehicle.missionId, sourceTimestamp,
    latitude: vehicle.latitude ?? 10.849, longitude: vehicle.longitude ?? 106.772, altitudeMeters: 80,
    altitudeReference: "Relative", groundSpeedMetersPerSecond: 12, batteryPercent: options.incident ? 5 : 70,
    flightMode: "AUTO", armed: true, missionSequence: second + 1 };
  const before = Date.now();
  try {
    const response = await fetch(`${config.apiBase.replace(/\/$/, "")}/api/integrations/telemetry/samples`, {
      method: "POST", headers: { Authorization: `Bearer ${config.integrationKey}`, "Idempotency-Key": key, "Content-Type": "application/json" },
      body: JSON.stringify(payload), signal: AbortSignal.timeout(10000),
    });
    const body = await response.json().catch(() => ({}));
    const acknowledgedAt = Date.now();
    const backendReceivedAt = body.receivedAt ? Date.parse(body.receivedAt) : NaN;
    write({ kind: "ingest", key, missionId: vehicle.missionId, sourceTimestamp, status: response.status,
      accepted: response.ok && body.accepted !== false, duplicate: Boolean(body.isDuplicate ?? body.duplicate),
      late: Boolean(body.isLateArrival ?? body.lateArrival), latencyMs: acknowledgedAt - before,
      networkLatencyMs: Number.isFinite(backendReceivedAt) ? backendReceivedAt - before : null,
      persistenceLatencyMs: Number.isFinite(backendReceivedAt) ? acknowledgedAt - backendReceivedAt : null });
    if (options.duplicate) {
      const replay = await fetch(`${config.apiBase.replace(/\/$/, "")}/api/integrations/telemetry/samples`, {
        method: "POST", headers: { Authorization: `Bearer ${config.integrationKey}`, "Idempotency-Key": key, "Content-Type": "application/json" }, body: JSON.stringify(payload) });
      const replayBody = await replay.json(); write({ kind: "ingest", key, missionId: vehicle.missionId, status: replay.status,
        accepted: replay.ok, duplicate: Boolean(replayBody.isDuplicate ?? replayBody.duplicate), late: Boolean(replayBody.isLateArrival ?? replayBody.lateArrival), latencyMs: Date.now() - before });
    }
  } catch (error) { write({ kind: "ingest", key, missionId: vehicle.missionId, accepted: false, duplicate: false, late: options.late, latencyMs: Date.now() - before, error: String(error) }); }
}

await sampleDiagnostics();
const diagnosticTimer = setInterval(() => void sampleDiagnostics(), Number(profile.diagnosticsIntervalSeconds ?? 10) * 1000);
const streams = Array.from({ length: sessionCount }, (_, i) => consumeSse(i + 1, config.vehicles[i % 5]));
await new Promise(resolve => setTimeout(resolve, 1000));
let expectedUnique = 0;
const incidentStart = Math.max(1, Math.floor(durationSeconds / 3));
for (let second = 0; second < durationSeconds; second++) {
  const tick = Date.now();
  // Deliberate five-second disconnect window at midpoint; evidence records that no
  // samples were acknowledged, then proves recovery on the next successful tick.
  const disconnected = second >= Math.floor(durationSeconds / 2) && second < Math.floor(durationSeconds / 2) + Math.min(5, Math.max(1, durationSeconds - 1));
  if (disconnected) write({ kind: "gateway-disconnected", second });
  else {
    expectedUnique += config.vehicles.length;
    await Promise.all(config.vehicles.map((vehicle, i) => {
      const incident = i === 0 && (second === incidentStart || second === incidentStart + 1);
      if (incident && second === incidentStart) incidentTriggers.set(vehicle.missionId, Date.now());
      return send(vehicle, second, { late: second > 0 && second % 30 === 0, duplicate: second > 0 && second % 20 === 0, incident });
    }));
    if (second === Math.floor(durationSeconds / 2) + Math.min(5, Math.max(1, durationSeconds - 1))) write({ kind: "gateway-reconnected", second });
  }
  await new Promise(resolve => setTimeout(resolve, Math.max(0, 1000 - (Date.now() - tick))));
}
await new Promise(resolve => setTimeout(resolve, 3000)); abort.abort(); await Promise.allSettled(streams);
clearInterval(diagnosticTimer); await new Promise(resolve => setTimeout(resolve, 2000)); await sampleDiagnostics();
const browserEvidence = arg("--browser-evidence");
if (browserEvidence && fs.existsSync(browserEvidence)) for (const line of fs.readFileSync(browserEvidence, "utf8").split(/\r?\n/).filter(Boolean)) write(JSON.parse(line));
const summary = { startedAt: startedAt.toISOString(), endedAt: new Date().toISOString(), durationSeconds, vehicles: 5, samplesPerSecondPerVehicle: 1,
  profile: { id: profile.id ?? null, approved: profileApproved, deployment: profile.deployment ?? null, network: profile.network ?? null },
  ...summarize(events, expectedUnique, sessionCount) };
summary.soak60MinutesSatisfied = durationSeconds >= 3600;
summary.pass = summary.soak60MinutesSatisfied && summary.lost === 0 && summary.requestErrors === 0 && summary.sseErrors === 0 &&
  summary.connectedSessions === sessionCount && summary.browserSessions === sessionCount && summary.reconnectObserved &&
  summary.publishLost === 0 && summary.browserRenderLost === 0 &&
  profileApproved && summary.diagnosticsSamples >= 2 && summary.subscriberCleanupDelta === 0 &&
  Number.isFinite(profile.maxManagedMemoryGrowthBytes) && summary.managedMemoryGrowthBytes <= profile.maxManagedMemoryGrowthBytes &&
  Number.isFinite(profile.maxPendingThreadPoolWorkItems) && summary.peakPendingThreadPoolWorkItems <= profile.maxPendingThreadPoolWorkItems &&
  summary.publishP95Ms !== null && summary.publishP95Ms <= 2000 && summary.browserRenderP95Ms !== null && summary.browserRenderP95Ms <= 2000 &&
  summary.incidentDetectionP95Ms !== null;
fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2)); process.exitCode = summary.pass ? 0 : 1;
