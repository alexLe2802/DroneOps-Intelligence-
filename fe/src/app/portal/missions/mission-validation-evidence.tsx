import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import type { MissionValidationResult } from "@/lib/portal/types";
import { validationState } from "@/lib/portal/mission-review";
import styles from "./mission-evidence.module.css";

const formatNumber = (value: number, maximumFractionDigits = 1) => new Intl.NumberFormat("en-US", { maximumFractionDigits }).format(value);

export default function MissionValidationEvidence({ missionId, validation, historical = false }: {
  missionId?: string; validation?: MissionValidationResult | null; historical?: boolean;
}) {
  const state = validationState(validation);
  const battery = validation?.battery;
  return <section className={styles.validationEvidence} aria-label="Validation run evidence">
    <p role="status"><strong>Validation: {state}</strong>{state === "Stale" ? " — Outdated evidence; revalidate before approval." : historical ? " — Historical run; does not establish current clearance." : ""}</p>
    {validation && <>
      <dl>
        <div><dt>Validation run</dt><dd>{validation.id ?? "Not recorded"}</dd></div>
        <div><dt>Review request</dt><dd>{validation.updateRequestId ?? "Current mission"}</dd></div>
        <div><dt>Checked at</dt><dd>{validation.checkedAt ? formatVietnamDateTime(validation.checkedAt) : "Not recorded"}</dd></div>
        <div><dt>Restriction revision</dt><dd>{validation.restrictionRevision || "Not recorded"}</dd></div>
      </dl>
      <p>{validation.message}</p>
      {battery && <section className={styles.batteryAdvisory} aria-label="Battery requirement">
        <header>
          <div><span>BATTERY REQUIREMENT</span><strong>{battery.status.toUpperCase()}</strong></div>
          <b>{battery.status.toLowerCase() === "available" ? `${Math.ceil(battery.requiredBatteryPercent)}% minimum` : battery.riskLevel}</b>
        </header>
        <p className={styles.batteryMessage}>{battery.message || "Battery requirement was calculated for this route."}</p>
        {battery.status.toLowerCase() === "available" && <dl className={styles.batteryMetrics}>
          <div><dt>Calculated use</dt><dd>{formatNumber(battery.requiredBatteryPercent, 2)}%</dd></div>
          <div><dt>Estimated flight</dt><dd>{formatNumber(battery.estimatedFlightMinutes)} min</dd></div>
          <div><dt>Route distance</dt><dd>{formatNumber(battery.routeDistanceMeters / 1000, 2)} km</dd></div>
          <div><dt>Safety reserve</dt><dd>{formatNumber(battery.safetyReservePercent)}%</dd></div>
          <div><dt>Weather adjustment</dt><dd>+{formatNumber(battery.weatherAdjustmentPercent)}%</dd></div>
        </dl>}
        {!!battery.reasons?.length && <ul>{battery.reasons.map((reason, index) => <li key={`battery-reason-${index}`}>{reason}</li>)}</ul>}
        {!!battery.warnings?.length && <ul className={styles.batteryWarnings}>{battery.warnings.map((warning, index) => <li key={`battery-warning-${index}`}>{warning}</li>)}</ul>}
      </section>}
      {!!validation.issues?.length && <ul>{validation.issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul>}
      {!!validation.findings?.length && <div aria-label="Route findings in text"><p>Route findings remain available here if the map cannot load.</p><ul>{validation.findings.map((finding, index) => <li key={index}>
        <strong>{finding.waypointSequence != null ? `Waypoint ${finding.waypointSequence}` : finding.segmentFrom != null && finding.segmentTo != null ? `Segment ${finding.segmentFrom}–${finding.segmentTo}` : "Route"}</strong>: {finding.reason} · Restriction {finding.geofenceId}, revision {finding.revision} · {finding.rule}
      </li>)}</ul></div>}
      {missionId && validation.id && <a href={`/portal/validation-results?missionId=${encodeURIComponent(missionId)}&runId=${encodeURIComponent(validation.id)}`}>Open this validation run</a>}
    </>}
  </section>;
}
