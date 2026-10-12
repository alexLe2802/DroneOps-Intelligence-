"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { ApiError } from "@/lib/auth/client";
import { createAiAssessment, getAiAssessments, getFlightHistory, getFlightTelemetry } from "@/lib/portal/client";
import type { AiAssessment, AiAssessmentRequest, FlightHistoryItem, FlightTelemetryPoint } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

function assessmentRequest(mission: FlightHistoryItem, telemetry: FlightTelemetryPoint[]): AiAssessmentRequest {
  const located = telemetry.filter((point): point is FlightTelemetryPoint & { latitude: number; longitude: number } => point.latitude !== null && point.longitude !== null);
  const stride = Math.max(1, Math.ceil(located.length / 100));
  const waypoints = located.filter((_, index) => index % stride === 0).slice(0, 100).map(point => ({
    latitude: point.latitude, longitude: point.longitude, altitude: point.altitude ?? 0, action: "RecordedTelemetry",
  }));
  return {
    missionRef: mission.missionId,
    missionName: mission.missionName,
    missionVersion: 1,
    missionStatus: mission.status,
    uavCode: mission.uavCode || null,
    startTime: mission.startTime,
    endTime: mission.endTime,
    waypoints,
    validationSummary: "Assessment uses recorded telemetry because the current Mission Detail API does not return planned waypoints or validation evidence.",
    incidents: mission.totalIncidents ? [`${mission.totalIncidents} incident(s) recorded for this mission.`] : [],
    telemetrySummary: mission.durationMinutes > 0 ? `Recorded flight duration: ${mission.durationMinutes.toFixed(1)} minutes.` : null,
  };
}

export default function AiAssessmentClient() {
  const [missions, setMissions] = useState<FlightHistoryItem[] | null>(null);
  const [missionId, setMissionId] = useState("");
  const [items, setItems] = useState<AiAssessment[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const generating = useRef(false);
  const selected = missions?.find(item => item.missionId === missionId);
  const loadHistory = useCallback(async () => {
    setError(null);
    try {
      const page = await getFlightHistory({ pageSize: 100 });
      setMissions(page.items);
      setMissionId(current => current || page.items[0]?.missionId || "");
    } catch (reason) { setError(reason); }
  }, []);
  useEffect(() => { void loadHistory(); }, [loadHistory]);
  useEffect(() => {
    if (!missionId) { setItems([]); return; }
    let active = true; setItems(null); setError(null);
    void getAiAssessments(missionId).then(value => { if (active) setItems(value); }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, [missionId]);
  async function generate() {
    if (!selected || generating.current) return;
    generating.current = true; setBusy(true); setError(null);
    try {
      const telemetry = await getFlightTelemetry(selected.missionId);
      const request = assessmentRequest(selected, telemetry);
      if (request.waypoints.length < 2) throw new Error("This mission has fewer than two recorded telemetry coordinates. An assessment cannot be generated until route data is available.");
      const created = await createAiAssessment(request);
      setItems(current => [created, ...(current ?? [])]);
    } catch (reason) {
      setError(reason instanceof Error ? reason : reason instanceof ApiError ? reason : new Error("AI assessment is unavailable. Retry later or continue manual review."));
      try { setItems(await getAiAssessments(selected.missionId)); } catch { /* preserve creation error */ }
    } finally { generating.current = false; setBusy(false); }
  }
  if (error && !missions) return <ModuleError error={error} retry={() => void loadHistory()} />;
  if (!missions) return <ModuleLoading label="Loading missions for AI assessment" />;
  const latest = items?.[0];
  return <>
    <header className="module-heading"><div><p className="module-kicker">MISSION / AI DECISION SUPPORT</p><h1>AI Risk Assessment</h1><p>Select an authorized mission. Gemini receives only the backend data shown below.</p></div><button className="module-primary" disabled={busy || !selected} onClick={() => void generate()}>{busy ? "ASSESSING…" : latest ? "GENERATE NEW ASSESSMENT" : "GENERATE ASSESSMENT"}</button></header>
    {missions.length === 0 ? <ModuleEmpty title="No mission available" message="Create a mission before requesting an AI assessment." /> : <section className="ai-context"><label className="ai-mission-picker">MISSION<select value={missionId} onChange={event => setMissionId(event.target.value)}>{missions.map(mission => <option key={mission.missionId} value={mission.missionId}>{mission.missionName} · {mission.status}</option>)}</select></label>{selected && <><header><div><p className="module-kicker">SUPPLIED BACKEND CONTEXT</p><h2>{selected.missionName}</h2></div><b>{selected.missionId.slice(0, 8).toUpperCase()}</b></header><dl><div><dt>UAV</dt><dd>{selected.uavCode || "Unavailable"}</dd></div><div><dt>STATUS</dt><dd>{selected.status}</dd></div><div><dt>INCIDENTS</dt><dd>{selected.totalIncidents}</dd></div><div><dt>DURATION</dt><dd>{selected.durationMinutes.toFixed(0)} min</dd></div></dl><small>Generate Assessment will load recorded telemetry coordinates from the backend. Planned waypoints and validation evidence remain unavailable from Mission Detail.</small></>}</section>}
    {error && <section className="ai-unavailable" role="alert"><b>AI ASSESSMENT UNAVAILABLE</b><p>{error instanceof Error ? error.message : "Request failed."}</p><span>Manual mission review remains available.</span></section>}
    {selected && !items && !error && <ModuleLoading label="Loading assessment history" />}
    {selected && items?.length === 0 && <ModuleEmpty title="No assessment yet" message="Generate an assessment to review risks and precautions." />}
    {latest?.status === "Failed" && <section className="ai-unavailable"><b>LAST REQUEST FAILED · {latest.errorCode}</b><p>Retry later or continue manual review.</p></section>}
    {latest?.result && <section className="ai-result"><header><div><p className="module-kicker">GEMINI ADVISORY · {latest.model}</p><h2>Assessment result</h2></div><time>{new Date(latest.completedAt ?? latest.createdAt).toLocaleString()}</time></header><p className="ai-summary">{latest.result.summary}</p><div className="ai-risk-list">{latest.result.risks.map((risk, index) => <article key={`${risk.title}-${index}`}><span className={`ai-severity ${risk.severity.toLowerCase()}`}>{risk.severity}</span><h3>{risk.title}</h3><p><b>Evidence:</b> {risk.evidence}</p><p><b>Precaution:</b> {risk.precaution}</p></article>)}</div><div className="ai-result-notes"><div><h3>Recommended precautions</h3><ul>{latest.result.precautions.map(value => <li key={value}>{value}</li>)}</ul></div><div><h3>Missing data</h3>{latest.result.missingData.length ? <ul>{latest.result.missingData.map(value => <li key={value}>{value}</li>)}</ul> : <p>None reported.</p>}</div></div><footer>{latest.result.disclaimer}</footer></section>}
    {items && items.length > 1 && <section className="ai-history"><h2>Assessment history</h2>{items.slice(1).map(item => <article key={item.id}><span>{item.status}</span><b>{item.provider} · {item.model}</b><time>{new Date(item.createdAt).toLocaleString()}</time></article>)}</section>}
  </>;
}
