"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import type { Account } from "@/lib/auth/types";
import { approveMission, getPendingMissionApprovals, rejectMission, validateMission } from "@/lib/portal/client";
import type { PendingMissionApproval } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

import MissionPlanComparison from "../missions/mission-plan-comparison";
import MissionEvidencePanel from "../missions/mission-evidence-panel";

type Decision = "approve" | "reject";

const formatDate = (value: string | null) => value
  ? new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "UTC", timeZoneName: "short" }).format(new Date(value))
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
  const [comment, setComment] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [notice, setNotice] = useState("");

  const load = useCallback(async (showLoading = false) => {
    if (showLoading) setItems(null);
    else setRefreshing(true);
    setError(null); setConfirmed(false);
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

  const filtered = useMemo(() => {
    const value = query.trim().toLowerCase();
    if (!value) return items ?? [];
    return (items ?? []).filter(item => `${item.missionId} ${item.missionName} ${item.pilotName} ${item.pilotEmail} ${item.uavCode} ${item.uavName}`.toLowerCase().includes(value));
  }, [items, query]);

  const selected = filtered.find(item => item.missionId === selectedId) ?? filtered[0] ?? null;
  const groupedWarnings = Array.from((selected?.validation?.warnings ?? []).reduce((groups, warning) => {
    const match = /^WP(\d+):\s*(.*)$/.exec(warning);
    const message = match ? match[2] : warning;
    const points = groups.get(message) ?? [];
    if (match) points.push(match[1]);
    groups.set(message, points);
    return groups;
  }, new Map<string, string[]>()));
  const missingReview = !selected?.updateRequestId;
  const missingPlans = !selected?.currentPlan || !selected?.proposedPlan;
  const validationPassed = selected?.validation?.isValid === true && !selected.validation.stale && !selected.validation.incomplete;
  const decisionHint = busy ? "Saving your decision…"
    : refreshing ? "Refreshing review details…"
    : missingReview ? "Review details are missing. Refresh the queue. If this persists, the running backend must be updated to the mission review version."
    : decision === "approve" && missingPlans ? "The current plan and proposal must be available before approval. Refresh the queue."
    : decision === "approve" && !validationPassed ? "Run Revalidate Current Conditions and resolve any blocking findings before approval. You can still reject with a reason."
    : decision === "reject" && !comment.trim() ? "Enter rejection findings, then confirm that you have reviewed the mission."
    : !confirmed ? "Tick the review confirmation checkbox to enable your selected decision."
    : decision === "approve" ? "Ready to approve. The system will validate the proposal again before saving."
    : "Ready to reject. The reason will be recorded in the review history.";
  const selectedIndex = selected ? filtered.findIndex(item => item.missionId === selected.missionId) : -1;

  function moveSelection(direction: -1 | 1) {
    if (!filtered.length) return;
    const next = Math.min(filtered.length - 1, Math.max(0, selectedIndex + direction));
    setSelectedId(filtered[next].missionId);
    setDecision("approve"); setComment(""); setConfirmed(false); setNotice(""); setError(null);
  }

  function selectMission(id: string) {
    setSelectedId(id); setDecision("approve"); setComment(""); setConfirmed(false); setNotice(""); setError(null);
  }

  async function revalidate() {
    if (!selected || busy || !selected.updateRequestId || !selected.currentPlan || !selected.proposedPlan) return;
    const missionId = selected.missionId;
    setBusy(true); setError(null); setConfirmed(false);
    try {
      const result = await validateMission({ id: missionId }, selected.updateRequestId);
      setNotice(result.message);
      await load();
    } catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  async function submitDecision(action: Decision) {
    if (!selected || busy || refreshing || !confirmed || !selected.updateRequestId) return;
    if (action === "approve" && (missingPlans || !validationPassed)) {
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
        ? await approveMission(selected.missionId, comment, selected.validation?.id, selected.updateRequestId)
        : await rejectMission(selected.missionId, comment, selected.updateRequestId);
      setNotice(`${missionCode(result.missionId)} was ${result.decision.toLowerCase()} successfully.`);
      setComment(""); setConfirmed(false);
      await load();
    } catch (reason) {
      setConfirmed(false);
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
        <article className="clearance-airspace"><header><span>AIRSPACE CLEARANCE</span><i>AS</i></header><strong>{selected.validationStatus || "Unavailable"}</strong><p>Backend validation summary</p><div><i /></div></article>
        <article><header><span>ASSIGNED AIRFRAME</span><i>UA</i></header><strong>{selected.uavCode || "UNASSIGNED"}</strong><p>{selected.uavName || "No aircraft record"}</p><div><i /></div></article>
        <article className="clearance-unavailable"><header><span>PILOT COMPLIANCE</span><i>PC</i></header><strong>Unavailable</strong><p>Certification data not returned</p><div><i /></div></article>
        <article><header><span>WEATHER ADVISORY</span><i>WX</i></header><strong>{selected.validation?.weather?.available ? "Forecast loaded" : "Incomplete"}</strong><p>Advisory only; does not block approval. <a href="https://open-meteo.com/" target="_blank" rel="noreferrer">Open-Meteo</a></p><div><i /></div></article>
      </section>

      <div className="approval-review-layout">
        <div className="approval-spatial-column">
          <div className="approval-request-context"><span>SELECTED REVIEW</span><strong>{selected.updateRequestId ? `Request ${selected.updateRequestId.slice(0, 8).toUpperCase()}` : "Review details unavailable"}</strong><p>{selected.updateRequestId ? "Review the proposal and its evidence before recording your decision." : "Refresh to retrieve the review details. Decisions remain disabled until the plans are available."}</p></div>
          <MissionPlanComparison key={selected.updateRequestId} isInitial={selected.isInitial} current={selected.currentPlan} proposed={selected.proposedPlan} validation={selected.validation} />
          <MissionEvidencePanel key={`evidence-${selected.updateRequestId}`} id={selected.missionId} requestId={selected.updateRequestId} />
        </div>

        <aside className="approval-decision-column">
          <section className="approval-summary-panel">
            <header><div><span className="approval-panel-code">VS</span><h2>Validation Summary</h2></div><b>BACKEND</b></header>
            <dl><div><dt>PLAN STATUS</dt><dd>{selected.status}</dd></div><div><dt>VALIDATION</dt><dd className={validationPassed ? "validation-passed" : "validation-attention"}>{selected.validationStatus || "Unavailable"}</dd></div><div><dt>FLIGHT WINDOW</dt><dd>{formatDate(selected.startTime)}<small>to {formatDate(selected.endTime)}</small></dd></div><div><dt>WAYPOINTS</dt><dd>{selected.waypointCount}</dd></div></dl>
            <button type="button" className="module-secondary" disabled={busy || !selected.updateRequestId || !selected.currentPlan || !selected.proposedPlan} onClick={() => void revalidate()}>{busy ? "CHECKING..." : "REVALIDATE CURRENT CONDITIONS"}</button>
            {selected.validation?.checkedAt && <p>Checked {formatDate(selected.validation.checkedAt)}</p>}
            {!!selected.validation?.issues?.length && <div className="approval-findings blocking"><h3>Blocking findings · {selected.validation.issues.length}</h3><ul>{selected.validation.issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul><p>Resolve these findings and revalidate before approving.</p></div>}
            {groupedWarnings.length > 0 && <details className="approval-findings advisory"><summary>Advisory warnings · {groupedWarnings.length} groups <small>Does not block approval</small></summary><ul>{groupedWarnings.map(([message, points]) => <li key={message}>{message}{points.length > 0 && <small>Applies to {points.length} waypoint{points.length === 1 ? "" : "s"} · {points.map(point => `WP${point}`).join(", ")}</small>}</li>)}</ul></details>}
            {selected.validation?.weather && <p>Forecast: {selected.validation.weather.provider}; wind {selected.validation.weather.maxWindMetersPerSecond ?? "unavailable"} m/s; gust {selected.validation.weather.maxGustMetersPerSecond ?? "unavailable"} m/s; rain {selected.validation.weather.maxRainMillimetersPerHour ?? "unavailable"} mm/h. Weather is advisory.</p>}
            <article><span>MISSION BRIEF</span><p>{selected.description || "No mission description was submitted."}</p></article>
          </section>

          <section className="approval-authorization-panel">
            <header><div><span className="approval-panel-code">DA</span><h2>Decision Authorization</h2></div><b>MANAGER</b></header>
            <div className="approval-authorizer"><span>{account.displayName.slice(0, 1).toUpperCase()}</span><div><b>{account.displayName}</b><small>{account.email}</small></div><em>SESSION ACTIVE</em></div>
            <fieldset disabled={busy || refreshing}><legend>DECISION DIRECTIVE</legend><div className="approval-choice"><label><input type="radio" name="decision" checked={decision === "approve"} onChange={() => setDecision("approve")} /> Authorize Flight</label><label><input type="radio" name="decision" checked={decision === "reject"} onChange={() => setDecision("reject")} /> Reject with Findings</label></div><label className="approval-comment">{decision === "reject" ? "REJECTION FINDINGS" : "REVIEW COMMENT"}<textarea value={comment} onChange={event => setComment(event.target.value)} maxLength={1000} rows={4} placeholder={decision === "reject" ? "Required: explain why the mission cannot be authorized" : "Optional manager comment"} /></label><label className="approval-confirm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} /> I have reviewed this mission and confirm my selected decision.</label></fieldset>
            <div className="approval-decision-guidance" id="decision-guidance" role="status"><strong>{missingReview ? "Review data required" : decision === "approve" ? "Before approving" : "Before rejecting"}</strong><p>{decisionHint}</p>{missingReview && <button type="button" className="module-secondary" disabled={busy || refreshing} onClick={() => void load()}>REFRESH REVIEW DETAILS</button>}</div>
            <div className="approval-decision-actions" aria-describedby="decision-guidance"><button type="button" className="reject" disabled={busy || refreshing || missingReview || !confirmed || decision !== "reject" || !comment.trim()} onClick={() => void submitDecision("reject")}>{busy && decision === "reject" ? "REJECTING..." : "REJECT PLAN"}</button><button type="button" className="approve" disabled={busy || refreshing || missingReview || missingPlans || !confirmed || decision !== "approve" || !validationPassed} onClick={() => void submitDecision("approve")}>{busy && decision === "approve" ? "APPROVING..." : "APPROVE & AUTHORIZE"}</button></div>
          </section>


        </aside>
      </div>
    </>}
  </div>;
}
