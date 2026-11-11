import { AlertTriangle, BatteryCharging, CloudRain, Clock3, Wind } from "lucide-react";
import type { MissionDetail } from "@/lib/portal/types";
import styles from "./high-risk-confirmation.module.css";

type Props = {
  mission: MissionDetail;
  busy: "confirm" | "cancel" | null;
  error: string;
  onConfirm: () => void;
  onCancel: () => void;
};

const metric = (value: number | null | undefined, unit: string) => value == null ? "N/A" : `${value} ${unit}`;

export default function HighRiskConfirmation({ mission, busy, error, onConfirm, onCancel }: Props) {
  const validation = mission.validation ?? mission.latestValidation ?? mission.preflightValidation ?? null;
  const weather = validation?.weather ?? null;
  const battery = validation?.battery ?? null;
  const weatherHigh = weather?.overallRisk.trim().toLowerCase() === "high";
  const batteryHigh = battery?.requiresConfirmation === true;
  const title = weatherHigh && batteryHigh
    ? "High-risk weather and battery requirement"
    : batteryHigh
      ? "Battery requirement needs confirmation"
      : "High-risk weather detected";
  const description = weatherHigh && batteryHigh
    ? "This route exceeds both weather and battery-risk thresholds. Review the required battery level and weather findings before sending it for manager review."
    : batteryHigh
      ? "The calculated battery requirement exceeds the configured safe operating threshold. Review the estimate before sending this mission for manager review."
      : "Mandatory checks completed, but this mission exceeds the configured weather-risk threshold. Confirm that you accept the risk before sending it for manager review.";
  const weatherWarnings = weather?.warnings ?? [];
  const batteryFindings = [...(battery?.reasons ?? []), ...(battery?.warnings ?? [])];

  return <div className={styles.backdrop} role="presentation">
    <section className={styles.dialog} role="alertdialog" aria-modal="true" aria-labelledby="high-risk-title" aria-describedby="high-risk-description">
      <header>
        <span><AlertTriangle size={20} aria-hidden="true" /></span>
        <div><p>VALIDATION RESULT // PILOT DECISION REQUIRED</p><h2 id="high-risk-title">{title}</h2></div>
      </header>
      <div className={styles.body}>
        <p id="high-risk-description">{description}</p>
        <div className={styles.mission}><span>MISSION</span><strong>{mission.name}</strong><small>{mission.id}</small></div>
        <div className={styles.metrics}>
          {weather && <><div><span>WEATHER RISK</span><strong className={styles.risk}>{weather.overallRisk}</strong></div>
            <div><span><Wind size={13} aria-hidden="true" /> MAX WIND</span><strong>{metric(weather.maxWindMetersPerSecond, "m/s")}</strong></div>
            <div><span><Wind size={13} aria-hidden="true" /> MAX GUST</span><strong>{metric(weather.maxGustMetersPerSecond, "m/s")}</strong></div>
            <div><span><CloudRain size={13} aria-hidden="true" /> MAX RAIN</span><strong>{metric(weather.maxRainMillimetersPerHour, "mm/h")}</strong></div></>}
          {battery && <><div><span><BatteryCharging size={13} aria-hidden="true" /> REQUIRED BATTERY</span><strong className={styles.risk}>{Math.ceil(battery.requiredBatteryPercent)}% minimum</strong></div>
            <div><span><Clock3 size={13} aria-hidden="true" /> EST. FLIGHT</span><strong>{metric(battery.estimatedFlightMinutes, "min")}</strong></div></>}
        </div>
        {battery && <div className={styles.battery}><span>BATTERY ASSESSMENT</span><strong>{battery.message}</strong><small>Calculated use: {battery.requiredBatteryPercent.toFixed(2)}% · Safety reserve: {battery.safetyReservePercent}% · Weather adjustment: +{battery.weatherAdjustmentPercent}%</small></div>}
        {weatherWarnings.length > 0 && <div className={styles.warnings}><span>WEATHER FINDINGS</span><ul>{weatherWarnings.map((warning, index) => <li key={`${warning}-${index}`}>{warning}</li>)}</ul></div>}
        {batteryFindings.length > 0 && <div className={styles.warnings}><span>BATTERY FINDINGS</span><ul>{batteryFindings.map((finding, index) => <li key={`${finding}-${index}`}>{finding}</li>)}</ul></div>}
        {error && <p className={styles.error} role="alert">{error}</p>}
      </div>
      <footer>
        <button type="button" className={styles.cancel} disabled={busy !== null} onClick={onCancel}>{busy === "cancel" ? "CANCELLING..." : "WITHDRAW PROPOSAL"}</button>
        <button type="button" className={styles.confirm} disabled={busy !== null} onClick={onConfirm}>{busy === "confirm" ? "CONFIRMING..." : "CONFIRM HIGH RISK"}</button>
      </footer>
    </section>
  </div>;
}
