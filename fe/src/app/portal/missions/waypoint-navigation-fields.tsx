"use client";
import type { FirmwareParameterSnapshot, MissionWaypoint, WaypointNavigation } from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";

export function FirmwareDefaultsNotice({ snapshot }: { snapshot?: FirmwareParameterSnapshot | null }) {
  const parameters = snapshot?.values ?? {};
  const cruise = parameters.MPC_XY_CRUISE ?? parameters.WP_SPD ?? (parameters.WPNAV_SPEED != null ? parameters.WPNAV_SPEED / 100 : null);
  const radius = parameters.NAV_ACC_RAD ?? (parameters.WPNAV_RADIUS != null ? parameters.WPNAV_RADIUS / 100 : null);
  return <aside className="form-warning"><strong>FC defaults and Home · required before flight, not required to type</strong><p>{snapshot ? `Last FC read: firmware ${snapshot.firmwareVersion}; cruise ${cruise ?? "unknown"} m/s; acceptance radius ${radius ?? "unknown"} m. Cached on ${formatVietnamDateTime(snapshot.observedAtUtc)}.` : "No FC parameter snapshot is available yet. Connect the assigned drone after approval to read its supported defaults."}</p><p>Home is read from the bound FC before ARM. Cached values are planning information; the system must recheck firmware, limits, Home and upload read-back before flight. Hold time has no universal FC default: blank means no hold except Hover, which requires a duration. Camera/gimbal are deferred.</p></aside>;
}

export default function WaypointNavigationFields({ point, maximumSpeed, onChange }: { point: MissionWaypoint; maximumSpeed: number; onChange: (value: WaypointNavigation) => void }) {
  const value = point.navigation ?? {};
  const number = (key: keyof WaypointNavigation, input: string) => onChange({ ...value, [key]: input === "" ? null : Number(input) });
  return <details open={point.actionType === "Hover" || Boolean(value.emergencyLandingCandidate)} style={{ gridColumn: "1 / -1" }}><summary>Waypoint {point.sequenceOrder} · optional navigation / landing-site settings</summary>
    <p>Latitude, longitude, altitude, command and order above are required. Overrides below remain inside the approved mission envelope.</p>
    <button type="button" className="module-secondary" onClick={() => onChange({})}>CLEAR OPTIONAL OVERRIDES</button>
    <div className="mission-field-grid">
      {point.actionType !== "TakeOff" && <label>Speed override (m/s) · optional<input type="number" min="0.5" max={maximumSpeed} step="0.01" value={value.speedMetersPerSecond ?? ""} placeholder="Bounded FC / mission default" onChange={event => number("speedMetersPerSecond", event.target.value)} /></label>}
      {(point.actionType === "FlyThrough" || point.actionType === "Hover") && <><label>Hold time (s) · {point.actionType === "Hover" ? "required for Hover" : "optional"}<input type="number" min={point.actionType === "Hover" ? 0.1 : 0} max="600" step="0.1" required={point.actionType === "Hover"} value={value.holdTimeSeconds ?? ""} placeholder="0 — no hold" onChange={event => number("holdTimeSeconds", event.target.value)} /></label>
      <label>Acceptance radius (m) · optional<input type="number" min="0.1" max="10" step="0.1" value={value.acceptanceRadiusMeters ?? ""} placeholder="Read from FC" onChange={event => number("acceptanceRadiusMeters", event.target.value)} /></label></>}
      <label>Pass radius · optional, unavailable for multicopter v1<input disabled value="" placeholder="Leave unset; not silently applied" /></label>
      <label>Yaw angle (degrees) · optional<input type="number" min="0" max="359.99" step="0.1" value={value.yawDegrees ?? ""} placeholder="FC yaw behavior" onChange={event => number("yawDegrees", event.target.value)} /></label>
      <label><span><input type="checkbox" checked={value.emergencyLandingCandidate ?? false} onChange={event => onChange({ ...value, emergencyLandingCandidate: event.target.checked })} /> Propose this waypoint as an emergency landing site · optional</span><small>Requires manager review, ground clearance and a verified reachable approach. A normal waypoint is not a landing site.</small></label>
      {(point.actionType === "Land" || value.emergencyLandingCandidate) && <>
        <label>Landing ground altitude relative to takeoff (m) · required before flight<input type="number" min="0" max="30" step="0.1" required={Boolean(value.emergencyLandingCandidate)} value={value.landingGroundAltitudeMeters ?? ""} placeholder="0 only when ground level equals Home" onChange={event => number("landingGroundAltitudeMeters", event.target.value)} /></label>
        <label>Landing-site clearance notes · {value.emergencyLandingCandidate ? "required" : "optional"}<textarea required={Boolean(value.emergencyLandingCandidate)} minLength={value.emergencyLandingCandidate ? 10 : undefined} maxLength={500} value={value.landingSiteNotes ?? ""} onChange={event => onChange({ ...value, landingSiteNotes: event.target.value || null })} /></label>
      </>}
    </div>
  </details>;
}
