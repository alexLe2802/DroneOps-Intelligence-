"use client";

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
import { getLicenseFiles, type StoredLicenseEvidence } from "@/lib/license-image-store";

const dateValue = (value: string | null | undefined) => value
  ? new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(new Date(value))
  : "Not provided";

const statusClass = (status: PilotRegistrationStatus) => `request-status ${status.toLowerCase()}`;

export default function PilotRegistrationRequests() {
  const [items, setItems] = useState<PilotRegistrationSummary[]>([]);
  const [selected, setSelected] = useState<PilotRegistrationDetail | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [status, setStatus] = useState("Pending");
  const [search, setSearch] = useState("");
  const [appliedSearch, setAppliedSearch] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [totalItems, setTotalItems] = useState(0);
  const [licenseEvidence, setLicenseEvidence] = useState<StoredLicenseEvidence | null>(null);
  const [licenseFileUrls, setLicenseFileUrls] = useState<string[]>([]);

  const describeError = useCallback((value: unknown) => {
    if (value instanceof ApiError && value.status === 403) {
      return "Manager access to registration requests is not enabled by the backend yet. Ask BE to allow the operations_manager role.";
    }
    return value instanceof Error ? value.message : "Unable to load registration requests.";
  }, []);

  const loadDetail = useCallback(async (id: string) => {
    setSelectedId(id);
    setDetailLoading(true);
    setLicenseEvidence(null);
    setError("");
    try {
      const detail = await getPilotRegistration(id);
      setSelected(detail);
      setLicenseEvidence(await getLicenseFiles(detail.email));
      setRejectReason("");
    } catch (value) {
      setSelected(null);
      setError(describeError(value));
    } finally {
      setDetailLoading(false);
    }
  }, [describeError]);

  useEffect(() => {
    if (!licenseEvidence) { setLicenseFileUrls([]); return; }
    const urls = licenseEvidence.files.map(file => URL.createObjectURL(file.blob));
    setLicenseFileUrls(urls);
    return () => urls.forEach(url => URL.revokeObjectURL(url));
  }, [licenseEvidence]);

  const loadList = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await getPilotRegistrations({ status, search: appliedSearch });
      setItems(result.items);
      setTotalItems(result.totalItems);
      const nextId = result.items[0]?.id ?? null;
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
  }, [appliedSearch, describeError, loadDetail, status]);

  useEffect(() => {
    const timer = window.setTimeout(() => void loadList(), 0);
    return () => window.clearTimeout(timer);
  }, [loadList]);

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setAppliedSearch(search.trim());
  }

  async function review(action: "approve" | "reject") {
    if (!selected || busy) return;
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
      await loadList();
    } catch (value) {
      setError(describeError(value));
    } finally {
      setBusy(false);
    }
  }

  return <section className="registration-requests" aria-label="Pilot registration requests">
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
        <div className="request-license-evidence"><span>Flight license documents</span>{licenseEvidence?.files.length ? <div className="request-license-files">{licenseEvidence.files.map((file, index) => <article key={`${file.fileName}-${index}`}>{file.contentType === "application/pdf" ? <a className="request-license-pdf" href={licenseFileUrls[index]} target="_blank" rel="noreferrer"><b>PDF</b><span>{file.fileName}</span><small>Open document</small></a> : <a href={licenseFileUrls[index]} target="_blank" rel="noreferrer"><img src={licenseFileUrls[index]} alt={`Flight license page ${index + 1} submitted by ${selected.fullName}`} /></a>}<small>{file.fileName}</small></article>)}</div> : <div><b>Documents unavailable</b><p>This request has no files in this browser. The backend will provide Supabase bucket URLs later.</p></div>}</div>
        <div className="request-description"><span>Applicant description</span><p>{selected.description || "No additional description was submitted."}</p></div>
        {selected.rejectReason && <div className="request-rejection"><span>Rejection reason</span><p>{selected.rejectReason}</p></div>}
        {selected.status === "Pending" && <div className="request-review-controls">
          <label>Rejection reason<textarea value={rejectReason} onChange={event => setRejectReason(event.target.value)} maxLength={500} rows={3} placeholder="Required only when rejecting this request" /></label>
          <div><button type="button" className="request-reject" disabled={busy} onClick={() => void review("reject")}>{busy ? "Working..." : "Reject Request"}</button><button type="button" className="request-approve" disabled={busy} onClick={() => void review("approve")}>{busy ? "Working..." : "Approve Pilot"}</button></div>
        </div>}
        {selected.status !== "Pending" && <footer>Reviewed {dateValue(selected.reviewedAt)}</footer>}
      </>}
    </aside>
  </section>;
}
