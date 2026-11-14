"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import type { AccountRole } from "@/lib/auth/types";
import { formatAutomaticLimits } from "@/lib/portal/automatic-flight-limits";
import {
  createAiAssessment,
  getAiAssessments,
  getMission,
  getValidationMissionOptions,
  type ValidationMissionOption,
} from "@/lib/portal/client";
import type { AiAssessment, MissionDetail } from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import LiveBattery from "./live-battery";

const metric = (value: number | null | undefined, unit: string) =>
  value == null ? "Unavailable" : `${value.toFixed(1)} ${unit}`;

const featureLabels: Record<string, { label: string; unit?: string }> = {
  centroidLatitude: { label: "Route center latitude", unit: "°" },
  centroidLongitude: { label: "Route center longitude", unit: "°" },
  durationMinutes: { label: "Flight duration", unit: "min" },
  meanAltitudeMeters: { label: "Mean altitude", unit: "m" },
  incidentCount: { label: "Recorded incidents" },
};

function featureValue(key: string, value: number | null) {
  if (value == null) return "Unavailable";
  const unit = featureLabels[key]?.unit ?? "";
  const digits = key.includes("Latitude") || key.includes("Longitude") ? 5 : 1;
  return `${value.toFixed(digits)}${unit ? ` ${unit}` : ""}`;
}

function statusClass(status: AiAssessment["status"]) {
  return status.toLowerCase().replaceAll("_", "-");
}

function readableIdentifier(value: string) {
  return value
    .replaceAll(/[-_]+/g, " ")
    .replace(/\bv(\d+)\b/gi, "v$1")
    .replace(/\b\w/g, character => character.toUpperCase());
}

