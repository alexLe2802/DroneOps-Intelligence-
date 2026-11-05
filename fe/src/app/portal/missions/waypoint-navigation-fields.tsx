"use client";
import { ChevronRight, RotateCcw } from "lucide-react";
import type { FirmwareParameterSnapshot, MissionWaypoint, WaypointNavigation } from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import styles from "./mission-authoring.module.css";

export function FirmwareDefaultsNotice({ snapshot }: { snapshot?: FirmwareParameterSnapshot | null }) {
  const parameters = snapshot?.values ?? {};
  const cruise = parameters.MPC_XY_CRUISE ?? parameters.WP_SPD ?? (parameters.WPNAV_SPEED != null ? parameters.WPNAV_SPEED / 100 : null);
  const radius = parameters.NAV_ACC_RAD ?? (parameters.WPNAV_RADIUS != null ? parameters.WPNAV_RADIUS / 100 : null);
  return <aside className={styles.fcNotice}><span className={styles.status} aria-hidden="true" /><details><summary><strong>Flight controller</strong> · {snapshot ? "Planning defaults available" : "Awaiting connection"}</summary>
    <p>{snapshot ? `Firmware ${snapshot.firmwareVersion} · cruise ${cruise ?? "unknown"} m/s · acceptance radius ${radius ?? "unknown"} m · ${formatVietnamDateTime(snapshot.observedAtUtc)}.` : "No FC parameter snapshot is available yet. Connect the assigned drone after approval."}</p>
    <p>Home is read from the bound FC before flight. Cached defaults must be verified again before launch.</p>
  </details></aside>;
}

export default function WaypointNavigationFields({ point, maximumSpeed, onChange }: { point: MissionWaypoint; maximumSpeed: number; onChange: (value: WaypointNavigation) => void }) {
  const value = point.navigation ?? {};
  const number = (key: keyof WaypointNavigation, input: string) => onChange({ ...value, [key]: input === "" ? null : Number(input) });
  const hover = point.actionType === "Hover";
  const landing = point.actionType === "Land" || Boolean(value.emergencyLandingCandidate);
  return <details className={styles.navigation} open={hover || landing}>
    <summary><span><ChevronRight size={14} aria-hidden="true" />Navigation settings</span><span className={hover || landing ? styles.required : styles.optional}>{hover ? "Hold time required" : landing ? "Ground elevation before flight" : "Optional"}</span></summary>
    <div className={styles.navigationBody}>
      <div className={styles.navigationToolbar}><button type="button" className={styles.reset} onClick={() => onChange({})}><RotateCcw size={13} aria-hidden="true" />Clear overrides</button></div>
      <div className={styles.navigationGrid}>
        {point.actionType !== "TakeOff" && <label className={styles.field}><span>Speed <em className={styles.optional}>Optional</em></span><input aria-label="Speed override (m/s) · optional" type="number" min="0.5" max={maximumSpeed} step="0.01" value={value.speedMetersPerSecond ?? ""} placeholder="Mission default" onChange={event => number("speedMetersPerSecond", event.target.value)} /><small>0.5–{maximumSpeed} m/s</small></label>}
        {(point.actionType === "FlyThrough" || hover) && <>
          <label className={styles.field}><span>Hold time <em className={hover ? styles.required : styles.optional}>{hover ? "Required" : "Optional"}</em></span><input aria-label={`Hold time (s) · ${hover ? "required for Hover" : "optional"}`} type="number" min={hover ? 0.1 : 0} max="600" step="0.1" required={hover} value={value.holdTimeSeconds ?? ""} placeholder={hover ? "Enter duration" : "No hold"} onChange={event => number("holdTimeSeconds", event.target.value)} /><small>Seconds · max 600</small></label>
          <label className={styles.field}><span>Acceptance radius <em className={styles.optional}>Optional</em></span><input aria-label="Acceptance radius (m) · optional" type="number" min="0.1" max="10" step="0.1" value={value.acceptanceRadiusMeters ?? ""} placeholder="Read from FC" onChange={event => number("acceptanceRadiusMeters", event.target.value)} /><small>Metres · max 10</small></label>
        </>}
        <label className={styles.field}><span>Yaw angle <em className={styles.optional}>Optional</em></span><input aria-label="Yaw angle (degrees) · optional" type="number" min="0" max="359.99" step="0.1" value={value.yawDegrees ?? ""} placeholder="FC default" onChange={event => number("yawDegrees", event.target.value)} /><small>Degrees · 0–359.99°</small></label>
        <label className={styles.landingToggle}><input type="checkbox" checked={value.emergencyLandingCandidate ?? false} onChange={event => onChange({ ...value, emergencyLandingCandidate: event.target.checked })} />Emergency landing candidate<span className={styles.optional}>Optional</span></label>
        {landing && <>
          <label className={styles.field}><span>Ground elevation <em className={styles.required}>{value.emergencyLandingCandidate ? "Required" : "Before flight"}</em></span><input aria-label="Landing ground altitude relative to takeoff (m) · required before flight" type="number" min="0" max="30" step="0.1" required={Boolean(value.emergencyLandingCandidate)} value={value.landingGroundAltitudeMeters ?? ""} placeholder="Relative to FC Home" onChange={event => number("landingGroundAltitudeMeters", event.target.value)} /><small>Metres · 0 only for the same ground level as Home</small></label>
          <label className={`${styles.field} ${styles.siteNotes}`}><span>Site clearance notes <em className={value.emergencyLandingCandidate ? styles.required : styles.optional}>{value.emergencyLandingCandidate ? "Required" : "Optional"}</em></span><textarea aria-label={`Landing-site clearance notes · ${value.emergencyLandingCandidate ? "required" : "optional"}`} required={Boolean(value.emergencyLandingCandidate)} minLength={value.emergencyLandingCandidate ? 10 : undefined} maxLength={500} placeholder="Landing area and approach clearance" value={value.landingSiteNotes ?? ""} onChange={event => onChange({ ...value, landingSiteNotes: event.target.value || null })} /></label>
        </>}
        {value.emergencyLandingCandidate && <p className={styles.landingHint}>Requires ground elevation, clearance notes and verification before use.</p>}
      </div>
    </div>
  </details>;
}
