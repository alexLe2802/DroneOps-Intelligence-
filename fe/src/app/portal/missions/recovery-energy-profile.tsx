"use client";

import { useState } from "react";
import type { RecoveryEnergyProfile } from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import styles from "./mission-authoring.module.css";

const numericFields = ["payloadKg", "maxWindMetersPerSecond", "minTemperatureCelsius", "maxTemperatureCelsius",
  "usableCapacityWh", "cruisePowerWatts", "climbPowerWatts", "descentPowerWatts", "landingPowerWatts", "hoverPowerWatts",
  "minimumGroundSpeedMetersPerSecond", "returnAltitudeMeters", "landingDurationSeconds", "holdDurationSeconds", "energyMarginFactor"] as const;

export default function RecoveryEnergyProfileFields({ value, onChange }: { value?: RecoveryEnergyProfile | null; onChange: (value: RecoveryEnergyProfile | null) => void }) {
  const [error, setError] = useState("");
  async function load(file: File | undefined) {
    if (!file) return;
    setError("");
    try {
      if (file.size > 32768) throw new Error("Use a JSON measurement file smaller than 32 KB.");
      const data = JSON.parse(await file.text());
      if (!data || typeof data.measurementReference !== "string" || data.measurementReference.length < 10
        || typeof data.uavId !== "string" || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(data.uavId)
        || typeof data.batteryIdentifier !== "string" || data.batteryIdentifier.length < 3
        || typeof data.airframeConfiguration !== "string" || data.airframeConfiguration.length < 5
        || ![data.measuredAtUtc, data.validUntilUtc].every(date => typeof date === "string" && /(?:Z|[+-]\d{2}:\d{2})$/i.test(date) && Number.isFinite(Date.parse(date)))
        || numericFields.some(key => typeof data[key] !== "number" || !Number.isFinite(data[key]))
        || !Array.isArray(data.remainingEnergyCurve) || data.remainingEnergyCurve.length < 3 || data.remainingEnergyCurve.length > 101
        || data.remainingEnergyCurve.some((p: {batteryPercent?: unknown; lowerBoundWh?: unknown} | null) => !p || typeof p.batteryPercent !== "number" || !Number.isFinite(p.batteryPercent) || typeof p.lowerBoundWh !== "number" || !Number.isFinite(p.lowerBoundWh)))
        throw new Error("The file needs measured phase power, operating conditions, dates and a remaining-energy curve. Empty template values are not measurements.");
      const profile: RecoveryEnergyProfile = { measurementReference: data.measurementReference,
        uavId: data.uavId, batteryIdentifier: data.batteryIdentifier, airframeConfiguration: data.airframeConfiguration,
        measuredAtUtc: new Date(data.measuredAtUtc).toISOString(), validUntilUtc: new Date(data.validUntilUtc).toISOString(),
        ...Object.fromEntries(numericFields.map(key => [key, data[key]])),
        remainingEnergyCurve: data.remainingEnergyCurve.map((p: {batteryPercent: number; lowerBoundWh: number}) => ({batteryPercent: p.batteryPercent, lowerBoundWh: p.lowerBoundWh})),
      } as RecoveryEnergyProfile;
      onChange(profile);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to read measurement file."); }
  }
  return <details className={styles.recoveryProfile}>
    <summary>Recovery energy measurements <span className={styles.optional}>{value ? "Supplied · review required" : "Optional during planning"}</span></summary>
    <p>Import measured data for this aircraft, battery and payload. Estimates remain advisory until the measured conditions, landing route and FC safety profile are verified.</p>
    <div className={styles.profileActions}><label className={styles.reset}>Import JSON<input type="file" accept=".json,application/json" className={styles.srOnly} onChange={event => { void load(event.target.files?.[0]); event.target.value = ""; }} /></label>
      <a className={styles.reset} href="/data/recovery-energy-profile.template.json" download>Download template</a>
      {value && <button className={styles.reset} type="button" onClick={() => onChange(null)}>Remove measurements</button>}</div>
    {value && <dl className={styles.profileValues}><div><dt>Measurement reference</dt><dd>{value.measurementReference}</dd></div><div><dt>Usable capacity / payload</dt><dd>{value.usableCapacityWh} Wh / {value.payloadKg} kg</dd></div><div><dt>Battery / configuration</dt><dd>{value.batteryIdentifier} · {value.airframeConfiguration}</dd></div><div><dt>Valid until</dt><dd>{formatVietnamDateTime(value.validUntilUtc)}</dd></div><div><dt>Energy margin</dt><dd>×{value.energyMarginFactor} · {value.remainingEnergyCurve.length} measured SOC points</dd></div></dl>}
    {error && <p role="alert">{error}</p>}
  </details>;
}
