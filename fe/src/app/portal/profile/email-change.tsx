"use client";

import { useEffect, useId, useRef, useState, type FormEvent } from "react";
import { api } from "@/lib/auth/client";
import styles from "./email-change.module.css";

export default function EmailChange({ onChanged }: { onChanged: () => Promise<void> }) {
  const notice = useRef<HTMLDivElement>(null);
  const noticeId = useId();
  const [errorTitle, setErrorTitle] = useState("Verification code not sent");
  const [email, setEmail] = useState("");
  const [sentTo, setSentTo] = useState("");
  const [password, setPassword] = useState("");
  const [otp, setOtp] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  useEffect(() => {
    if (error || message) notice.current?.focus();
  }, [error, message]);
  const duplicateEmail = /email.*already registered/i.test(error);
  async function send(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    setBusy(true); setError(""); setMessage(""); setErrorTitle("Verification code not sent");
    try {
      const result = await api<{ message: string }>("/users/change-email/request", { method: "POST", body: JSON.stringify({ newEmail: email.trim(), currentPassword: password }) });
      setSentTo(email.trim()); setOtp(""); setMessage(result.message);
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to send verification code."); }
    finally { setPassword(""); setBusy(false); }
  }
  async function confirm(event: FormEvent) {
    event.preventDefault(); if (busy) return;
    setBusy(true); setError(""); setMessage(""); setErrorTitle("Email could not be changed");
    try {
      await api("/users/change-email/confirm", { method: "POST", body: JSON.stringify({ newEmail: sentTo, otp: otp.trim() }) });
      setSentTo(""); setOtp(""); setEmail(""); setMessage("Email changed successfully. Use your new email the next time you sign in.");
      await onChanged();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to verify code."); }
    finally { setBusy(false); }
  }
  return <section className="mission-fields"><h2>Change email</h2><p>Your current email stays active until you verify the new address.</p>
    {(error || message) && <div id={noticeId} ref={notice} tabIndex={-1} role={error ? "alert" : "status"} className={`${styles.notice} ${error ? styles.error : styles.success}`}>
      <span className={styles.icon} aria-hidden="true">{error ? "!" : "✓"}</span>
      <div><strong>{error ? duplicateEmail ? "Email already in use" : errorTitle : sentTo ? "Verification code sent" : "Email updated"}</strong><p>{error ? duplicateEmail ? "This email address is already registered. Enter a different email address. No new verification code was sent." : error : message}</p></div>
    </div>}
    <form className="profile-form" onSubmit={send}><label>New email<input type="email" autoComplete="email" aria-invalid={duplicateEmail || undefined} aria-describedby={duplicateEmail ? noticeId : undefined} required maxLength={254} value={email} disabled={busy} onChange={e => setEmail(e.target.value)} /></label><label>Current password<input type="password" autoComplete="current-password" required value={password} disabled={busy} onChange={e => setPassword(e.target.value)} /></label><button className="module-primary" disabled={busy}>{busy ? "Working…" : sentTo ? "Send a new code" : "Send verification code"}</button><small>Codes expire after 10 minutes. Wait 60 seconds before requesting another.</small></form>
    {sentTo && <form className="profile-form" onSubmit={confirm}><p>Enter the code sent to {sentTo}.</p><label>Verification code<input autoComplete="one-time-code" required maxLength={5} minLength={5} value={otp} onChange={e => setOtp(e.target.value)} disabled={busy} /></label><button className="module-primary" disabled={busy}>Verify & change email</button></form>}
  </section>;
}
