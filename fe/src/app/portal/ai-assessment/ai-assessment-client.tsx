"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import { createAiAssessment, getAiAssessments, getMission, getValidationMissionOptions, type ValidationMissionOption } from "@/lib/portal/client";
import type { AccountRole } from "@/lib/auth/types";
import type { AiAssessment, MissionDetail } from "@/lib/portal/types";
import { formatAutomaticLimits } from "@/lib/portal/automatic-flight-limits";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

const metric = (value: number | null | undefined, unit: string) => value == null ? "Unavailable" : `${value.toFixed(1)} ${unit}`;

export default function AiAssessmentClient({ role }: { role: AccountRole }) {
  const [missions, setMissions] = useState<ValidationMissionOption[] | null>(null);
  const [missionId, setMissionId] = useState("");
  const [context, setContext] = useState<MissionDetail | null>(null);
  const [items, setItems] = useState<AiAssessment[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const generating = useRef(false);
  const selected = missions?.find(item => item.id === missionId);
  const loadMissions = useCallback(async () => {
    setError(null);
    try {
      const options = await getValidationMissionOptions(role);
      setMissions(options);
      setMissionId(current => options.some(item => item.id === current) ? current : options.find(item => item.status === "Approved")?.id ?? options[0]?.id ?? "");
    } catch (reason) { setError(reason); }
  }, [role]);
  useEffect(() => {
    const timer = window.setTimeout(() => void loadMissions(), 0);
    return () => window.clearTimeout(timer);
  }, [loadMissions]);
  useEffect(() => {
    let active = true;
    const timer = window.setTimeout(() => {
      if (!missionId) { setItems([]); setContext(null); return; }
      setItems(null); setContext(null); setError(null);
      void Promise.all([getMission(missionId), getAiAssessments(missionId)]).then(([mission, history]) => {
        if (active) { setContext(mission); setItems(history); }
      }).catch(reason => { if (active) setError(reason); });
    }, 0);
    return () => { active = false; window.clearTimeout(timer); };
  }, [missionId]);
  async function generate() {
    if (!selected || generating.current) return;
    const requestedMission = selected.id;
    generating.current = true; setBusy(true); setError(null);
    try {
      const created = await createAiAssessment({ missionRef: requestedMission, missionVersion: 1 });
      setItems(current => [created, ...(current ?? [])]);
    } catch (reason) { setError(reason instanceof Error ? reason : new Error("Assessment is unavailable. Retry later or continue manual review.")); }
    finally { generating.current = false; setBusy(false); }
  }
  if (error && !missions) return <ModuleError error={error} retry={() => void loadMissions()} />;
  if (!missions) return <ModuleLoading label="Loading missions for assessment" />;
  const latest = items?.[0];
  const battery = latest?.result?.battery;
  return <>
    <header className="module-heading"><div><p className="module-kicker">MISSION / AI DECISION SUPPORT</p><h1>AI Risk Assessment</h1><p>Assess an authorized planned or historical mission using backend evidence. Battery estimates require fresh drone battery data and the installed pack capacity configured on the UAV.</p></div><button className="module-primary" disabled={busy || !selected || context?.id !== missionId} onClick={() => void generate()}>{busy ? "ASSESSING…" : latest ? "GENERATE NEW ASSESSMENT" : "GENERATE ASSESSMENT"}</button></header>
    {missions.length === 0 ? <ModuleEmpty title="No mission available" message="Create a mission before requesting an assessment." /> : <section className="ai-context"><label className="ai-mission-picker">MISSION<select value={missionId} disabled={busy} onChange={event => setMissionId(event.target.value)}>{missions.map(mission => <option key={mission.id} value={mission.id}>{mission.name} · {mission.status}</option>)}</select></label>{selected && <><h2>{selected.name}</h2><p>Status: {selected.status} · Planned waypoints: {context?.waypoints.length ?? "Loading…"}</p><small>The backend reads the mission plan, UAV specification, latest battery telemetry and matching weather evidence. Missing or stale battery data is reported explicitly. A battery estimate is not a flight authorization.</small></>}</section>}
    {error && <section className="ai-unavailable" role="alert"><b>ASSESSMENT UNAVAILABLE</b><p>{error instanceof Error ? error.message : "Request failed."}</p></section>}
    {selected && !items && !error && <ModuleLoading label="Loading assessment history" />}
    {selected && items?.length === 0 && <ModuleEmpty title="No assessment yet" message="Generate an assessment to review recorded risks and battery evidence." />}
    {latest?.result?.automaticFlightLimits && <section className="ai-context"><h2>Automatic operating envelope</h2><p>{formatAutomaticLimits(latest.result.automaticFlightLimits)}</p><p>Cruise: {metric(latest.result.cruiseSpeedMetersPerSecond, "m/s")} · Maximum horizontal speed: {metric(latest.result.maxSpeedMetersPerSecond, "m/s")}</p></section>}
    {battery && <section className="ai-context" aria-label="Battery assessment"><header><h2>Battery requirement · {battery.status === "EstimateOnly" ? "Estimate only" : "More data required"}</h2></header>
      <dl><div><dt>FC BATTERY REMAINING</dt><dd>{metric(battery.currentBatteryPercent, "%")}</dd></div><div><dt>FC CONSUMED CHARGE</dt><dd>{metric(battery.consumedMah, "mAh")}</dd></div><div><dt>CONFIGURED PACK CAPACITY</dt><dd>{metric(battery.configuredCapacityMah, "mAh")}</dd></div><div><dt>ESTIMATED CHARGE REMAINING</dt><dd>{metric(battery.estimatedRemainingChargeMah, "mAh")}</dd></div><div><dt>ESTIMATED MISSION REQUIREMENT</dt><dd>{metric(battery.requiredBatteryPercent, "%")} / {metric(battery.estimatedRequiredChargeMah, "mAh")}</dd></div></dl>
      <p>Capacity source: {battery.capacitySource}. Consumed charge is not the full capacity of the battery.</p>
      <p>{battery.meetsEstimatedRequirement == null ? "Battery sufficiency cannot be determined from the available evidence." : battery.meetsEstimatedRequirement ? "Reported battery meets this estimated requirement; preflight and operational checks still apply." : "Reported battery is below the estimated mission requirement."}</p>
      <p>Telemetry snapshot: {battery.telemetrySourceTimestamp ? formatVietnamDateTime(battery.telemetrySourceTimestamp) : "Unavailable"}. Regenerate the assessment to refresh this evidence.</p>
      {battery.warnings.length > 0 && <ul>{battery.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
    </section>}
    {latest?.result && <section className="ai-result"><header><div><p className="module-kicker">{latest.provider.toUpperCase()} · {latest.model}</p><h2>Assessment result</h2></div><time>{formatVietnamDateTime(latest.completedAt ?? latest.createdAt)}</time></header><p className="ai-summary">{latest.result.summary}</p>{latest.result.historicalFlights?.length ? <div className="ai-risk-list">{latest.result.historicalFlights.map(flight => <article key={flight.missionId}><span className="ai-severity low">{(flight.similarityScore * 100).toFixed(1)}% MATCH</span><h3>{flight.missionName}</h3><p><b>Source:</b> {flight.missionId}</p><p><b>Linked incidents:</b> {flight.incidentIds.length}</p></article>)}</div> : null}<div className="ai-risk-list">{latest.result.risks.map((risk, index) => <article key={`${risk.title}-${index}`}><span className={`ai-severity ${risk.severity.toLowerCase()}`}>{risk.severity}</span><h3>{risk.title}</h3><p><b>Evidence:</b> {risk.evidence}</p><p><b>Precaution:</b> {risk.precaution}</p></article>)}</div><div className="ai-result-notes"><div><h3>Recommended precautions</h3><ul>{latest.result.precautions.map(value => <li key={value}>{value}</li>)}</ul></div><div><h3>Missing data</h3>{latest.result.missingData.length ? <ul>{latest.result.missingData.map(value => <li key={value}>{value}</li>)}</ul> : <p>None reported.</p>}</div></div><footer>{latest.result.disclaimer}</footer></section>}
    {items && items.length > 1 && <section className="ai-history"><h2>Assessment history</h2>{items.slice(1).map(item => <article key={item.id}><span>{item.status}</span><b>{item.provider} · {item.model}</b><time>{formatVietnamDateTime(item.createdAt)}</time></article>)}</section>}
  </>;
}
