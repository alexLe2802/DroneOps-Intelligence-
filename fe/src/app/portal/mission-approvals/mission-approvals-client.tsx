"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import type { Account } from "@/lib/auth/types";
import { approveMission, getGeofences, getMission, getPendingMissionApprovals, rejectMission, validateMission } from "@/lib/portal/client";
import type { Geofence, MissionDetail, MissionValidationResult, PendingMissionApproval } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import MissionMap from "../missions/mission-map";

type Decision = "approve" | "reject";

const formatDate = (value: string | null) => value
  ? new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "UTC", timeZoneName: "short" }).format(new Date(value))
  : "Not scheduled";

const missionCode = (id: string) => `#MS-${id.slice(0, 4).toUpperCase()}`;

export default function MissionApprovalsClient({ account }: { account: Account }) {
  const [items, setItems] = useState<PendingMissionApproval[] | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [decision, setDecision] = useState<Decision>("approve");
  const [comment, setComment] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [validating, setValidating] = useState(false);
  const [validation, setValidation] = useState<MissionValidationResult | null>(null);
  const [validationMissionId, setValidationMissionId] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");
  const [routeData, setRouteData] = useState<{ missionId: string; mission: MissionDetail; geofences: Geofence[] } | null>(null);
  const [routeFailure, setRouteFailure] = useState<{ missionId: string; message: string } | null>(null);

  const load = useCallback(async (showLoading = false) => {
    if (showLoading) setItems(null);
    else setRefreshing(true);
    setError(null);
    try {
      const result = await getPendingMissionApprovals();
      setItems(result.items);
      setTotalCount(result.totalCount);
      setSelectedId(current => result.items.some(item => item.missionId === current) ? current : result.items[0]?.missionId ?? null);
    } catch (reason) {
      setError(reason);
    } finally {
      setRefreshing(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    void getPendingMissionApprovals().then(result => {
      if (!active) return;
      setItems(result.items);
      setTotalCount(result.totalCount);
      setSelectedId(result.items[0]?.missionId ?? null);
    }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);

  const filtered = useMemo(() => {
    const value = query.trim().toLowerCase();
    if (!value) return items ?? [];
    return (items ?? []).filter(item => `${item.missionId} ${item.missionName} ${item.pilotName} ${item.pilotEmail} ${item.uavCode} ${item.uavName}`.toLowerCase().includes(value));
  }, [items, query]);

  const selected = filtered.find(item => item.missionId === selectedId) ?? filtered[0] ?? null;
  const selectedIndex = selected ? filtered.findIndex(item => item.missionId === selected.missionId) : -1;
  const currentValidation = selected?.missionId === validationMissionId ? validation : null;
  const summaryPassed = selected?.validationStatus?.toLowerCase() === "passed";
  const validationPassed = currentValidation?.isValid === true && summaryPassed;
  const selectedMissionId = selected?.missionId ?? null;
  const selectedRoute = routeData?.missionId === selectedMissionId ? routeData : null;
  const selectedRouteFailure = routeFailure?.missionId === selectedMissionId ? routeFailure.message : "";

  useEffect(() => {
    if (!selectedMissionId) return;
    let active = true;
    void Promise.allSettled([getMission(selectedMissionId), getGeofences()]).then(([missionResult, geofenceResult]) => {
      if (!active) return;
      if (missionResult.status === "rejected") {
        setRouteFailure({ missionId: selectedMissionId, message: missionResult.reason instanceof Error ? missionResult.reason.message : "Mission route could not be loaded." });
        return;
      }
      setRouteData({ missionId: selectedMissionId, mission: missionResult.value, geofences: geofenceResult.status === "fulfilled" ? geofenceResult.value : [] });
      setRouteFailure(current => current?.missionId === selectedMissionId ? null : current);
    });
    return () => { active = false; };
  }, [selectedMissionId]);

  function moveSelection(direction: -1 | 1) {
    if (!filtered.length) return;
    const next = Math.min(filtered.length - 1, Math.max(0, selectedIndex + direction));
    setSelectedId(filtered[next].missionId);
    setDecision("approve"); setComment(""); setConfirmed(false); setValidation(null); setValidationMissionId(null); setNotice(""); setError(null);
  }

  function selectMission(id: string) {
    setSelectedId(id); setDecision("approve"); setComment(""); setConfirmed(false); setValidation(null); setValidationMissionId(null); setNotice(""); setError(null);
  }

  async function runManagerValidation() {
    if (!selected || busy || validating) return;
    setValidating(true); setError(null); setNotice(""); setConfirmed(false);
    try {
      const detail = selectedRoute?.mission ?? await getMission(selected.missionId);
      const result = await validateMission(detail);
      setValidation(result);
      setValidationMissionId(selected.missionId);
      if (result.isValid && summaryPassed) {
        setNotice(`${missionCode(selected.missionId)} passed manager validation and is ready for a decision.`);
      } else {
        setError(new Error(`${missionCode(selected.missionId)} has validation findings and cannot be approved.`));
      }
    } catch (reason) {
      setValidation(null);
      setValidationMissionId(null);
      setError(reason);
    } finally {
      setValidating(false);
    }
  }

  async function submitDecision(action: Decision) {
    if (!selected || busy || validating || !confirmed || !currentValidation) return;
    if (action === "approve" && !validationPassed) {
      setError(new Error("Run manager validation and resolve all findings before approval."));
      return;
    }
    if (action === "reject" && !comment.trim()) {
      setError(new Error("Enter rejection findings before rejecting this mission."));
      return;
    }
    setBusy(true); setError(null); setNotice("");
    try {
      const result = action === "approve"
        ? await approveMission(selected.missionId, comment)
        : await rejectMission(selected.missionId, comment);
      setNotice(`${missionCode(result.missionId)} was ${result.decision.toLowerCase()} successfully.`);
      setComment(""); setConfirmed(false);
      await load();
    } catch (reason) {
      setError(reason);
    } finally {
      setBusy(false);
    }
  }

  if (!items && !error) return <ModuleLoading label="Loading mission approval queue" />;
  if (!items && error) return <ModuleError error={error} retry={() => void load(true)} />;
  if (!items) return null;

  return <div className="approval-workspace">
    <section className="approval-queue-bar" aria-label="Pending mission queue">
      <div><span>PENDING REVIEW QUEUE</span><strong>{String(totalCount).padStart(2, "0")}</strong></div>
      <label><span className="sr-only">Search pending missions</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder="Search mission, pilot or UAV" /></label>
      <label><span className="sr-only">Selected mission</span><select value={selected?.missionId ?? ""} onChange={event => selectMission(event.target.value)} disabled={!filtered.length}>{filtered.length ? filtered.map(item => <option key={item.missionId} value={item.missionId}>{missionCode(item.missionId)}{" // "}{item.missionName}</option>) : <option value="">No matching mission</option>}</select></label>
      <nav aria-label="Queue navigation"><button type="button" aria-label="Previous mission" disabled={selectedIndex <= 0} onClick={() => moveSelection(-1)}>&lt;</button><span>{selectedIndex >= 0 ? selectedIndex + 1 : 0} / {filtered.length}</span><button type="button" aria-label="Next mission" disabled={selectedIndex < 0 || selectedIndex >= filtered.length - 1} onClick={() => moveSelection(1)}>&gt;</button></nav>
      <button type="button" className="approval-refresh" disabled={refreshing} onClick={() => void load()}>{refreshing ? "SYNCING" : "REFRESH"}</button>
    </section>

    {notice && <p className="approval-notice" role="status">{notice}</p>}
    {error !== null && <div className="approval-error" role="alert"><span>{error instanceof Error ? error.message : "The approval request could not be completed."}</span><button type="button" onClick={() => setError(null)}>x</button></div>}
    {!selected ? <ModuleEmpty title="No pending mission" message={items.length ? "No mission matches the current search." : "There are no mission plans waiting for manager approval."} /> : <>
      <header className="approval-heading">
        <div><p>OPERATIONAL CLEARANCE // TASK GATE UC-09 // REF: SCR-09</p><div><h1>Approvals / Mission Review: <span>{missionCode(selected.missionId)}</span></h1><b>{selected.status}</b></div><small>{selected.missionName}</small></div>
        <dl><div><dt>SUBMITTED BY</dt><dd>{selected.pilotName || "Unknown Pilot"}<small>{selected.pilotEmail || "Email unavailable"}</small></dd></div><div><dt>TIMESTAMP</dt><dd>{formatDate(selected.createdAt)}</dd></div></dl>
      </header>

      <section className="approval-clearance-grid" aria-label="Mission clearance summary">
        <article className="clearance-airspace"><header><span>AIRSPACE CLEARANCE</span><i>AS</i></header><strong>{validating ? "Running" : currentValidation ? validationPassed ? "Passed" : "Failed" : "Awaiting check"}</strong><p>{currentValidation?.message ?? "Manager validation has not been run"}</p><div><i /></div></article>
        <article><header><span>ASSIGNED AIRFRAME</span><i>UA</i></header><strong>{selected.uavCode || "UNASSIGNED"}</strong><p>{selected.uavName || "No aircraft record"}</p><div><i /></div></article>
        <article className="clearance-unavailable"><header><span>PILOT COMPLIANCE</span><i>PC</i></header><strong>Unavailable</strong><p>Certification data not returned</p><div><i /></div></article>
        <article className="clearance-unavailable"><header><span>WEATHER &amp; METAR</span><i>WX</i></header><strong>Unavailable</strong><p>No mission weather contract</p><div><i /></div></article>
      </section>

      <div className="approval-review-layout">
        <div className="approval-spatial-column">
          <section className="approval-spatial-panel">
            <header><div><span className="approval-panel-code">SV</span><h2>Spatial Verification Map</h2></div><div><b>UTM SECTOR 04</b><em>EPSG:4326</em></div></header>
            {selectedRoute?.mission.waypoints.length ? <div className="approval-route-map"><MissionMap waypoints={selectedRoute.mission.waypoints} geofences={selectedRoute.geofences} readOnly /></div> : selectedRouteFailure ? <div className="approval-map-unavailable approval-map-loading"><span>ROUTE LOAD FAILED</span><strong>MAP UNAVAILABLE</strong><p>{selectedRouteFailure}</p></div> : selectedRoute ? <div className="approval-map-unavailable approval-map-loading"><span>NO ROUTE NODES</span><strong>0 WAYPOINTS</strong><p>This mission detail does not contain route coordinates.</p></div> : <div className="approval-map-unavailable approval-map-loading"><div className="approval-map-crosshair" /><span>LOADING ROUTE DETAIL</span><strong>{selected.waypointCount} WAYPOINT{selected.waypointCount === 1 ? "" : "S"} REPORTED</strong><p>Retrieving waypoint coordinates and airspace zones.</p></div>}
            <footer><div><b>GEOSPATIAL AUDIT</b><span>{selected.validationStatus || "Unavailable"}</span></div><div><small>RULE ENGINE</small><b>BACKEND SUMMARY</b></div><div><small>MISSION ID</small><b>{selected.missionId.slice(0, 8).toUpperCase()}</b></div></footer>
          </section>
          <section className="approval-waypoint-strip"><header><span>WAYPOINT CLEARANCE INSPECTION</span><b>{selectedRoute?.mission.waypoints.length ?? selected.waypointCount} NODES</b></header><div>{selectedRoute?.mission.waypoints.length ? selectedRoute.mission.waypoints.slice(0, 12).map((point, index) => <span key={`${point.sequenceOrder}-${index}`}><b>WP{String(point.sequenceOrder).padStart(2, "0")}</b><small>{point.altitude}M AGL</small></span>) : selected.waypointCount ? Array.from({ length: Math.min(selected.waypointCount, 12) }, (_, index) => <span key={index}><b>WP{String(index + 1).padStart(2, "0")}</b><small>LOADING</small></span>) : <p>No waypoint records were reported.</p>}</div></section>
        </div>

        <aside className="approval-decision-column">
          <section className="approval-summary-panel">
            <header><div><span className="approval-panel-code">VS</span><h2>Validation Summary</h2></div><b>MANAGER GATE</b></header>
            <dl><div><dt>PLAN STATUS</dt><dd>{selected.status}</dd></div><div><dt>VALIDATION</dt><dd>{validating ? "Running" : currentValidation ? validationPassed ? "Passed" : "Failed" : "Not run"}</dd></div><div><dt>FLIGHT WINDOW</dt><dd>{formatDate(selected.startTime)}<small>to {formatDate(selected.endTime)}</small></dd></div><div><dt>WAYPOINTS</dt><dd>{selected.waypointCount}</dd></div></dl>
            <button type="button" className="approval-validation-action" disabled={busy || validating} onClick={() => void runManagerValidation()}>{validating ? "RUNNING VALIDATION..." : currentValidation ? "RUN VALIDATION AGAIN" : "RUN MANAGER VALIDATION"}</button>
            {selected.validationIssues?.length ? <ul className="approval-validation-issues">{selected.validationIssues.map(issue => <li key={issue}>{issue}</li>)}</ul> : null}
            <article><span>MISSION BRIEF</span><p>{selected.description || "No mission description was submitted."}</p></article>
          </section>

          <section className="approval-authorization-panel">
            <header><div><span className="approval-panel-code">DA</span><h2>Decision Authorization</h2></div><b>MANAGER</b></header>
            <div className="approval-authorizer"><span>{account.displayName.slice(0, 1).toUpperCase()}</span><div><b>{account.displayName}</b><small>{account.email}</small></div><em>SESSION ACTIVE</em></div>
            <fieldset disabled={busy || validating || !currentValidation}><legend>DECISION DIRECTIVE</legend><div className="approval-choice"><label><input type="radio" name="decision" checked={decision === "approve"} onChange={() => setDecision("approve")} /> Authorize Flight</label><label><input type="radio" name="decision" checked={decision === "reject"} onChange={() => setDecision("reject")} /> Reject with Findings</label></div><label className="approval-comment">{decision === "reject" ? "REJECTION FINDINGS" : "REVIEW COMMENT"}<textarea value={comment} onChange={event => setComment(event.target.value)} maxLength={1000} rows={4} placeholder={decision === "reject" ? "Required: explain why the mission cannot be authorized" : "Optional manager comment"} /></label><label className="approval-confirm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I confirm that I reviewed the manager validation result and mission information.</label></fieldset>
            {!currentValidation && <p className="approval-validation-hint">Run manager validation to unlock the approval decision.</p>}
            <div className="approval-decision-actions"><button type="button" className="reject" disabled={busy || validating || !currentValidation || !confirmed || decision !== "reject" || !comment.trim()} onClick={() => void submitDecision("reject")}>{busy && decision === "reject" ? "REJECTING..." : "REJECT PLAN"}</button><button type="button" className="approve" disabled={busy || validating || !validationPassed || !confirmed || decision !== "approve"} onClick={() => void submitDecision("approve")}>{busy && decision === "approve" ? "APPROVING..." : "APPROVE & AUTHORIZE"}</button></div>
          </section>

          <section className="approval-history-panel"><header><span>PRIOR DECISIONS LOG</span><b>NOT AVAILABLE</b></header><p>No approval-history endpoint is available for this mission.</p></section>
        </aside>
      </div>
    </>}
  </div>;
}
