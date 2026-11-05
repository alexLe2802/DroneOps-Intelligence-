"use client";

import { Gamepad2, Route } from "lucide-react";
import styles from "./mission-authoring.module.css";

export type FlightMode = "manual" | "automatic";

export default function FlightModeFields({ value, onChange }: { value: FlightMode | null; onChange: (mode: FlightMode) => void }) {
  return <section className={`mission-fields ${styles.mode}`} aria-labelledby="mission-flight-mode-title">
    <div className={styles.heading}><h2 id="mission-flight-mode-title">Flight mode</h2><span className={styles.required}>Required</span></div>
    <fieldset className={styles.modeOptions}>
      <legend className={styles.srOnly}>Choose flight mode</legend>
      <label className={value === "manual" ? styles.selected : ""}>
        <input type="radio" name="flight-mode" value="manual" checked={value === "manual"} required onChange={() => onChange("manual")} />
        <Gamepad2 size={20} aria-hidden="true" /><span>Manual operation</span>
      </label>
      <label className={value === "automatic" ? styles.selected : ""}>
        <input type="radio" name="flight-mode" value="automatic" checked={value === "automatic"} required onChange={() => onChange("automatic")} />
        <Route size={20} aria-hidden="true" /><span>Automatic mission</span>
      </label>
    </fieldset>
  </section>;
}
