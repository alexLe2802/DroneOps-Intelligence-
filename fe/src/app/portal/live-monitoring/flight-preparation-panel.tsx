"use client";

import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/auth/client";
import DroneConnectionClient from "../drone-connection/drone-connection-client";
import styles from "../flight-preparation.module.css";
import FlightControls from "./flight-controls";

type Preparation = {
  missionId: string; planHash: string; serverNow: string; scheduledAt: string; endsAt: string;
  confirmationOpen: boolean; gatewayConnected: boolean; droneConnected: boolean; operatorOnline: boolean;
  preflightReady: boolean; confirmationRecorded: boolean; readinessConfirmed: boolean;
  dispatchEnabled: boolean; blockingReasons: string[];
};

export default function FlightPreparationPanel({ missionId }: { missionId: string }) {
  const [state, setState] = useState<Preparation | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [connectionOpen, setConnectionOpen] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const receivedAt = useRef(0);
  const sequence = useRef(0);
  const acting = useRef(false);
  useEffect(() => {
    let active = true, pending = false;
    async function poll() {
      if (!active || pending || acting.current || document.visibilityState !== "visible" || !navigator.onLine) return;
      pending = true;
      const request = ++sequence.current;
      try {
        const result = await api<Preparation>(`/missions/${missionId}/automation`);
        if (active && sequence.current === request) {
          receivedAt.current = performance.now(); setElapsed(0); setState(result); setError("");
        }
      } catch (reason) {
        if (active && sequence.current === request) { setState(null); setError(reason instanceof Error ? reason.message : "Preparation is unavailable."); }
      } finally { pending = false; }
    }
    const refresh = () => { void poll(); };
    void poll();
    const timer = window.setInterval(refresh, 10_000);
    const clock = window.setInterval(() => setElapsed(performance.now() - receivedAt.current), 1000);
    document.addEventListener("visibilitychange", refresh); window.addEventListener("online", refresh);
    return () => { active = false; clearInterval(timer); clearInterval(clock); document.removeEventListener("visibilitychange", refresh); window.removeEventListener("online", refresh); };
  }, [missionId]);
  const windowOpen = state?.confirmationOpen && new Date(state.serverNow).getTime() + elapsed < new Date(state.endsAt).getTime();
  const ended = state && new Date(state.serverNow).getTime() + elapsed >= new Date(state.endsAt).getTime();
  const canConfirm = windowOpen && state?.droneConnected && state.operatorOnline && state.preflightReady && !state.confirmationRecorded;
  async function decide(action: "confirm" | "cancel") {
    if (!state || acting.current) return;
    acting.current = true; ++sequence.current; setBusy(true); setError("");
    try {
      const result = await api<Preparation | { cancelled: boolean }>(`/missions/${missionId}/automation/${action}`, {
        method: "POST", ...(action === "confirm" ? { body: JSON.stringify({ planHash: state.planHash }) } : {}),
      });
      if ("missionId" in result) { receivedAt.current = performance.now(); setElapsed(0); setState(result); }
      else setState(null);
      window.dispatchEvent(new Event("droneops:notifications-refresh"));
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to save your decision."); }
    finally { acting.current = false; setBusy(false); }
  }
  return <section className={styles.panel} aria-label="Automatic flight preparation">
    <h2>Automatic flight preparation</h2>
    {error && <p role="alert">{error}</p>}
    {!state && !error && <p>Checking the mission, drone and preflight data…</p>}
    {state && <>
      <p>Scheduled start: {formatVietnamDateTime(state.scheduledAt)} · End: {formatVietnamDateTime(state.endsAt)}</p>
      <p>Gateway: <strong>{state.gatewayConnected ? "Connected" : "Not connected"}</strong> · Telemetry: <strong>{state.droneConnected ? "Fresh" : "Waiting"}</strong> · Preflight: <strong>{state.preflightReady ? "Pass" : "Not ready"}</strong></p>
      <p role="status">{!windowOpen ? "Confirmation opens at T−5 and closes when the mission ends." : state.confirmationRecorded ? "Your confirmation is recorded for this plan and drone connection." : !state.gatewayConnected ? "Connect the assigned drone. One connection reminder is sent for this mission." : !state.droneConnected || !state.preflightReady ? "Waiting for fresh drone data and passing preflight checks before confirmation." : "Ready for your confirmation. Reminders repeat every two minutes until you confirm or the mission ends."}</p>
      {!state.preflightReady && state.blockingReasons.filter(reason => reason.includes("preflight")).map(reason => <p key={reason}>{reason}</p>)}
      <div className={styles.actions}>
        <Link className="module-secondary module-link" href={`/portal/missions/${missionId}`} target="_blank" rel="noopener noreferrer">REVIEW MISSION</Link>
        <button className="module-secondary" disabled={Boolean(ended)} aria-expanded={connectionOpen} onClick={() => setConnectionOpen(value => !value)}>CONNECT DRONE / PAIRING</button>
        <button className="module-primary" disabled={busy || !canConfirm} onClick={() => void decide("confirm")}>{busy ? "CHECKING…" : "CONFIRM AUTOMATIC PREPARATION"}</button>
        {state.confirmationRecorded && <button className="module-secondary" disabled={busy || !windowOpen} onClick={() => void decide("cancel")}>WITHDRAW CONFIRMATION</button>}
      </div>
      <p>Confirmation records your consent. You decide when to ARM; FC confirmation starts a 10-second takeoff countdown. DISARM is available only with fresh FC ground-state evidence.</p>
      <FlightControls missionId={missionId} planHash={state.planHash} confirmed={state.confirmationRecorded} />
    </>}
    {connectionOpen && !ended && <DroneConnectionClient key={missionId} initialMissionId={missionId} embedded />}
  </section>;
}
