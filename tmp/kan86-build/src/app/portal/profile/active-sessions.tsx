"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/auth/client";
import type { SessionInfo } from "@/lib/auth/types";

export default function ActiveSessions() {
  const [sessions, setSessions] = useState<SessionInfo[] | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  useEffect(() => {
    let alive = true;
    api<SessionInfo[]>("/auth/sessions").then(value => { if (alive) setSessions(value); })
      .catch(reason => { if (alive) setError(reason instanceof Error ? reason.message : "Unable to load sessions."); });
    return () => { alive = false; };
  }, []);
  async function act(task: () => Promise<void>) {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError("");
    try { await task(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Request failed. Please try again."); }
    finally { pending.current = false; setBusy(false); }
  }
  async function load() { setSessions(await api<SessionInfo[]>("/auth/sessions")); }
  async function revoke(session: SessionInfo) {
    await api(`/auth/sessions/${session.id}`, { method: "DELETE" });
    if (session.isCurrent) window.location.replace("/login");
    else await load();
  }
  return <section className="profile-sessions" aria-labelledby="sessions-title">
    <header><div><p className="module-kicker">ACCOUNT SECURITY</p><h2 id="sessions-title">Your active sessions</h2><p>Recognize where you are signed in. Revoke any session you no longer use.</p></div><button className="module-secondary" disabled={busy || !sessions} onClick={() => void act(async () => {
      await api("/auth/logout-all", { method: "POST" });
      window.location.replace("/login");
    })}>Sign out everywhere</button></header>
    {error && <div className="sessions-error" role="alert"><p>{error}</p><button className="module-secondary" disabled={busy} onClick={() => void act(load)}>Try again</button></div>}
    {!sessions && !error && <p role="status">Loading your sessions…</p>}
    {sessions && <ul>{sessions.map(session => <li key={session.id}><div><h3>{session.isCurrent ? "This browser" : "Another browser"}</h3><p className="session-user-agent">{session.userAgent || "Browser information unavailable"}</p><p>Signed in <time dateTime={session.createdAt}>{new Date(session.createdAt).toLocaleString()}</time> · Expires <time dateTime={session.expiresAt}>{new Date(session.expiresAt).toLocaleString()}</time></p></div><button className="module-secondary" disabled={busy} onClick={() => void act(() => revoke(session))}>{session.isCurrent ? "Sign out" : "Revoke"}</button></li>)}</ul>}
    {sessions?.length === 0 && <p>No active sessions found.</p>}
  </section>;
}
