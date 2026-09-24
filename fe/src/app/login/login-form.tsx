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

type Action = "google" | "password" | "reset";
const resetMessage = "If an account exists for this email, a password reset link has been sent. Check your inbox and spam folder.";

export default function LoginForm() {
  const [busy, setBusy] = useState<Action | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [showPassword, setShowPassword] = useState(false);
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
          // No application session is created until the user proves ownership of the email.
          await sendEmailVerification(credential.user);
          setNotice("Verification email sent. Open the link in your inbox or spam folder, then return here and sign in again.");
          return;
        }
        await api("/auth/session", { method: "POST", body: JSON.stringify({ idToken: await credential.user.getIdToken() }) });
        completed = true;
      } finally {
        // The browser keeps only the server HttpOnly session after exchange.
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
  return <div className="login-action">
    <form className="email-login-form" onSubmit={submit} aria-busy={busy !== null}>
      <label htmlFor="login-email">Email address</label>
      <input ref={emailInput} id="login-email" name="email" type="email" required maxLength={254} autoComplete="username" placeholder="you@your-team.com" disabled={busy !== null} />
      <div className="password-label"><label htmlFor="login-password">Password</label><button type="button" className="text-button" disabled={busy !== null} onClick={() => void login("reset")}>Forgot password?</button></div>
      <div className="password-field"><input ref={passwordInput} id="login-password" name="password" type={showPassword ? "text" : "password"} required maxLength={4096} autoComplete="current-password" disabled={busy !== null} /><button type="button" aria-label={showPassword ? "Hide password" : "Show password"} aria-pressed={showPassword} disabled={busy !== null} onClick={() => setShowPassword(!showPassword)}>{showPassword ? "Hide" : "Show"}</button></div>
      <button type="submit" className="button primary email-login-submit" disabled={busy !== null}>{busy === "password" ? "Signing in…" : "Sign in"}<span aria-hidden="true">→</span></button>
    </form>
    <div className="login-divider"><span>OR CONTINUE WITH</span></div>
    <button type="button" className="google-login" onClick={() => void login("google")} disabled={busy !== null} aria-busy={busy === "google"}>
      <Image src="/google-signin.png" width={270} height={60} alt="Sign in with Google" priority />
    </button>
    {busy === "google" && <p className="auth-feedback" role="status">Waiting for Google sign-in…</p>}
    {busy === "reset" && <p className="auth-feedback" role="status">Requesting a password reset…</p>}
    {notice && <p className="auth-feedback" role="status">{notice}</p>}
    {error && <p className="auth-error" role="alert">{error}</p>}
  </div>;
}
