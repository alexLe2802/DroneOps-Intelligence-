"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getFlightHistory, getLatestMissionValidation, getMissionValidationRun } from "@/lib/portal/client";
import type { FlightHistoryItem, MissionValidationResult } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import styles from "./validation-results.module.css";

export default function ValidationResultsClient({ missionId, runId }: { missionId?: string; runId?: string }) {
  const [result, setResult] = useState<MissionValidationResult | null>(null);
  const [missions, setMissions] = useState<FlightHistoryItem[]>([]);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    setBusy(true);
    setError(null);
    const load = async () => {
      if (!missionId) {
        const history = await getFlightHistory({ pageSize: 100 });
        if (active) setMissions(history.items);
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
  }, [missionId, runId, attempt]);

  if (busy) return <ModuleLoading label="Loading validation results" />;
  if (error) return <ModuleError error={error} retry={() => setAttempt(value => value + 1)} />;

  return <>
    <header className="module-heading"><div><h1>Validation Results</h1><p>{runId ? "Saved result from the selected validation run." : "Review the latest mission validation."}</p></div>
      {missionId && <Link className="module-secondary module-link" href={`/portal/missions/${encodeURIComponent(missionId)}`}>Back to mission</Link>}
    </header>
    {!missionId ? <section className={styles.panel}><h2>Select a mission</h2>
      {missions.length ? <ul className={styles.missions}>{missions.map(mission => <li key={mission.missionId}><Link href={`/portal/validation-results?missionId=${encodeURIComponent(mission.missionId)}`}>{mission.missionName}</Link><span>{mission.status}</span></li>)}</ul>
        : <ModuleEmpty title="No missions" message="Mission validation results will appear here after a mission is created." />}
    </section> : !result ? <ModuleEmpty title="No validation result" message="This mission has no saved validation result yet." /> : <>
      <section className={styles.panel}>
        <div className={styles.summary}><h2>Flight readiness: {result.status === "Passed" ? "Pass" : result.status ?? (result.isValid ? "Pass" : "Blocked")}</h2><span>{result.stage}</span></div>
        <p>{result.message.replace(/\s*Weather warnings are advisory\./gi, "").trim()}</p>
        <dl className={styles.metadata}><div><dt>Checked</dt><dd>{result.checkedAt ? new Date(result.checkedAt).toLocaleString() : "Not recorded"}</dd></div><div><dt>Validation ID</dt><dd>{result.id ?? "Not recorded"}</dd></div><div><dt>Mission ID</dt><dd>{missionId}</dd></div></dl>
        {!runId && result.id && <Link className="module-link" href={`/portal/validation-results?missionId=${encodeURIComponent(missionId)}&runId=${encodeURIComponent(result.id)}`}>Link to this validation run</Link>}
      </section>
      <section className={styles.panel}><h2>Mandatory checks</h2>{result.issues?.length ? <ul>{[...new Set(result.issues)].map(issue => <li key={issue}>{issue}</li>)}</ul> : <p>No blocking issues were recorded.</p>}</section>
      <section className={styles.panel}><h2>Advisories</h2>{result.warnings?.length ? <ul>{[...new Set(result.warnings)].map(warning => <li key={warning}>{warning}</li>)}</ul> : <p>No advisories were recorded.</p>}</section>
      {result.weather && <section className={styles.panel}><h2>Weather at validation</h2><p>{result.weather.provider} · {result.weather.status}</p><div className={styles.weather}><span>Max wind <b>{result.weather.maxWindMetersPerSecond ?? "—"} m/s</b></span><span>Max gust <b>{result.weather.maxGustMetersPerSecond ?? "—"} m/s</b></span><span>Rain <b>{result.weather.maxRainMillimetersPerHour ?? "—"} mm/h</b></span></div><p>Weather is advisory. These values are saved with this validation run.</p></section>}
    </>}
  </>;
}
