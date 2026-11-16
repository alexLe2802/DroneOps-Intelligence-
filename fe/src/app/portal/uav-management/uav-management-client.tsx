"use client";

import { formatVietnamDate } from "@/lib/portal/vietnam-time";

import Image from "next/image";
import { useCallback, useEffect, useRef, useState } from "react";
import { approveManagedUavImage, getManagedUav, getManagedUavs, rejectManagedUavImage } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleLoading } from "../module-state";
import UavReviewPanel from "../uav/uav-review-panel";
import UavInformation from "../uav/uav-information";
import { Camera, ChevronLeft, ChevronRight, Plane, Search, ShieldCheck } from "lucide-react";
import styles from "./uav-management.module.css";

function AircraftImage({ src, name, thumbnail = false }: { src: string | null | undefined; name: string; thumbnail?: boolean }) {
  const [failed, setFailed] = useState(false);
  return <div className={thumbnail ? styles.thumbnail : styles.image}>
    {src && !failed ? <Image src={src} alt={thumbnail ? "" : `${name} UAV`} width={thumbnail ? 96 : 720} height={thumbnail ? 80 : 360} unoptimized onError={() => setFailed(true)} />
      : <><Plane size={thumbnail ? 20 : 32} aria-hidden="true" />{!thumbnail && <span>{failed ? "Image unavailable" : "No aircraft image"}</span>}</>}
  </div>;
}

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
  const [rejectingImage, setRejectingImage] = useState(false);
  const [imageReason, setImageReason] = useState("");

  const load = useCallback(async (search = appliedKeyword, requestedPage = page, requestedStatus = status, requestedModerationStatus = imageModerationStatus) => {
    const currentRequest = ++requestId.current;
    setError(null);
    setBusy(true);
    try {
      const listRequest = getManagedUavs({ keyword: search, pageNumber: requestedPage, status: requestedStatus, imageModerationStatus: requestedModerationStatus });
      const countRequest = getManagedUavs({ pageNumber: 1, pageSize: 1, imageModerationStatus: "Pending" });
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
    setRejectingImage(false);
    setImageReason("");
    try {
      const updated = await getManagedUav(id);
      setSelected(updated);
      setAircraft(current => current?.map(item => item.id === updated.id ? updated : item) ?? null);
    }
    catch (reason) { setError(reason); }
    finally { setBusy(false); }
  }

  async function rejectImage() {
    if (!selected || mutationPending.current || !selected.pendingImageUrl) return;
    const reason = imageReason.trim();
    if (!reason) { setError(new Error("A rejection reason is required.")); return; }
    mutationPending.current = true;
    setBusy(true);
    setError(null);
    setMessage("");
    try {
      const updated = await rejectManagedUavImage(selected.id, reason);
      setSelected(updated);
      setMessage("Proposed UAV image rejected; the approved image was kept.");
      await load();
      setSelected(updated);
      setRejectingImage(false);
      setImageReason("");
    } catch (reasonValue) { setError(reasonValue); }
    finally { mutationPending.current = false; setBusy(false); }
  }

  async function approveImage() {
    if (!selected || mutationPending.current || !selected.pendingImageUrl) return;
    const reason = imageReason.trim();
    if (!reason) { setError(new Error("An approval reason is required.")); return; }
    mutationPending.current = true;
    setBusy(true);
    setError(null);
    setMessage("");
    try {
      const updated = await approveManagedUavImage(selected.id, reason);
      setSelected(updated);
      setMessage("Proposed UAV image approved and published to the aircraft record.");
      await load();
      setSelected(updated);
      setRejectingImage(false);
      setImageReason("");
    } catch (reasonValue) { setError(reasonValue); }
    finally { mutationPending.current = false; setBusy(false); }
  }

  if (!aircraft && !error) return <ModuleLoading label="Loading UAV fleet" />;

  return <div className={styles.page}>
    <header className={styles.heading}>
      <div><p className={styles.eyebrow}>FLEET OPERATIONS</p><h1>UAV Management</h1><p>Review aircraft information and manage your operational fleet.</p></div>
      <button type="button" className={`${styles.reviewFilter} ${imageModerationStatus === "Pending" ? styles.filterActive : ""}`} aria-pressed={imageModerationStatus === "Pending"} disabled={busy} onClick={() => { setPage(1); setImageModerationStatus(current => current === "Pending" ? "" : "Pending"); }}>
        <ShieldCheck size={22} aria-hidden="true" /><span>Images to review<small>Manager approvals</small></span><strong>{manualReviewCount}</strong>
      </button>
    </header>
    {Boolean(error) && <div className={styles.error} role="alert"><p>{error instanceof Error ? error.message : "Unable to load UAVs. Please try again."}</p><button disabled={busy} onClick={() => void load()}>Try again</button></div>}
    {message && <p className={styles.notice} role="status">{message}</p>}
    <form className={styles.toolbar} onSubmit={event => { event.preventDefault(); if (page === 1 && appliedKeyword === keyword.trim()) void load(); else { setPage(1); setAppliedKeyword(keyword.trim()); } }}>
      <label className={styles.search}>Search fleet<span><Search size={18} aria-hidden="true" /><input value={keyword} onChange={event => setKeyword(event.target.value)} placeholder="Code, aircraft name, model or pilot" /></span></label>
      <label>Aircraft status<select value={status} disabled={busy} onChange={event => { setStatus(event.target.value); setPage(1); }}><option value="">All statuses</option>{["Pending", "Active", "Inactive", "Rejected"].map(value => <option key={value} value={value}>{value}</option>)}</select></label>
      <button className={styles.primary} disabled={busy}>Search</button>
    </form>
    <div className={styles.layout}>
      <section className={styles.queue} aria-label="UAV fleet" aria-busy={busy}>
        <header className={styles.sectionHeading}><div><h2>Aircraft fleet</h2><p>{totalItems} aircraft matching your filters</p></div><span className={styles.count}>{totalItems}</span></header>
        {imageModerationStatus === "Pending" && <div className={styles.notice} role="status">Showing proposed images awaiting manager review. <button type="button" disabled={busy} onClick={() => { setPage(1); setImageModerationStatus(""); }}>Clear filter</button></div>}
        <div className={styles.columns} aria-hidden="true"><span>Aircraft</span><span>Operator / registered</span><span>Status</span></div>
        {busy && !selected ? <ModuleLoading label="Loading UAV fleet" /> : error && !aircraft ? <p className={styles.empty}>Fleet data is unavailable. Please retry.</p> : !aircraft?.length ? <ModuleEmpty title="No UAV found" message="Try a different search or status filter." /> : <div className={styles.rows}>{aircraft.map(uav => <button type="button" className={`${styles.row} ${selected?.id === uav.id ? styles.selected : ""}`} aria-pressed={selected?.id === uav.id} key={uav.id} onClick={() => void inspect(uav.id)} disabled={busy}>
          <span className={styles.aircraftCell}><AircraftImage key={uav.imageUrl} src={uav.imageUrl} name={uav.name} thumbnail /><span><b>{uav.code || "Unnamed UAV"}</b><small>{uav.name || "Name not specified"}</small><small>{[uav.manufacturer, uav.model].filter(Boolean).join(" · ") || "Model not specified"}</small></span></span>
          <span className={styles.owner}><b>{uav.pilotName || "Unknown operator"}</b><small>{formatVietnamDate(uav.createdAt)}</small></span>
          <span className={styles.rowStatus}><span className={styles.badge} data-status={uav.status.toLowerCase()}>{uav.status}</span>{uav.hasPendingProposal && <small className={styles.pending}>Proposal pending</small>}{uav.imageModerationStatus === "Pending" && <small className={styles.pending}>Image review pending</small>}</span>
        </button>)}</div>}
        <footer className={styles.pagination}><span role="status">Page {page} of {Math.max(1, totalPages)}</span><div><button type="button" aria-label="Previous page" disabled={busy || page <= 1} onClick={() => setPage(value => value - 1)}><ChevronLeft size={18} /></button><button type="button" aria-label="Next page" disabled={busy || page >= totalPages} onClick={() => setPage(value => value + 1)}><ChevronRight size={18} /></button></div></footer>
      </section>
      <aside className={styles.detail} aria-label="Selected UAV details" aria-busy={busy}>
        {!selected ? <div className={styles.placeholder}><Plane size={40} aria-hidden="true" /><h2>Select an aircraft</h2><p>Choose a UAV to review its information, proposals and operational status.</p></div> : <>
          <header className={styles.detailHeading}><div><p className={styles.eyebrow}>AIRCRAFT RECORD</p><h2>{selected.code || "Unnamed UAV"}</h2><p>{selected.name}</p></div><span className={styles.badge} data-status={selected.status.toLowerCase()}>{selected.status}</span></header>
          <UavReviewPanel key={selected.id} uavId={selected.id} manager onDecision={() => void inspect(selected.id)} />
          <details className={styles.disclosure}><summary>Aircraft specifications<span>Identity, battery & performance</span></summary><UavInformation aircraft={selected} /></details>
          <details className={styles.disclosure}>
            <summary><span className={styles.summaryLabel}><Camera size={17} aria-hidden="true" />Aircraft image</span>{["Approved", "Rejected"].includes(selected.imageModerationStatus ?? "") && <span className={styles.imageStatus} data-status={selected.imageModerationStatus?.toLowerCase()}><span aria-hidden="true">{selected.imageModerationStatus === "Approved" ? "✓" : "×"}</span> {selected.imageModerationStatus === "Approved" ? "Image approved" : "Rejected by admin"}</span>}</summary>
            <div className={styles.imageSection}>
              <p>Current approved image</p><AircraftImage key={`${selected.id}-${selected.imageUrl}`} src={selected.imageUrl} name={selected.name} />
              {selected.pendingImageUrl && <><p>Proposed image awaiting manager review</p><AircraftImage key={`${selected.id}-${selected.pendingImageUrl}`} src={selected.pendingImageUrl} name={`Proposed ${selected.name}`} /></>}
              {selected.pendingImageUrl && selected.imageModerationStatus === "Pending" && <div className={styles.imageActions}><p>Approving publishes the proposed image. Rejecting removes only the proposal; the approved image and UAV operational status remain unchanged.</p>
                {!rejectingImage ? <button type="button" className={styles.dangerOutline} disabled={busy} onClick={() => setRejectingImage(true)}>Review proposed image</button> : <form className={styles.imageForm} onSubmit={event => event.preventDefault()}><label>Manager decision reason<textarea required maxLength={500} value={imageReason} disabled={busy} onChange={event => setImageReason(event.target.value)} autoFocus /></label><div><button type="button" className={styles.primary} disabled={busy || !imageReason.trim()} onClick={() => void approveImage()}>Approve image</button><button type="button" className={styles.danger} disabled={busy || !imageReason.trim()} onClick={() => void rejectImage()}>Reject image</button><button type="button" disabled={busy} onClick={() => setRejectingImage(false)}>Cancel</button></div></form>}
              </div>}
            </div>
          </details>
        </>}
      </aside>
    </div>
  </div>;
}
