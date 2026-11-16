"use client";

import { useEffect, useRef, useState } from "react";
import { getUavReviews, setManagedUavStatus } from "@/lib/portal/client";
import type { CreateUavRequest, UavReviewState } from "@/lib/portal/types";
import { formatVietnamDate } from "@/lib/portal/vietnam-time";
import styles from "./uav-review.module.css";

const fields: [keyof CreateUavRequest, string][] = [
  ["code", "Code"], ["name", "Name"], ["manufacturer", "Manufacturer"], ["model", "Model"], ["serialNumber", "Serial number"],
  ["emptyWeightKg", "Empty weight (kg)"], ["maxTakeoffWeightKg", "Maximum takeoff weight (kg)"], ["maxPayloadKg", "Payload (kg)"],
  ["batteryType", "Battery type"], ["batteryCapacityMah", "Capacity (mAh)"], ["batteryVoltage", "Nominal voltage (V)"], ["batteryMaxVoltage", "Full-charge voltage (V)"], ["batteryCellCount", "Cells"],
  ["maxFlightTimeMinutes", "Endurance (min)"], ["maxRangeKm", "Range (km)"], ["maxSpeedKmh", "Speed (km/h)"], ["maxWindSpeedMps", "Wind limit (m/s)"], ["maxOperatingAltitudeMeters", "Altitude limit (m)"], ["purchaseDate", "Purchase date"]
];
const display = (value: unknown) => value == null || value === "" ? "Not provided" : String(value);
const date = (value: string | null) => value ? formatVietnamDate(value) : "Not recorded";
type Action = "approve" | "reject" | "activate" | "deactivate";

export default function UavReviewPanel({ uavId, manager = false, onDecision }: { uavId: string; manager?: boolean; onDecision?: () => void }) {
  const [state, setState] = useState<UavReviewState | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [stale, setStale] = useState(false);
  const [revision, setRevision] = useState(0);
  const [reason, setReason] = useState("");
  const [action, setAction] = useState<Action | null>(null);
  const [notice, setNotice] = useState("");
  const pending = useRef(false);
  useEffect(() => {
    let active = true;
    getUavReviews(uavId).then(value => { if (active) setState(value); }).catch(value => { if (active) setError(value instanceof Error ? value.message : "Review evidence unavailable."); });
    return () => { active = false; };
  }, [uavId, revision]);
  function reload() { setState(null); setError(""); setStale(false); setAction(null); setReason(""); setRevision(value => value + 1); }
  async function decide(event: React.FormEvent) {
    event.preventDefault();
    if (!state || !action || pending.current || stale) return;
    pending.current = true; setBusy(true); setError("");
    try {
      await setManagedUavStatus(uavId, action, { expectedVersion: state.registryVersion, requestId: action === "approve" || action === "reject" ? state.pending?.id : null, reason: reason.trim() });
      setNotice("Decision saved. Reloaded current information and review history.");
      window.dispatchEvent(new Event("droneops:notifications-refresh"));
      reload(); onDecision?.();
    } catch (value) { setError(value instanceof Error ? value.message : "Decision failed."); setStale(true); }
    finally { pending.current = false; setBusy(false); }
  }
  const reviewable = Boolean(state?.pending || state?.status === "Pending" || state?.status === "Pending Review");
  return <section className={styles.panel}>
    <header><div><span className={styles.eyebrow}>APPROVAL & OPERATIONS</span><h3>UAV information review</h3></div><button type="button" disabled={busy} onClick={reload}>Reload review</button></header>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert">{error} Reload before trying another decision.</p>}
    {!state ? <p>Review evidence {error ? "unavailable" : "loading…"}</p> : <>
      <p className={styles.status}>Operational status: <strong>{state.status}</strong></p>
      <p>{state.hasApprovedInformation ? "Approved information stays in use until a new proposal is approved." : "This registration has not yet been approved. Current information is submitted evidence."}</p>
      {state.pending ? <>
        <p>Pending {state.pending.kind} · Submitted by {display(state.pending.submittedBy)} · {date(state.pending.submittedAt)}</p>
        <div className={styles.table} tabIndex={0} role="region" aria-label="Current and proposed UAV information"><table>
          <caption>Current information compared with proposal</caption>
          <thead><tr><th scope="col">Field</th><th scope="col">{state.hasApprovedInformation ? "Approved current" : "Current registration"}</th><th scope="col">Proposed</th></tr></thead>
          <tbody>{fields.map(([key, label]) => <tr key={key}><th scope="row">{label}</th><td>{display(state.current[key])}</td><td>{display(state.pending!.proposed[key])}{state.current[key] !== state.pending!.proposed[key] && <strong className={styles.changed}>Changed</strong>}</td></tr>)}</tbody>
        </table></div>
      </> : <p className={styles.empty}>No pending information proposal.</p>}
      <p className={styles.hint}>Changes are blocked during active flight.</p>
      {manager && <>
        <div className={styles.actions}>
          {reviewable && <><button type="button" className={styles.primary} disabled={busy || stale} onClick={() => setAction("approve")}>Review approval</button><button type="button" className={styles.danger} disabled={busy || stale} onClick={() => setAction("reject")}>Review rejection</button></>}
          {state.status === "Active" && <button type="button" className={styles.danger} disabled={busy || stale} onClick={() => setAction("deactivate")}>Deactivate UAV</button>}
          {state.status === "Inactive" && state.hasApprovedInformation && <button type="button" className={styles.primary} disabled={busy || stale} onClick={() => setAction("activate")}>Reactivate UAV</button>}
        </div>
        {action && <form onSubmit={decide} className={styles.confirm}>
          <h4>Confirm {action}</h4><p>{action === "reject" && state.hasApprovedInformation ? "Reject only this proposal; retain the approved UAV information and operational status." : "Confirm the action using the record and proposal shown above."}</p>
          <label>Decision reason<textarea required maxLength={500} value={reason} disabled={busy || stale} onChange={event => setReason(event.target.value)} /></label>
          <button type="submit" className={action === "deactivate" || action === "reject" ? styles.danger : styles.primary} disabled={busy || stale || !reason.trim()}>{busy ? "Saving…" : `Confirm ${action}`}</button><button type="button" disabled={busy} onClick={() => setAction(null)}>Cancel decision</button>
        </form>}
      </>}
      <details className={styles.history}><summary>Review & status history <span>{state.history.length} recorded</span></summary>
      <p className={styles.hint}>Record version {state.registryVersion}. Aircraft image checks are separate from information approval.</p>
      {state.legacyHistoryUnavailable && <p>Earlier submission/reviewer evidence is unavailable. Historical approval dates are not reconstructed.</p>}
      {state.history.length ? <ol>{state.history.map(row => <li key={row.id}>
        <strong>{row.kind} · {row.status}</strong><p>Submitted: {display(row.submittedBy)} · {date(row.submittedAt)}</p>
        <p>Reviewed: {display(row.reviewedBy)} · {date(row.reviewedAt)} · Reason: {display(row.reason)}</p>
        <details><summary>Information captured in this request</summary><dl>{fields.filter(([key]) => row.currentAtSubmission[key] !== row.proposed[key]).map(([key,label]) => <div key={key}><dt>{label}</dt><dd>{display(row.currentAtSubmission[key])} → {display(row.proposed[key])}</dd></div>)}</dl></details>
      </li>)}</ol> : <p>No recorded decisions.</p>}
      </details>
    </>}
  </section>;
}
