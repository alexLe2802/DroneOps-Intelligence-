"use client";

import { useEffect } from "react";
import styles from "./review-confirmation-dialog.module.css";

export type ReviewAction = "approve" | "reject";

type Props = {
  action: ReviewAction | null;
  requestKind: string;
  pilotName: string;
  currentLicense?: string | null;
  proposedLicense?: string | null;
  reason: string;
  busy: boolean;
  onReasonChange: (value: string) => void;
  onCancel: () => void;
  onConfirm: () => void;
};

export default function ReviewConfirmationDialog({
  action,
  requestKind,
  pilotName,
  currentLicense,
  proposedLicense,
  reason,
  busy,
  onReasonChange,
  onCancel,
  onConfirm,
}: Props) {
  useEffect(() => {
    if (!action) return;
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busy) onCancel();
    };
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [action, busy, onCancel]);

  if (!action) return null;
  const rejecting = action === "reject";
  const reasonMissing = rejecting && !reason.trim();
  const title = rejecting ? `Reject ${requestKind}?` : `Approve ${requestKind}?`;

  return <div className={styles.backdrop} role="presentation">
    <section className={styles.dialog} role="dialog" aria-modal="true" aria-labelledby="review-confirmation-title">
      <header><div><span>MANAGER DECISION</span><h2 id="review-confirmation-title">{title}</h2></div><button type="button" aria-label="Close confirmation" disabled={busy} onClick={onCancel}>×</button></header>
      <div className={styles.body}>
        <p>{rejecting
          ? "The request will remain in history and the proposed credentials will not be applied."
          : "The reviewed credentials will become the approved record after the server accepts this decision."}</p>
        <dl>
          <div><dt>Pilot</dt><dd>{pilotName}</dd></div>
          {currentLicense !== undefined && <div><dt>Current license</dt><dd>{currentLicense || "Not registered"}</dd></div>}
          <div><dt>Proposed license</dt><dd>{proposedLicense || "Not provided"}</dd></div>
        </dl>
        {rejecting && <label>REJECTION REASON · REQUIRED<textarea autoFocus required maxLength={1000} value={reason} disabled={busy} aria-invalid={reasonMissing} onChange={event => onReasonChange(event.target.value)} placeholder="Explain why this request cannot be approved" /><small>{reason.trim().length}/1000 characters</small></label>}
      </div>
      <footer><button type="button" className={styles.cancel} disabled={busy} onClick={onCancel}>Cancel</button><button type="button" className={rejecting ? styles.reject : styles.approve} disabled={busy || reasonMissing} onClick={onConfirm}>{busy ? "Submitting…" : rejecting ? "Confirm rejection" : "Confirm approval"}</button></footer>
    </section>
  </div>;
}
