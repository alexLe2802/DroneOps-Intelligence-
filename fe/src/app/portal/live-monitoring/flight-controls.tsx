"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/auth/client";
import styles from "../flight-preparation.module.css";

type Options = { configureRecovery?: boolean; firmwareTarget: string; vehicleClass: string; acceptanceRadiusMeters: number | null;
  yawPolicy: string | null; yawDegrees: number | null; hoverDurationSeconds: number | null; landingGroundAltitudeMeters: number | null };
type Execution = { enabled: boolean; serverNow: string; scheduledAt: string; endsAt: string; stage: string;
  message: string; options: Options | null; compiledHash: string | null; uploadVerified: boolean; launchAt: string | null;
  armed: boolean | null; onGround: boolean; canArm: boolean; canDisarm: boolean; reason: string | null;
  readinessChecks?: {key: string; label: string; satisfied: boolean; detail: string}[];
  recoveryAssessment?: {status: string; availableEnergyWh: number | null; reserveEnergyWh: number | null;
    canAuthorizeRecovery: boolean; missingData: string[]; sites: {siteId: string; distanceMeters: number | null;
      requiredEnergyWh: number | null; fitsDeclaredEnergyBudget: boolean | null; surveyDeclaredCurrent: boolean; missingData: string[]}[]};
  homeLatitudeDegrees?: number | null; homeLongitudeDegrees?: number | null; homeAltitudeMslMeters?: number | null };

