"use client";

import "./account-menu.css";
import Link from "next/link";
import { useEffect, useId, useRef, useState } from "react";
import { api } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";

export default function AccountMenu({ account }: { account: Account }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const pending = useRef(false);
  const panelId = useId();
  useEffect(() => {
    function outside(event: PointerEvent) {
      if (event.target instanceof Node && !root.current?.contains(event.target)) setOpen(false);
    }
    function escape(event: KeyboardEvent) {
      if (event.key === "Escape" && open) { setOpen(false); trigger.current?.focus(); }
    }
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("pointerdown", outside); document.removeEventListener("keydown", escape); };
  }, [open]);
  async function signOut() {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError("");
    try {
      await api("/auth/logout", { method: "POST" });
      window.location.replace("/login");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unable to sign out. Please try again.");
      pending.current = false; setBusy(false);
    }
  }
  return <div className="account-menu" ref={root} onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false);
  }}>
    <button type="button" className="avatar" ref={trigger} aria-label={`Account options for ${account.displayName}`} aria-expanded={open} aria-controls={panelId} onClick={() => setOpen(value => !value)}>{account.displayName.slice(0, 1).toUpperCase()}</button>
    {open && <div id={panelId} className="account-menu-panel">
      <Link href="/portal/profile" onClick={() => setOpen(false)}>Profile</Link>
      <button type="button" disabled={busy} onClick={() => void signOut()}>{busy ? "Signing out…" : "Sign out"}</button>
      {error && <p role="alert">{error}</p>}
    </div>}
  </div>;
}
