import { AlertTriangle, CloudRain, Wind } from "lucide-react";
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
  const weather = mission.validation?.weather ?? mission.latestValidation?.weather ?? null;
  const warnings = weather?.warnings ?? [];

  return <div className={styles.backdrop} role="presentation">
    <section className={styles.dialog} role="alertdialog" aria-modal="true" aria-labelledby="high-risk-title" aria-describedby="high-risk-description">
      <header>
        <span><AlertTriangle size={20} aria-hidden="true" /></span>
        <div><p>VALIDATION RESULT // PILOT DECISION REQUIRED</p><h2 id="high-risk-title">High-risk weather detected</h2></div>
      </header>
      <div className={styles.body}>
        <p id="high-risk-description">Mandatory checks completed, but this mission exceeds the configured weather-risk threshold. Confirm that you accept the risk before sending it for manager review.</p>
        <div className={styles.mission}><span>MISSION</span><strong>{mission.name}</strong><small>{mission.id}</small></div>
        <div className={styles.metrics}>
          <div><span>RISK LEVEL</span><strong className={styles.risk}>{weather?.overallRisk ?? "High"}</strong></div>
          <div><span><Wind size={13} aria-hidden="true" /> MAX WIND</span><strong>{metric(weather?.maxWindMetersPerSecond, "m/s")}</strong></div>
          <div><span><Wind size={13} aria-hidden="true" /> MAX GUST</span><strong>{metric(weather?.maxGustMetersPerSecond, "m/s")}</strong></div>
          <div><span><CloudRain size={13} aria-hidden="true" /> MAX RAIN</span><strong>{metric(weather?.maxRainMillimetersPerHour, "mm/h")}</strong></div>
        </div>
        {warnings.length > 0 && <div className={styles.warnings}><span>WEATHER FINDINGS</span><ul>{warnings.map((warning, index) => <li key={`${warning}-${index}`}>{warning}</li>)}</ul></div>}
        {error && <p className={styles.error} role="alert">{error}</p>}
      </div>
      <footer>
        <button type="button" className={styles.cancel} disabled={busy !== null} onClick={onCancel}>{busy === "cancel" ? "CANCELLING..." : "CANCEL MISSION"}</button>
        <button type="button" className={styles.confirm} disabled={busy !== null} onClick={onConfirm}>{busy === "confirm" ? "CONFIRMING..." : "OK — CONFIRM HIGH RISK"}</button>
      </footer>
    </section>
  </div>;
}
