"use client";

import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { AccountRole } from "@/lib/auth/types";
import { getFlightTelemetry, getPostFlightIncidents, getPostFlightReport, getPostFlightReportRevisions, regeneratePostFlightReport } from "@/lib/portal/client";
import type { FlightTelemetryPoint, PostFlightIncident, PostFlightReport } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import FlightPathMap from "./flight-path-map";
import styles from "../flight-history.module.css";

const dateTime = (value: string | null) => value ? new Date(value).toLocaleString("en-GB") : "Not available";
const metric = (value: number | null, unit: string, digits = 1) => value == null ? "N/A" : `${value.toFixed(digits)} ${unit}`;
const duration = (minutes: number) => minutes >= 60 ? `${Math.floor(minutes / 60)}h ${Math.round(minutes % 60)}m` : `${Math.round(minutes)}m`;
const label = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2");

type ReportData = { report: PostFlightReport; revisions: PostFlightReport[]; telemetry: FlightTelemetryPoint[]; incidents: PostFlightIncident[] };

export default function PostFlightReportClient({ missionId, role }: { missionId: string; role: AccountRole }) {
  const [data, setData] = useState<ReportData | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");
  const [reason, setReason] = useState("");
  const [dialogOpen, setDialogOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      const report = await getPostFlightReport(missionId);
      const [revisions, telemetry, incidents] = await Promise.all([
        getPostFlightReportRevisions(missionId).catch(() => [report]),
        getFlightTelemetry(missionId).catch(() => []),
        getPostFlightIncidents(missionId).catch(() => []),
      ]);
      setData({ report, revisions, telemetry, incidents });
    } catch (reasonValue) { setError(reasonValue); }
  }, [missionId]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);
  useEffect(() => {
    const status = data?.report.generationStatus;
    if (status !== "Queued" && status !== "Generating") return;
    const timer = window.setInterval(() => void load(), 5_000);
    return () => window.clearInterval(timer);
  }, [data?.report.generationStatus, load]);
  useEffect(() => {
    if (!dialogOpen) return;
    const close = (event: KeyboardEvent) => { if (event.key === "Escape" && !pending.current) setDialogOpen(false); };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [dialogOpen]);

  const telemetryWindow = useMemo(() => data?.telemetry.slice(-8).reverse() ?? [], [data?.telemetry]);

  async function regenerate() {
    if (!reason.trim() || pending.current) return;
    pending.current = true; setBusy(true); setError(null); setNotice("");
    try {
      const response = await regeneratePostFlightReport(missionId, reason);
      setData(current => current ? { ...current, report: response.data, revisions: [response.data, ...current.revisions] } : current);
      setNotice(response.message || "Report regeneration queued.");
      setReason(""); setDialogOpen(false);
    } catch (reasonValue) { setError(reasonValue); setDialogOpen(false); }
    finally { pending.current = false; setBusy(false); }
  }

  if (error && !data) return <ModuleError error={error} retry={() => void load()} />;
  if (!data) return <ModuleLoading label="Loading post-flight report" />;
  const { report, revisions, telemetry, incidents } = data;
  const available = report.generationStatus === "Available";
  const coverage = report.totalTelemetryRecords > 0 && report.coveredTelemetryThrough;

  return <div className={styles.reportPage}>
    <header className={styles.reportHeading}>
      <div><Link className={styles.reportBackButton} href="/portal/flight-history"><ArrowLeft size={15} strokeWidth={2} aria-hidden="true" /> BACK TO FLIGHT HISTORY</Link><p>POST-FLIGHT DEBRIEF // REVISION {report.revision}</p><h1>{report.missionName}</h1><span>{report.missionDescription || "No mission description was recorded."}</span></div>
      <div className={styles.reportHeadingActions}><span className={`${styles.generationState} ${styles[report.generationStatus.toLowerCase()] ?? ""}`}>{label(report.generationStatus)}</span>{role === "operation_manager" && <button type="button" onClick={() => setDialogOpen(true)} disabled={busy}>REGENERATE REPORT</button>}</div>
    </header>

    {!available && <section className={`${styles.reportNotice} ${report.generationStatus === "Failed" ? styles.noticeDanger : ""}`} role="status"><b>REPORT {report.generationStatus.toUpperCase()}</b><span>{report.failureReason || "This report is not currently available as a finalized snapshot."}</span></section>}
    {notice && <section className={styles.reportNotice} role="status"><b>REQUEST ACCEPTED</b><span>{notice}</span><button type="button" onClick={() => setNotice("")}>DISMISS</button></section>}
    {Boolean(error) && <section className={`${styles.reportNotice} ${styles.noticeDanger}`} role="alert"><b>REQUEST FAILED</b><span>{error instanceof Error ? error.message : "Unable to process this report."}</span><button type="button" onClick={() => setError(null)}>DISMISS</button></section>}

    <section className={styles.reportMetrics} aria-label="Flight summary">
      <article><span>FLIGHT DURATION</span><strong>{duration(report.totalFlightDurationMinutes)}</strong><small>{dateTime(report.startTime)} to {dateTime(report.endTime)}</small></article>
      <article><span>TRACK DISTANCE</span><strong>{report.totalDistanceMeters == null ? "N/A" : `${(report.totalDistanceMeters / 1000).toFixed(2)} km`}</strong><small>{report.totalTelemetryRecords} telemetry records</small></article>
      <article><span>MAX ALTITUDE</span><strong>{metric(report.maxAltitude, "m")}</strong><small>Average {metric(report.avgAltitude, "m")}</small></article>
      <article><span>BATTERY USE</span><strong>{report.batteryConsumedPercentage == null ? "N/A" : `${report.batteryConsumedPercentage}%`}</strong><small>{report.initialBatteryLevel == null ? "N/A" : `${report.initialBatteryLevel}%`} to {report.finalBatteryLevel == null ? "N/A" : `${report.finalBatteryLevel}%`}</small></article>
      <article><span>INCIDENT RECORDS</span><strong className={report.totalIncidents ? styles.metricAlert : ""}>{report.totalIncidents}</strong><small>{report.safetyEvaluation || "No safety evaluation"}</small></article>
    </section>

    <section className={styles.reportGrid}>
      <div className={styles.reportMain}>
        <article className={styles.mapPanel}><header><div><span>TELEMETRY PLAYBACK</span><h2>Recorded Flight Path</h2></div><b>{coverage ? `COVERED THROUGH ${dateTime(report.coveredTelemetryThrough)}` : "COVERAGE UNAVAILABLE"}</b></header><FlightPathMap telemetry={telemetry} uavCode={report.uavCode} /></article>
        <article className={styles.summaryPanel}><header><span>GENERATED DEBRIEF</span><h2>Report Summary</h2></header><p>{report.summary || "The generated report does not include a narrative summary."}</p><dl><div><dt>Safety evaluation</dt><dd>{report.safetyEvaluation || "Not evaluated"}</dd></div><div><dt>Generation reason</dt><dd>{report.generationReason || "Not recorded"}</dd></div><div><dt>Generated at</dt><dd>{dateTime(report.generatedAt)}</dd></div><div><dt>Notes</dt><dd>{report.notes || "No report notes"}</dd></div></dl></article>
        <article className={styles.telemetryPanel}><header><span>RECENT TELEMETRY SAMPLES</span><b>{telemetry.length} TOTAL</b></header>{telemetryWindow.length ? <div><table><thead><tr><th>RECORDED</th><th>POSITION</th><th>ALTITUDE</th><th>SPEED</th><th>BATTERY</th></tr></thead><tbody>{telemetryWindow.map((point, index) => <tr key={`${point.recordedAt}-${index}`}><td>{dateTime(point.recordedAt)}</td><td>{point.latitude == null || point.longitude == null ? "N/A" : `${point.latitude.toFixed(5)}, ${point.longitude.toFixed(5)}`}</td><td>{metric(point.altitude, "m")}</td><td>{metric(point.speed, "m/s")}</td><td>{point.batteryLevel == null ? "N/A" : `${point.batteryLevel}%`}</td></tr>)}</tbody></table></div> : <p>No telemetry samples were returned for this flight.</p>}</article>
      </div>

      <aside className={styles.reportAside}>
        <article className={styles.identityPanel}><header><span>FLIGHT RECORD</span><b>MS-{report.missionId.slice(0, 8).toUpperCase()}</b></header><dl><div><dt>AIRFRAME</dt><dd>{report.uavCode || "N/A"}<small>{report.uavName || "Aircraft unavailable"}</small></dd></div><div><dt>PILOT IN COMMAND</dt><dd>{report.pilotName || "N/A"}</dd></div><div><dt>MISSION STATUS</dt><dd>{report.status}</dd></div><div><dt>REPORT ID</dt><dd>{report.reportId}</dd></div></dl></article>
        <article className={styles.incidentPanel}><header><span>INCIDENT CROSS-REFERENCES</span><b>{incidents.length}</b></header>{incidents.length ? <ul>{incidents.map(incident => <li key={incident.id}><div><span className={styles[incident.severity?.toLowerCase() ?? ""]}>{incident.severity || "Unrated"}</span><b>{incident.title}</b><small>{incident.type}{" // "}{dateTime(incident.createdAt)}</small></div><Link href={`/portal/incidents/${encodeURIComponent(incident.id)}`}>OPEN</Link></li>)}</ul> : <p>No incidents are associated with this flight.</p>}</article>
        <article className={styles.revisionPanel}><header><span>IMMUTABLE REPORT REVISIONS</span><b>{revisions.length}</b></header><ol>{revisions.map(revision => <li key={revision.reportId} className={revision.isCurrent ? styles.currentRevision : ""}><div><b>REV {revision.revision}</b><span>{revision.generationStatus}</span></div><time>{dateTime(revision.createdAt)}</time><small>{revision.generationReason || "No generation reason"}</small></li>)}</ol></article>
      </aside>
    </section>

    {dialogOpen && <div className={styles.dialogBackdrop} role="presentation"><section className={styles.dialog} role="dialog" aria-modal="true" aria-labelledby="regenerate-title"><span>MANAGER ACTION</span><h2 id="regenerate-title">Regenerate post-flight report?</h2><p>A new immutable revision will be queued. Existing report revisions remain available.</p><label>REGENERATION REASON<textarea rows={4} maxLength={500} value={reason} onChange={event => setReason(event.target.value)} placeholder="Explain why a new report snapshot is required" /></label><div><button type="button" disabled={busy} onClick={() => setDialogOpen(false)}>CANCEL</button><button type="button" disabled={busy || !reason.trim()} onClick={() => void regenerate()}>{busy ? "QUEUING..." : "QUEUE NEW REVISION"}</button></div></section></div>}
  </div>;
}
