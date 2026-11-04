"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/auth/client";
import styles from "./flight-preparation.module.css";

type Preparation = {
  missionId: string; planHash: string; serverNow: string; scheduledAt: string;
  confirmationOpen: boolean; droneConnected: boolean; operatorOnline: boolean;
  readinessConfirmed: boolean; dispatchEnabled: boolean; blockingReasons: string[];
};
type DueMission = { id: string; name: string; startTime: string };

export default function FlightPreparationPrompt() {
  const dialog = useRef<HTMLDialogElement>(null);
  const dismissed = useRef(new Set<string>());
  const [selected, setSelected] = useState<{ mission: DueMission; preparation: Preparation } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [confirmed, setConfirmed] = useState<string | null>(null);
  useEffect(() => {
    let active = true, pending = false;
    async function poll() {
      if (pending || document.visibilityState !== "visible" || !navigator.onLine) return;
      pending = true;
      try {
        const due = await api<{ missions: DueMission[] }>("/missions/automation/due");
        let next: typeof selected = null;
        for (const mission of due.missions) {
          if (dismissed.current.has(mission.id)) continue;
          const preparation = await api<Preparation>(`/missions/${mission.id}/automation/presence`, { method: "POST" });
          const reviewing = window.location.pathname === `/portal/missions/${mission.id}` && new URLSearchParams(window.location.search).get("flightReview") === "1";
          if (!reviewing && preparation.confirmationOpen && !preparation.readinessConfirmed && !next) next = { mission, preparation };
        }
        if (active && document.visibilityState === "visible") setSelected(next);
      } catch (reason) {
        if (active) { setSelected(null); setError(reason instanceof Error ? reason.message : "Flight preparation could not be checked."); }
      } finally { pending = false; }
    }
    function visibility() { if (document.visibilityState !== "visible") setSelected(null); else void poll(); }
    void poll();
    const timer = window.setInterval(() => void poll(), 10_000);
    document.addEventListener("visibilitychange", visibility);
    window.addEventListener("online", poll);
    return () => { active = false; clearInterval(timer); document.removeEventListener("visibilitychange", visibility); window.removeEventListener("online", poll); };
  }, []);
  useEffect(() => { if (selected && dialog.current && !dialog.current.open) dialog.current.showModal(); }, [selected]);
  async function confirm() {
    if (!selected) return;
    setBusy(true); setError("");
    try {
      const state = await api<Preparation>(`/missions/${selected.mission.id}/automation/confirm`, {
        method: "POST", body: JSON.stringify({ planHash: selected.preparation.planHash }),
      });
      if (!state.readinessConfirmed) throw new Error("The plan or connection changed. Review the current mission and confirm again.");
      setConfirmed(selected.mission.name); setSelected(null);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Confirmation failed."); }
    finally { setBusy(false); }
  }
  async function dismiss() {
    if (!selected || busy) return;
    setBusy(true); setError("");
    try {
      await api(`/missions/${selected.mission.id}/automation/cancel`, { method: "POST" });
      dismissed.current.add(selected.mission.id); setSelected(null);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to dismiss preparation."); }
    finally { setBusy(false); }
  }
  return <>
    {confirmed && <p className={styles.notice} role="status">Readiness confirmed for {confirmed}. Keep this tab visible and the drone connected. Aircraft command execution is not enabled; no launch command has been sent.</p>}
    {error && !selected && <p className={styles.notice} role="status">Automatic preparation: {error}</p>}
    {selected && <dialog className={styles.dialog} ref={dialog} aria-labelledby="preparation-title" onCancel={event => { event.preventDefault(); void dismiss(); }}>
      <h2 id="preparation-title">Flight starts within five minutes</h2>
      <h3>{selected.mission.name}</h3>
      <p>Scheduled start: {new Date(selected.preparation.scheduledAt).toLocaleString()}</p>
      <p>Drone telemetry: <strong>{selected.preparation.droneConnected ? "Connected and fresh" : "Not ready"}</strong> · Pilot: <strong>{selected.preparation.operatorOnline ? "Online" : "Offline"}</strong></p>
      <p>Review the route and conditions before confirming. Confirmation is tied to this exact plan and drone connection. Losing presence or changing the plan requires another readiness check.</p>
      <p>Preparation only: aircraft command execution is not enabled. This confirmation does not arm or launch the drone.</p>
      {error && <p role="alert">{error}</p>}
      <div className={styles.actions}><Link className="module-secondary module-link" href={`/portal/missions/${selected.mission.id}?flightReview=1`} target="_blank" rel="noopener noreferrer">REVIEW MISSION</Link><button className="module-secondary" disabled={busy} onClick={() => void dismiss()}>NOT NOW</button><button className="module-primary" disabled={busy || !selected.preparation.droneConnected || !selected.preparation.operatorOnline} onClick={() => void confirm()}>{busy ? "CHECKING…" : "CONFIRM READINESS"}</button></div>
    </dialog>}
  </>;
}