export default function AiAssessmentClient({ role }: { role: AccountRole }) {
  const [missions, setMissions] = useState<ValidationMissionOption[] | null>(null);
  const [missionId, setMissionId] = useState("");
  const [context, setContext] = useState<MissionDetail | null>(null);
  const [items, setItems] = useState<AiAssessment[] | null>(null);
  const [activeAssessmentId, setActiveAssessmentId] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [failureKind, setFailureKind] = useState<"load" | "generate" | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [busy, setBusy] = useState(false);
  const generating = useRef(false);
  const selectedMission = missions?.find(item => item.id === missionId);

  const loadMissions = useCallback(async () => {
    setError(null);
    setFailureKind(null);
    try {
      const options = await getValidationMissionOptions(role);
      setMissions(options);
      setMissionId(current =>
        options.some(item => item.id === current)
          ? current
          : options.find(item => item.status === "Approved")?.id ?? options[0]?.id ?? "",
      );
    } catch (reason) {
      setError(reason);
      setFailureKind("load");
    }
  }, [role]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadMissions(), 0);
    return () => window.clearTimeout(timer);
  }, [loadMissions]);

  useEffect(() => {
    let active = true;
    const timer = window.setTimeout(() => {
      if (!missionId) {
        setItems([]);
        setContext(null);
        setActiveAssessmentId("");
        return;
      }

      setItems(null);
      setContext(null);
      setError(null);
      setFailureKind(null);
      void Promise.all([getMission(missionId), getAiAssessments(missionId)])
        .then(([mission, history]) => {
          if (!active) return;
          setContext(mission);
          setItems(history);
          setActiveAssessmentId(history[0]?.id ?? "");
        })
        .catch(reason => {
          if (!active) return;
          setError(reason);
          setFailureKind("load");
        });
    }, 0);

    return () => {
      active = false;
      window.clearTimeout(timer);
    };
  }, [missionId, reloadToken]);

  async function generate() {
    if (!selectedMission || generating.current) return;
    generating.current = true;
    setBusy(true);
    setError(null);
    setFailureKind(null);
    try {
      const created = await createAiAssessment({ missionRef: selectedMission.id, missionVersion: 1 });
      setItems(current => [created, ...(current ?? []).filter(item => item.id !== created.id)]);
      setActiveAssessmentId(created.id);
    } catch (reason) {
      setError(reason instanceof Error ? reason : new Error("Assessment provider is unavailable."));
      setFailureKind("generate");
    } finally {
      generating.current = false;
      setBusy(false);
    }
  }

  if (error && !missions) return <ModuleError error={error} retry={() => void loadMissions()} />;
  if (!missions) return <ModuleLoading label="Loading missions for assessment" />;

  const activeAssessment = items?.find(item => item.id === activeAssessmentId) ?? items?.[0];
  const result = activeAssessment?.result;
  const battery = result?.battery;
  const historicalFlights = result?.historicalFlights ?? [];

  return <>
    <header className="module-heading">
      <div>
        <p className="module-kicker">MISSION / AI DECISION SUPPORT</p>
        <h1>AI Risk Assessment</h1>
        <p>Review route risks, estimated battery demand and connected UAV charge. Each finding explains the evidence and what to do next.</p>
      </div>
      <button className="module-primary" disabled={busy || !selectedMission || context?.id !== missionId} onClick={() => void generate()}>
        {busy ? "ASSESSING…" : items?.length ? "GENERATE NEW ASSESSMENT" : "GENERATE ASSESSMENT"}
      </button>
    </header>

    {missions.length === 0 ? <ModuleEmpty title="No mission available" message="Create a mission before requesting an assessment." /> :
      <section className="ai-context">
        <label className="ai-mission-picker">MISSION
          <select value={missionId} disabled={busy} onChange={event => setMissionId(event.target.value)}>
            {missions.map(mission => <option key={mission.id} value={mission.id}>{mission.name} · {mission.status}</option>)}
          </select>
        </label>
        {selectedMission && <>
          <div className="ai-context-heading">
            <div><h2>{selectedMission.name}</h2><p>Status: {selectedMission.status} · Planned waypoints: {context?.waypoints.length ?? "Loading…"}</p></div>
            <span>CURRENT MISSION CONTEXT</span>
          </div>
          <small>Assessments use the current saved mission. Pending proposals are separate; an assessment does not approve a mission.</small>
        </>}
      </section>}

    {error && missions && <section className="ai-unavailable" role="alert">
      <div><b>{failureKind === "generate" ? "ASSESSMENT UNAVAILABLE" : "EVIDENCE LOAD FAILED"}</b><p>{error instanceof Error ? error.message : "Request failed."}</p><span>{items?.length ? "Previously loaded evidence remains available below." : "Manual review is still available."}</span></div>
      <button className="module-secondary" disabled={busy} onClick={() => failureKind === "generate" ? void generate() : setReloadToken(value => value + 1)}>{failureKind === "generate" ? "RETRY ASSESSMENT" : "RETRY LOAD"}</button>
    </section>}

    {selectedMission && !items && !error && <ModuleLoading label="Loading assessment history" />}
    {selectedMission && items?.length === 0 && <ModuleEmpty title="No assessment yet" message="Generate an assessment to compare this mission with recorded evidence." />}

    {context?.id === missionId && <LiveBattery key={missionId} missionId={missionId} uavId={context.uavId} assessment={activeAssessment} />}

    {items && items.length > 0 && <div className="ai-workspace">
      <aside className="ai-history" aria-label="Assessment history">
        <header><div><p className="module-kicker">ASSESSMENT RUNS</p><h2>History</h2></div><span>{items.length}</span></header>
        <div className="ai-history-list">
          {items.map(item => <button key={item.id} type="button" className={item.id === activeAssessment?.id ? "selected" : ""} aria-pressed={item.id === activeAssessment?.id} onClick={() => setActiveAssessmentId(item.id)}>
            <span className={`ai-run-status ${statusClass(item.status)}`}>{item.status}</span>
            <b>{readableIdentifier(item.provider)}</b>
            <small>{readableIdentifier(item.model)}</small>
            <time>{formatVietnamDateTime(item.completedAt ?? item.createdAt)}</time>
          </button>)}
        </div>
      </aside>

      {activeAssessment && <div className="ai-assessment-detail">
        <section className="ai-run-summary">
          <div className="ai-run-summary-copy"><p className="module-kicker">SELECTED ASSESSMENT</p><h2>Assessment evidence</h2><div className="ai-run-meta"><span><small>PROVIDER</small>{readableIdentifier(activeAssessment.provider)}</span><span><small>METHOD</small>{readableIdentifier(activeAssessment.model)}</span><span><small>MISSION VERSION</small>{activeAssessment.missionVersion}</span></div></div>
          <span className={`ai-run-status ${statusClass(activeAssessment.status)}`}>{activeAssessment.status}</span>
        </section>

        {activeAssessment.isOutdated && <section className="ai-unavailable"><b>ASSESSMENT OUTDATED</b><p>{activeAssessment.outdatedReason ?? "The mission plan has changed."} Generate a new assessment before using its findings.</p></section>}

        {result && <section className="ai-result">
          <header><div><p className="module-kicker">WHAT NEEDS ATTENTION</p><h2>Risks and next actions</h2></div><span>NO APPROVAL AUTHORITY</span></header>
          <dl className="ai-risk-overview">
            <div><dt>CURRENT-PLAN FINDINGS</dt><dd>{result.risks.filter(risk => risk.category !== "Historical" && !risk.title.startsWith("Historical")).length}</dd></div>
            <div><dt>EVIDENCE GAPS</dt><dd>{result.missingData.length}</dd></div>
            <div><dt>SIMILAR FLIGHTS REVIEWED</dt><dd>{historicalFlights.length}</dd></div>
          </dl>
          <p className="ai-summary">{result.summary}</p>
          {result.risks.length > 0 ? <div className="ai-risk-list">{[...result.risks].sort((a, b) => Number(a.category === "Historical" || a.title.startsWith("Historical")) - Number(b.category === "Historical" || b.title.startsWith("Historical"))).map((risk, index) => <article key={`${risk.title}-${index}`}>
            <span className={`ai-severity ${risk.severity.toLowerCase()}`}>{risk.severity} · {risk.category === "Historical" || risk.title.startsWith("Historical") ? "Historical evidence" : "Current plan"}</span>
            <h3>{risk.title}</h3><p><b>Why it matters:</b> {risk.evidence}</p><p><b>What to do:</b> {risk.precaution}</p>
            {risk.sourceIncidentIds?.map((id, i) => <Link className="ai-flight-link" key={id} href={`/portal/incidents/${encodeURIComponent(id)}`}>Inspect incident {i + 1} →</Link>)}
          </article>)}</div> : <div className="ai-no-evidence"><b>No generated risk item</b><p>This is not a zero-risk conclusion. Review the missing evidence and mandatory validation.</p></div>}
          <div className="ai-result-notes"><div><h3>Recommended precautions</h3>{result.precautions.length ? <ul>{result.precautions.map(value => <li key={value}>{value}</li>)}</ul> : <p>None generated; manual review still applies.</p>}</div><div><h3>Missing evidence to resolve</h3>{result.missingData.length ? <ul>{result.missingData.map(value => <li key={value}>{value}</li>)}</ul> : <p>None reported in this snapshot.</p>}</div></div>
          <p><Link className="module-secondary" href={`/portal/missions/${encodeURIComponent(missionId)}`}>Review mission and validation</Link></p>
          <footer>{result.disclaimer}</footer>
        </section>}

        {result?.automaticFlightLimits && <section className="ai-context">
          <h2>Automatic operating envelope</h2>
          <p>{formatAutomaticLimits(result.automaticFlightLimits)}</p>
          <p>Cruise: {metric(result.cruiseSpeedMetersPerSecond, "m/s")} · Maximum horizontal speed: {metric(result.maxSpeedMetersPerSecond, "m/s")}</p>
        </section>}

        {battery && <section className="ai-context" aria-label="Battery assessment">
          <header><h2>Planning estimate · {battery.requiredBatteryPercent != null ? `${battery.requiredBatteryPercent.toFixed(1)}% needed at departure` : "Inputs needed"}</h2></header>
          <p>Captured {formatVietnamDateTime(activeAssessment.completedAt ?? activeAssessment.createdAt)}. The live battery panel above updates separately.</p>
          <dl><div><dt>ROUTE DISTANCE</dt><dd>{metric(battery.routeDistanceMeters, "m")}</dd></div><div><dt>ESTIMATED FLIGHT TIME</dt><dd>{metric(battery.estimatedFlightMinutes, "min")}</dd></div><div><dt>REQUIRED CHARGE ESTIMATE</dt><dd>{metric(battery.estimatedRequiredChargeMah, "mAh")}</dd></div><div><dt>RESERVE INCLUDED</dt><dd>{metric(battery.safetyReservePercent, "%")}</dd></div></dl>
          <p>Calculation: route demand {metric(battery.baseBatteryPercent, "%")} + weather adjustment {metric(battery.weatherAdjustmentPercent, "percentage points")} + reserve {metric(battery.safetyReservePercent, "percentage points")}.</p>
          {battery.reasons && <ul>{battery.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul>}
          <p>Capacity source: {battery.capacitySource}. Telemetry snapshot: {battery.telemetrySourceTimestamp ? formatVietnamDateTime(battery.telemetrySourceTimestamp) : "Unavailable"}.</p>
          <p>At capture: FC charge {metric(battery.currentBatteryPercent, "%")}. {battery.meetsEstimatedRequirement == null ? "Battery sufficiency was not established." : battery.meetsEstimatedRequirement ? "Reported battery met the estimate at capture; preflight checks still apply." : "Reported battery was below the estimate at capture."}</p>
          {battery.missingData.length > 0 && <div><h3>To complete the battery assessment</h3><ul>{battery.missingData.map(value => <li key={value}>{value}</li>)}</ul></div>}
          {battery.warnings.length > 0 && <ul>{battery.warnings.map(warning => <li key={warning}>{warning}</li>)}</ul>}
        </section>}

        {result && <>
          <details className="ai-history-evidence">
          <summary>Historical supporting evidence · {historicalFlights.length} similar completed flights</summary>
          <section className="ai-evidence">
            <header><div><p className="module-kicker">RECORDED HISTORICAL EVIDENCE</p><h2>Similar flights</h2></div><div className="ai-retrieval"><span>{result.retrieval?.method ?? "Retrieval method unavailable"}</span><small>{result.retrieval?.featureSchema ?? "Feature schema unavailable"} · {historicalFlights.length}/{result.retrieval?.maximumResults ?? 5} sources</small></div></header>
            <p>Similarity compares recorded route, duration, altitude and UAV features. A match of 80% does not mean 80% safe or a 20% chance of failure. Historical incidents require review for relevance.</p>
            {historicalFlights.length === 0 ? <div className="ai-no-evidence"><b>No historical evidence</b><p>No authorized matching flight was returned for this assessment. This does not mean zero risk; continue manual review using mission, weather, airspace and UAV evidence.</p></div> :
              <div className="ai-source-list">{historicalFlights.map(flight => <article className="ai-source-card" key={flight.missionId}>
                <header><div><span className="ai-severity low">{(flight.similarityScore * 100).toFixed(1)}% MATCH</span><h3>{flight.missionName}</h3></div><span className="ai-source-type">FLIGHT RECORD</span></header>
                <dl>{Object.entries(flight.features).map(([key, value]) => <div key={key}><dt>{featureLabels[key]?.label ?? key}</dt><dd>{featureValue(key, value)}</dd></div>)}</dl>
                <div className="ai-source-actions"><Link className="ai-flight-link" href={`/portal/flight-history/${encodeURIComponent(flight.missionId)}`}>View flight evidence <span aria-hidden="true">→</span></Link>{flight.incidentIds.length > 0 ? <div className="ai-incident-links"><span>RELATED INCIDENTS</span>{flight.incidentIds.map((incidentId, index) => <Link key={incidentId} aria-label={`Open linked incident ${index + 1}`} href={`/portal/incidents/${encodeURIComponent(incidentId)}`}>Incident {String(index + 1).padStart(2, "0")} <span aria-hidden="true">→</span></Link>)}</div> : <span className="ai-no-incidents">NO LINKED INCIDENTS</span>}</div>
              </article>)}</div>}
          </section>
          </details>
        </>}

        {!result && <section className="ai-unavailable"><div><b>RESULT NOT AVAILABLE</b><p>{activeAssessment.errorCode ?? "The selected assessment has no result payload."}</p><span>Use the mission evidence for manual review or retry the provider.</span></div></section>}
      </div>}
    </div>}
  </>;
}
