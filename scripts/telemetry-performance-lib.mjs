export function percentile(values, percentileValue) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.max(0, Math.ceil(percentileValue / 100 * sorted.length) - 1)];
}

export function summarize(events, expectedUnique, expectedSessions) {
  const acceptedKeys = new Set(events.filter(x => x.kind === "ingest" && x.accepted && !x.duplicate).map(x => x.key));
  const duplicates = events.filter(x => x.kind === "ingest" && x.duplicate).length;
  const late = events.filter(x => x.kind === "ingest" && x.late).length;
  const errors = events.filter(x => x.kind === "ingest" && !x.accepted).length;
  const persist = events.filter(x => x.kind === "ingest" && x.accepted).map(x => x.latencyMs);
  const publish = events.filter(x => x.kind === "sse-telemetry").map(x => x.latencyMs).filter(Number.isFinite);
  const browser = events.filter(x => x.kind === "telemetry-rendered").map(x => x.latencyMs).filter(Number.isFinite);
  const incident = events.filter(x => x.kind === "sse-incident").map(x => x.latencyMs).filter(Number.isFinite);
  const connectedSessions = new Set(events.filter(x => x.kind === "sse-connected").map(x => x.session)).size;
  const sseErrors = events.filter(x => x.kind === "sse-error").length;
  const reconnectObserved = events.some(x => x.kind === "gateway-disconnected") && events.some(x => x.kind === "gateway-reconnected");
  return {
    expectedUnique, acceptedUnique: acceptedKeys.size,
    lost: Math.max(0, expectedUnique - acceptedKeys.size), late, duplicates, requestErrors: errors,
    expectedSessions, connectedSessions, sseErrors, reconnectObserved,
    persistP95Ms: percentile(persist, 95), publishP95Ms: percentile(publish, 95),
    browserRenderP95Ms: percentile(browser, 95), incidentDetectionP95Ms: percentile(incident, 95),
  };
}
