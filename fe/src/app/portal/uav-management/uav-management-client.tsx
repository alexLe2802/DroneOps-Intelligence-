"use client";

import { formatVietnamDate } from "@/lib/portal/vietnam-time";

import Image from "next/image";
import { useCallback, useEffect, useRef, useState } from "react";
import { getManagedUav, getManagedUavs, rejectManagedUavImage, setManagedUavStatus } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleLoading } from "../module-state";
import UavInformation from "../uav/uav-information";

export default function UavManagementClient({ initialImageModerationStatus = "", initialStatus = "" }: { initialImageModerationStatus?: string; initialStatus?: string }) {
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [selected, setSelected] = useState<AssignedUav | null>(null);
  const requestId = useRef(0);
  const mutationPending = useRef(false);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(0);
  const [totalItems, setTotalItems] = useState(0);
  const [manualReviewCount, setManualReviewCount] = useState(0);
  const [status, setStatus] = useState(initialStatus);
  const [imageModerationStatus, setImageModerationStatus] = useState(initialImageModerationStatus);
  const [appliedKeyword, setAppliedKeyword] = useState("");
  const [keyword, setKeyword] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  const load = useCallback(async (search = appliedKeyword, requestedPage = page, requestedStatus = status, requestedModerationStatus = imageModerationStatus) => {
    const currentRequest = ++requestId.current;
    setError(null);
    setBusy(true);
    try {
      const listRequest = getManagedUavs({ keyword: search, pageNumber: requestedPage, status: requestedStatus, imageModerationStatus: requestedModerationStatus });
      const countRequest = getManagedUavs({ pageNumber: 1, pageSize: 1, imageModerationStatus: "ManualReview" });
      const [result, manualReviewResult] = await Promise.all([listRequest, countRequest]);
      if (currentRequest !== requestId.current) return;
      setAircraft(result.items);
      setTotalPages(result.totalPages);
      setTotalItems(result.totalItems);
      setManualReviewCount(manualReviewResult.totalItems);
      setSelected(null);
    } catch (reason) {
      if (currentRequest === requestId.current) setError(reason);
    } finally {
      if (currentRequest === requestId.current) setBusy(false);
    }
  }, [appliedKeyword, imageModerationStatus, page, status]);

  useEffect(() => {
    const requests = requestId;
    const timer = window.setTimeout(() => void load(), 0);
    return () => { window.clearTimeout(timer); requests.current++; };
  }, [load]);

  async function inspect(id: string) {
    setBusy(true);
    setError(null);
    setMessage("");
    try { setSelected(await getManagedUav(id)); }
    catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  async function changeStatus(action: "approve" | "activate" | "reject" | "deactivate") {
    if (!selected || mutationPending.current) return;
    if (action === "reject" && !window.confirm(`Reject UAV ${selected.code}?`)) return;
    if (action === "deactivate" && !window.confirm(`Deactivate UAV ${selected.code}?`)) return;
    mutationPending.current = true;
    setBusy(true);
    setError(null);
    setMessage("");
    try {
      const updated = await setManagedUavStatus(selected.id, action);
      setMessage(`${updated.code} is now ${updated.status}.`);
      setSelected(null);
      if (page > 1) setPage(1);
      else await load();
      window.dispatchEvent(new Event("droneops:notifications-refresh"));
    } catch (reason) { setError(reason); }
    finally { mutationPending.current = false; setBusy(false); }
  }

  async function rejectImage() {
    if (!selected || mutationPending.current || !selected.imageUrl) return;
    const entered = window.prompt("Why is this UAV image being rejected?");
    if (entered === null) return;
    const reason = entered.trim();
    if (!reason) { setError(new Error("A rejection reason is required.")); return; }
    mutationPending.current = true;
    setBusy(true);
    setError(null);
    setMessage("");
    try {
      const updated = await rejectManagedUavImage(selected.id, reason);
      setSelected(updated);
      setMessage("UAV image removed and the operator was notified.");
      await load();
    } catch (reasonValue) { setError(reasonValue); }
    finally { mutationPending.current = false; setBusy(false); }
  }

  if (!aircraft && !error) return <ModuleLoading label="Loading UAV fleet" />;

  return <>
    <header className="module-heading"><div><p className="module-kicker">OPERATIONS MANAGER / UAV REVIEW</p><h1>UAV Management</h1><p>Browse the UAV fleet, review registrations, and manage aircraft status.</p></div><button type="button" className={`uav-manual-review-filter${imageModerationStatus === "ManualReview" ? " active" : ""}`} aria-pressed={imageModerationStatus === "ManualReview"} disabled={busy} onClick={() => { setPage(1); setImageModerationStatus(current => current === "ManualReview" ? "" : "ManualReview"); }}><span>MANUAL REVIEW</span><strong>{manualReviewCount}</strong><small>AI image checks</small></button></header>
    {error && <div className="form-error" role="alert"><p>{error instanceof Error ? error.message : "Unable to load UAVs. Please try again."}</p><button className="module-secondary" disabled={busy} onClick={() => void load()}>TRY AGAIN</button></div>}
    {message && <p className="approval-banner" role="status">{message}</p>}
    <form className="module-toolbar" onSubmit={event => { event.preventDefault(); if (page === 1 && appliedKeyword === keyword.trim()) void load(); else { setPage(1); setAppliedKeyword(keyword.trim()); } }}>
      <label>SEARCH UAV<input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Code, name, model or pilot" /></label>
      <label>STATUS<select value={status} disabled={busy} onChange={event => { setStatus(event.target.value); setPage(1); }}><option value="">All statuses</option>{["Pending", "Active", "Inactive", "Rejected"].map(value => <option key={value} value={value}>{value}</option>)}</select></label>
      <button className="module-primary" disabled={busy}>SEARCH</button>
    </form>
    <div className="manager-uav-layout">
      <section className="manager-uav-queue">
        {imageModerationStatus === "ManualReview" && <div className="uav-moderation-filter-banner" role="status"><span>IMAGE MODERATION QUEUE</span><strong>Manual Review ({totalItems})</strong><small>Only UAVs that exhausted AI moderation retries are shown.</small></div>}
        {busy && !selected ? <ModuleLoading label="Loading UAV fleet" /> : error && !aircraft ? <p>Fleet data is unavailable. Please retry.</p> : !aircraft?.length ? <ModuleEmpty title="No UAV found" message="No aircraft matches your search and status filter." /> : aircraft.map(uav => <button type="button" className={`manager-uav-row${selected?.id === uav.id ? " selected" : ""}`} key={uav.id} onClick={() => void inspect(uav.id)} disabled={busy}>
          <span className={`manager-uav-thumbnail${uav.imageUrl ? " has-image" : ""}`}>{uav.imageUrl ? <Image src={uav.imageUrl} alt="" width={120} height={90} unoptimized /> : uav.code.slice(0, 3)}</span>
          <span><b>{uav.code}</b><small>{uav.name} · {[uav.manufacturer, uav.model].filter(Boolean).join(" ") || "Model not specified"}</small>{uav.imageModerationStatus === "ManualReview" && <small className="uav-manual-review-label">IMAGE: MANUAL REVIEW</small>}</span>
          <span><b>{uav.pilotName || "Unknown pilot"}</b><small>{formatVietnamDate(uav.createdAt)}</small></span>
          <em>{uav.status}</em>
        </button>)}
        <div className="uav-actions" aria-label="Fleet pagination"><button className="module-secondary" disabled={busy || page <= 1} onClick={() => setPage(value => value - 1)}>PREVIOUS</button><span role="status">Page {page} / {Math.max(1, totalPages)} · {totalItems} UAVs</span><button className="module-secondary" disabled={busy || page >= totalPages} onClick={() => setPage(value => value + 1)}>NEXT</button></div>
      </section>
      <aside className="manager-uav-detail">
        {!selected ? <div className="manager-uav-placeholder"><span>UAV</span><h2>Select a UAV</h2><p>Select an aircraft from the list to view its complete record.</p></div> : <>
          <div className={`manager-uav-image${selected.imageUrl ? " has-image" : ""}`}>{selected.imageUrl ? <Image src={selected.imageUrl} alt={`${selected.name} UAV`} width={900} height={500} unoptimized /> : <><span>UAV</span><small>No aircraft image</small></>}</div>
          <section className={`uav-moderation-evidence status-${selected.imageModerationStatus?.toLowerCase() ?? "unknown"}`} aria-label="UAV image moderation result">
            <header><span>IMAGE MODERATION STATUS</span><strong>{selected.imageModerationStatus ?? "Not submitted"}</strong></header>
            <p>{selected.imageModerationReason || "No moderation reason was returned for this UAV image."}</p>
            {selected.imageModeratedAt && <small>Last checked {formatVietnamDate(selected.imageModeratedAt)}</small>}
          </section>
          <header><div><p className="module-kicker">UAV RECORD</p><h2>{selected.code}</h2><p>{selected.name}</p></div><span className={`status status-${selected.status.toLowerCase()}`}>{selected.status}</span></header>
          <UavInformation aircraft={selected} />
          {selected.imageUrl && <div className="uav-actions" aria-label="Image review actions">
            <button className="module-danger" disabled={busy} onClick={() => void rejectImage()}>REJECT IMAGE</button>
          </div>}
          <div className="uav-actions">{["pending", "inactive"].includes(selected.status.toLowerCase()) && <button className="module-primary" disabled={busy} onClick={() => void changeStatus(selected.status.toLowerCase() === "pending" ? "approve" : "activate")}>{selected.status.toLowerCase() === "inactive" ? "REACTIVATE" : "APPROVE & ACTIVATE"}</button>}<button className="module-danger" disabled={busy || selected.status.toLowerCase() !== "pending"} onClick={() => void changeStatus("reject")}>REJECT</button>{selected.status.toLowerCase() === "active" && <button className="module-danger" disabled={busy} onClick={() => void changeStatus("deactivate")}>DEACTIVATE</button>}</div>
        </>}
      </aside>
    </div>
  </>;
}
