"use client";

import {
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  ClipboardCheck,
  CloudSun,
  Info,
  Search,
  ShieldAlert,
  TriangleAlert,
} from "lucide-react";
import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import type { AccountRole } from "@/lib/auth/types";
import { getLatestMissionValidation, getMissionValidationRun, getValidationMissionOptions } from "@/lib/portal/client";
import type { ValidationMissionOption } from "@/lib/portal/client";
import type { MissionValidationResult } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import styles from "./validation-results.module.css";

function normalizeStatus(value?: string) {
  return (value || "Unknown").replace(/([a-z])([A-Z])/g, "$1 $2").trim();
}

function missionStatusClass(value?: string) {
  const status = normalizeStatus(value).toLowerCase();
  if (status === "approved" || status === "completed") return styles.statusReady;
  if (status === "rejected" || status === "cancelled") return styles.statusBlocked;
  if (status.includes("pending") || status === "submitted") return styles.statusPending;
  if (status === "in progress") return styles.statusActive;
  return styles.statusNeutral;
}

function unique(values?: string[]) {
  return [...new Set(values ?? [])];
}

export default function ValidationResultsClient({ role, missionId, runId }: { role: AccountRole; missionId?: string; runId?: string }) {
  const [result, setResult] = useState<MissionValidationResult | null>(null);
  const [missions, setMissions] = useState<ValidationMissionOption[]>([]);
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    const load = async () => {
      if (!missionId) {
        const availableMissions = await getValidationMissionOptions(role);
        if (active) setMissions(availableMissions);
      } else if (runId) {
        const record = await getMissionValidationRun(missionId, runId);
        if (active) setResult(record);
      } else {
        const latest = await getLatestMissionValidation(missionId);
        if (active) setResult(latest);
      }
    };
    void load().catch(reason => { if (active) setError(reason); })
      .finally(() => { if (active) setBusy(false); });
    return () => { active = false; };
  }, [role, missionId, runId, attempt]);

  const filteredMissions = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return missions;
    return missions.filter(mission => `${mission.name} ${mission.id} ${mission.status}`.toLowerCase().includes(term));
  }, [missions, query]);

  if (busy) return <ModuleLoading label="Loading validation results" />;
  if (error) return <ModuleError error={error} retry={() => { setBusy(true); setError(null); setAttempt(value => value + 1); }} />;

  if (!missionId) return <main className={styles.page}>
    <header className={styles.pageHeading}>
      <div><p className="module-kicker">MISSION ASSURANCE / VALIDATION LEDGER</p><h1>Validation Results</h1><span>Review mandatory flight checks and saved validation evidence for missions in your authorized scope.</span></div>
      <div className={styles.scope}><small>ACCESS SCOPE</small><strong>{role === "operation_manager" ? "OPERATIONS MANAGER" : "UAV OPERATOR"}</strong><span>{missions.length} mission{missions.length === 1 ? "" : "s"} available</span></div>
    </header>

    <section className={styles.registry} aria-labelledby="validation-registry-title">
      <header>
        <div><span>MISSION REGISTRY</span><h2 id="validation-registry-title">Select a validation record</h2></div>
        <label className={styles.search}><Search size={16} aria-hidden="true" /><span className={styles.srOnly}>Search missions</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder="Search mission name, ID or status" /></label>
      </header>
      {missions.length === 0 ? <ModuleEmpty title="No missions" message={role === "operation_manager" ? "No mission records are available for validation review." : "Create a mission to review its validation results here."} />
        : filteredMissions.length === 0 ? <div className={styles.noResults}><Search size={22} /><strong>No matching missions</strong><span>Try another mission name, ID or status.</span></div>
          : <ol className={styles.missions}>{filteredMissions.map((mission, index) => <li key={mission.id}>
            <span className={styles.sequence}>{String(index + 1).padStart(2, "0")}</span>
            <div className={styles.missionIdentity}><h3>{mission.name}</h3><code>{mission.id}</code></div>
            <span className={`${styles.missionStatus} ${missionStatusClass(mission.status)}`}>{normalizeStatus(mission.status)}</span>
            <Link className={styles.openAction} href={`/portal/validation-results?missionId=${encodeURIComponent(mission.id)}`}>OPEN VALIDATION <ArrowRight size={15} /></Link>
          </li>)}</ol>}
    </section>
  </main>;

  if (!result) return <main className={styles.page}>
    <nav className={styles.detailNav}><Link href="/portal/validation-results"><ArrowLeft size={15} /> VALIDATION REGISTRY</Link><Link href={`/portal/missions/${encodeURIComponent(missionId)}`}>OPEN MISSION <ArrowRight size={15} /></Link></nav>
    <ModuleEmpty title="No validation result" message="This mission has no saved validation result yet. Validate the mission plan first, then return here." />
  </main>;

  const issues = unique(result.issues);
  const warnings = unique(result.warnings);
  const passed = result.outcome === "Pass" || result.isValid;
  const readiness = result.outcome ?? (passed ? "Pass" : "Fail");
  const message = result.message.replace(/\s*Weather warnings are advisory\./gi, "").trim();

  return <main className={styles.page}>
    <nav className={styles.detailNav} aria-label="Validation navigation">
      <Link href="/portal/validation-results"><ArrowLeft size={15} /> VALIDATION REGISTRY</Link>
      <Link href={`/portal/missions/${encodeURIComponent(missionId)}`}>OPEN MISSION <ArrowRight size={15} /></Link>
    </nav>

    <header className={`${styles.readiness} ${passed ? styles.readinessPassed : styles.readinessBlocked}`}>
      <span className={styles.readinessIcon}>{passed ? <CheckCircle2 size={27} /> : <ShieldAlert size={27} />}</span>
      <div className={styles.readinessCopy}><p>FLIGHT READINESS / {runId ? "SAVED RUN" : "LATEST CHECK"}</p><h1>{passed ? "Cleared for operational review" : "Action required before flight"}</h1><span>{message || (passed ? "All mandatory validation checks passed." : "One or more mandatory checks require attention.")}</span></div>
      <div className={styles.readinessState}><small>VALIDATION STATE</small><strong>{normalizeStatus(readiness).toUpperCase()}</strong><span>{result.stage || "Pre-flight"}</span></div>
    </header>

    <dl className={styles.evidenceMeta}>
      <div><dt>MISSION ID</dt><dd>{missionId}</dd></div>
      <div><dt>VALIDATION ID</dt><dd>{result.id ?? "Not recorded"}</dd></div>
      <div><dt>CHECKED AT</dt><dd>{result.checkedAt ? new Date(result.checkedAt).toLocaleString() : "Not recorded"}</dd></div>
      <div><dt>EVIDENCE</dt><dd>{runId ? "Immutable saved run" : "Latest available result"}</dd></div>
    </dl>

    <section className={styles.findingsGrid}>
      <article className={`${styles.findingPanel} ${issues.length ? styles.findingDanger : styles.findingClear}`}>
        <header><div>{issues.length ? <TriangleAlert size={18} /> : <CheckCircle2 size={18} />}<span>MANDATORY CHECKS</span></div><b>{issues.length ? `${issues.length} BLOCKING` : "CLEAR"}</b></header>
        {issues.length ? <ol>{issues.map((issue, index) => <li key={issue}><span>{String(index + 1).padStart(2, "0")}</span><p>{issue}</p></li>)}</ol>
          : <div className={styles.clearState}><ClipboardCheck size={27} /><div><strong>No blocking issues</strong><span>Route, aircraft and operational rules passed the mandatory checks recorded in this run.</span></div></div>}
      </article>

      <article className={`${styles.findingPanel} ${warnings.length ? styles.findingWarning : styles.findingClear}`}>
        <header><div><Info size={18} /><span>OPERATIONAL ADVISORIES</span></div><b>{warnings.length ? `${warnings.length} TO REVIEW` : "NONE"}</b></header>
        {warnings.length ? <ol>{warnings.map((warning, index) => <li key={warning}><span>{String(index + 1).padStart(2, "0")}</span><p>{warning}</p></li>)}</ol>
          : <div className={styles.clearState}><CheckCircle2 size={27} /><div><strong>No advisories recorded</strong><span>No non-blocking warnings were attached to this validation run.</span></div></div>}
      </article>
    </section>

    {result.weather && <section className={styles.weatherPanel}>
      <header><div><CloudSun size={20} /><span>WEATHER EVIDENCE</span><b>{result.weather.provider}</b></div><strong>{result.weather.status.toUpperCase()}</strong></header>
      <div className={styles.weatherMetrics}>
        <div><span>OVERALL RISK</span><b>{result.weather.overallRisk || "Not rated"}</b></div>
        <div><span>MAX WIND</span><b>{result.weather.maxWindMetersPerSecond ?? "N/A"}<small>{result.weather.maxWindMetersPerSecond == null ? "" : " m/s"}</small></b></div>
        <div><span>MAX GUST</span><b>{result.weather.maxGustMetersPerSecond ?? "N/A"}<small>{result.weather.maxGustMetersPerSecond == null ? "" : " m/s"}</small></b></div>
        <div><span>MAX RAIN</span><b>{result.weather.maxRainMillimetersPerHour ?? "N/A"}<small>{result.weather.maxRainMillimetersPerHour == null ? "" : " mm/h"}</small></b></div>
        <div><span>THUNDERSTORM</span><b>{result.weather.thunderstormForecast ? "FORECAST" : "NOT DETECTED"}</b></div>
      </div>
      <p><Info size={14} /> Weather evidence is advisory and was saved at the time of validation.</p>
    </section>}

    {!runId && result.id && <div className={styles.archiveAction}><div><span>VALIDATION ARCHIVE</span><p>Open the immutable URL for this exact validation run.</p></div><Link href={`/portal/validation-results?missionId=${encodeURIComponent(missionId)}&runId=${encodeURIComponent(result.id)}`}>OPEN SAVED RUN <ArrowRight size={15} /></Link></div>}
  </main>;
}
