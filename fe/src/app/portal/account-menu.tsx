"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";

export default function AccountMenu({ account }: { account: Account }) {
  const root = useRef<HTMLDetailsElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    function outside(event: PointerEvent) {
      if (root.current && event.target instanceof Node && !root.current.contains(event.target)) root.current.open = false;
    }
    function escape(event: KeyboardEvent) {
      if (event.key === "Escape" && root.current?.open) {
        root.current.open = false;
        root.current.querySelector("summary")?.focus();
      }
    }
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, []);
  async function signOut() {
    if (busy) return;
    setBusy(true); setError("");
    try {
      await api("/auth/logout", { method: "POST" });
      window.location.replace("/login");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to sign out. Please try again.");
      setBusy(false);
    }
  }
  return <details className="account-menu" ref={root} onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) event.currentTarget.open = false;
  }}>
    <summary className="avatar" aria-label={`Account options for ${account.displayName}`} title={account.displayName}>{account.displayName.slice(0, 1).toUpperCase()}</summary>
    <div className="account-menu-panel">
      <Link href="/portal/profile" onClick={() => { if (root.current) root.current.open = false; }}>Profile</Link>
      <button type="button" disabled={busy} onClick={signOut}>{busy ? "Signing out…" : "Sign out"}</button>
      {error && <p role="alert">{error}</p>}
    </div>
  </details>;
}
