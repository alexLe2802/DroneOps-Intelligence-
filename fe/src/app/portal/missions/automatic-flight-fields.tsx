"use client";
import type { AutomaticFlightLimits } from "@/lib/portal/types";

export const defaultAutomaticLimits = (): AutomaticFlightLimits => ({
  takeoffSpeedMetersPerSecond: 1, climbSpeedMetersPerSecond: 1,
  descentSpeedMetersPerSecond: 0.7, landingSpeedMetersPerSecond: 0.7,
  maxAltitudeMeters: 30, maxWindMetersPerSecond: 3, maxGustMetersPerSecond: 5,
  policyVersion: "multicopter-envelope-v1",
});
export default function AutomaticFlightFields({ value, onChange }: { value: AutomaticFlightLimits; onChange: (value: AutomaticFlightLimits) => void }) {
  const fields = [
    ["takeoffSpeedMetersPerSecond", "Takeoff speed (m/s)", 1, 1.5],
    ["climbSpeedMetersPerSecond", "Climb speed (m/s)", 1, 1.5],
    ["descentSpeedMetersPerSecond", "Descent speed (m/s)", 0.6, 1],
    ["landingSpeedMetersPerSecond", "Landing speed (m/s)", 0.6, 0.7],
    ["maxAltitudeMeters", "Altitude ceiling above takeoff home (m)", 10.1, 30],
    ["maxWindMetersPerSecond", "Maximum forecast wind (m/s)", 0.1, 3],
    ["maxGustMetersPerSecond", "Maximum forecast gust (m/s)", 0.1, 5],
  ] as const;
  return <section className="mission-fields" aria-label="Automatic flight operating envelope">
    <h2>Automatic flight limits</h2>
    <button type="button" className="module-secondary" onClick={() => onChange(defaultAutomaticLimits())}>RESTORE SYSTEM LIMITS</button>
    <p>Multicopter trial profile: default cruise 3 m/s (10.8 km/h), horizontal ceiling 5 m/s (18 km/h), default waypoint altitude 20 m, altitude ceiling 30 m relative to takeoff home. These limits are reviewed with your mission.</p>
    <div className="mission-field-grid">{fields.map(([key, label, min, max]) => <label key={key}>{label} · optional override<input type="number" min={min} max={max} step="0.1" value={Number.isFinite(value[key]) ? value[key] : ""} onBlur={() => { if (!Number.isFinite(value[key])) onChange({ ...value, [key]: defaultAutomaticLimits()[key] }); }} onChange={event => onChange({ ...value, [key]: event.target.value === "" ? NaN : Number(event.target.value) })} /><small>Allowed: {min}–{max}; blank resets to system policy.</small></label>)}</div>
    <p>Takeoff ≤ climb; landing ≤ descent; gust ≥ wind. Lower site, aircraft or airspace limits still apply. Relative altitude does not establish clearance above terrain or obstacles. FC limits and recovery behavior must be verified before automatic launch.</p>
    <details><summary>Required data before automatic flight</summary><ul>
      <li>At creation: assigned UAV, mission name, start/end time and route coordinates, heights and actions. Hover needs a duration.</li>
      <li>Before launch: approved unchanged plan, connected bound FC, fresh Home/navigation/battery data, verified FC limits and mission upload, pilot online and explicit confirmation.</li>
      <li>Landing ground elevation is required before flight. An emergency landing candidate also needs clearance notes; it must be verified before use.</li>
      <li>Optional overrides: cruise/maximum speed, vertical rates, ceiling, weather limits, waypoint speed/radius/yaw and landing-site candidates. Blank waypoint values use supported FC/mission defaults.</li>
    </ul></details>
    <details><summary>Failsafe behavior and release status</summary><p>Critical battery takes priority. Recovery to Home or an alternate requires verified navigation, landing clearance and enough calibrated energy including landing reserve. A normal waypoint is not an emergency landing site. Loss of navigation, stale data and missing battery evidence cannot authorize a new return leg. In-flight DISARM and automatic mission resume are blocked.</p><p>FC-specific enforcement and recovery are awaiting simulator and aircraft verification. Creating or approving this plan does not enable automatic launch.</p></details>
  </section>;
}
