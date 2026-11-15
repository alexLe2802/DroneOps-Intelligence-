"use client";

import { vietnamDateYearsAgo } from "@/lib/portal/vietnam-time";

import { formatVietnamDate, formatVietnamDateTime } from "@/lib/portal/vietnam-time";

import { licenseDocumentLabel } from "@/lib/license-document";

import { validateLicenseChange } from "@/lib/license-change";

import Image from "next/image";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import styles from "./license-changes.module.css";
import { ApiError, api } from "@/lib/auth/client";
import { approvePilotUpdateRequest, rejectPilotUpdateRequest } from "@/lib/pilot-update-review";
import LicenseFilePicker from "@/app/license-file-picker";
import ReviewConfirmationDialog, { type ReviewAction } from "../review-confirmation-dialog";

type Change = {
  id: string; fullName: string; email: string; pilotLicenseNo: string;
  licenseIssuedDate: string; licenseExpiredDate: string; status: string;
  rejectReason: string | null; createdAt: string; reviewedAt: string | null;
};
type Detail = Change & { currentLicenseNo: string; currentIssuedDate: string | null; currentExpiredDate: string | null; licenseImageUrls: string[] };

export default function LicenseChanges({ manager = false, onRefresh, initialRequestId }: { manager?: boolean; onRefresh?: () => void; initialRequestId?: string }) {
  const [items, setItems] = useState<Change[]>([]);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [loading, setLoading] = useState(true);
  const [opening, setOpening] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [listError, setListError] = useState("");
  const [message, setMessage] = useState("");
  const [reason, setReason] = useState("");
  const [reviewAction, setReviewAction] = useState<ReviewAction | null>(null);
  const [formVersion, setFormVersion] = useState(0);
  const [replacement, setReplacement] = useState<Change | null>(null);
  const sectionRef = useRef<HTMLElement>(null);
  const detailAnchorRef = useRef<HTMLDivElement>(null);
  const openedFromNotification = useRef("");
  const selection = useRef(0);
  const pendingAction = useRef(false);
  const load = useCallback(async () => {
    setLoading(true); setListError("");
    try { setItems((await api<{ items: Change[] }>("/users/pilot-update-requests?pageSize=100")).items); }
    catch (e) {
      const message = e instanceof ApiError && e.status === 404
        ? "License review is unavailable. Restart the backend with the latest code, then try again."
        : e instanceof ApiError && e.status === 401 ? "Your session has expired. Sign in again."
        : e instanceof ApiError && e.status === 403 ? "Your account does not have permission to view these requests."
        : e instanceof ApiError ? `Unable to load license changes (HTTP ${e.status}). Please try again.`
        : "Unable to connect to the license review service. Please try again.";
      setListError(message);
    }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { const requestSelection = selection; const timer = window.setTimeout(() => void load(), 0); return () => { window.clearTimeout(timer); requestSelection.current++; }; }, [load]);
  const inspect = useCallback(async (id: string) => {
    const current = ++selection.current; setDetail(null); setOpening(true); setError(""); setReason(""); setReviewAction(null);
    try { const value = await api<Detail>(`/users/pilot-update-requests/${id}`); if (current === selection.current) setDetail(value); }
    catch (e) { if (current === selection.current) setError(e instanceof Error ? e.message : "Unable to open documents."); }
    finally { if (current === selection.current) setOpening(false); }
  }, []);
  useEffect(() => {
    if (!detail || opening) return;
    const frame = window.requestAnimationFrame(() => detailAnchorRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }));
    return () => window.cancelAnimationFrame(frame);
  }, [detail, opening]);
  useEffect(() => {
    if (!initialRequestId || openedFromNotification.current === initialRequestId) return;
    openedFromNotification.current = initialRequestId;
    const timer = window.setTimeout(() => {
      sectionRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
      void inspect(initialRequestId);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [initialRequestId, inspect]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pendingAction.current) return;
    const values = new FormData(event.currentTarget);
    const files = values.getAll("licenseFiles").filter((f): f is File => f instanceof File && f.size > 0);
    const issued = String(values.get("licenseIssuedDate")); const expires = String(values.get("licenseExpiredDate"));
    const validationError = validateLicenseChange(String(values.get("pilotLicenseNo") ?? ""), issued, expires, files);
    if (validationError) { setError(validationError); return; }
    pendingAction.current = true; setBusy(true); setError(""); setMessage("");
    try {
      const body = new FormData();
      body.set("PilotLicenseNo", String(values.get("pilotLicenseNo")).trim());
      body.set("LicenseIssuedDate", issued);
      body.set("LicenseExpiredDate", expires);
      const expectedPending = String(values.get("expectedPendingRequestId") ?? "");
      if (expectedPending) body.set("ExpectedPendingRequestId", expectedPending);
      files.forEach(file => body.append("Files", file));
      await api("/users/pilot-update-requests", { method: "POST", body });
      selection.current++; setDetail(null); setReplacement(null); setFormVersion(v => v + 1); setMessage(expectedPending ? "Replacement submitted. The previous request is superseded; your approved license is unchanged." : "License change submitted. Your current license remains active until a manager approves it."); await load();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) { setReplacement(null); setError("The pending request changed or was already reviewed. The latest history has been loaded; inspect it before trying again."); await load(); }
      else setError(e instanceof Error ? e.message : "Unable to confirm submission. Refresh the request history before retrying.");
    }
    finally { pendingAction.current = false; setBusy(false); }
  }
  async function review() {
    if (!detail || !reviewAction || pendingAction.current) return;
    const action = reviewAction;
    const approve = action === "approve";
    if (!approve && !reason.trim()) { setError("Enter a rejection reason."); return; }
    pendingAction.current = true; setBusy(true); setError(""); setMessage("");
    try {
      if (approve) await approvePilotUpdateRequest(detail.id);
      else await rejectPilotUpdateRequest(detail.id, reason);
      setMessage(approve ? "License change approved." : "License change rejected."); setReviewAction(null); setDetail(null); selection.current++; await load();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setReviewAction(null); setDetail(null); selection.current++; await load();
        setError("This request changed or was already reviewed. The latest data has been loaded.");
      } else {
        setError(e instanceof Error ? e.message : "Unable to review request.");
      }
    } finally { pendingAction.current = false; setBusy(false); }
  }
  const pending = items.find(x => x.status === "Pending");
  const rejectedRevision = !manager && !pending && detail?.status === "Rejected" ? detail : null;
  const submissionForm = (source?: Change) => <form key={`${formVersion}-${source?.id ?? "new"}`} className="profile-form" onSubmit={submit} onChange={() => { if (error) setError(""); if (message) setMessage(""); }}>
    {source?.status === "Pending" && <input type="hidden" name="expectedPendingRequestId" value={source.id} />}
    {source && <div className={styles.revisionHeading}><span>{source.status === "Pending" ? "REPLACE PENDING REQUEST" : "REVISE REJECTED REQUEST"}</span><h3>{source.status === "Pending" ? "Submit a replacement for review" : "Correct and submit a new request"}</h3><p>{source.status === "Pending" ? "After a successful submission, this pending request becomes Superseded and cannot be approved. Attach the supporting file again. Your approved license stays unchanged." : "The rejected request remains in your history. Update the fields below and attach the supporting file again to create a new review request."}</p>{source.rejectReason && <strong>Manager feedback: {source.rejectReason}</strong>}</div>}
    <label>{source ? "Revised license number" : "New license number"}<input name="pilotLicenseNo" defaultValue={source?.pilotLicenseNo ?? ""} required maxLength={255} disabled={busy} /></label>
    <div className={styles.dateGrid}>
      <label>Issued date<span className={styles.dateControl}><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 3v3m10-3v3M4.5 9h15M6 5h12a2 2 0 0 1 2 2v12H4V7a2 2 0 0 1 2-2Z" /></svg><input name="licenseIssuedDate" type="date" defaultValue={source?.licenseIssuedDate?.slice(0, 10) ?? ""} required disabled={busy} onClick={event => event.currentTarget.showPicker?.()} /></span></label>
      <label>Expiration date<span className={styles.dateControl}><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 3v3m10-3v3M4.5 9h15M6 5h12a2 2 0 0 1 2 2v12H4V7a2 2 0 0 1 2-2Z" /></svg><input name="licenseExpiredDate" type="date" defaultValue={source?.licenseExpiredDate?.slice(0, 10) ?? ""} min={vietnamDateYearsAgo()} required disabled={busy} onClick={event => event.currentTarget.showPicker?.()} /></span></label>
    </div>
    <fieldset className={styles.uploadFieldset} disabled={busy}><LicenseFilePicker allowImages /></fieldset>
    {source && error && <p className={styles.error} role="alert">{error}</p>}
    <button className="module-primary" disabled={busy}>{busy ? "Uploading…" : source?.status === "Pending" ? "Submit replacement for approval" : source ? "Submit revised license" : "Submit for approval"}</button>
    {source?.status === "Pending" && <button className="module-secondary" type="button" disabled={busy} onClick={() => setReplacement(null)}>Cancel replacement</button>}
  </form>;
  return <section id="license-change-requests" ref={sectionRef} className={styles.panel}><header className={styles.header}><div><p className={styles.kicker}>FLIGHT LICENSE / {manager ? "MANAGER REVIEW" : "YOUR DOCUMENTS"}</p><h2>{manager ? "Review license changes" : "Change flight license"}</h2>
    <p className={styles.description}>{manager ? "Review supporting documents and compare the current and proposed license before deciding." : "Your approved license stays unchanged while a request is pending or rejected. You may replace a pending request; its previous version remains in history as Superseded and cannot be approved."}</p>
    </div><button type="button" className="module-secondary" disabled={busy || loading} onClick={() => { void load(); onRefresh?.(); if (detail) void inspect(detail.id); }}>{loading ? "Refreshing…" : "↻ Refresh"}</button></header>
    {listError && <div className={styles.error} role="alert"><strong>Unable to load requests</strong><p>{listError}</p></div>}
    {error && !rejectedRevision && <p className={styles.error} role="alert">{error}</p>}{message && <p className="module-success" role="status">{message}</p>}
    {!manager && !listError && (loading ? <p role="status">Loading license requests…</p> : replacement ? submissionForm(replacement) : pending ? <div><p role="status">Your license change is pending review.</p><button className="module-secondary" type="button" disabled={busy} onClick={() => { setReplacement(pending); setError(""); setMessage(""); }}>Replace pending request</button></div> : rejectedRevision ? null : submissionForm())}
    <div className={styles.listHeading}><h3>{manager ? "License change requests" : "Your license requests"}</h3><span>{loading ? "Loading" : listError ? "Unavailable" : `${items.length} requests · latest 100`}</span></div>
    {loading ? <div className={styles.empty} role="status">Loading license change requests…</div> : listError ? null : !items.length ? <div className={styles.empty}><span aria-hidden="true">▤</span><h3>{manager ? "No license changes to review" : "No license changes yet"}</h3><p>{manager ? "Requests will appear here when an operator submits an updated flight license." : "Your submitted requests and review results will appear here."}</p></div> : <div className="request-table-wrap"><table className="request-table"><thead><tr>{manager && <th>Pilot</th>}<th>License</th><th>Status</th><th>Submitted / decision</th><th>Details</th></tr></thead><tbody>{items.map(item => <tr key={item.id}>{manager && <td>{item.fullName}<small>{item.email}</small></td>}<td>{item.pilotLicenseNo}</td><td><span className={`${styles.status} ${item.status === "Approved" ? styles.approved : item.status === "Rejected" ? styles.rejected : styles.pending}`}>{item.status}</span>{item.rejectReason && <p>{item.rejectReason}</p>}</td><td>{formatVietnamDate(item.createdAt)}{item.reviewedAt && <small>Decision: {formatVietnamDateTime(item.reviewedAt)}</small>}{item.status === "Superseded" && <small>Replaced by a newer request; this request cannot be approved.</small>}</td><td><button type="button" disabled={busy} onClick={() => void inspect(item.id)}>View documents</button></td></tr>)}</tbody></table></div>}
    <div ref={detailAnchorRef} className={styles.detailAnchor}>
    {opening && <div className={styles.detailLoading} role="status"><span aria-hidden="true" />Loading private documents…</div>}
    {detail && <article className={`request-detail-panel ${styles.detail}`}><h3>{detail.fullName} · {detail.status}</h3><div className="request-detail-grid"><div><span>Current license</span><b>{detail.currentLicenseNo}</b><p>{detail.currentIssuedDate} → {detail.currentExpiredDate}</p></div><div><span>Requested license</span><b>{detail.pilotLicenseNo}</b><p>{detail.licenseIssuedDate} → {detail.licenseExpiredDate}</p></div></div>
      <div className="request-license-files">{detail.licenseImageUrls.map((url, i) => <a key={url} href={url} target="_blank" rel="noreferrer">{licenseDocumentLabel(url) ? `Open license ${licenseDocumentLabel(url)} ${i + 1}` : <Image unoptimized src={url} alt={`License page ${i + 1}`} width={600} height={400} style={{ width: "100%", height: "auto" }} />}</a>)}</div>
      <button type="button" className="module-secondary" disabled={busy} onClick={() => void inspect(detail.id)}>Refresh document links</button>
      {detail.rejectReason && <p>Rejection reason: {detail.rejectReason}</p>}
      {manager && detail.status === "Pending" && <div className="request-review-controls"><p>Confirm the decision before it is sent. A reason is required when rejecting.</p><button className="module-primary" type="button" disabled={busy} onClick={() => setReviewAction("approve")}>Review approval</button><button className="module-danger" type="button" disabled={busy} onClick={() => setReviewAction("reject")}>Review rejection</button></div>}
      {rejectedRevision && <div className={styles.resubmit}>{submissionForm(rejectedRevision)}</div>}
    </article>}
    </div>
    <ReviewConfirmationDialog action={reviewAction} requestKind="license change" pilotName={detail?.fullName ?? "Selected pilot"} currentLicense={detail?.currentLicenseNo} proposedLicense={detail?.pilotLicenseNo} reason={reason} busy={busy} onReasonChange={setReason} onCancel={() => { if (!busy) setReviewAction(null); }} onConfirm={() => void review()} />
  </section>;
}
