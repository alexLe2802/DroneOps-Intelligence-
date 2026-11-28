"use client";

import { apiInstant } from "@/lib/portal/vietnam-time";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import type { Account } from "@/lib/auth/types";
import { approveMission, getPendingMissionApprovals, rejectMission, validateMission } from "@/lib/portal/client";
import type { PendingMissionApproval } from "@/lib/portal/types";
import { approvalBlockReason, validationState } from "@/lib/portal/mission-review";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

import MissionPlanComparison from "../missions/mission-plan-comparison";
import MissionEvidencePanel from "../missions/mission-evidence-panel";
import MissionReviewAdvice from "./mission-review-advice";
import ApprovalContextPanel from "./approval-context-panel";

type Decision = "approve" | "reject";

const formatDate = (value: string | null) => value
  ? new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "Asia/Ho_Chi_Minh", timeZoneName: "short" }).format(apiInstant(value))
  : "Not scheduled";

const missionCode = (id: string) => `#MS-${id.slice(0, 4).toUpperCase()}`;

export default function MissionApprovalsClient({ account }: { account: Account }) {
  const searchParams = useSearchParams();
  const requestedMissionId = searchParams.get("missionId");
  const [items, setItems] = useState<PendingMissionApproval[] | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [decision, setDecision] = useState<Decision>("approve");
  const [permitChoice, setPermitChoice] = useState({ review: "", value: "" });
  const [comment, setComment] = useState("");
  const [confirmedFor, setConfirmedFor] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");

  const load = useCallback(async (showLoading = false, silent = false) => {
    if (showLoading) setItems(null);
    else if (!silent) setRefreshing(true);
    if (!silent) { setError(null); setConfirmedFor(null); }
    try {
      const result = await getPendingMissionApprovals();
      setItems(result.items);
      setTotalCount(result.totalCount);
      setSelectedId(current => {
        if (result.items.some(item => item.missionId === current)) return current;
        if (requestedMissionId && result.items.some(item => item.missionId === requestedMissionId)) return requestedMissionId;
        return result.items[0]?.missionId ?? null;
      });
    } catch (reason) {
      setError(reason);
    } finally {
      setRefreshing(false);
    }
  }, [requestedMissionId]);

  useEffect(() => {
    let active = true;
    void getPendingMissionApprovals().then(result => {
      if (!active) return;
      setItems(result.items);
      setTotalCount(result.totalCount);
      setSelectedId(requestedMissionId && result.items.some(item => item.missionId === requestedMissionId)
        ? requestedMissionId
        : result.items[0]?.missionId ?? null);
    }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, [requestedMissionId]);

  useEffect(() => {
    const refresh = () => {
      if (document.visibilityState === "visible") void load(false, true);
    };
    const timer = window.setInterval(refresh, 10_000);
    document.addEventListener("visibilitychange", refresh);
    window.addEventListener("droneops:mission-approvals-refresh", refresh);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", refresh);
      window.removeEventListener("droneops:mission-approvals-refresh", refresh);
    };
  }, [load]);

  const filtered = useMemo(() => {
    const value = query.trim().toLowerCase();
    if (!value) return items ?? [];
    return (items ?? []).filter(item => `${item.missionId} ${item.missionName} ${item.pilotName} ${item.pilotEmail} ${item.uavCode} ${item.uavName}`.toLowerCase().includes(value));
  }, [items, query]);

  const selected = filtered.find(item => item.missionId === selectedId) ?? filtered[0] ?? null;
  const reviewIdentity = JSON.stringify([selected?.missionId, selected?.updateRequestId, selected?.validation?.id, selected?.permitDocumentId]);
  const permit = permitChoice.review === reviewIdentity ? permitChoice.value : "";
  const permitReady = !!permit && (permit === "Valid" ? !!selected?.permitDocumentId : !!comment.trim());
  const confirmed = confirmedFor === reviewIdentity;
  const groupedWarnings = Array.from((selected?.validation?.warnings ?? []).reduce((groups, warning) => {
    const match = /^WP(\d+):\s*(.*)$/.exec(warning);
    const message = match ? match[2] : warning;
    const points = groups.get(message) ?? [];
    if (match) points.push(match[1]);
    groups.set(message, points);
    return groups;
  }, new Map<string, string[]>()));
  const missingReview = !selected?.updateRequestId;
  const missingPlans = !Array.isArray(selected?.proposedPlan?.waypoints) || (!selected?.isInitial && !Array.isArray(selected?.currentPlan?.waypoints));
  const validationBlock = !selected ? "Select a mission."
    : selected.proposalAuthorId === account.id ? "Another Manager must approve your proposed update."
    : selected.requiresPilotConfirmation ? "The pilot must confirm the High Risk evidence for this proposal."
    : approvalBlockReason(selected.validation, selected.missionId, selected.updateRequestId);
  const validationPassed = !validationBlock;
  const decisionHint = busy ? "Saving your decision…"
    : refreshing ? "Refreshing review details…"
    : missingReview ? "Review details are missing. Refresh the queue. If this persists, the running backend must be updated to the mission review version."
    : decision === "approve" && missingPlans ? "The current plan and proposal must be available before approval. Refresh the queue."
    : decision === "approve" && validationBlock ? `${validationBlock} You can still reject with a reason.`
    : decision === "approve" && !permitReady ? "Review the permit and record the correction or exemption basis if applicable."
    : decision === "reject" && !comment.trim() ? "Enter rejection findings, then confirm that you have reviewed the mission."
    : !confirmed ? "Tick the review confirmation checkbox to enable your selected decision."
    : decision === "approve" ? "Ready to approve. The system will validate the proposal again before saving."
    : "Ready to reject. The reason will be recorded in the review history.";
  const selectedIndex = selected ? filtered.findIndex(item => item.missionId === selected.missionId) : -1;

  function moveSelection(direction: -1 | 1) {
    if (!filtered.length) return;
    const next = Math.min(filtered.length - 1, Math.max(0, selectedIndex + direction));
    setSelectedId(filtered[next].missionId);
    setDecision("approve"); setComment(""); setConfirmedFor(null); setNotice(""); setError(null);
  }

  function selectMission(id: string) {
    setSelectedId(id); setDecision("approve"); setComment(""); setConfirmedFor(null); setNotice(""); setError(null);
  }

  async function revalidate() {
    if (!selected || busy || refreshing || !selected.updateRequestId || missingPlans) return;
    const missionId = selected.missionId;
    setBusy(true); setError(null); setConfirmedFor(null);
    try {
      const result = await validateMission({ id: missionId }, selected.updateRequestId);
      setNotice(result.message);
      await load();
    } catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  async function submitDecision(action: Decision) {
    if (!selected || busy || refreshing || !confirmed || !selected.updateRequestId) return;
    if (action === "approve" && (missingPlans || !validationPassed || !permitReady)) {
      setError(new Error("Mandatory validation has not passed. Revalidate this mission first."));
      return;
    }
    if (action === "reject" && !comment.trim()) {
      setError(new Error("Enter rejection findings before rejecting this mission."));
      return;
    }
    setBusy(true); setError(null); setNotice("");
    try {
      const result = action === "approve"
        ? await approveMission(selected.missionId, comment, permit, selected.permitDocumentId, selected.validation?.id, selected.updateRequestId)
        : await rejectMission(selected.missionId, comment, selected.updateRequestId);
      setNotice(`${missionCode(result.missionId)} was ${result.decision.toLowerCase()} successfully.`);
      setComment(""); setConfirmedFor(null);
      await load();
    } catch (reason) {
      setConfirmedFor(null);
      await load();
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
      <label><span className="sr-only">Search pending missions</span><input value={query} onChange={event => setQuery(event.target.value)} placeholder="Search mission, pilot or UAV" disabled={busy || refreshing} /></label>
      <label><span className="sr-only">Selected mission</span><select value={selected?.missionId ?? ""} onChange={event => selectMission(event.target.value)} disabled={busy || refreshing || !filtered.length}>{filtered.length ? filtered.map(item => <option key={item.missionId} value={item.missionId}>{missionCode(item.missionId)}{" · "}{item.missionName}</option>) : <option value="">No matching mission</option>}</select></label>
      <nav aria-label="Queue navigation"><button type="button" aria-label="Previous mission" disabled={busy || refreshing || selectedIndex <= 0} onClick={() => moveSelection(-1)}>&lt;</button><span>{selectedIndex >= 0 ? selectedIndex + 1 : 0} / {filtered.length}</span><button type="button" aria-label="Next mission" disabled={busy || refreshing || selectedIndex < 0 || selectedIndex >= filtered.length - 1} onClick={() => moveSelection(1)}>&gt;</button></nav>
      <button type="button" className="approval-refresh" disabled={busy || refreshing} onClick={() => void load()}>{refreshing ? "SYNCING" : "REFRESH"}</button>
    </section>

    {notice && <p className="approval-notice" role="status">{notice}</p>}
    {error !== null && <div className="approval-error" role="alert"><span>{error instanceof Error ? error.message : "The approval request could not be completed."}</span><button type="button" onClick={() => setError(null)}>x</button></div>}
    {!selected ? <ModuleEmpty title="No pending mission" message={items.length ? "No mission matches the current search." : "There are no mission plans waiting for manager approval."} /> : <>
      <header className="approval-heading">
        <div><p>MISSION REVIEW</p><div><h1>Mission Review: <span>{missionCode(selected.missionId)}</span></h1><b>{selected.isInitial ? "Pending Review" : "Pending Update"}</b></div><small>{selected.missionName}{!selected.isInitial ? ` · Current status: ${selected.status}` : ""}</small></div>
        <dl><div><dt>SUBMITTED BY</dt><dd>{selected.pilotName || "Unknown Pilot"}<small>{selected.pilotEmail || "Email unavailable"}</small></dd></div><div><dt>SUBMITTED AT</dt><dd>{formatDate(selected.createdAt)}</dd></div></dl>
      </header>

      <section className="approval-clearance-grid" aria-label="Mission clearance summary">
        <article className="clearance-airspace"><header><span>AIRSPACE VALIDATION</span><i>AS</i></header><strong>{validationState(selected.validation)}</strong><p>Backend validation summary</p><div><i /></div></article>
        <article><header><span>PILOT TRACK RECORD</span><i>PT</i></header><strong>{selected.pilotRecord ? `${selected.pilotRecord.completedMissionsCount} completed` : "Unavailable"}</strong><p>{selected.pilotRecord ? `${selected.pilotRecord.totalIncidentsCount} incident${selected.pilotRecord.totalIncidentsCount === 1 ? "" : "s"}${selected.pilotRecord.openIncidentsCount > 0 ? ` · ${selected.pilotRecord.openIncidentsCount} open` : ""}` : "Pilot history unavailable"}</p><div><i /></div></article>
        <article><header><span>ASSIGNED AIRFRAME</span><i>UA</i></header><strong>{selected.uavCode || "UNASSIGNED"}</strong><p>{selected.uavName || "No aircraft record"}</p><div><i /></div></article>
        <article><header><span>WEATHER ADVISORY</span><i>WX</i></header><strong>{selected.validation?.weather?.available ? "Forecast loaded" : "Incomplete"}</strong><p>Advisory only; does not block approval. <a href="https://open-meteo.com/" target="_blank" rel="noreferrer">Open-Meteo</a></p><div><i /></div></article>
      </section>

      <div className="approval-review-layout">
        <div className="approval-spatial-column">
          <div className="approval-request-context"><span>SELECTED REVIEW</span><strong>{selected.updateRequestId ? `${selected.isInitial ? "Initial submission" : "Proposed update"} · Request ${selected.updateRequestId.slice(0, 8).toUpperCase()}` : "Review details unavailable"}</strong><p>{selected.updateRequestId ? "Review the pilot's track record, aircraft specifications, proposed route, and validation evidence before recording your decision." : "Refresh to retrieve the review details. Decisions remain disabled until the plans are available."}</p></div>
          <MissionReviewAdvice key={reviewIdentity} missionId={selected.missionId} />
          <ApprovalContextPanel key={`context-${selected.missionId}-${selected.updateRequestId}`} selected={selected} />
          <MissionPlanComparison key={selected.updateRequestId} missionId={selected.missionId} isInitial={selected.isInitial} current={selected.currentPlan} proposed={selected.proposedPlan} validation={selected.validation} />
          <MissionEvidencePanel key={`evidence-${selected.updateRequestId}`} id={selected.missionId} requestId={selected.updateRequestId} />
        </div>

        <aside className="approval-decision-column">
          <section className="approval-summary-panel">
            <header><div><span className="approval-panel-code">VS</span><h2>Validation Summary</h2></div><b>BACKEND</b></header>
            <dl><div><dt>CURRENT MISSION STATUS</dt><dd>{selected.status}</dd></div><div><dt>VALIDATION</dt><dd className={validationPassed ? "validation-passed" : "validation-attention"}>{validationState(selected.validation)}</dd></div><div><dt>FLIGHT WINDOW</dt><dd>{formatDate(selected.startTime)}<small>to {formatDate(selected.endTime)}</small></dd></div><div><dt>WAYPOINTS</dt><dd>{selected.waypointCount}</dd></div></dl>
            <button type="button" className="module-secondary" disabled={busy || refreshing || !selected.updateRequestId || missingPlans} onClick={() => void revalidate()}>{busy ? "CHECKING..." : "REVALIDATE CURRENT CONDITIONS"}</button>
            {selected.validation?.checkedAt && <p>Checked {formatDate(selected.validation.checkedAt)}</p>}
            {!!selected.validation?.issues?.length && <div className="approval-findings blocking"><h3>Blocking findings · {selected.validation.issues.length}</h3><ul>{selected.validation.issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul><p>Resolve these findings and revalidate before approving.</p></div>}
            {groupedWarnings.length > 0 && <details className="approval-findings advisory"><summary>Advisory warnings · {groupedWarnings.length} groups <small>Does not block approval</small></summary><ul>{groupedWarnings.map(([message, points]) => <li key={message}>{message}{points.length > 0 && <small>Applies to {points.length} waypoint{points.length === 1 ? "" : "s"} · {points.map(point => `WP${point}`).join(", ")}</small>}</li>)}</ul></details>}
            {selected.validation?.weather && <p>Forecast: {selected.validation.weather.provider}; wind {selected.validation.weather.maxWindMetersPerSecond ?? "unavailable"} m/s; gust {selected.validation.weather.maxGustMetersPerSecond ?? "unavailable"} m/s; rain {selected.validation.weather.maxRainMillimetersPerHour ?? "unavailable"} mm/h. Weather is advisory.</p>}
            <article><span>MISSION BRIEF</span><p>{selected.description || "No mission description was submitted."}</p></article>
          </section>

          <section className="approval-authorization-panel">
            <header><div><span className="approval-panel-code">DA</span><h2>Decision Authorization</h2></div><b>MANAGER</b></header>
            <div className="approval-authorizer"><span>{account.displayName.slice(0, 1).toUpperCase()}</span><div><b>{account.displayName}</b><small>{account.email}</small></div><em>SESSION ACTIVE</em></div>
            <fieldset disabled={busy || refreshing}><legend>Flight permit validation</legend>
              {selected.permitDocumentId ? <a href={`/api/documents/${selected.permitDocumentId}`} download>Download submitted permit</a> : <p>No flight permit attached.</p>}
              <label>Permit outcome<select value={permit} onChange={e => { setPermitChoice({ review: reviewIdentity, value: e.target.value }); setConfirmedFor(null); }}>
                <option value="">Select an outcome</option><option value="Valid" disabled={!selected.permitDocumentId}>Permit checked and valid</option>
                <option value="Supplement">Request a missing or corrected permit</option><option value="Exempt">Exempt — record basis below</option>
              </select></label>
            </fieldset>
            <fieldset disabled={busy || refreshing}><legend>DECISION DIRECTIVE</legend><div className="approval-choice"><label><input type="radio" name="decision" checked={decision === "approve"} onChange={() => setDecision("approve")} /> Approve plan</label><label><input type="radio" name="decision" checked={decision === "reject"} onChange={() => setDecision("reject")} /> Reject with Findings</label></div><label className="approval-comment">{decision === "reject" ? "REJECTION FINDINGS" : "REVIEW COMMENT"}<textarea value={comment} onChange={event => setComment(event.target.value)} maxLength={1000} rows={4} placeholder={decision === "reject" ? "Required: explain why the mission cannot be authorized" : permit === "Exempt" ? "Required: basis for exemption" : permit === "Supplement" ? "Required: tell the pilot what to provide" : "Optional manager comment"} /></label><label className="approval-confirm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmedFor(event.target.checked ? reviewIdentity : null)} /> I have reviewed this mission and confirm my selected decision.</label></fieldset>
            <div className="approval-decision-guidance" id="decision-guidance" role="status"><strong>{missingReview ? "Review data required" : decision === "approve" ? "Before approving" : "Before rejecting"}</strong><p>{decisionHint}</p>{missingReview && <button type="button" className="module-secondary" disabled={busy || refreshing} onClick={() => void load()}>REFRESH REVIEW DETAILS</button>}</div>
            <div className="approval-decision-actions" aria-describedby="decision-guidance"><button type="button" className="reject" disabled={busy || refreshing || missingReview || !confirmed || decision !== "reject" || !comment.trim()} onClick={() => void submitDecision("reject")}>{busy && decision === "reject" ? "REJECTING..." : "REJECT PLAN"}</button><button type="button" className="approve" disabled={busy || refreshing || missingReview || missingPlans || !confirmed || decision !== "approve" || !validationPassed || !permitReady} onClick={() => void submitDecision("approve")}>{busy && decision === "approve" ? "APPROVING..." : "APPROVE PLAN"}</button></div>
          </section>


        </aside>
      </div>
    </>}
  </div>;
}
