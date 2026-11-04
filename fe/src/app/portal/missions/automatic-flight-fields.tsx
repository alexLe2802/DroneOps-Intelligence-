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
    <p>Multicopter trial profile: default cruise 3 m/s (10.8 km/h), horizontal ceiling 5 m/s (18 km/h), default waypoint altitude 20 m, altitude ceiling 30 m relative to takeoff home. These limits are reviewed with your mission.</p>
    <div className="mission-field-grid">{fields.map(([key, label, min, max]) => <label key={key}>{label} · optional override<input type="number" min={min} max={max} step="0.1" value={Number.isFinite(value[key]) ? value[key] : ""} onBlur={() => { if (!Number.isFinite(value[key])) onChange({ ...value, [key]: defaultAutomaticLimits()[key] }); }} onChange={event => onChange({ ...value, [key]: event.target.value === "" ? NaN : Number(event.target.value) })} /><small>Allowed: {min}–{max}; blank resets to system policy.</small></label>)}</div>
    <p>Takeoff ≤ climb; landing ≤ descent; gust ≥ wind. Lower site, aircraft or airspace limits still apply. Relative altitude does not establish clearance above terrain or obstacles. FC limits and recovery behavior must be verified before automatic launch.</p>
  </section>;
}
