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
    await act(async () => {
      await api("/accounts", { method: "POST", body: JSON.stringify({ email: values.get("email"), displayName: values.get("displayName") }) });
      form.reset(); setMessage("Operator access granted. They can now sign in with their verified Firebase account."); await load();
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
    {manager && <section className="portal-card"><div className="portal-card-heading"><div><span className="auth-kicker">TEAM ACCESS</span><h2>Operators & accounts</h2><p>Grant access using the exact email your operator will sign in with.</p></div></div><form onSubmit={provision} className="provision-form"><label>Display name<input name="displayName" required maxLength={120} autoComplete="off" placeholder="Operator name" /></label><label>Email address<input name="email" type="email" required maxLength={254} autoComplete="off" placeholder="operator@example.com" /></label><button className="button primary" disabled={busy}>Grant operator access</button></form><div className="account-table-wrap"><table className="account-table"><thead><tr><th>Team member</th><th>Role</th><th>Access</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>{accounts.map(a => <tr key={a.id}><td><strong>{a.displayName}</strong><br /><span>{a.email}</span></td><td>{roleLabels[a.role]}</td><td><span className={a.isActive ? "green" : "warning"}>{a.isActive ? "Enabled" : "Disabled"}</span></td><td>{a.role === "uav_operator" && <button className="text-button" disabled={busy} onClick={() => act(async () => { await api(`/accounts/${a.id}/access`, { method: "PATCH", body: JSON.stringify({ isActive: !a.isActive }) }); await load(); })}>{a.isActive ? "Disable access" : "Enable access"}</button>}</td></tr>)}</tbody></table></div></section>}
  </div>;
}
