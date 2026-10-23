"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { AccountRole } from "@/lib/auth/types";
import { cancelMission, getGeofences, getMission, startMission, validateMission } from "@/lib/portal/client";
import type { Geofence, MissionDetail, MissionValidationResult } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

import MissionPlanComparison from "../mission-plan-comparison";
import MissionEvidencePanel from "../mission-evidence-panel";

const formatDate = (value: string | null) => value ? new Date(value).toLocaleString() : "Not scheduled";
const formatStatus = (value: string) => value.replace(/([A-Z])/g, " $1").trim();
const statusKey = (value: string) => value.toLowerCase().replaceAll(" ", "");
type MissionDialog = "cancel" | null;
type BusyAction = "validate" | "start" | "cancel" | null;

export default function MissionDetailClient({ id, role }: { id: string; role: AccountRole }) {
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
      setValidation(missionResult.value.validation ?? null);
      setValidatedAt(missionResult.value.validation?.checkedAt ? new Date(missionResult.value.validation.checkedAt) : null);
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
      setValidation(missionResult.value.validation ?? null);
      setValidatedAt(missionResult.value.validation?.checkedAt ? new Date(missionResult.value.validation.checkedAt) : null);
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
      const result = await validateMission(mission, mission.pendingUpdate?.id);
      setValidation(result);
      setValidatedAt(result.checkedAt ? new Date(result.checkedAt) : new Date());
    } catch (reason) {
      setValidation(null);
      setValidatedAt(null);
      setActionError(reason instanceof Error ? reason.message : "Unable to validate this mission.");
    } finally {
      actionPending.current = false;
      setBusy(null);
    }
  }

  async function beginMission() {
    if (!mission || actionPending.current) return;
    actionPending.current = true; setBusy("start"); setActionError("");
    try {
      const result = await startMission(mission.id);
      setValidation(result);
      setValidatedAt(result.checkedAt ? new Date(result.checkedAt) : new Date());
      setMission(await getMission(mission.id));
      setNotice("Mission marked In Progress after fresh mandatory checks. No aircraft control command was sent.");
    } catch (reason) {
      try { const refreshed = await getMission(mission.id); setMission(refreshed); setValidation(refreshed.validation ?? null); } catch { /* Preserve the original action error. */ }
      setActionError(reason instanceof Error ? reason.message : "Unable to start mission.");
    }
    finally { actionPending.current = false; setBusy(null); }
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
  const isManager = role === "operation_manager";
  const currentStatus = statusKey(mission.status);
  const canEdit = !isManager && ["draft", "rejected", "pending", "pendingreview", "submitted", "approved"].includes(currentStatus);
  const canValidate = !["completed", "cancelled", "inprogress"].includes(currentStatus);
  const canCancel = !["inprogress", "completed", "cancelled"].includes(currentStatus);
  const awaitingApproval = ["submitted", "pending", "pendingapproval", "pendingreview"].includes(currentStatus);
  const approvalTitle = currentStatus === "approved" ? "Flight authorized" : currentStatus === "rejected" ? "Changes requested" : currentStatus === "cancelled" ? "Mission cancelled" : awaitingApproval ? "Awaiting manager review" : "No decision recorded";
  const approvalCopy = currentStatus === "approved" ? "The backend reports that this mission has manager approval." : currentStatus === "rejected" ? "Edit and save the plan to run validation and send it for review again." : currentStatus === "cancelled" ? "No further approval action is available for this mission." : currentStatus === "submitted" ? "The backend accepted the submission. Manager queue visibility depends on pending-status mapping." : awaitingApproval ? "The mission is waiting for manager review. Saving changes submits a separate proposal." : "Approval evidence will appear after the plan is submitted and reviewed.";

  return <>
    <header className="module-heading mission-detail-heading">
      <div>
        <p className="module-kicker">{isManager ? "OPERATIONS / MISSION PLANNING / MISSION DETAIL" : "PILOT / MY MISSIONS / MISSION DETAIL"}</p>
        <div className="mission-title-line"><h1>{mission.name}</h1><span className={`status status-${mission.status.toLowerCase()}`}>{formatStatus(mission.status)}</span></div>
        <p>{mission.description ?? "No mission description has been provided."}</p>
      </div>
      <div className="heading-actions"><Link className="module-secondary module-link" href={isManager ? "/portal/mission-planning" : "/portal/missions"}>&lt; {isManager ? "MISSION PLANNING" : "MY MISSIONS"}</Link>{canEdit && <Link className="module-primary module-link" href={`/portal/missions/${mission.id}/edit`}>EDIT PLAN</Link>}{isManager && awaitingApproval && <Link className="module-primary module-link" href={`/portal/mission-approvals?missionId=${encodeURIComponent(mission.id)}`}>REVIEW REQUEST</Link>}</div>
    </header>
    {mission.detailAvailable === false && <p className="api-pending-banner" role="status"><b>MISSION DETAIL API PENDING</b> Showing the available mission summary. Route and waypoint data will appear when the backend returns a response from GET /api/missions/{mission.id}.</p>}
    {actionError && <div className="mission-action-feedback error" role="alert"><span>{actionError}</span><button type="button" onClick={() => setActionError("")}>DISMISS</button></div>}
    {notice && <div className="mission-action-feedback success" role="status"><span>{notice}</span><button type="button" onClick={() => setNotice("")}>DISMISS</button></div>}

    <section className="mission-action-feedback" aria-label="Persisted mission validation">
      <div><b>FLIGHT READINESS: {mission.flightReadiness ?? "AwaitingPreflight"}</b>
      <p>Mandatory checks run when saving, approving, and within 15 minutes before departure. Weather is advisory.</p>
      {mission.preflightValidation?.issues?.map((issue, index) => <p role="alert" key={`preflight-${index}`}>{issue}</p>)}
      {mission.preflightValidation?.warnings?.map((warning, index) => <p role="status" key={`preflight-warning-${index}`}>{warning}</p>)}
      {validation?.issues?.map((issue, index) => <p role="alert" key={`issue-${index}`}>{issue}</p>)}
      {validation?.warnings?.map((warning, index) => <p role="status" key={`warning-${index}`}>{warning}</p>)}
      {validation?.weather && <p><a href="https://open-meteo.com/" target="_blank" rel="noreferrer">{validation.weather.provider}</a> forecast: wind {validation.weather.maxWindMetersPerSecond ?? "unavailable"} m/s; gust {validation.weather.maxGustMetersPerSecond ?? "unavailable"} m/s; rain {validation.weather.maxRainMillimetersPerHour ?? "unavailable"} mm/h.</p>}
      </div>
    </section>

    {mission.pendingUpdate && <>
      <p role="status">A {mission.pendingUpdate.isInitial ? "submission" : "proposed update"} is pending review. Current mission status remains {mission.status}.</p>
      <MissionPlanComparison current={{ uavId: mission.uavId ?? "", name: mission.name, description: mission.description, startTime: mission.startTime ?? "", endTime: mission.endTime ?? "", altitudeReference: mission.altitudeReference as "AGL" | "MSL" | "Relative" | null, waypoints: mission.waypoints }} proposed={mission.pendingUpdate.proposedPlan} validation={mission.pendingUpdate.validation} />
    </>}
    <MissionEvidencePanel key={id} id={id} />
    <section className="mission-command-panel" aria-label="Mission lifecycle actions">
      <div className="mission-command-state">
        <span>PRE-FLIGHT CONTROL</span>
        <strong>{validation ? validation.isValid ? "VALIDATION PASSED" : "VALIDATION FAILED" : canValidate ? "VALIDATION REQUIRED" : formatStatus(mission.status).toUpperCase()}</strong>
        <small>{validation ? validation.message : canValidate ? "Saving the plan automatically validates and sends it for review." : "Lifecycle actions are restricted by the current mission status."}</small>
      </div>
      <div className="mission-command-actions">
        {currentStatus === "approved" && <button type="button" className="mission-submit-button" disabled={busy !== null} onClick={() => void beginMission()}>{busy === "start" ? "CHECKING..." : "START MISSION WITH PREFLIGHT CHECK"}</button>}
        <button type="button" className="mission-validate-button" disabled={!canValidate || busy !== null} onClick={() => void runValidation()}>{busy === "validate" ? "VALIDATING..." : "RUN VALIDATION"}</button>
        {canCancel && <button type="button" className="mission-cancel-button" disabled={busy !== null} onClick={() => setDialog("cancel")}>CANCEL MISSION</button>}
      </div>
    </section>

    <section className="mission-detail-stats" aria-label="Mission summary">
      <article><span>UPDATE REQUEST</span><strong>{mission.pendingUpdate?.status ?? "No pending update"}</strong><small>{mission.id.slice(0, 8).toUpperCase()}</small></article>
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

    {dialog && <div className="mission-dialog-backdrop" role="presentation">
      <section className="mission-dialog" role="dialog" aria-modal="true" aria-labelledby="mission-dialog-title">
        <h2 id="mission-dialog-title">Cancel this mission?</h2>
        <p>Cancellation preserves the record and does not issue an aircraft stop command.</p>
        <label>Reason<textarea rows={4} maxLength={1000} value={cancelReason} onChange={event => setCancelReason(event.target.value)} /></label>
        <button type="button" disabled={busy !== null} onClick={() => setDialog(null)}>BACK</button>
        <button type="button" disabled={busy !== null || !cancelReason.trim()} onClick={() => void confirmCancel()}>CONFIRM CANCELLATION</button>
      </section>
    </div>}
  </>;
}
