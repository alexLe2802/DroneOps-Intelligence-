"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "@/lib/auth/client";
import { roleLabels, type Account, type SessionInfo } from "@/lib/auth/types";

export default function PortalControls({ account }: { account: Account }) {
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [accountType, setAccountType] = useState("new");
  const pending = useRef(false);
  const manager = account.role === "operations_manager";
  const handleError = useCallback((e: unknown) => {
    if (e instanceof ApiError && (e.status === 401 || e.status === 403)) { window.location.replace("/login"); return; }
    setError(e instanceof Error ? e.message : "Request failed. Please try again.");
  }, []);
  const load = useCallback(async () => {
    const [nextSessions, nextAccounts] = await Promise.all([
      api<SessionInfo[]>("/auth/sessions"), manager ? api<Account[]>("/accounts") : Promise.resolve([]),
    ]);
    setSessions(nextSessions); setAccounts(nextAccounts); setLoaded(true);
  }, [manager]);
  useEffect(() => {
    let alive = true;
    async function verify() { try { await api("/auth/me"); } catch (e) { if (alive) handleError(e); } }
    Promise.all([
      api<SessionInfo[]>("/auth/sessions"), manager ? api<Account[]>("/accounts") : Promise.resolve([]),
    ]).then(([nextSessions, nextAccounts]) => {
      if (alive) { setSessions(nextSessions); setAccounts(nextAccounts); setLoaded(true); }
    }).catch(e => { if (alive) handleError(e); });
    // Revalidate on focus/BFCache restore and periodically, including expiry or revocation from another tab.
    const focus = () => { if (document.visibilityState === "visible") void verify(); };
    window.addEventListener("pageshow", focus);
    document.addEventListener("visibilitychange", focus);
    const timer = window.setInterval(focus, 60000);
    return () => { alive = false; clearInterval(timer); window.removeEventListener("pageshow", focus); document.removeEventListener("visibilitychange", focus); };
  }, [manager, handleError]);
  async function act(task: () => Promise<void>) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(""); setMessage("");
    try { await task(); } catch (e) { handleError(e); }
    finally { pending.current = false; setBusy(false); }
  }
  const logout = (all: boolean) => act(async () => {
    await api(all ? "/auth/logout-all" : "/auth/logout", { method: "POST" });
    window.location.replace("/login");
  });
  async function provision(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = new FormData(form);
    const displayName = String(values.get("displayName") ?? "").trim();
    const password = accountType === "new" ? String(values.get("password") ?? "") : undefined;
    if (!displayName) { setError("Enter the Pilot's full name."); setMessage(""); return; }
    if (password !== undefined && (!password.trim() || password !== values.get("confirmPassword"))) {
      setError("Enter a password and make sure both passwords match."); setMessage(""); return;
    }
    await act(async () => {
      try {
        const result = await api<{ message: string }>("/accounts", { method: "POST", body: JSON.stringify({
          email: String(values.get("email") ?? "").trim(), displayName, accountType, password,
          isActive: values.get("isActive") === "true",
        }) });
        form.reset(); setAccountType("new"); setMessage(result.message); await load();
      } finally {
        // Passwords are never persisted in component state or retained after a network attempt.
        for (const name of ["password", "confirmPassword"]) {
          const input = form.elements.namedItem(name);
          if (input instanceof HTMLInputElement) input.value = "";
        }
      }
    });
  }
  return <div className="portal-content">
    <div className="portal-toolbar"><span className="green">● Authorized workspace</span><button className="button secondary" disabled={busy} onClick={() => logout(false)}>Sign out →</button></div>
    {error && <p className="auth-error" role="alert">{error} <button className="text-button" disabled={busy} onClick={() => act(load)}>Retry</button></p>}
    {message && <p className="auth-feedback" role="status">{message}</p>}
    <section className="portal-card"><div className="portal-card-heading"><div><span className="auth-kicker">ACCOUNT SECURITY</span><h2>Your active sessions</h2><p>Recognize where you are signed in. Revoke any session you no longer use.</p></div><button className="button outline" disabled={busy || !loaded} onClick={() => logout(true)}>Sign out everywhere</button></div>
      {!loaded && <p role="status">Loading your sessions…</p>}
      <ul className="session-list">{sessions.map(s => <li key={s.id}><div><strong>{s.isCurrent ? "This browser" : "Another browser"}</strong><p className="session-agent">{s.userAgent || "Browser information unavailable"}</p><small>Signed in {new Date(s.createdAt).toLocaleString()} · Expires {new Date(s.expiresAt).toLocaleString()}</small></div><button className="button secondary" disabled={busy} onClick={() => act(async () => { await api(`/auth/sessions/${s.id}`, { method: "DELETE" }); if (s.isCurrent) window.location.replace("/login"); else await load(); })}>{s.isCurrent ? "Sign out" : "Revoke"}</button></li>)}</ul>
    </section>
    {manager && <section className="portal-card"><div className="portal-card-heading"><div><span className="auth-kicker">TEAM ACCESS</span><h2>Operators & accounts</h2><p>Create a Pilot account or grant access to an existing Firebase user.</p></div></div>
      <form onSubmit={provision}>
        <fieldset className="provision-form" disabled={busy} aria-describedby="pilot-form-help">
          <legend>Create Pilot account</legend>
          <label>Account setup<select name="accountType" value={accountType} onChange={e => setAccountType(e.target.value)}>
            <option value="new">New email/password account</option><option value="existing">Existing Firebase account</option>
          </select></label>
          <label>Role<input value="UAV Operator / Pilot" readOnly aria-readonly="true" /></label>
          <label>Full name<input name="displayName" required maxLength={120} autoComplete="off" placeholder="Pilot's full name" /></label>
          <label>Email address<input name="email" type="email" required maxLength={254} autoComplete="off" placeholder="pilot@example.com" /></label>
          {accountType === "new" && <>
            <label>Initial password<input name="password" type="password" required minLength={12} maxLength={128} autoComplete="new-password" aria-describedby="pilot-password-help" /></label>
            <label>Confirm password<input name="confirmPassword" type="password" required minLength={12} maxLength={128} autoComplete="new-password" /></label>
            <p id="pilot-password-help" className="provision-note">Use 12–128 characters. Share the password securely with the Pilot. Email verification is required at first sign-in.</p>
          </>}
          <label>DroneOps access<select name="isActive" defaultValue="true"><option value="true">Enabled</option><option value="false">Disabled</option></select></label>
          <div className="provision-submit"><button className="button primary" type="submit" disabled={busy}>{busy ? "Please wait…" : accountType === "new" ? "Create Pilot account →" : "Grant Pilot access →"}</button></div>
          <p id="pilot-form-help" className="provision-note">{accountType === "existing" ? "Use the email already registered in Firebase. Its password and sign-in methods will stay unchanged. " : ""}Mission and UAV assignments are managed separately after account creation. Disabled access blocks sign-in to DroneOps.</p>
        </fieldset>
      </form><div className="account-table-wrap"><table className="account-table"><thead><tr><th>Team member</th><th>Role</th><th>Access</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{accounts.map(a => <tr key={a.id}><td><strong>{a.displayName}</strong><br /><span>{a.email}</span></td><td>{roleLabels[a.role]}</td><td><span className={a.isActive ? "green" : "warning"}>{a.isActive ? "Enabled" : "Disabled"}</span></td><td>{a.role === "uav_operator" && <button className="text-button" disabled={busy} onClick={() => act(async () => { await api(`/accounts/${a.id}/access`, { method: "PATCH", body: JSON.stringify({ isActive: !a.isActive }) }); await load(); })}>{a.isActive ? "Disable access" : "Enable access"}</button>}</td></tr>)}</tbody></table></div></section>}
  </div>;
}
