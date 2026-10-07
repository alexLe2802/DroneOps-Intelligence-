"use client";

import { useCallback, useEffect, useState } from "react";
import { ApiError } from "@/lib/auth/client";
import { createAiAssessment, getAiAssessments } from "@/lib/portal/client";
import type { AiAssessment, AiAssessmentRequest } from "@/lib/portal/types";

const missionContext: AiAssessmentRequest = {
  missionRef: "MS-8849",
  missionName: "Transmission Line Audit",
  missionVersion: 3,
  missionStatus: "PendingApproval",
  uavCode: "UAV-01",
  startTime: "2026-10-06T02:00:00Z",
  endTime: "2026-10-06T03:15:00Z",
  waypoints: [
    { latitude: 10.8490, longitude: 106.7725, altitude: 80, action: "FlyThrough" },
    { latitude: 10.8562, longitude: 106.7814, altitude: 120, action: "Photo" },
    { latitude: 10.8631, longitude: 106.7902, altitude: 100, action: "Hover" },
  ],
  validationSummary: "Configured spatial checks passed. One prior geofence breach remains in mission history and is not part of the submitted route.",
  incidents: [],
  telemetrySummary: null,
};

export default function AiAssessmentClient() {
  const [items, setItems] = useState<AiAssessment[] | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const load = useCallback(async () => {
    setError("");
    try { setItems(await getAiAssessments(missionContext.missionRef)); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to load AI assessments."); }
  }, []);
  useEffect(() => {
    let active = true;
    void getAiAssessments(missionContext.missionRef)
      .then(value => { if (active) setItems(value); })
      .catch(reason => { if (active) setError(reason instanceof Error ? reason.message : "Unable to load AI assessments."); });
    return () => { active = false; };
  }, []);
  async function generate() {
    setBusy(true); setError("");
    try {
      const created = await createAiAssessment(missionContext);
      setItems(current => [created, ...(current ?? [])]);
    } catch (reason) {
      setError(reason instanceof ApiError ? reason.message : "AI assessment is unavailable. Retry later or continue manual review.");
      await load();
    } finally { setBusy(false); }
  }
  const latest = items?.[0];
  return <>
    <header className="module-heading"><div><p className="module-kicker">MISSION / AI DECISION SUPPORT</p><h1>AI Risk Assessment</h1><p>Review advisory risks and precautions against the supplied mission context. Human approval remains required.</p></div><button className="module-primary" disabled={busy} onClick={() => void generate()}>{busy ? "ASSESSING…" : latest ? "GENERATE NEW ASSESSMENT" : "GENERATE ASSESSMENT"}</button></header>
    {error && <section className="ai-unavailable" role="alert"><b>AI ASSESSMENT UNAVAILABLE</b><p>{error}</p><span>Manual mission review remains available.</span></section>}
    <section className="ai-context"><header><div><p className="module-kicker">SUPPLIED CONTEXT</p><h2>{missionContext.missionRef} · {missionContext.missionName}</h2></div><b>VERSION {String(missionContext.missionVersion).padStart(2, "0")}</b></header><dl><div><dt>UAV</dt><dd>{missionContext.uavCode}</dd></div><div><dt>STATUS</dt><dd>{missionContext.missionStatus}</dd></div><div><dt>ROUTE</dt><dd>{missionContext.waypoints.length} waypoints</dd></div><div><dt>VALIDATION</dt><dd>PASS</dd></div></dl><p>{missionContext.validationSummary}</p><small>Demo mission snapshot. It will be replaced by the authorized Mission API record when that API is available.</small></section>
    {!items && !error && <p className="module-loading" role="status">Loading assessments…</p>}
    {items?.length === 0 && <section className="module-empty"><h2>No assessment yet</h2><p>Generate an assessment to review mission risks and missing data.</p></section>}
    {latest?.status === "Failed" && <section className="ai-unavailable"><b>LAST REQUEST FAILED · {latest.errorCode}</b><p>Retry later or continue manual review.</p></section>}
    {latest?.result && <section className="ai-result"><header><div><p className="module-kicker">GEMINI ADVISORY · {latest.model}</p><h2>Assessment result</h2></div><time>{new Date(latest.completedAt ?? latest.createdAt).toLocaleString()}</time></header><p className="ai-summary">{latest.result.summary}</p><div className="ai-risk-list">{latest.result.risks.map((risk, index) => <article key={`${risk.title}-${index}`}><span className={`ai-severity ${risk.severity.toLowerCase()}`}>{risk.severity}</span><h3>{risk.title}</h3><p><b>Evidence:</b> {risk.evidence}</p><p><b>Precaution:</b> {risk.precaution}</p></article>)}</div><div className="ai-result-notes"><div><h3>Recommended precautions</h3><ul>{latest.result.precautions.map(value => <li key={value}>{value}</li>)}</ul></div><div><h3>Missing data</h3>{latest.result.missingData.length ? <ul>{latest.result.missingData.map(value => <li key={value}>{value}</li>)}</ul> : <p>None reported.</p>}</div></div><footer>{latest.result.disclaimer}</footer></section>}
    {items && items.length > 1 && <section className="ai-history"><h2>Assessment history</h2>{items.slice(1).map(item => <article key={item.id}><span>{item.status}</span><b>{item.provider} · {item.model}</b><time>{new Date(item.createdAt).toLocaleString()}</time></article>)}</section>}
  </>;
}
