"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/auth/client";
import { readRegistryFile } from "@/lib/portal/registry-file";

type Document = { id: string; name: string; uploadedAt: string; decision?: string; reason?: string };

function PermitFile({ doc }: { doc: Document }) {
  return <p><a href={`/api/documents/${doc.id}`} download={doc.name}>{doc.name}</a>
    {doc.decision && <> · {doc.decision}</>}{doc.reason && <> · {doc.reason}</>}</p>;
}

type PermitRecord = { documents: Document[]; planReview?: { permitDecision?: string; comment?: string } };

export default function MissionPermits({ path, status, manager, pendingPlan, refresh }: {
  path: string; status: string; manager: boolean; pendingPlan: boolean; refresh: () => void
}) {
  const [record, setRecord] = useState<PermitRecord | null>(null);
  const documents = record?.documents;
  const [error, setError] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    let active = true;
    api<PermitRecord>(path).then(result => { if (active) setRecord(result); })
      .catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [path, revision]);

  async function submit(accept?: boolean) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      if (accept === undefined && file) {
        await api(path, { method: "POST", body: JSON.stringify(await readRegistryFile(file)) });
      } else if (accept !== undefined && documents?.[0]) {
        await api(path + "/review", { method: "POST", body: JSON.stringify({ documentId: documents[0].id, accept, reason }) });
      } else return;
      refresh();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to update the permit."); }
    finally { setBusy(false); }
  }

  return <section className="module-panel">
    <h3>Mission flight permit</h3>
    {record?.planReview?.permitDecision === "Exempt" && <p>Exemption confirmed · {record.planReview.comment}</p>}
    {record?.planReview?.permitDecision === "Valid" && <p>Permit verified during plan approval.</p>}
    {status === "Awaiting Permit" && <p>{documents?.[0]?.reason || record?.planReview?.comment}</p>}
    {documents?.[0] && <PermitFile doc={documents[0]} />}
    {documents && documents.length > 1 && <details>
      <summary>Previous submissions ({documents.length - 1})</summary>
      {documents.slice(1).map(doc => <PermitFile key={doc.id} doc={doc} />)}
    </details>}
    {documents?.length === 0 && <p>No document attached.</p>}
    {!documents && !error && <p>Loading documents…</p>}
    {status === "Awaiting Permit" && <p>The mission plan is approved. A missing or corrected permit is required.</p>}
    {status === "Permit Review" && <p>The mission plan is approved. The submitted permit awaits Manager review.</p>}
    {!pendingPlan && status === "Awaiting Permit" && !manager && <div>
      <label>Corrected flight permit · Scan all pages into one PDF, up to 2 MB.<input type="file" accept=".pdf,application/pdf" disabled={busy} onChange={e => setFile(e.target.files?.[0] ?? null)} /></label>
      <button type="button" disabled={busy || !file} onClick={() => void submit()}>Submit permit for review</button>
    </div>}
    {!pendingPlan && status === "Permit Review" && manager && documents?.length ? <div>
      <label>Review findings<textarea value={reason} maxLength={1000} onChange={e => setReason(e.target.value)} /></label>
      <button type="button" disabled={busy} onClick={() => void submit(true)}>Accept permit</button>
      <button type="button" disabled={busy || !reason.trim()} onClick={() => void submit(false)}>Request correction</button>
    </div> : null}
    {error && <p role="alert">{error} <button type="button" onClick={() => { setError(""); setRevision(n => n + 1); }}>Reload documents</button></p>}
  </section>;
}
