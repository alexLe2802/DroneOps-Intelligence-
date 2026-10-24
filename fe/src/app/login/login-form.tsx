"use client";

import { isLicenseDocument } from "@/lib/license-document";

import { useRouter } from "next/navigation";
import { useRef, useState, type FormEvent } from "react";
import { api } from "@/lib/auth/client";
import type { AccountRole, AuthSession } from "@/lib/auth/types";
import { createPilotRegistration } from "@/lib/pilot-registration";
import LicenseFilePicker from "@/app/license-file-picker";

type Action = "password" | "reset" | "register";
type AccessMode = "pilot" | "manager";


const roleByMode: Record<AccessMode, AccountRole> = { pilot: "uav_operator", manager: "operation_manager" };
const roleMismatch: Record<AccessMode, string> = {
  pilot: "This account belongs to Manager Access. Use the Manager panel to sign in.",
  manager: "This account belongs to Pilot Access. Use the Pilot panel to sign in.",
};

export default function LoginForm() {
  const router = useRouter();
  const [mode, setMode] = useState<AccessMode>("pilot");
  const [busy, setBusy] = useState<Action | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [registerOpen, setRegisterOpen] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [resetOtp, setResetOtp] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const inProgress = useRef(false);
  const emailInput = useRef<HTMLInputElement>(null);
  const passwordInput = useRef<HTMLInputElement>(null);

  async function login(action: Action) {
    if (inProgress.current) return;
    if (action === "reset" && !emailInput.current?.reportValidity()) return;
    inProgress.current = true;
    setBusy(action); setError(""); setNotice("");
    let completed = false;
    try {
      if (action === "reset") {
        const result = await api<{ message?: string; Message?: string }>("/auth/forgot-password", { method: "POST", body: JSON.stringify({ email: emailInput.current!.value.trim() }) });
        setResetOpen(true); setNotice(result.message ?? result.Message ?? "Reset code sent. Check your email.");
        return;
      }
      const session = await api<AuthSession | { code: "first_login_required"; requiresFirstLogin: true }>("/auth/login", {
        method: "POST", body: JSON.stringify({ email: emailInput.current!.value.trim(), password: passwordInput.current!.value }),
      });
      if ("requiresFirstLogin" in session && session.requiresFirstLogin) {
        router.push("/login/first-time"); return;
      }
      if (!("account" in session)) throw new Error("Invalid login response.");
      if (session.account.role !== roleByMode[mode]) {
        await api("/auth/logout", { method: "POST" });
        setError(roleMismatch[mode]); return;
      }
      completed = true;
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to complete your request.");
    } finally {
      if (passwordInput.current) passwordInput.current.value = "";
      setShowPassword(false);
      inProgress.current = false; setBusy(null);
      if (completed) window.location.replace("/portal");
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); void login("password");
  }

  async function submitRegistration(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (inProgress.current) return;
    const form = event.currentTarget;
    const values = new FormData(form);
    const read = (name: string) => String(values.get(name) ?? "").trim();
    const password = String(values.get("password") ?? "");
    if (password !== values.get("confirmPassword")) {
      setError("Passwords do not match.");
      return;
    }
    const licenseIssuedDate = read("licenseIssuedDate");
    const licenseExpiredDate = read("licenseExpiredDate");
    const licenseFiles = values.getAll("licenseFiles").filter((value): value is File => value instanceof File && value.size > 0);
    if (licenseFiles.length !== 1 || licenseFiles.some(file => !isLicenseDocument(file) || file.size > 10 * 1024 * 1024)) {
      setError("Attach exactly one PDF or Word (.doc, .docx) file, up to 10 MB."); return;
    }
    if (new Date(licenseExpiredDate) <= new Date(licenseIssuedDate)) {
      setError("License expiration date must be after the issued date.");
      return;
    }
    inProgress.current = true;
    setBusy("register"); setError(""); setNotice("");
    try {
      const result = await createPilotRegistration({
        fullName: read("fullName"), email: read("email"), password,
        phoneNumber: read("phoneNumber"), dateOfBirth: read("dateOfBirth"),
        pilotLicenseNo: read("pilotLicenseNo"), licenseIssuedDate, licenseExpiredDate,
        experienceYears: Number(read("experienceYears") || 0),
        droneType: "Not specified", usagePurpose: "Not specified",
        description: read("description") || null,
      }, licenseFiles);
      form.reset();
      setRegisterOpen(false);
      setNotice(result.message || "Registration submitted. An Operations Manager will review your request.");
    } catch (value) {
      setError(value instanceof Error ? value.message : "Unable to submit your registration request.");
    } finally {
      for (const name of ["password", "confirmPassword"]) {
        const input = form.elements.namedItem(name);
        if (input instanceof HTMLInputElement) input.value = "";
      }
      inProgress.current = false;
      setBusy(null);
    }
  }

  return <div className="login-action">
    <div className="access-tabs" role="tablist" aria-label="Access type">
      <button type="button" role="tab" aria-selected={mode === "pilot"} className={mode === "pilot" ? "active" : ""} onClick={() => { setMode("pilot"); setError(""); setNotice(""); }}>UAV Operator / Pilot</button>
      <button type="button" role="tab" aria-selected={mode === "manager"} className={mode === "manager" ? "active" : ""} onClick={() => { setMode("manager"); setError(""); setNotice(""); }}>Operations Manager</button>
    </div>

    <form className="email-login-form" onSubmit={submit} aria-busy={busy !== null}>
      <label htmlFor="login-email">OPERATOR IDENTIFIER / EMAIL</label>
      <input ref={emailInput} id="login-email" name="email" type="email" required maxLength={254} autoComplete="username" placeholder={mode === "pilot" ? "pilot@droneops.com" : "manager@droneops.com"} disabled={busy !== null} />
      <div className="password-label"><label htmlFor="login-password">SECURITY TOKEN / PASSWORD</label><button type="button" className="text-button" disabled={busy !== null} onClick={() => void login("reset")}>Forgot credentials?</button></div>
      <div className="password-field"><input ref={passwordInput} id="login-password" name="password" type={showPassword ? "text" : "password"} required maxLength={4096} autoComplete="current-password" disabled={busy !== null} /><button type="button" aria-label={showPassword ? "Hide password" : "Show password"} aria-pressed={showPassword} disabled={busy !== null} onClick={() => setShowPassword(!showPassword)}>{showPassword ? "Hide" : "Show"}</button></div>
      <button type="submit" className="button primary email-login-submit" disabled={busy !== null}>{busy === "password" ? "Authenticating..." : "Authenticate & Enter Portal"}<span aria-hidden="true">→</span></button>
    </form>

    {mode === "pilot" && <button type="button" className="pilot-register-link" disabled={busy !== null} onClick={() => { setRegisterOpen(true); setNotice(""); setError(""); }}>Request Pilot Registration</button>}

    {busy === "reset" && <p className="auth-feedback" role="status">Requesting a password reset...</p>}
    {resetOpen && <form className="email-login-form" onSubmit={async event => {
      event.preventDefault(); if (inProgress.current) return;
      inProgress.current = true; setBusy("reset"); setError("");
      try {
        if (newPassword !== confirmPassword) throw new Error("Passwords do not match.");
        const result = await api<{ message?: string; Message?: string }>("/auth/reset-password", { method: "POST", body: JSON.stringify({ email: emailInput.current!.value.trim(), otp: resetOtp.trim(), newPassword, confirmPassword }) });
        await api("/auth/logout", { method: "POST" });
        setNotice(result.message ?? result.Message ?? "Password updated. Sign in again."); setResetOpen(false); setResetOtp("");
      } catch (value) { setError(value instanceof Error ? value.message : "Unable to reset password."); }
      finally { setNewPassword(""); setConfirmPassword(""); setBusy(null); inProgress.current = false; }
    }}><h3>Reset password</h3><label>EMAIL OTP<input value={resetOtp} onChange={event => setResetOtp(event.target.value)} required autoComplete="one-time-code" /></label><label>NEW PASSWORD<input type="password" value={newPassword} onChange={event => setNewPassword(event.target.value)} required minLength={8} autoComplete="new-password" /></label><label>CONFIRM PASSWORD<input type="password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} required minLength={8} autoComplete="new-password" /></label><button className="button primary" disabled={busy !== null}>RESET PASSWORD</button><button type="button" className="text-button" disabled={busy !== null} onClick={() => { setResetOpen(false); setNewPassword(""); setConfirmPassword(""); }}>CANCEL</button></form>}
    {notice && <p className="auth-feedback" role="status">{notice}</p>}
    {error && !registerOpen && <p className="auth-error" role="alert">{error}</p>}


    {registerOpen && <div className="pilot-register-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) { setRegisterOpen(false); setError(""); } }}>
      <form className="pilot-register-modal" aria-label="Pilot registration request" onSubmit={submitRegistration}>
        <header><div><span className="auth-kicker">PILOT ACCESS REQUEST</span><h3>Request Pilot Registration</h3><p>Your request will be reviewed by an Operations Manager.</p></div><button type="button" aria-label="Close registration form" onClick={() => { setRegisterOpen(false); setError(""); }}>×</button></header>
        <div className="pilot-register-grid">
          <label>Full name<input required name="fullName" maxLength={120} placeholder="Nguyen Long" /></label>
          <label>Email<input required name="email" type="email" maxLength={254} placeholder="pilot@example.com" /></label>
          <label>Phone number<input required name="phoneNumber" type="tel" minLength={8} maxLength={20} placeholder="+84..." /></label>
          <label>Date of birth<input required name="dateOfBirth" type="date" /></label>
          <label>Initial password<input required name="password" type="password" minLength={12} maxLength={100} autoComplete="new-password" /></label>
          <label>Confirm password<input required name="confirmPassword" type="password" minLength={12} maxLength={100} autoComplete="new-password" /></label>
          <label>Pilot license number<input required name="pilotLicenseNo" maxLength={80} placeholder="CAAV-UAV-88412" /></label>
          <label>Experience years<input required name="experienceYears" type="number" min={0} max={80} defaultValue={0} /></label>
          <label>License issued date<input required name="licenseIssuedDate" type="date" /></label>
          <label>License expired date<input required name="licenseExpiredDate" type="date" /></label>
          <LicenseFilePicker />
          <label className="pilot-register-wide">Briefly describe your experience<textarea name="description" rows={4} maxLength={500} placeholder="Briefly describe your flight experience." /></label>
        </div>
        {error && <p className="pilot-register-error" role="alert">{error}</p>}
        <footer><p>Your application will remain pending until an Operations Manager reviews it.</p><button type="submit" className="button primary" disabled={busy !== null}>{busy === "register" ? "Sending..." : "Submit Registration Request"}</button></footer>
      </form>
    </div>}
  </div>;
}
