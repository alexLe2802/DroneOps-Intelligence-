"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import type { Account } from "@/lib/auth/types";
import { getIncident } from "@/lib/portal/client";
import type { IncidentDetail } from "@/lib/portal/types";
import IncidentMap from "../incident-map";
import styles from "./incident-detail.module.css";

type EvidenceRecord = Record<string, unknown>;

const NOTE_LIMIT = 500;
const NOTE_TEMPLATES = [
  "MAVLink connection restored and telemetry confirmed stable.",
  "Return-to-home procedure activated and aircraft recovery monitored.",
  "Manual override established by the assigned operator.",
] as const;

const keyOf = (value: string | null | undefined) => (value ?? "unknown").replace(/\s+/g, "").toLowerCase();
const labelOf = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/_/g, " ");

function formatDate(value: string | null | undefined) {
  if (!value) return "Unavailable";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Unavailable";
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    timeZoneName: "short",
  }).format(date);
}

function elapsedSince(value: string, now: number) {
  const elapsed = Math.max(0, now - new Date(value).getTime());
  const seconds = Math.floor(elapsed / 1000);
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainder = seconds % 60;
  return `${days ? `${days}D ` : ""}${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(remainder).padStart(2, "0")}`;
}

function asEvidence(value: unknown): EvidenceRecord {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as EvidenceRecord : {};
}

function valueOf(evidence: EvidenceRecord, key: string): unknown {
  return Object.prototype.hasOwnProperty.call(evidence, key) ? evidence[key] : null;
}

function numberOf(evidence: EvidenceRecord, key: string): number | null {
  const value = valueOf(evidence, key);
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string" && value.trim() && Number.isFinite(Number(value))) return Number(value);
  return null;
}

function textOf(evidence: EvidenceRecord, key: string): string | null {
  const value = valueOf(evidence, key);
  return typeof value === "string" && value.trim() ? value : null;
}

function metric(value: number | null, suffix: string, digits = 1) {
  return value === null ? "Not captured" : `${value.toFixed(digits)}${suffix}`;
}

function evidenceEntries(evidence: EvidenceRecord) {
  return Object.entries(evidence).filter(([, value]) => value !== null && value !== undefined).slice(0, 14).map(([key, value]) => [
    labelOf(key),
    typeof value === "object" ? JSON.stringify(value) : String(value),
  ] as const);
}

