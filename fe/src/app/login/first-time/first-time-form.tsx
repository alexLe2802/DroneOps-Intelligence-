"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "@/lib/auth/client";
import type { AuthSession } from "@/lib/auth/types";

export default function FirstTimeForm() {
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const [cooldown, setCooldown] = useState(0);
  useEffect(() => { if (!cooldown) return; const timer = window.setTimeout(() => setCooldown(value => value - 1), 1000); return () => window.clearTimeout(timer); }, [cooldown]);
  const [error, setError] = useState("");
  const pending = useRef(false);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (pending.current) return;
    const form = event.currentTarget;
    const values = new FormData(form);
    pending.current = true; setBusy(true); setError("");
    try {
      const session = await api<AuthSession>("/pilot-registrations/first-time-login", { method: "POST", body: JSON.stringify({ otp: String(values.get("otp")).trim() }) });
      if (session.account.role !== "uav_operator") {
        await api("/auth/logout", { method: "POST" });
        throw new Error("This activation page is for UAV Operator accounts.");
      }
      window.location.replace("/portal");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to activate your account. Please try again."); }
    finally {
      pending.current = false; setBusy(false);
    }
  }
  async function resend() {
    if (pending.current || cooldown) return;
    pending.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const result = await api<{ message: string; retryAfterSeconds: number }>("/pilot-registrations/first-time-login/resend-otp", { method: "POST", body: "{}" });
      setNotice(result.message); setCooldown(result.retryAfterSeconds);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to resend OTP."); }
    finally { pending.current = false; setBusy(false); }
  }
  return <form className="email-login-form" onSubmit={submit} aria-busy={busy}>
    <label htmlFor="activation-otp">APPROVAL OTP</label><input id="activation-otp" name="otp" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" minLength={6} maxLength={6} required disabled={busy} placeholder="6-digit code" />
    <p className="auth-feedback">The OTP emailed when your account was approved expires in 10 minutes. If it has expired or you did not receive it, request a new code. Check Spam/Junk too.</p>
    {notice && <p className="auth-feedback" role="status">{notice}</p>}
    <button type="button" className="button secondary" disabled={busy || cooldown > 0} onClick={() => void resend()}>{cooldown ? `RESEND OTP IN ${cooldown}s` : "RESEND OTP"}</button>
    {error && <p className="auth-error" role="alert">{error}</p>}
    <button className="button primary email-login-submit" disabled={busy}>{busy ? "VERIFYING…" : "VERIFY OTP & ENTER PORTAL"}</button>
    <Link className="pilot-register-link" href="/login">Back to sign in</Link>
  </form>;
}
