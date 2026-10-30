export function percentile(values, percentileValue) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.max(0, Math.ceil(percentileValue / 100 * sorted.length) - 1)];
}

export function distribution(values) {
  const valid = values.filter(Number.isFinite);
  return {
    count: valid.length,
    p50Ms: percentile(valid, 50),
    p95Ms: percentile(valid, 95),
    maxMs: valid.length ? Math.max(...valid) : null,
  };
}

export function summarize(events, expectedUnique, expectedSessions) {
  const identity = x => x.missionId && x.sourceTimestamp ? `${x.missionId}:${x.sourceTimestamp}` : null;
  const accepted = events.filter(x => x.kind === "ingest" && x.accepted && !x.duplicate);
  const acceptedKeys = new Set(accepted.map(x => x.key));
  // Late arrivals are durably persisted as historical evidence but deliberately
  // do not replace/publish the current projection.
  const projectedSamples = new Set(accepted.filter(x => !x.late).map(identity).filter(Boolean));
  const publishedSamples = new Set(events.filter(x => x.kind === "sse-telemetry").map(identity).filter(Boolean));
  const renderedSamples = new Set(events.filter(x => x.kind === "telemetry-rendered").map(identity).filter(Boolean));
  const duplicates = events.filter(x => x.kind === "ingest" && x.duplicate).length;
  const late = events.filter(x => x.kind === "ingest" && x.late).length;
  const errors = events.filter(x => x.kind === "ingest" && !x.accepted).length;
  const persist = events.filter(x => x.kind === "ingest" && x.accepted).map(x => x.latencyMs);
  const publish = events.filter(x => x.kind === "sse-telemetry").map(x => x.latencyMs).filter(Number.isFinite);
  const browser = events.filter(x => x.kind === "telemetry-rendered").map(x => x.latencyMs).filter(Number.isFinite);
  const incident = events.filter(x => x.kind === "sse-incident").map(x => x.latencyMs).filter(Number.isFinite);
  const network = events.filter(x => x.kind === "ingest" && x.accepted).map(x => x.networkLatencyMs);
  const persistence = events.filter(x => x.kind === "ingest" && x.accepted).map(x => x.persistenceLatencyMs);
  const backendPublish = events.filter(x => x.kind === "sse-telemetry").map(x => x.backendPublishLatencyMs);
  const browserPaint = events.filter(x => x.kind === "telemetry-rendered").map(x => x.browserPaintLatencyMs);
  const diagnostics = events.filter(x => x.kind === "server-diagnostics");
  const connectedSessions = new Set(events.filter(x => x.kind === "sse-connected").map(x => x.session)).size;
  const browserSessions = new Set(events.filter(x => x.kind === "telemetry-rendered").map(x => x.browserSession ?? x.session).filter(Boolean)).size;
  const sseErrors = events.filter(x => x.kind === "sse-error").length;
  const reconnectObserved = events.some(x => x.kind === "gateway-disconnected") && events.some(x => x.kind === "gateway-reconnected");
  return {
    expectedUnique, acceptedUnique: acceptedKeys.size,
    lost: Math.max(0, expectedUnique - acceptedKeys.size), late, duplicates, requestErrors: errors,
    acknowledgedRecords: acceptedKeys.size,
    publishLost: [...projectedSamples].filter(x => !publishedSamples.has(x)).length,
    browserRenderLost: [...projectedSamples].filter(x => !renderedSamples.has(x)).length,
    expectedSessions, connectedSessions, browserSessions, sseErrors, reconnectObserved,
    latency: {
      gatewayToAcknowledgement: distribution(persist), networkIngress: distribution(network),
      backendPersistToPublish: distribution(backendPublish), backendPersistence: distribution(persistence),
      browserReceiveToPaint: distribution(browserPaint), sourceToPublish: distribution(publish),
      sourceToBrowserRender: distribution(browser), incidentDetection: distribution(incident),
    },
    persistP95Ms: percentile(persist, 95), publishP95Ms: percentile(publish, 95),
    browserRenderP95Ms: percentile(browser, 95), incidentDetectionP95Ms: percentile(incident, 95),
    diagnosticsSamples: diagnostics.length,
    peakActiveSubscribers: diagnostics.length ? Math.max(...diagnostics.map(x => x.activeSubscribers)) : null,
    peakPendingThreadPoolWorkItems: diagnostics.length ? Math.max(...diagnostics.map(x => x.pendingThreadPoolWorkItems)) : null,
    peakManagedMemoryBytes: diagnostics.length ? Math.max(...diagnostics.map(x => x.managedMemoryBytes)) : null,
    managedMemoryGrowthBytes: diagnostics.length > 1 ? diagnostics.at(-1).managedMemoryBytes - diagnostics[0].managedMemoryBytes : null,
    subscriberCleanupDelta: diagnostics.length > 1 ? diagnostics.at(-1).activeSubscribers - diagnostics[0].activeSubscribers : null,
  };
}
