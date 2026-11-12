"use client";

import { apiInstant } from "@/lib/portal/vietnam-time";

import { licenseDocumentLabel } from "@/lib/license-document";

import Image from "next/image";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ApiError } from "@/lib/auth/client";
import {
  approvePilotRegistration,
  getPilotRegistration,
  getPilotRegistrations,
  rejectPilotRegistration,
  type PilotRegistrationDetail,
  type PilotRegistrationStatus,
  type PilotRegistrationSummary,
} from "@/lib/pilot-registration";
import ReviewConfirmationDialog, { type ReviewAction } from "../review-confirmation-dialog";

const dateValue = (value: string | null | undefined) => value
  ? new Intl.DateTimeFormat("en-GB", { timeZone: "Asia/Ho_Chi_Minh", day: "2-digit", month: "short", year: "numeric" }).format(apiInstant(value))
  : "Not provided";

const statusClass = (status: PilotRegistrationStatus) => `request-status ${status.toLowerCase()}`;

export default function PilotRegistrationRequests({ initialRegistrationId }: { initialRegistrationId?: string }) {
  const [items, setItems] = useState<PilotRegistrationSummary[]>([]);
  const [selected, setSelected] = useState<PilotRegistrationDetail | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState("Pending");
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const [pendingAction, setPendingAction] = useState<ReviewAction | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [totalItems, setTotalItems] = useState(0);

  const describeError = useCallback((value: unknown) => {
    if (value instanceof ApiError && value.status === 403) {
      return "Your account does not have permission to review registration requests. Sign in with an Operations Manager account.";
    }
    return value instanceof Error ? value.message : "Unable to load registration requests.";
  }, []);

  const loadDetail = useCallback(async (id: string) => {
    setSelectedId(id);
    setDetailLoading(true);
    setError("");
    try {
      const detail = await getPilotRegistration(id);
      setSelected(detail);
      setRejectReason("");
      setPendingAction(null);
    } catch (value) {
      setSelected(null);
      setError(describeError(value));
    } finally {
      setDetailLoading(false);
    }
  }, [describeError]);

  const loadList = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await getPilotRegistrations({ status, search: appliedSearch });
      setItems(result.items);
      setTotalItems(result.totalItems);
      const nextId = initialRegistrationId ?? result.items[0]?.id ?? null;
      if (nextId) await loadDetail(nextId);
      else { setSelectedId(null); setSelected(null); }
    } catch (value) {
      setItems([]);
      setSelected(null);
      setTotalItems(0);
      setError(describeError(value));
    } finally {
      setLoading(false);
    }
  }, [appliedSearch, describeError, initialRegistrationId, loadDetail, status]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadList(), 0);
    return () => window.clearTimeout(timer);
  }, [loadList]);

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setAppliedSearch(search.trim());
  }

  async function review() {
    if (!selected || !pendingAction || busy) return;
    const action = pendingAction;
    if (action === "reject" && !rejectReason.trim()) {
      setError("Enter a rejection reason before rejecting this request.");
      return;
    }
    setBusy(true); setError(""); setMessage("");
    try {
      const result = action === "approve"
        ? await approvePilotRegistration(selected.id)
        : await rejectPilotRegistration(selected.id, rejectReason);
      setMessage(result.message);
      setSelected(null); setSelectedId(null); setRejectReason("");
      setPendingAction(null);
      await loadList();
    } catch (value) {
      const staleDecision = value instanceof ApiError && (value.status === 409
        || value.status === 400 && /already|currently in|cannot be/i.test(value.message));
      if (staleDecision) {
        setPendingAction(null);
        await loadList();
        setError("This request changed or was already reviewed. The latest data has been loaded.");
      } else {
        setError(describeError(value));
      }
    } finally {
      setBusy(false);
    }
  }

  return <section id="registration-requests" className="registration-requests" aria-label="Pilot registration requests">
    <div className="request-list-panel">
      <header className="request-panel-heading">
        <div><span className="auth-kicker">PILOT INTAKE QUEUE</span><h2>Registration Requests</h2><p>Review identity, license, and operating details submitted by new Pilots.</p></div>
        <strong>{String(totalItems).padStart(2, "0")}<small> records</small></strong>
      </header>

      <form className="request-filter-strip" onSubmit={submitSearch}>
        <label><span className="sr-only">Search registration requests</span><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Search name or email" /></label>
        <label><span className="sr-only">Request status</span><select value={status} onChange={event => setStatus(event.target.value)}><option>Pending</option><option>Approved</option><option>Rejected</option><option>All</option></select></label>
        <button type="submit" disabled={loading}>Search</button>
        <button type="button" aria-label="Refresh registration requests" disabled={loading} onClick={() => void loadList()}>Refresh</button>
      </form>

      {error && <div className="request-error" role="alert"><b>REQUEST CHANNEL UNAVAILABLE</b><p>{error}</p><button type="button" onClick={() => void loadList()} disabled={loading}>Try again</button></div>}
      {message && <p className="request-message" role="status">{message}</p>}
      {loading && <div className="request-empty" role="status">Loading registration requests...</div>}
      {!loading && !error && items.length === 0 && <div className="request-empty"><b>No matching requests</b><span>New Pilot submissions will appear here after the login form reaches the backend.</span></div>}
      {!loading && items.length > 0 && <div className="request-table-wrap"><table className="request-table">
        <thead><tr><th>Applicant</th><th>License</th><th>Submitted</th><th>Status</th></tr></thead>
        <tbody>{items.map(item => <tr key={item.id} className={selectedId === item.id ? "selected" : ""}>
          <td><button type="button" onClick={() => void loadDetail(item.id)}><strong>{item.fullName}</strong><span>{item.email}</span></button></td>
          <td><b>{item.pilotLicenseNo || "Not supplied"}</b><span>Flight license</span></td>
          <td>{dateValue(item.createdAt)}</td>
          <td><span className={statusClass(item.status)}>{item.status}</span></td>
        </tr>)}</tbody>
      </table></div>}
    </div>

    <aside className="request-detail-panel">
      {detailLoading && <div className="request-empty" role="status">Loading request details...</div>}
      {!detailLoading && !selected && <div className="request-empty"><b>No request selected</b><span>Select a registration request to inspect its full submission.</span></div>}
      {!detailLoading && selected && <>
        <header><div><span className="auth-kicker">APPLICATION DOSSIER</span><h2>{selected.fullName}</h2><p>{selected.email}</p></div><span className={statusClass(selected.status)}>{selected.status}</span></header>
        <div className="request-detail-grid">
          <div><span>Phone number</span><b>{selected.phoneNumber || "Not provided"}</b></div>
          <div><span>Date of birth</span><b>{dateValue(selected.dateOfBirth)}</b></div>
          <div><span>Pilot license</span><b>{selected.pilotLicenseNo || "Not provided"}</b></div>
          <div><span>Experience</span><b>{selected.experienceYears ?? 0} years</b></div>
          <div><span>License issued</span><b>{dateValue(selected.licenseIssuedDate)}</b></div>
          <div><span>License expires</span><b>{dateValue(selected.licenseExpiredDate)}</b></div>
        </div>
        <div className="request-license-evidence"><span>Flight license documents</span>{selected.licenseImageUrls?.length ? <div className="request-license-files">{selected.licenseImageUrls.map((url, index) => <article key={url}><a href={url} target="_blank" rel="noreferrer">{licenseDocumentLabel(url) ? <b>Open license {licenseDocumentLabel(url)} {index + 1}</b> : <Image unoptimized width={800} height={800} style={{ width: "100%", height: "auto" }} src={url} alt={`Flight license page ${index + 1} submitted by ${selected.fullName}`} />}</a><small>License document {index + 1}</small></article>)}</div> : <div><b>Documents unavailable</b><p>No documents were uploaded with this application.</p></div>}<button type="button" onClick={() => void loadDetail(selected.id)}>Refresh document links</button></div>
        <div className="request-description"><span>Flight experience</span><p>{selected.description || "No additional description was submitted."}</p></div>
        {selected.rejectReason && <div className="request-rejection"><span>Rejection reason</span><p>{selected.rejectReason}</p></div>}
        {selected.status === "Pending" && <div className="request-review-controls"><p>Confirm the decision before it is sent. A reason is required when rejecting.</p><div><button type="button" className="request-reject" disabled={busy} onClick={() => setPendingAction("reject")}>Review rejection</button><button type="button" className="request-approve" disabled={busy} onClick={() => setPendingAction("approve")}>Review approval</button></div></div>}
        {selected.status !== "Pending" && <footer>Reviewed {dateValue(selected.reviewedAt)}</footer>}
      </>}
    </aside>
    <ReviewConfirmationDialog action={pendingAction} requestKind="pilot registration" pilotName={selected?.fullName ?? "Selected pilot"} proposedLicense={selected?.pilotLicenseNo} reason={rejectReason} busy={busy} onReasonChange={setRejectReason} onCancel={() => { if (!busy) setPendingAction(null); }} onConfirm={() => void review()} />
  </section>;
}
