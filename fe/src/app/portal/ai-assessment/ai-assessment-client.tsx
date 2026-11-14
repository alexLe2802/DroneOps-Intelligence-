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
import { ArrowUpRight, ShieldAlert, RefreshCw, FileCheck2, Clock3, Database, Route, ChevronRight, Info, History } from "lucide-react";
import styles from "./assessment.module.css";

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
  const isHistorical = (risk: NonNullable<AiAssessment["result"]>["risks"][number]) => risk.category === "Historical" || risk.title.startsWith("Historical");
  const severityOrder = { Critical: 0, High: 1, Moderate: 2, Low: 3 };
  const currentRisks = (result?.risks ?? []).filter(risk => !isHistorical(risk)).sort((a, b) => severityOrder[a.severity] - severityOrder[b.severity]);
  const historicalRisks = (result?.risks ?? []).filter(isHistorical);
  const highRisks = currentRisks.filter(risk => risk.severity === "High" || risk.severity === "Critical").length;
  const outdated = activeAssessment?.isOutdated || activeAssessment?.status === "Outdated";
  const reviewLabel = outdated ? "Assessment outdated" : !result ? "Awaiting assessment" : highRisks ? "Action required" : currentRisks.length || result.missingData.length ? "Review required" : "Advisory available";
  const tone = highRisks && !outdated ? "high" : "moderate";

  return <div className={styles.page}>
    <header className={styles.heading}>
      <div><p className={styles.eyebrow}>OPERATIONS <span>/</span> DECISION SUPPORT</p><h1>AI Risk Assessment</h1><p>Understand the risks. Check the energy. Know what to do next.</p></div>
      <button className={styles.primaryButton} disabled={busy || !selectedMission || context?.id !== missionId} onClick={() => void generate()}><RefreshCw size={16} aria-hidden="true" className={busy ? styles.spinning : ""} />{busy ? "Assessing mission…" : items?.length ? "Run new assessment" : "Run assessment"}</button>
    </header>

    {missions.length === 0 ? <ModuleEmpty title="No mission available" message="Create a mission before requesting an assessment." /> :
      <section className={styles.missionBar}>
        <div className={styles.missionIcon}><Route size={22} aria-hidden="true" /></div>
        <label className={styles.missionPicker} htmlFor="assessment-mission"><span>MISSION TO ASSESS</span><select id="assessment-mission" value={missionId} disabled={busy} onChange={event => setMissionId(event.target.value)}>{missions.map(mission => <option key={mission.id} value={mission.id}>{mission.name} · {mission.status}</option>)}</select></label>
        <div className={styles.missionMeta}><span>{selectedMission?.status}</span><span>{context?.waypoints.length ?? "—"} waypoints</span><span>{context?.uavCode ?? "No UAV assigned"}</span></div>
        {selectedMission && <Link className={styles.textLink} href={`/portal/missions/${encodeURIComponent(missionId)}`}>View mission <ArrowUpRight size={15} aria-hidden="true" /></Link>}
      </section>}

    {Boolean(error) && missions && <section className={styles.error} role="alert"><div><b>{failureKind === "generate" ? "ASSESSMENT UNAVAILABLE" : "EVIDENCE LOAD FAILED"}</b><p>{error instanceof Error ? error.message : "Request failed."}</p><small>{items?.length ? "Previously loaded evidence remains available below." : "Manual review is still available."}</small></div><button className={styles.secondaryButton} disabled={busy} onClick={() => failureKind === "generate" ? void generate() : setReloadToken(value => value + 1)}>{failureKind === "generate" ? "RETRY ASSESSMENT" : "RETRY LOAD"}</button></section>}
    {selectedMission && !items && !error && <ModuleLoading label="Loading assessment history" />}

    {selectedMission && <>
      <section className={styles.overview}>
        <div className={styles.overviewIntro}><span className={`${styles.statusIcon} ${styles[tone]}`}><ShieldAlert size={24} aria-hidden="true" /></span><div><span className={styles.eyebrow}>MISSION RISK OVERVIEW</span><h2>{reviewLabel}</h2><p>{outdated ? "The mission changed after this report. Run a new assessment." : highRisks ? `${highRisks} high-priority finding${highRisks > 1 ? "s" : ""} to address before operational review.` : result?.missingData.length ? "Resolve the evidence gaps before drawing a readiness conclusion." : result ? "Review the findings alongside mandatory validation and preflight checks." : "Run an assessment to evaluate the saved plan. Connected UAV data appears below."}</p></div></div>
        <dl className={styles.overviewStats}><div><dt>Current-plan risks</dt><dd className={highRisks ? styles.highText : ""}>{result ? currentRisks.length : "—"}<ShieldAlert size={17} aria-hidden="true" /></dd></div><div><dt>Evidence gaps</dt><dd>{result ? result.missingData.length : "—"}<FileCheck2 size={17} aria-hidden="true" /></dd></div><div><dt>Historical matches</dt><dd>{result ? historicalFlights.length : "—"}<Database size={17} aria-hidden="true" /></dd></div></dl>
        <div className={styles.overviewFoot}><span><Info size={14} aria-hidden="true" /> Decision support only. Mandatory validation and manager approval remain separate.</span>{activeAssessment && <time><Clock3 size={13} aria-hidden="true" />{formatVietnamDateTime(activeAssessment.completedAt ?? activeAssessment.createdAt)}</time>}</div>
      </section>

      {items && items.length > 0 && <details className={styles.history}>
        <summary><History size={16} aria-hidden="true" /> Assessment history <span>{items.length} runs</span><ChevronRight size={15} aria-hidden="true" /></summary>
        <div className={styles.historyList}>{items.map(item => <button key={item.id} type="button" className={item.id === activeAssessment?.id ? styles.selectedRun : ""} aria-pressed={item.id === activeAssessment?.id} onClick={() => setActiveAssessmentId(item.id)}><span>{item.isOutdated ? "Outdated" : item.status}</span><time>{formatVietnamDateTime(item.completedAt ?? item.createdAt)}</time><small>{item.id === items[0]?.id ? "Latest report" : "Recorded report"}</small></button>)}</div>
      </details>}

      <div className={styles.workspace}>
        <div className={styles.mainColumn}>
          <section className={styles.panel}>
            <header className={styles.panelHeader}><div><p className={styles.eyebrow}>01 / CURRENT PLAN</p><h2>Risks & next actions</h2></div><span className={`${styles.badge} ${styles[tone]}`}>{result ? `${currentRisks.length} finding${currentRisks.length === 1 ? "" : "s"}` : "Not assessed"}</span></header>
            {outdated && <div className={styles.notice}><Info size={17} aria-hidden="true" /><p>{activeAssessment?.outdatedReason ?? "The plan changed after this report."} These findings describe the captured plan.</p></div>}
            {!result ? <div className={styles.empty}><ShieldAlert size={32} aria-hidden="true" /><h3>Start with the mission, not a blank report</h3><p>Run an assessment to identify plan risks, estimate departure charge and retrieve relevant flight history.</p><span>No assessment does not mean zero risk.</span></div> : currentRisks.length ? <ol className={styles.riskList}>{currentRisks.map((risk, index) => <li className={styles.riskCard} data-severity={risk.severity.toLowerCase()} key={`${risk.title}-${index}`}><div className={styles.riskTitle}><span className={styles.riskNumber}>{String(index + 1).padStart(2, "0")}</span><h3>{risk.title}</h3><span className={`${styles.badge} ${styles[risk.severity.toLowerCase()]}`}>{risk.severity}</span></div><div className={styles.riskBody}><p><b>Evidence</b>{risk.evidence}</p><div className={styles.action}><ArrowUpRight size={16} aria-hidden="true" /><p><b>Next action</b>{risk.precaution}</p></div></div>{risk.sourceIncidentIds?.map((id, i) => <Link className={styles.textLink} key={id} href={`/portal/incidents/${encodeURIComponent(id)}`}>Inspect incident {i + 1} <ArrowUpRight size={14} aria-hidden="true" /></Link>)}</li>)}</ol> : <div className={styles.empty}><FileCheck2 size={30} aria-hidden="true" /><h3>No generated risk item for the current plan</h3><p>This is not a zero-risk conclusion. Missing evidence and mandatory validation still need review.</p></div>}
            <footer className={styles.panelFooter}><Link className={styles.textLink} href={`/portal/missions/${encodeURIComponent(missionId)}`}>Review mission and validation <ArrowUpRight size={15} aria-hidden="true" /></Link><span>NO APPROVAL AUTHORITY</span></footer>
          </section>

          <section className={styles.panel}>
            <header className={styles.panelHeader}><div><p className={styles.eyebrow}>02 / EVIDENCE CHECKLIST</p><h2>What needs your attention</h2></div><FileCheck2 size={20} aria-hidden="true" /></header>
            {result ? result.missingData.length ? <ul className={styles.checklist}>{result.missingData.map((value, i) => <li key={value}><span className={styles.checkNumber}>{String(i + 1).padStart(2, "0")}</span><div><span className={styles.gapLabel}>EVIDENCE NEEDED</span><p>{value}</p></div></li>)}</ul> : <p className={styles.bodyCopy}>No missing inputs were reported at capture. Confirm the report is current and inspect live battery evidence.</p> : <p className={styles.bodyCopy}>The checklist will show the specific inputs missing from your assessment.</p>}
            {result?.precautions.length ? <details className={styles.disclosure}><summary>Recommended precautions <ChevronRight size={15} aria-hidden="true" /></summary><ul>{result.precautions.map(value => <li key={value}>{value}</li>)}</ul></details> : null}
          </section>
        </div>

        <aside className={styles.sideColumn} aria-label="Battery and mission estimates">
          {context?.id === missionId && <LiveBattery key={missionId} missionId={missionId} uavId={context.uavId} assessment={activeAssessment} />}
          <section className={styles.panel}>
            <header className={styles.panelHeader}><div><p className={styles.eyebrow}>PLANNING ESTIMATE</p><h2>Energy for this mission</h2></div><Route size={19} aria-hidden="true" /></header>
            <div className={styles.planDemand}><span>{battery?.requiredBatteryPercent == null ? "—" : battery.requiredBatteryPercent.toFixed(1)}<small>%</small></span><p>estimated departure charge<br /><b>{battery?.safetyReservePercent == null ? "Reserve not reported" : `Includes ${battery.safetyReservePercent}% reserve`}</b></p></div>
            <dl className={styles.factList}><div><dt>Route distance</dt><dd>{metric(battery?.routeDistanceMeters, "m")}</dd></div><div><dt>Estimated flight time</dt><dd>{metric(battery?.estimatedFlightMinutes, "min")}</dd></div><div><dt>Required charge</dt><dd>{metric(battery?.estimatedRequiredChargeMah, "mAh")}</dd></div><div><dt>Configured pack</dt><dd>{metric(battery?.configuredCapacityMah, "mAh")}</dd></div></dl>
            {battery && <details className={styles.disclosure}><summary>How this is calculated <ChevronRight size={15} aria-hidden="true" /></summary><dl className={styles.factList}><div><dt>Route consumption</dt><dd>{metric(battery.baseBatteryPercent, "%")}</dd></div><div><dt>Weather adjustment</dt><dd>{metric(battery.weatherAdjustmentPercent, "pp")}</dd></div><div><dt>Reserve</dt><dd>{metric(battery.safetyReservePercent, "pp")}</dd></div></dl><p className={styles.bodyCopy}>pp = percentage points. These components add up to the departure estimate.</p>{battery.reasons && <ul>{battery.reasons.map(reason => <li key={reason}>{reason}</li>)}</ul>}<p>Capacity source: {battery.capacitySource}.</p><p>FC charge at capture: {metric(battery.currentBatteryPercent, "%")}. This is a saved snapshot, not the current reading.</p>{battery.warnings.length > 0 && <ul>{battery.warnings.map(value => <li key={value}>{value}</li>)}</ul>}</details>}
            <p className={styles.cardNote}>{battery?.requiredBatteryPercent == null ? "Run an assessment with a complete route, valid cruise speed and configured UAV endurance." : outdated ? "Captured plan is outdated. Reassess before using this estimate." : "Route/endurance estimate. Actual payload, battery condition and weather can change consumption."}</p>
          </section>
          {result?.automaticFlightLimits && <section className={styles.panel}><header className={styles.panelHeader}><div><p className={styles.eyebrow}>OPERATING LIMITS</p><h2>Automatic flight envelope</h2></div></header><p className={styles.bodyCopy}>{formatAutomaticLimits(result.automaticFlightLimits)}</p><dl className={styles.factList}><div><dt>Cruise speed</dt><dd>{metric(result.cruiseSpeedMetersPerSecond, "m/s")}</dd></div><div><dt>Maximum speed</dt><dd>{metric(result.maxSpeedMetersPerSecond, "m/s")}</dd></div></dl></section>}
        </aside>
      </div>

      {result && <section className={styles.panel}>
        <header className={styles.panelHeader}><div><p className={styles.eyebrow}>03 / RECORDED HISTORICAL EVIDENCE</p><h2>Learn from similar flights</h2><p>Past incidents provide context. They are not detections on this mission.</p></div><span className={styles.badge}>{historicalFlights.length} matches</span></header>
        {historicalRisks.length > 0 && <div className={styles.patternList}>{historicalRisks.map((risk, i) => <article key={`${risk.title}-${i}`}><span className={`${styles.badge} ${styles.moderate}`}>Historical pattern</span><h3>{risk.title}</h3><p>{risk.evidence}</p><p><b>Review:</b> {risk.precaution}</p>{risk.sourceIncidentIds?.map((id, index) => <Link className={styles.textLink} key={id} href={`/portal/incidents/${encodeURIComponent(id)}`}>Incident {index + 1} <ArrowUpRight size={13} aria-hidden="true" /></Link>)}</article>)}</div>}
        {historicalFlights.length === 0 ? <div className={styles.empty}><Database size={28} aria-hidden="true" /><h3>No historical evidence</h3><p>No authorized matching flight was returned. This does not mean zero risk; continue manual review.</p></div> : <div className={styles.flightList}>{historicalFlights.map(flight => <details className={styles.flight} key={flight.missionId}><summary><span className={styles.flightIcon}><Route size={17} aria-hidden="true" /></span><span className={styles.flightName}>{flight.missionName}<small>{flight.incidentIds.length} linked incident{flight.incidentIds.length === 1 ? "" : "s"}</small></span><span className={styles.match}>{(flight.similarityScore * 100).toFixed(1)}%<small>similarity</small></span><ChevronRight size={16} aria-hidden="true" /></summary><div className={styles.flightDetail}><dl className={styles.flightFeatures}>{Object.entries(flight.features).map(([key, value]) => <div key={key}><dt>{featureLabels[key]?.label ?? key}</dt><dd>{featureValue(key, value)}</dd></div>)}</dl><div className={styles.links}><Link className={styles.textLink} href={`/portal/flight-history/${encodeURIComponent(flight.missionId)}`}>View flight evidence <ArrowUpRight size={14} aria-hidden="true" /></Link>{flight.incidentIds.map((incidentId, index) => <Link className={styles.textLink} key={incidentId} aria-label={`Open linked incident ${index + 1}`} href={`/portal/incidents/${encodeURIComponent(incidentId)}`}>Incident {String(index + 1).padStart(2, "0")} <ArrowUpRight size={14} aria-hidden="true" /></Link>)}</div></div></details>)}</div>}
        <footer className={styles.explainer}><Info size={16} aria-hidden="true" /><p>Similarity compares recorded flight features. <b>An 80% match does not mean 80% safe.</b> Missing features reduce the evidence available for comparison.</p></footer>
      </section>}

      {activeAssessment && <details className={styles.reportDetails}><summary>Report provenance & methodology <ChevronRight size={15} aria-hidden="true" /></summary><dl className={styles.factList}><div><dt>Provider</dt><dd>{readableIdentifier(activeAssessment.provider)}</dd></div><div><dt>Method</dt><dd>{readableIdentifier(activeAssessment.model)}</dd></div><div><dt>Retrieval</dt><dd>{result?.retrieval?.method ?? "Unavailable"}</dd></div><div><dt>Feature schema</dt><dd>{result?.retrieval?.featureSchema ?? "Unavailable"}</dd></div><div><dt>Mission version</dt><dd>{activeAssessment.missionVersion}</dd></div><div><dt>Captured at</dt><dd>{formatVietnamDateTime(activeAssessment.completedAt ?? activeAssessment.createdAt)}</dd></div></dl><p>{result?.summary ?? activeAssessment.errorCode ?? "The selected assessment has no result payload."}</p><p>{result?.disclaimer ?? "AI assessment is advisory and cannot approve a mission."}</p></details>}
      <footer className={styles.pageFooter}><ShieldAlert size={14} aria-hidden="true" /><span>Assessments use the saved mission. Pending proposals, operational approval and live flight evidence remain separate.</span></footer>
    </>}
  </div>;
}