export default function FlightControls({ missionId, planHash, confirmed }: { missionId: string; planHash: string; confirmed: boolean }) {
  const [state, setState] = useState<Execution | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [editing, setEditing] = useState(false);
  const latest = useRef<Execution | null>(null);
  const received = useRef(0);
  const acting = useRef(false);
  const version = useRef(0);
  const base = `/missions/${missionId}/automation`;
  const apply = (value: Execution) => { received.current = performance.now(); setElapsed(0); latest.current = value; setState(value); };
  useEffect(() => {
    let active = true, pending = false;
    async function poll() {
      if (!active || pending || acting.current || document.visibilityState !== "visible" || !navigator.onLine) return;
      pending = true;
      const request = ++version.current;
      try {
        const value = await api<Execution>(`${base}/execution`, { method: "POST" });
        if (active && request === version.current) { apply(value); setError(""); }
      } catch (reason) {
        if (active && request === version.current) { setState(null); setError(reason instanceof Error ? reason.message : "FC controls unavailable."); }
      } finally { pending = false; }
    }
    function leave() {
      ++version.current;
      if (latest.current && ["Arming", "Countdown"].includes(latest.current.stage)) {
        void api(`${base}/cancel`, { method: "POST" }).catch(() => {});
      }
      latest.current = null;
      if (active) setState(null);
    }
    const visibility = () => { if (document.visibilityState !== "visible" || !navigator.onLine) leave(); else void poll(); };
    void poll();
    const timer = window.setInterval(() => void poll(), 1000);
    const clock = window.setInterval(() => setElapsed(performance.now() - received.current), 250);
    document.addEventListener("visibilitychange", visibility);
    window.addEventListener("offline", visibility); window.addEventListener("online", visibility);
    return () => { active = false; leave(); clearInterval(timer); clearInterval(clock);
      document.removeEventListener("visibilitychange", visibility); window.removeEventListener("offline", visibility); window.removeEventListener("online", visibility); };
  }, [base]);

  async function act(action: "arm" | "disarm" | "cancel") {
    if (!state || acting.current) return;
    acting.current = true; ++version.current; setBusy(true); setError("");
    try {
      if (action === "cancel") {
        await api(`${base}/cancel`, { method: "POST" });
        setState(null); latest.current = null;
      } else {
        apply(await api<Execution>(`${base}/${action}`, { method: "POST", body: JSON.stringify({
          operationId: crypto.randomUUID(), compiledHash: state.compiledHash,
        }) }));
      }
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Command outcome unavailable. Check FC status before trying again."); }
    finally { acting.current = false; setBusy(false); }
  }

  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (acting.current) return;
    const fields = new FormData(event.currentTarget);
    const number = (key: string) => fields.get(key) === "" ? null : Number(fields.get(key));
    acting.current = true; ++version.current; setBusy(true); setError("");
    try {
      apply(await api<Execution>(`${base}/plan`, { method: "POST", body: JSON.stringify({ planHash, options: {
        firmwareTarget: fields.get("firmware"), vehicleClass: "Multicopter", acceptanceRadiusMeters: number("radius"),
        yawPolicy: fields.get("yaw"), yawDegrees: fields.get("yaw") === "Fixed" ? number("angle") : null,
        hoverDurationSeconds: number("hover"), landingGroundAltitudeMeters: number("ground"),
        configureRecovery: fields.get("recovery") === "on",
      } }) }));
      setEditing(false);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to save FC settings."); }
    finally { acting.current = false; setBusy(false); }
  }
  const fresh = elapsed < 3000;
  const now = state ? Date.parse(state.serverNow) + elapsed : 0;
  const ended = state && now >= Date.parse(state.endsAt);
  const countdown = state?.stage === "Countdown" && state.launchAt && fresh && !ended;
  const seconds = countdown ? Math.max(0, Math.ceil((Date.parse(state.launchAt!) - now) / 1000)) : null;
  return <div className={styles.controls} aria-label="Pilot aircraft controls">
    {error && <p role="alert">{error}</p>}
    {state && <>
      <div className={countdown ? styles.countdown : styles.status} role="status" aria-live="polite">
        {countdown ? <><strong>{seconds ? `TAKEOFF IN ${seconds}s` : "WAITING FOR FC MISSION START"}</strong><span>FC confirmed ARM. Keep this screen online and the takeoff area clear.</span></>
          : <><strong>{!fresh ? "FC STATUS STALE" : state.armed === true ? "ARMED" : state.armed === false ? "DISARMED" : "ARM STATUS UNKNOWN"}</strong><span>{state.message}</span></>}
      </div>
      <p>FC mission: <strong>{state.uploadVerified ? "Upload and read-back verified" : "Not verified"}</strong>. ARM becomes available from the scheduled start, after your confirmation and passing readiness checks.</p>
      <p>FC Home: {fresh && state.homeLatitudeDegrees != null && state.homeLongitudeDegrees != null
        ? `${state.homeLatitudeDegrees.toFixed(7)}, ${state.homeLongitudeDegrees.toFixed(7)} · ${state.homeAltitudeMslMeters ?? "unknown"} m MSL`
        : "Waiting for fresh Home from the bound flight controller"}. This is the FC reference, not a map-click estimate.</p>
      {state.reason && <p>{state.reason}</p>}
      {state.readinessChecks && <details className={styles.readiness}>
        <summary>Flight readiness <span>{state.readinessChecks.filter(check => check.satisfied && fresh).length}/{state.readinessChecks.length} conditions</span></summary>
        <ul>{state.readinessChecks.map(check => <li key={check.key}><strong data-ready={check.satisfied && fresh}>{check.satisfied && fresh ? "Ready" : "Pending"}</strong><div><b>{check.label}</b><small>{!fresh ? "Waiting for a fresh server status" : check.detail}</small></div></li>)}</ul>
      </details>}
      {state.recoveryAssessment && <details className={styles.readiness}>
        <summary>Recovery preparation <span>Planning only</span></summary>
        <p>Declared energy budget: {fresh && state.recoveryAssessment.availableEnergyWh != null ? `${state.recoveryAssessment.availableEnergyWh.toFixed(2)} Wh · reserve ${state.recoveryAssessment.reserveEnergyWh?.toFixed(2)} Wh` : "Waiting for measured data and fresh telemetry"}.</p>
        <p>Site inspections and energy measurements are declarations reviewed with the plan. They do not authorize a recovery maneuver.</p>
        {state.recoveryAssessment.sites.length > 0 && <div className={styles.siteTable}><table><thead><tr><th>Site</th><th>Direct distance</th><th>Energy + reserve</th><th>Declared survey</th></tr></thead><tbody>{state.recoveryAssessment.sites.map(site => <tr key={site.siteId}><td>{site.siteId}</td><td>{fresh && site.distanceMeters != null ? `${site.distanceMeters.toFixed(0)} m` : "Unavailable"}</td><td>{fresh && site.requiredEnergyWh != null ? `${site.requiredEnergyWh.toFixed(2)} + ${state.recoveryAssessment?.reserveEnergyWh?.toFixed(2) ?? "?"} Wh · ${site.fitsDeclaredEnergyBudget ? "within declared budget" : "insufficient budget"}` : "Missing data"}</td><td>{fresh && site.surveyDeclaredCurrent ? "Current declaration" : "Required / refresh"}</td></tr>)}</tbody></table></div>}
        <ul>{state.recoveryAssessment.missingData.map(reason => <li key={reason}>{reason}</li>)}</ul>
      </details>}
      {(confirmed || state.armed === true) && <div className={styles.actions}>
        <button className="module-primary" disabled={busy || !confirmed || !fresh || ended || !state.canArm} onClick={() => void act("arm")}>ARM — TAKE OFF AFTER 10s</button>
        <button className="module-secondary" disabled={busy || !fresh || !state.canDisarm} onClick={() => void act("disarm")}>DISARM ON GROUND</button>
        {["Arming", "Countdown"].includes(state.stage) && <button className="module-secondary" disabled={busy} onClick={() => void act("cancel")}>CANCEL TAKEOFF</button>}
      </div>}
      {state.stage === "Cancelled" && state.armed === true && <p>Takeoff cancelled. The aircraft remains armed; use DISARM while on the ground.</p>}
      <button className="module-secondary" disabled={busy || ended || state.armed === true || ["Arming", "Countdown", "Starting", "Running"].includes(state.stage)} aria-expanded={editing} onClick={() => setEditing(value => !value)}>FC MISSION SETTINGS</button>
      {editing && <form className={styles.settings} onSubmit={save}>
        <p>Saving settings withdraws the previous confirmation. When command execution is enabled, the connected, disarmed FC receives the mission automatically; upload never arms the aircraft.</p>
        <label>Firmware<select name="firmware" defaultValue={state.options?.firmwareTarget ?? "ArduPilot"}><option>ArduPilot</option><option>PX4</option></select></label>
        <label>Waypoint acceptance radius (m)<input name="radius" type="number" step="any" min="0.01" required defaultValue={state.options?.acceptanceRadiusMeters ?? ""} /></label>
        <label>Yaw policy<select name="yaw" defaultValue={state.options?.yawPolicy ?? "Autopilot"}><option>Autopilot</option><option>Fixed</option></select></label>
        <label>Fixed yaw angle (degrees, only for Fixed)<input name="angle" type="number" step="any" min="0" max="359.99" defaultValue={state.options?.yawDegrees ?? ""} /></label>
        <label>Hover duration (seconds, required for Hover nodes)<input name="hover" type="number" step="any" min="0.01" defaultValue={state.options?.hoverDurationSeconds ?? ""} /></label>
        <label>Landing ground altitude (m, in the mission altitude frame)<input name="ground" type="number" step="any" defaultValue={state.options?.landingGroundAltitudeMeters ?? ""} /></label>
        <p>ArduCopter lands using its onboard landing detector; ground elevation is optional. PX4 requires explicit ground elevation.</p>
        <label><input name="recovery" type="checkbox" defaultChecked={state.options?.configureRecovery ?? false} />Apply extended FC recovery profile (optional; requires calibrated battery settings)</label>
        <button className="module-primary" disabled={busy}>SAVE AND PREPARE FC MISSION</button>
      </form>}
    </>}
  </div>;
}
