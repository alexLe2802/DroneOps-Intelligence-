"use client";

import { useEffect, useId, useState } from "react";
import { api } from "@/lib/auth/client";
import { readRegistryFile } from "@/lib/portal/registry-file";
import styles from "./mission-permits.module.css";

type Document = { id: string; name: string; uploadedAt: string; decision?: string; reason?: string };

function PermitFile({ doc }: { doc: Document }) {
  return <p className={styles.fileRow}>
    <a href={`/api/documents/${doc.id}`} download={doc.name}>{doc.name}</a>
    {doc.decision && <span className={styles.fileMeta}>· {doc.decision}</span>}
    {doc.reason && <span className={styles.fileMeta}>· {doc.reason}</span>}
  </p>;
}

type PermitRecord = { documents: Document[]; planReview?: { permitDecision?: string; comment?: string } };

export default function MissionPermits({ path, status, manager, pendingPlan, refresh }: {
  path: string; status: string; manager: boolean; pendingPlan: boolean; refresh: () => void
}) {
  const fileInputId = useId();
  const fileHintId = useId();
  const reasonInputId = useId();
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

  const awaitingNote = status === "Awaiting Permit" ? (documents?.[0]?.reason || record?.planReview?.comment) : null;
  const permitDecision = record?.planReview?.permitDecision;
  const badgeLabel = status === "Awaiting Permit"
    ? "Permit required"
    : status === "Permit Review"
      ? "Awaiting review"
      : permitDecision === "Exempt"
        ? "Exempt"
        : permitDecision === "Valid"
          ? "Verified"
          : null;
  const badgeClass = status === "Awaiting Permit"
    ? `${styles.badge} ${styles.badgeWarning}`
    : status === "Permit Review"
      ? `${styles.badge} ${styles.badgeInfo}`
      : permitDecision === "Exempt" || permitDecision === "Valid"
        ? `${styles.badge} ${styles.badgeSuccess}`
        : styles.badge;

  return <section className={styles.panel} aria-label="Mission flight permit">
    <header className={styles.heading}>
      <div>
        <span className={styles.kicker}>FLIGHT PERMIT</span>
        <h2>Mission flight permit</h2>
      </div>
      {badgeLabel && <span className={badgeClass}>{badgeLabel}</span>}
    </header>

    <div className={styles.body}>
      {permitDecision === "Exempt" && <div className={`${styles.callout} ${styles.calloutSuccess}`}>
        <p>Exemption confirmed{record?.planReview?.comment ? ` · ${record.planReview.comment}` : ""}</p>
      </div>}

      {permitDecision === "Valid" && <div className={`${styles.callout} ${styles.calloutSuccess}`}>
        <p>Permit verified during plan approval.</p>
      </div>}

      {status === "Awaiting Permit" && <div className={`${styles.callout} ${styles.calloutWarning}`}>
        {awaitingNote && <p className={styles.calloutNote}>{awaitingNote}</p>}
        <p>The mission plan is approved. A missing or corrected permit is required.</p>
      </div>}

      {status === "Permit Review" && <div className={`${styles.callout} ${styles.calloutInfo}`}>
        <p>The mission plan is approved. The submitted permit awaits Manager review.</p>
      </div>}

      {documents?.[0] && <PermitFile doc={documents[0]} />}

      {documents && documents.length > 1 && <details className={styles.history}>
        <summary>Previous submissions ({documents.length - 1})</summary>
        <div className={styles.historyList}>
          {documents.slice(1).map(doc => <PermitFile key={doc.id} doc={doc} />)}
        </div>
      </details>}

      {documents?.length === 0 && <p className={styles.emptyText}>No document attached.</p>}
      {!documents && !error && <p className={styles.emptyText} role="status">Loading documents…</p>}

      {!pendingPlan && status === "Awaiting Permit" && !manager && <div className={styles.actionBox}>
        <div className={styles.field}>
          <label htmlFor={fileInputId}>Corrected flight permit</label>
          <p id={fileHintId} className={styles.hint}>Scan all pages into one PDF, up to 2 MB.</p>
          <div className={styles.uploadRow}>
            <input
              id={fileInputId}
              className={styles.fileInput}
              type="file"
              accept=".pdf,application/pdf"
              aria-describedby={fileHintId}
              disabled={busy}
              onChange={e => setFile(e.target.files?.[0] ?? null)}
            />
            <button
              type="button"
              className={styles.primaryButton}
              disabled={busy || !file}
              onClick={() => void submit()}
            >
              {busy ? "Submitting…" : "Submit permit for review"}
            </button>
          </div>
        </div>
      </div>}

      {!pendingPlan && status === "Permit Review" && manager && documents?.length ? <div className={styles.actionBox}>
        <div className={styles.field}>
          <label htmlFor={reasonInputId}>Review findings</label>
          <textarea
            id={reasonInputId}
            className={styles.textarea}
            value={reason}
            maxLength={1000}
            placeholder="Optional for acceptance; required if requesting a correction."
            onChange={e => setReason(e.target.value)}
          />
        </div>
        <div className={styles.actions}>
          <button type="button" className={styles.primaryButton} disabled={busy} onClick={() => void submit(true)}>
            {busy ? "Saving…" : "Accept permit"}
          </button>
          <button type="button" className={styles.dangerButton} disabled={busy || !reason.trim()} onClick={() => void submit(false)}>
            Request correction
          </button>
        </div>
      </div> : null}

      {error && <div className={styles.errorBanner} role="alert">
        <span>{error}</span>
        <button type="button" className={styles.secondaryButton} onClick={() => { setError(""); setRevision(n => n + 1); }}>
          Reload documents
        </button>
      </div>}
    </div>
  </section>;
}
