"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { api, ApiError } from "@/lib/auth/client";
import type { AuthSession } from "@/lib/auth/types";
import AccountMenu from "./portal/account-menu";

export default function LandingAccount({ initialSession }: { initialSession: AuthSession | null }) {
  const [session, setSession] = useState(initialSession);
  useEffect(() => {
    let alive = true;
    let expiry: ReturnType<typeof setTimeout> | undefined;
    let pending = false;
    function schedule(value: AuthSession | null) {
      clearTimeout(expiry);
      if (value) expiry = setTimeout(() => { if (alive) setSession(null); }, Math.max(0, new Date(value.expiresAt).getTime() - Date.now()));
    }
    async function verify() {
      if (pending) return;
      pending = true;
      try {
        const value = await api<AuthSession>("/auth/me");
        if (alive) { setSession(value); schedule(value); }
      } catch (reason) {
        if (alive && reason instanceof ApiError && (reason.status === 401 || reason.status === 403)) { setSession(null); schedule(null); }
      } finally { pending = false; }
    }
    const focus = () => { if (document.visibilityState === "visible") void verify(); };
    schedule(initialSession);
    void verify();
    window.addEventListener("pageshow", focus);
    window.addEventListener("focus", focus);
    document.addEventListener("visibilitychange", focus);
    const timer = setInterval(focus, 60000);
    return () => { alive = false; clearTimeout(expiry); clearInterval(timer); window.removeEventListener("pageshow", focus); window.removeEventListener("focus", focus); document.removeEventListener("visibilitychange", focus); };
  }, [initialSession]);
  return <>
    {session ? <div className="landing-account"><Link href="/portal" className="landing-portal">Open portal</Link><AccountMenu account={session.account} /></div> : <Link className="button primary header-cta" href="/login">Sign in <span aria-hidden="true">↗</span></Link>}
    <details className="mobile-nav"><summary>Menu <span aria-hidden="true">☰</span></summary><nav aria-label="Mobile navigation"><Link href={session ? "/portal" : "/login"}>{session ? "Open operator portal" : "Sign in / Operator portal"}</Link><a href="#overview">Platform overview</a><a href="#demo">Interactive demo</a><a href="#geofence">Geofence engine</a><a href="#telemetry">MAVLink telemetry</a><a href="#ai-risk">AI risk assist</a></nav></details>
  </>;
}
