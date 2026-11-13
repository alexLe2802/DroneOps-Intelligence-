"use client";
import { RotateCcw } from "lucide-react";
import type { AutomaticFlightLimits } from "@/lib/portal/types";
import styles from "./mission-authoring.module.css";
import RecoveryEnergyProfileFields from "./recovery-energy-profile";

export const defaultAutomaticLimits = (): AutomaticFlightLimits => ({
  takeoffSpeedMetersPerSecond: 6.5, climbSpeedMetersPerSecond: 6.5,
  descentSpeedMetersPerSecond: 5.8, landingSpeedMetersPerSecond: 1,
  maxAltitudeMeters: 30, maxWindMetersPerSecond: 3, maxGustMetersPerSecond: 5,
  policyVersion: "multicopter-envelope-v2",
});
const groups = (v2: boolean) => [
  { title: "Vertical speeds", fields: [
    ["takeoffSpeedMetersPerSecond", "Takeoff", "m/s", 1, v2 ? 8 : 1.5],
    ["climbSpeedMetersPerSecond", "Climb", "m/s", 1, v2 ? 8 : 1.5],
    ["descentSpeedMetersPerSecond", "Descent", "m/s", 0.6, v2 ? 6 : 1],
    ["landingSpeedMetersPerSecond", "Landing", "m/s", 0.6, v2 ? 1.5 : 0.7],
  ] },
  { title: "Altitude & weather", fields: [
    ["maxAltitudeMeters", "Altitude ceiling", "m", 10.1, 30],
    ["maxWindMetersPerSecond", "Wind limit", "m/s", 0.1, 3],
    ["maxGustMetersPerSecond", "Gust limit", "m/s", 0.1, 5],
  ] },
] as const;
export default function AutomaticFlightFields({ value, onChange }: { value: AutomaticFlightLimits; onChange: (value: AutomaticFlightLimits) => void }) {
  return <section className={`mission-fields ${styles.limits}`} aria-label="Automatic flight operating envelope">
    <header className={styles.heading}><div><h2>Automatic flight limits</h2><p>Optional overrides · system defaults are prefilled</p></div>
      <button type="button" className={styles.reset} onClick={() => onChange({ ...defaultAutomaticLimits(), recoveryEnergyProfile: value.recoveryEnergyProfile })}><RotateCcw size={14} aria-hidden="true" />Reset defaults</button>
    </header>
    {groups(value.policyVersion === "multicopter-envelope-v2").map(group => <fieldset className={styles.group} key={group.title}>
      <legend>{group.title}</legend>
      <div className={styles.limitGrid}>{group.fields.map(([key, label, unit, min, max]) => <label key={key} className={styles.field}>
        <span>{label}</span>
        <span className={styles.inputUnit}><input aria-label={`${label} (${unit}) · optional override`} type="number" min={min} max={max} step="0.1" value={Number.isFinite(value[key]) ? value[key] : ""} onBlur={() => { if (!Number.isFinite(value[key])) onChange({ ...value, [key]: defaultAutomaticLimits()[key] }); }} onChange={event => onChange({ ...value, [key]: event.target.value === "" ? NaN : Number(event.target.value) })} /><span aria-hidden="true">{unit}</span></span>
        <small>{min}–{max} {unit}{key === "maxAltitudeMeters" ? " · above FC Home" : ""}</small>
      </label>)}</div>
    </fieldset>)}
    <p className={styles.footnote}>Takeoff ≤ climb · Landing ≤ descent · Wind ≤ gust</p>
    <RecoveryEnergyProfileFields value={value.recoveryEnergyProfile} onChange={profile => onChange({ ...value, recoveryEnergyProfile: profile })} />
  </section>;
}
