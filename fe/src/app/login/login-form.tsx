"use client";

import Image from "next/image";
import { useRef, useState, type FormEvent } from "react";
import {
  GoogleAuthProvider, signInWithPopup, signInWithEmailAndPassword,
  sendEmailVerification, sendPasswordResetEmail, signOut, type UserCredential,
} from "firebase/auth";
import { FirebaseError } from "firebase/app";
import { getFirebaseAuth } from "@/lib/auth/firebase";
import { api, ApiError } from "@/lib/auth/client";
import type { AccountRole, AuthSession } from "@/lib/auth/types";
import { createPilotRegistration } from "@/lib/pilot-registration";
import { saveLicenseFiles } from "@/lib/license-image-store";
import LicenseFilePicker from "@/app/license-file-picker";

type Action = "google" | "password" | "reset" | "register";
type AccessMode = "pilot" | "manager";

const resetMessage = "If an account exists for this email, a password reset link has been sent. Check your inbox and spam folder.";
const roleByMode: Record<AccessMode, AccountRole> = { pilot: "uav_operator", manager: "operation_manager" };
const roleMismatch: Record<AccessMode, string> = {
  pilot: "This account belongs to Manager Access. Use the Manager panel to sign in.",
  manager: "This account belongs to Pilot Access. Use the Pilot panel to sign in.",
};

export default function LoginForm() {
  const [mode, setMode] = useState<AccessMode>("pilot");
  const [busy, setBusy] = useState<Action | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [registerOpen, setRegisterOpen] = useState(false);
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
      const auth = getFirebaseAuth();
      try {
        if (action === "reset") {
          await sendPasswordResetEmail(auth, emailInput.current!.value.trim());
          setNotice(resetMessage);
          return;
        }
        let credential: UserCredential;
        if (action === "google") {
          const provider = new GoogleAuthProvider();
          provider.setCustomParameters({ prompt: "select_account" });
          credential = await signInWithPopup(auth, provider);
        } else {
          credential = await signInWithEmailAndPassword(auth, emailInput.current!.value.trim(), passwordInput.current!.value);
        }
        if (!credential.user.emailVerified) {
          await sendEmailVerification(credential.user);
          setNotice("Verification email sent. Open the link in your inbox or spam folder, then return here and sign in again.");
          return;
        }
        const session = await api<AuthSession>("/auth/session", { method: "POST", body: JSON.stringify({ idToken: await credential.user.getIdToken(true) }) });
        if (session.account.role !== roleByMode[mode]) {
          await api("/auth/logout", { method: "POST" }).catch(() => undefined);
          setError(roleMismatch[mode]);
          return;
        }
        completed = true;
      } finally {
        await signOut(auth);
      }
    } catch (e) {
      if (e instanceof ApiError) setError(e.message);
      else if (e instanceof FirebaseError) {
        if (action === "reset" && e.code === "auth/user-not-found") {
          setNotice(resetMessage);
        } else {
          const messages: Record<string, string> = {
            "auth/invalid-credential": "Unable to sign in. Check your email and password.",
            "auth/invalid-login-credentials": "Unable to sign in. Check your email and password.",
            "auth/user-not-found": "Unable to sign in. Check your email and password.",
            "auth/wrong-password": "Unable to sign in. Check your email and password.",
            "auth/invalid-email": "Enter a valid email address.",
            "auth/user-disabled": "This account cannot sign in. Contact your Operations Manager.",
            "auth/too-many-requests": "Too many attempts. Please wait before trying again.",
            "auth/popup-closed-by-user": "Sign-in was cancelled. Select Google to try again.",
            "auth/cancelled-popup-request": "A sign-in window is already open.",
            "auth/popup-blocked": "Allow pop-ups for this site, then try again.",
            "auth/unauthorized-domain": "Sign-in is not enabled for this domain. Contact your Operations Manager.",
            "auth/operation-not-allowed": "This sign-in method has not been enabled. Contact your Operations Manager.",
            "auth/network-request-failed": "Check your connection and try again.",
          };
          setError(messages[e.code] ?? "Unable to complete your request. Please try again.");
        }
      } else setError("Unable to sign in. Please try again.");
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
    if (!licenseFiles.length || licenseFiles.some(file => !file.type.startsWith("image/") && file.type !== "application/pdf")) {
      setError("Select one or more flight license images or PDF files.");
      return;
    }
    if (licenseFiles.length > 10 || licenseFiles.some(file => file.size > 10 * 1024 * 1024)) {
      setError("Upload at most 10 files, with each file 10 MB or smaller.");
      return;
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
      });
      await saveLicenseFiles(read("email"), licenseFiles).catch(() => undefined);
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

    <div className="login-divider"><span>OR CONTINUE WITH</span></div>
    <button type="button" className="google-login" onClick={() => void login("google")} disabled={busy !== null} aria-busy={busy === "google"}>
      <Image src="/google-signin.png" width={270} height={60} alt="Sign in with Google" priority />
    </button>
    {busy === "google" && <p className="auth-feedback" role="status">Waiting for Google sign-in...</p>}
    {busy === "reset" && <p className="auth-feedback" role="status">Requesting a password reset...</p>}
    {notice && <p className="auth-feedback" role="status">{notice}</p>}
    {error && !registerOpen && <p className="auth-error" role="alert">{error}</p>}

    <div className="login-directive"><b>NOTICE BR-01:</b> Strict role-based access control applies. Pilot registrations require Operations Manager approval before portal access is enabled.</div>

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
          <label className="pilot-register-wide">Description<textarea name="description" rows={4} maxLength={500} placeholder="Briefly describe your operating scope or manager contact." /></label>
        </div>
        {error && <p className="pilot-register-error" role="alert">{error}</p>}
        <footer><p>Your application will remain pending until an Operations Manager reviews it.</p><button type="submit" className="button primary" disabled={busy !== null}>{busy === "register" ? "Sending..." : "Submit Registration Request"}</button></footer>
      </form>
    </div>}
  </div>;
}