export default function IncidentDetailClient({ id, account }: { id: string; account: Account }) {
  const [incident, setIncident] = useState<IncidentDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [refreshToken, setRefreshToken] = useState(0);
  const [clock, setClock] = useState(() => Date.now());
  const [resolutionDraft, setResolutionDraft] = useState("");

  useEffect(() => {
    const timer = window.setInterval(() => setClock(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    let active = true;
    const task = window.setTimeout(() => {
      setLoading(true);
      setError("");
      void getIncident(id).then(result => {
        if (active) {
          setIncident(result);
          setResolutionDraft(result.resolutionNotes ?? "");
        }
      }).catch(reason => {
        if (!active) return;
        setIncident(null);
        setError(reason instanceof Error ? reason.message : "Unable to load incident details.");
      }).finally(() => { if (active) setLoading(false); });
    }, 0);
    return () => { active = false; window.clearTimeout(task); };
  }, [id, refreshToken]);

  const evidence = useMemo(() => asEvidence(incident?.evidence), [incident?.evidence]);
  const entries = useMemo(() => evidenceEntries(evidence), [evidence]);

  if (loading) return <DetailState title="RETRIEVING INCIDENT RECORD" description="Loading telemetry evidence and handling history." loading />;
  if (error || !incident) return <DetailState title="INCIDENT RECORD UNAVAILABLE" description={error || "The backend returned no incident record."} action={<button type="button" onClick={() => setRefreshToken(value => value + 1)}>RETRY REQUEST</button>} />;

  const severity = keyOf(incident.severity);
  const status = keyOf(incident.status);
  const latitude = numberOf(evidence, "latitude") ?? incident.latitude;
  const longitude = numberOf(evidence, "longitude") ?? incident.longitude;
  const altitude = numberOf(evidence, "altitude");
  const speed = numberOf(evidence, "speed");
  const battery = numberOf(evidence, "batteryPercent");
  const voltage = numberOf(evidence, "batteryVoltageVolts");
  const heading = numberOf(evidence, "headingDegrees");
  const temperature = numberOf(evidence, "boardTemperatureCelsius");
  const flightMode = textOf(evidence, "flightMode");
  const sourceId = textOf(evidence, "sourceId");
  const sourceTimestamp = incident.sourceTimestamp ?? textOf(evidence, "sourceTimestamp");
  const receivedAt = incident.receivedAt ?? textOf(evidence, "receivedAt");
  const handlingState = status === "open" ? "Awaiting operator or manager acknowledgement" : status === "acknowledged" ? "Acknowledged and under operational review" : "Resolution recorded; incident history is read-only";
  const canDraftResolution = account.role === "operation_manager" && status !== "resolved";
  const trimmedDraft = resolutionDraft.trim();

  function applyNoteTemplate(template: string) {
    if (!canDraftResolution) return;
    setResolutionDraft(current => {
      const lines = current.split(/\r?\n/);
      const templateApplied = lines.some(line => line.trim() === template);
      if (templateApplied) return lines.filter(line => line.trim() !== template).join("\n").replace(/\n{3,}/g, "\n\n").trim();
      const normalized = current.trim();
      const next = normalized ? `${normalized}\n${template}` : template;
      return next.slice(0, NOTE_LIMIT);
    });
  }

  return <div className={styles.detailPage}>
    <section className={`${styles.alertBand} ${styles[`alert${severity}`] ?? ""}`}>
      <div className={styles.alertIdentity}><i /><strong>{incident.severity.toUpperCase()} ALERT <em>{"//"}</em></strong></div>
      <div className={styles.alertState}><span>STATE:</span><b>{incident.status.toUpperCase()}</b></div>
      <p>{handlingState}</p>
      <div className={styles.elapsed}><span>ELAPSED</span><b>{elapsedSince(incident.createdAt, clock)}</b></div>
      <div className={styles.severityTier}>{incident.severity.toUpperCase()} TIER</div>
    </section>

    <header className={styles.titleBlock}>
      <div>
        <div className={styles.breadcrumb}><Link className={styles.backButton} href="/portal/incidents" aria-label="Back to incident list"><span aria-hidden="true">&larr;</span> INCIDENTS</Link><span>/</span><b>{incident.id.toUpperCase()}</b></div>
        <h1>{incident.title}</h1>
        <p>{incident.description ?? "Immutable telemetry evidence and operational handling record."}</p>
      </div>
      <div className={styles.identityChips}>
        <span><i aria-hidden="true">MS</i>MISSION <b>{incident.missionName || incident.missionId}</b></span>
        <span><i aria-hidden="true">UA</i>AIRFRAME <b>{incident.uavCode ?? "UNASSIGNED"}</b></span>
        <span><i aria-hidden="true">EV</i>TYPE <b>{labelOf(incident.type)}</b></span>
        <button type="button" onClick={() => setRefreshToken(value => value + 1)}>REFRESH RECORD</button>
      </div>
    </header>

    <div className={styles.investigationGrid}>
      <section className={styles.telemetryPanel}>
        <PanelHeader label="TELEMETRY FREEZE-FRAME" value="TRIGGER SNAPSHOT" />
        <div className={styles.triggerTime}><span>EXACT TRIGGER TIMESTAMP <small>FR-16.1</small></span><b>{formatDate(sourceTimestamp ?? incident.createdAt)}</b></div>
        <div className={styles.metricGrid}>
          <MetricCard label="ALTITUDE" value={metric(altitude, " m AGL")} tone="cyan" />
          <MetricCard label="GROUND SPEED" value={metric(speed, " m/s")} tone="green" />
          <MetricCard label="BATTERY STATE" value={metric(battery, "%", 0)} subValue={voltage === null ? undefined : `${voltage.toFixed(1)}V`} tone={battery !== null && battery <= 20 ? "danger" : "green"} />
          <MetricCard label="HEADING" value={metric(heading, " deg", 0)} tone="violet" />
          <MetricCard label="BOARD TEMPERATURE" value={metric(temperature, " C")} tone="amber" />
          <MetricCard label="FLIGHT MODE" value={flightMode ?? "Not captured"} tone="cyan" />
        </div>
        <div className={styles.positionReadout}><span>LAST KNOWN FIX</span><b>{latitude !== null && longitude !== null ? `${latitude.toFixed(5)} N, ${longitude.toFixed(5)} E` : "Position not captured"}</b><small>SOURCE: {sourceId ?? "Unavailable"}</small></div>
        <div className={styles.rulePanel}><span>APPLIED MONITORING RULE</span><b>{incident.appliedRule ?? "Rule unavailable"}</b><p>Threshold: {incident.threshold ?? "Not defined"}</p></div>
        <div className={styles.streamPanel}>
          <header><span>TELEMETRY EVIDENCE BUFFER</span><b>{entries.length} FIELDS</b></header>
          <div>{entries.length ? entries.map(([label, value]) => <p key={label}><span>{label}</span><code>{value}</code></p>) : <p className={styles.noEvidence}>No structured evidence was stored for this incident.</p>}</div>
        </div>
      </section>

      <section className={styles.spatialPanel}>
        <PanelHeader label="SPATIAL INCIDENT CONTEXT" value={latitude !== null && longitude !== null ? "GPS FIX AVAILABLE" : "NO GPS FIX"} />
        <IncidentMap latitude={latitude} longitude={longitude} uavCode={incident.uavCode} />
        <div className={styles.mapLegend}><span><i />LAST KNOWN FIX</span><span>WGS84 / INCIDENT SNAPSHOT</span></div>
        <div className={styles.spatialFacts}>
          <div><span>LATITUDE</span><b>{latitude?.toFixed(6) ?? "Unavailable"}</b></div>
          <div><span>LONGITUDE</span><b>{longitude?.toFixed(6) ?? "Unavailable"}</b></div>
          <div><span>SOURCE TIME</span><b>{formatDate(sourceTimestamp)}</b></div>
          <div><span>RECEIVED AT</span><b>{formatDate(receivedAt)}</b></div>
        </div>
        <div className={styles.integrityStrip}><i /><span>SPATIAL EVIDENCE IS READ-ONLY AND PRESERVED WITH THE INCIDENT RECORD</span></div>
      </section>

      <aside className={styles.actionPanel}>
        <PanelHeader label="RESOLUTION & ACTION LOG" value={status === "resolved" ? "CLOSED" : "ACTION REQUIRED"} />
        <div className={styles.timeline}>
          <h2>APPEND-ONLY INCIDENT TIMELINE</h2>
          <TimelineItem time={incident.createdAt} label="SYSTEM" title="Incident generated" text={incident.description ?? `${labelOf(incident.type)} event detected.`} done />
          <TimelineItem time={incident.acknowledgedAt} label="ACK" title={incident.acknowledgedAt ? `Acknowledged by ${incident.acknowledgedByName ?? "authorized operator"}` : "Awaiting acknowledgement"} text={incident.acknowledgedAt ? "The incident was accepted for operational handling." : "No acknowledgement has been recorded."} done={Boolean(incident.acknowledgedAt)} />
          <TimelineItem time={incident.resolvedAt} label="RESOLUTION" title={incident.resolvedAt ? `Resolved by ${incident.resolvedByName ?? "authorized operator"}` : "Resolution pending"} text={incident.resolutionNotes ?? "No resolution notes have been recorded."} done={Boolean(incident.resolvedAt)} />
        </div>
        <div className={styles.notesPanel}>
          <label htmlFor="incident-resolution-notes">OPERATIONAL NOTES / RESOLUTION REASON {canDraftResolution && <b>*</b>}</label>
          <textarea id="incident-resolution-notes" value={resolutionDraft} maxLength={NOTE_LIMIT} readOnly={!canDraftResolution} aria-describedby="incident-resolution-guidance incident-resolution-count" placeholder={canDraftResolution ? "Enter the operational mitigation and resolution reason..." : "No operational resolution notes are available."} onChange={event => setResolutionDraft(event.target.value)} />
          <div className={styles.noteMeta}><small id="incident-resolution-guidance">{canDraftResolution && !trimmedDraft ? "A resolution reason will be required before this incident can be closed." : canDraftResolution ? "Draft stored on this page only until the resolution API is available." : "This incident record is read-only for your current access state."}</small><b id="incident-resolution-count">{resolutionDraft.length}/{NOTE_LIMIT}</b></div>
          <div className={styles.noteTemplates} aria-label="Resolution note templates">
            <span>PRE-FILL NOTE TEMPLATES</span>
            <div>{NOTE_TEMPLATES.map((template, index) => {
              const applied = resolutionDraft.split(/\r?\n/).some(line => line.trim() === template);
              return <button key={template} type="button" className={applied ? styles.noteTemplateActive : ""} aria-pressed={applied} disabled={!canDraftResolution} onClick={() => applyNoteTemplate(template)}>{index === 0 ? "MAVLINK RESTORED" : index === 1 ? "RTH ACTIVATED" : "MANUAL OVERRIDE"}</button>;
            })}</div>
          </div>
        </div>
        {account.role === "operation_manager" && status !== "resolved" ? <div className={styles.actionUnavailable}>
          <div><button type="button" disabled>ACKNOWLEDGE INCIDENT</button><button type="button" className={styles.primaryAction} disabled>RESOLVE &amp; CLOSE INCIDENT</button></div>
          <p>Backend acknowledge/resolve endpoints are not available yet. This page will not report a false mutation success.</p>
        </div> : <div className={styles.readOnlyNotice}>{status === "resolved" ? "INCIDENT CLOSED / HISTORY IS READ-ONLY" : "PILOT ACCESS / DECISION CONTROLS ARE MANAGER-ONLY"}</div>}
      </aside>
    </div>

    <section className={styles.recordFooter}>
      <RecordCard label="DUTY AUTHORITY" value={account.displayName} detail={account.role === "operation_manager" ? "Operations Manager / Incident Authority" : "UAV Operator / Read-only Access"} />
      <RecordCard label="MISSION & AIRFRAME" value={incident.missionName || incident.missionId} detail={`${incident.uavCode ?? "Unassigned UAV"} / ${incident.uavName ?? "Airframe unavailable"}`} />
      <RecordCard label="AUDIT INTEGRITY" value="READ-ONLY INCIDENT LEDGER" detail={`Record created ${formatDate(incident.createdAt)}`} />
    </section>
  </div>;
}

function PanelHeader({ label, value }: { label: string; value: string }) {
  return <header className={styles.panelHeader}><span>{label}</span><b>{value}</b></header>;
}

function MetricCard({ label, value, subValue, tone }: { label: string; value: string; subValue?: string; tone: "cyan" | "green" | "danger" | "violet" | "amber" }) {
  return <article className={styles.metricCard}><span>{label}</span><strong className={styles[tone]}>{value}</strong>{subValue && <small>{subValue}</small>}</article>;
}

function TimelineItem({ time, label, title, text, done }: { time: string | null; label: string; title: string; text: string; done: boolean }) {
  return <article className={`${styles.timelineItem} ${done ? styles.timelineDone : ""}`}><i /><div><header><time>{formatDate(time)}</time><b>{label}</b></header><strong>{title}</strong><p>{text}</p></div></article>;
}

function RecordCard({ label, value, detail }: { label: string; value: string; detail: string }) {
  return <article className={styles.recordCard}><span>{label}</span><strong>{value}</strong><small>{detail}</small></article>;
}

function DetailState({ title, description, loading = false, action }: { title: string; description: string; loading?: boolean; action?: React.ReactNode }) {
  return <section className={styles.detailState}>{loading && <i />}<h1>{title}</h1><p>{description}</p>{action}</section>;
}
