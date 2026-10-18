"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { cancelMission, getGeofences, getMission, submitMission, validateMission } from "@/lib/portal/client";
import type { Geofence, MissionDetail, MissionValidationResult } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

const formatDate = (value: string | null) => value ? new Date(value).toLocaleString() : "Not scheduled";
const formatStatus = (value: string) => value.replace(/([A-Z])/g, " $1").trim();
const statusKey = (value: string) => value.toLowerCase().replaceAll(" ", "");
type MissionDialog = "submit" | "cancel" | null;
type BusyAction = "validate" | "submit" | "cancel" | null;

export default function MissionDetailClient({ id }: { id: string }) {
  const [mission, setMission] = useState<MissionDetail | null>(null);
  const [geofences, setGeofences] = useState<Geofence[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [actionError, setActionError] = useState("");
  const [notice, setNotice] = useState("");
  const [validation, setValidation] = useState<MissionValidationResult | null>(null);
  const [validatedAt, setValidatedAt] = useState<Date | null>(null);
  const [dialog, setDialog] = useState<MissionDialog>(null);
  const [cancelReason, setCancelReason] = useState("");
  const [busy, setBusy] = useState<BusyAction>(null);
  const actionPending = useRef(false);

  const load = useCallback(() => {
    setMission(null);
    setError(null);
    setActionError("");
    setNotice("");
    setValidation(null);
    setValidatedAt(null);
    void Promise.allSettled([getMission(id), getGeofences()]).then(([missionResult, geofenceResult]) => {
      if (missionResult.status === "rejected") {
        setError(missionResult.reason);
        return;
      }
      setMission(missionResult.value);
      setGeofences(geofenceResult.status === "fulfilled" ? geofenceResult.value : []);
    });
  }, [id]);

  useEffect(() => {
    let active = true;
    void Promise.allSettled([getMission(id), getGeofences()]).then(([missionResult, geofenceResult]) => {
      if (!active) return;
      if (missionResult.status === "rejected") {
        setError(missionResult.reason);
        return;
      }
      setMission(missionResult.value);
      setGeofences(geofenceResult.status === "fulfilled" ? geofenceResult.value : []);
    });
    return () => { active = false; };
  }, [id]);

  useEffect(() => {
    if (!dialog) return;
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !actionPending.current) setDialog(null);
    };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [dialog]);

  const duration = useMemo(() => {
    if (!mission?.startTime || !mission.endTime) return "Not available";
    const minutes = Math.max(0, Math.round((new Date(mission.endTime).getTime() - new Date(mission.startTime).getTime()) / 60000));
    return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes} min`;
  }, [mission]);

  async function runValidation() {
    if (!mission || actionPending.current) return;
    actionPending.current = true;
    setBusy("validate");
    setActionError("");
    setNotice("");
    try {
      const result = await validateMission(mission);
      setValidation(result);
      setValidatedAt(new Date());
    } catch (reason) {
      setValidation(null);
      setValidatedAt(null);
      setActionError(reason instanceof Error ? reason.message : "Unable to validate this mission.");
    } finally {
      actionPending.current = false;
      setBusy(null);
    }
  }

  async function confirmSubmit() {
    if (!mission || !validation?.isValid || actionPending.current) return;
    actionPending.current = true;
    setBusy("submit");
    setActionError("");
    setNotice("");
    try {
      await submitMission(mission.id);
      const refreshed = await getMission(mission.id);
      setMission(refreshed);
      setDialog(null);
      setNotice("Mission submitted. The displayed status was refreshed from the backend.");
    } catch (reason) {
      setActionError(reason instanceof Error ? reason.message : "Unable to submit this mission.");
      setDialog(null);
    } finally {
      actionPending.current = false;
      setBusy(null);
    }
  }

  async function confirmCancel() {
    if (!mission || !cancelReason.trim() || actionPending.current) return;
    actionPending.current = true;
    setBusy("cancel");
    setActionError("");
    setNotice("");
    try {
      await cancelMission(mission.id, cancelReason);
      const refreshed = await getMission(mission.id);
      setMission(refreshed);
      setValidation(null);
      setValidatedAt(null);
      setCancelReason("");
      setDialog(null);
      setNotice("Mission cancelled. The displayed status was refreshed from the backend.");
    } catch (reason) {
      setActionError(reason instanceof Error ? reason.message : "Unable to cancel this mission.");
      setDialog(null);
    } finally {
      actionPending.current = false;
      setBusy(null);
    }
  }

  if (error) return <ModuleError error={error} retry={load} />;
  if (!mission) return <ModuleLoading label="Loading mission details" />;
  const currentStatus = statusKey(mission.status);
  const canEdit = ["draft", "rejected"].includes(currentStatus);
  const canValidate = canEdit;
  const canCancel = !["inprogress", "completed", "cancelled"].includes(currentStatus);
  const awaitingApproval = ["submitted", "pending", "pendingapproval", "pendingreview"].includes(currentStatus);
  const approvalTitle = currentStatus === "approved" ? "Flight authorized" : currentStatus === "rejected" ? "Changes requested" : currentStatus === "cancelled" ? "Mission cancelled" : awaitingApproval ? "Awaiting manager review" : "No decision recorded";
  const approvalCopy = currentStatus === "approved" ? "The backend reports that this mission has manager approval." : currentStatus === "rejected" ? "Edit the draft, validate it again and resubmit when the findings are resolved." : currentStatus === "cancelled" ? "No further approval action is available for this mission." : currentStatus === "submitted" ? "The backend accepted the submission. Manager queue visibility depends on pending-status mapping." : awaitingApproval ? "The mission is locked while a manager reviews the submitted plan." : "Approval evidence will appear after the plan is submitted and reviewed.";

  return <>
    <header className="module-heading mission-detail-heading">
      <div>
        <p className="module-kicker">PILOT / MY MISSIONS / MISSION DETAIL</p>
        <div className="mission-title-line"><h1>{mission.name}</h1><span className={`status status-${mission.status.toLowerCase()}`}>{formatStatus(mission.status)}</span></div>
        <p>{mission.description ?? "No mission description has been provided."}</p>
      </div>
      <div className="heading-actions"><Link className="module-secondary module-link" href="/portal/missions">← MY MISSIONS</Link>{canEdit && <Link className="module-primary module-link" href={`/portal/missions/${mission.id}/edit`}>EDIT PLAN</Link>}</div>
    </header>
    {mission.detailAvailable === false && <p className="api-pending-banner" role="status"><b>MISSION DETAIL API PENDING</b> Showing the available mission summary. Route and waypoint data will appear when the backend returns a response from GET /api/missions/{mission.id}.</p>}
    {actionError && <div className="mission-action-feedback error" role="alert"><span>{actionError}</span><button type="button" onClick={() => setActionError("")}>DISMISS</button></div>}
    {notice && <div className="mission-action-feedback success" role="status"><span>{notice}</span><button type="button" onClick={() => setNotice("")}>DISMISS</button></div>}

    <section className="mission-command-panel" aria-label="Mission lifecycle actions">
      <div className="mission-command-state">
        <span>PRE-FLIGHT CONTROL</span>
        <strong>{validation ? validation.isValid ? "VALIDATION PASSED" : "VALIDATION FAILED" : canValidate ? "VALIDATION REQUIRED" : formatStatus(mission.status).toUpperCase()}</strong>
        <small>{validation ? validation.message : canValidate ? "Run backend validation before submitting this plan." : "Lifecycle actions are restricted by the current mission status."}</small>
      </div>
      <div className="mission-command-actions">
        <button type="button" className="mission-validate-button" disabled={!canValidate || busy !== null} onClick={() => void runValidation()}>{busy === "validate" ? "VALIDATING..." : "RUN VALIDATION"}</button>
        <button type="button" className="mission-submit-button" disabled={!canEdit || !validation?.isValid || busy !== null} onClick={() => setDialog("submit")}>SUBMIT FOR APPROVAL</button>
        {canCancel && <button type="button" className="mission-cancel-button" disabled={busy !== null} onClick={() => setDialog("cancel")}>CANCEL MISSION</button>}
      </div>
    </section>

    <section className="mission-detail-stats" aria-label="Mission summary">
      <article><span>MISSION VERSION</span><strong>V{mission.latestVersion.toString().padStart(2, "0")}</strong><small>{mission.id.slice(0, 8).toUpperCase()}</small></article>
      <article><span>ASSIGNED UAV</span><strong>{mission.uavCode ?? "UNASSIGNED"}</strong><small>{mission.uavId ? mission.uavId.slice(0, 8).toUpperCase() : "NO AIRCRAFT ID"}</small></article>
      <article><span>WAYPOINTS</span><strong>{mission.waypoints.length.toString().padStart(2, "0")}</strong><small>ROUTE NODES</small></article>
      <article><span>EST. DURATION</span><strong>{duration}</strong><small>PLANNED WINDOW</small></article>
    </section>

    <section className="mission-detail-layout">
      <div className="mission-route-panel">
        <header><div><p className="module-kicker">ROUTE PLAN</p><h2>Mission Flight Path</h2></div><span>{geofences.length} AIRSPACE ZONES</span></header>
        {mission.waypoints.length > 0 ? <MissionMap waypoints={mission.waypoints} geofences={geofences} readOnly /> : <div className="mission-route-empty"><span>⌁</span><h3>Route data unavailable</h3><p>No waypoint data was returned for this mission.</p></div>}
      </div>
      <aside className="mission-overview-panel">
        <header><p className="module-kicker">MISSION RECORD</p><h2>Plan Overview</h2></header>
        <dl>
          <div><dt>Status</dt><dd>{formatStatus(mission.status)}</dd></div>
          <div><dt>Start time</dt><dd>{formatDate(mission.startTime)}</dd></div>
          <div><dt>End time</dt><dd>{formatDate(mission.endTime)}</dd></div>
          <div><dt>Created</dt><dd>{mission.createdAt ? formatDate(mission.createdAt) : "Not provided"}</dd></div>
          <div><dt>Mission ID</dt><dd className="mission-id-value">{mission.id}</dd></div>
        </dl>
      </aside>
    </section>

    <section className="mission-waypoint-panel">
      <header><div><p className="module-kicker">ROUTE MANIFEST</p><h2>Waypoint Sequence</h2></div><span>{mission.waypoints.length} TOTAL</span></header>
      <div className="mission-waypoint-table-wrap">
        <table className="mission-waypoint-table">
          <thead><tr><th>SEQ</th><th>LATITUDE</th><th>LONGITUDE</th><th>ALTITUDE</th><th>ACTION</th></tr></thead>
          <tbody>{mission.waypoints.length > 0 ? mission.waypoints.map((point, index) => <tr key={`${point.sequenceOrder}-${index}`}><td><b>{String(point.sequenceOrder).padStart(2, "0")}</b></td><td>{point.latitude.toFixed(6)}</td><td>{point.longitude.toFixed(6)}</td><td>{point.altitude} m</td><td><span>{formatStatus(point.actionType)}</span></td></tr>) : <tr><td className="mission-waypoint-empty" colSpan={5}>Waypoint data is waiting for the mission detail API.</td></tr>}</tbody>
        </table>
      </div>
    </section>

    <section className="mission-review-grid">
      <article><span className={`review-state ${busy === "validate" ? "running" : validation?.isValid ? "passed" : validation ? "failed" : "pending"}`}>{busy === "validate" ? "..." : validation?.isValid ? "OK" : validation ? "!" : "--"}</span><div><p className="module-kicker">BACKEND VALIDATION</p><h2>{busy === "validate" ? "Validation in progress" : validation?.isValid ? "Pre-flight check passed" : validation ? "Pre-flight check failed" : "Not validated"}</h2><p>{validation?.message ?? "No persisted validation result is available for this mission."}</p>{validatedAt && <time>Checked {validatedAt.toLocaleString()}</time>}</div></article>
      <article><span className={`review-state ${currentStatus === "approved" ? "passed" : currentStatus === "rejected" || currentStatus === "cancelled" ? "failed" : awaitingApproval ? "running" : ""}`}>{currentStatus === "approved" ? "OK" : currentStatus === "rejected" || currentStatus === "cancelled" ? "!" : awaitingApproval ? "..." : "--"}</span><div><p className="module-kicker">MANAGER APPROVAL</p><h2>{approvalTitle}</h2><p>{approvalCopy}</p></div></article>
    </section>

    {dialog && <div className="mission-dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && busy === null) setDialog(null); }}>
      <section className="mission-dialog" role="dialog" aria-modal="true" aria-labelledby="mission-dialog-title">
        <header><div><p className="module-kicker">MISSION CONTROL / CONFIRMATION</p><h2 id="mission-dialog-title">{dialog === "submit" ? "Submit mission for review?" : "Cancel this mission?"}</h2></div><button type="button" aria-label="Close dialog" disabled={busy !== null} onClick={() => setDialog(null)}>x</button></header>
        {dialog === "submit" ? <div className="mission-dialog-body"><p>The saved mission version will be sent to the manager approval queue. Editing should remain locked until the plan is approved or rejected.</p><dl><div><dt>Mission</dt><dd>{mission.name}</dd></div><div><dt>Validation</dt><dd>{validation?.message ?? "No validation result"}</dd></div><div><dt>Waypoints</dt><dd>{mission.waypoints.length}</dd></div></dl></div> : <div className="mission-dialog-body"><p>Cancellation preserves the mission record. It does not send a command to stop an aircraft.</p><label>CANCELLATION REASON<textarea rows={4} maxLength={1000} value={cancelReason} onChange={event => setCancelReason(event.target.value)} placeholder="Explain why this mission is being cancelled" autoFocus /></label></div>}
        <footer><button type="button" className="module-secondary" disabled={busy !== null} onClick={() => setDialog(null)}>BACK</button>{dialog === "submit" ? <button type="button" className="module-primary" disabled={busy !== null || !validation?.isValid} onClick={() => void confirmSubmit()} autoFocus>{busy === "submit" ? "SUBMITTING..." : "CONFIRM SUBMISSION"}</button> : <button type="button" className="module-danger" disabled={busy !== null || !cancelReason.trim()} onClick={() => void confirmCancel()}>{busy === "cancel" ? "CANCELLING..." : "CONFIRM CANCELLATION"}</button>}</footer>
      </section>
    </div>}
  </>;
}
