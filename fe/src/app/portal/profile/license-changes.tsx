"use client";

import Image from "next/image";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "@/lib/auth/client";
import LicenseFilePicker from "@/app/license-file-picker";

type Change = {
  id: string; fullName: string; email: string; pilotLicenseNo: string;
  licenseIssuedDate: string; licenseExpiredDate: string; status: string;
  rejectReason: string | null; createdAt: string;
};
type Detail = Change & { currentLicenseNo: string; currentIssuedDate: string | null; currentExpiredDate: string | null; licenseImageUrls: string[] };

export default function LicenseChanges({ manager = false, onRefresh }: { manager?: boolean; onRefresh?: () => void }) {
  const [items, setItems] = useState<Change[]>([]);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [reason, setReason] = useState("");
  const [formVersion, setFormVersion] = useState(0);
  const selection = useRef(0);
  const pendingAction = useRef(false);
  const load = useCallback(async () => {
    setLoading(true); setError("");
    try { setItems(await api<Change[]>("/license-changes")); }
    catch (e) { setError(e instanceof Error ? e.message : "Unable to load license changes."); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { const requestSelection = selection; const timer = window.setTimeout(() => void load(), 0); return () => { window.clearTimeout(timer); requestSelection.current++; }; }, [load]);
  async function inspect(id: string) {
    const current = ++selection.current; setDetail(null); setError(""); setReason("");
    try { const value = await api<Detail>(`/license-changes/${id}`); if (current === selection.current) setDetail(value); }
    catch (e) { if (current === selection.current) setError(e instanceof Error ? e.message : "Unable to open documents."); }
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pendingAction.current) return;
    const values = new FormData(event.currentTarget);
    const files = values.getAll("licenseFiles").filter((f): f is File => f instanceof File && f.size > 0);
    if (!files.length || files.length > 10 || files.some(f => f.size > 10 * 1024 * 1024 || !["image/jpeg", "image/png", "image/webp", "application/pdf"].includes(f.type))) {
      setError("Attach 1–10 JPEG, PNG, WebP or PDF files, up to 10 MB each."); return;
    }
    const issued = String(values.get("licenseIssuedDate")); const expires = String(values.get("licenseExpiredDate"));
    if (expires <= issued) { setError("Expiration must be after the issue date."); return; }
    pendingAction.current = true; setBusy(true); setError(""); setMessage("");
    try {
      const body = new FormData(); body.set("data", JSON.stringify({ pilotLicenseNo: String(values.get("pilotLicenseNo")).trim(), licenseIssuedDate: issued, licenseExpiredDate: expires }));
      files.forEach(file => body.append("files", file));
      await api("/license-changes", { method: "POST", body });
      setFormVersion(v => v + 1); setMessage("License change submitted. Your current license remains active until a manager approves it."); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to submit license change."); }
    finally { pendingAction.current = false; setBusy(false); }
  }
  async function review(approve: boolean) {
    if (!detail || pendingAction.current) return;
    if (!approve && !reason.trim()) { setError("Enter a rejection reason."); return; }
    pendingAction.current = true; setBusy(true); setError(""); setMessage("");
    try {
      const result = await api<{ message: string }>(`/license-changes/${detail.id}/review`, { method: "POST", body: JSON.stringify({ approve, reason: reason.trim() || null }) });
      setMessage(result.message); setDetail(null); selection.current++; await load();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to review request."); }
    finally { pendingAction.current = false; setBusy(false); }
  }
  const pending = items.some(x => x.status === "Pending");
  return <section className="mission-fields"><h2>{manager ? "Review license changes" : "Change flight license"}</h2>
    <p>{manager ? "Review supporting documents and compare the current and proposed license before deciding." : "License number and dates change only after manager approval."}</p>
    <button type="button" className="module-secondary" disabled={busy || loading} onClick={() => { void load(); onRefresh?.(); if (detail) void inspect(detail.id); }}>Refresh</button>
    {error && <p role="alert">{error}</p>}{message && <p className="module-success" role="status">{message}</p>}
    {!manager && (loading ? <p role="status">Loading license requests…</p> : pending ? <p role="status">Your license change is pending review. You can submit another after it is reviewed.</p> : <form key={formVersion} className="profile-form" onSubmit={submit}>
      <label>New license number<input name="pilotLicenseNo" required maxLength={255} disabled={busy} /></label>
      <label>Issued date<input name="licenseIssuedDate" type="date" required disabled={busy} /></label>
      <label>Expiration date<input name="licenseExpiredDate" type="date" min={new Date().toISOString().slice(0, 10)} required disabled={busy} /></label>
      <fieldset disabled={busy}><LicenseFilePicker /></fieldset><button className="module-primary" disabled={busy}>{busy ? "Uploading…" : "Submit for approval"}</button>
    </form>)}
    <h3>{manager ? "Latest 100 requests" : "Your latest license requests"}</h3>
    {loading ? <p role="status">Loading…</p> : !items.length ? <p>No license changes submitted.</p> : <div className="request-table-wrap"><table className="request-table"><thead><tr>{manager && <th>Pilot</th>}<th>License</th><th>Status</th><th>Submitted</th><th>Details</th></tr></thead><tbody>{items.map(item => <tr key={item.id}>{manager && <td>{item.fullName}<small>{item.email}</small></td>}<td>{item.pilotLicenseNo}</td><td>{item.status}{item.rejectReason && <p>{item.rejectReason}</p>}</td><td>{new Date(item.createdAt).toLocaleDateString()}</td><td><button type="button" disabled={busy} onClick={() => void inspect(item.id)}>View documents</button></td></tr>)}</tbody></table></div>}
    {detail && <article className="request-detail-panel"><h3>{detail.fullName} · {detail.status}</h3><div className="request-detail-grid"><div><span>Current license</span><b>{detail.currentLicenseNo}</b><p>{detail.currentIssuedDate} → {detail.currentExpiredDate}</p></div><div><span>Requested license</span><b>{detail.pilotLicenseNo}</b><p>{detail.licenseIssuedDate} → {detail.licenseExpiredDate}</p></div></div>
      <div className="request-license-files">{detail.licenseImageUrls.map((url, i) => <a key={url} href={url} target="_blank" rel="noreferrer">{new URL(url).pathname.endsWith(".pdf") ? `Open license PDF ${i + 1}` : <Image unoptimized src={url} alt={`License page ${i + 1}`} width={600} height={400} style={{ width: "100%", height: "auto" }} />}</a>)}</div>
      <button type="button" className="module-secondary" disabled={busy} onClick={() => void inspect(detail.id)}>Refresh document links</button>
      {detail.rejectReason && <p>Rejection reason: {detail.rejectReason}</p>}
      {manager && detail.status === "Pending" && <div className="request-review-controls"><label>Rejection reason<textarea value={reason} maxLength={500} onChange={e => setReason(e.target.value)} disabled={busy} /></label><button className="module-primary" type="button" disabled={busy} onClick={() => void review(true)}>Approve license change</button><button className="module-danger" type="button" disabled={busy} onClick={() => void review(false)}>Reject</button></div>}
    </article>}
  </section>;
}
