import type { MissionValidationResult } from "@/lib/portal/types";
import { validationState } from "@/lib/portal/mission-review";
import styles from "./mission-evidence.module.css";

export default function MissionValidationEvidence({ missionId, validation, historical = false }: {
  missionId?: string; validation?: MissionValidationResult | null; historical?: boolean;
}) {
  const state = validationState(validation);
  return <section className={styles.validationEvidence} aria-label="Validation run evidence">
    <p role="status"><strong>Validation: {state}</strong>{state === "Stale" ? " — Outdated evidence; revalidate before approval." : historical ? " — Historical run; does not establish current clearance." : ""}</p>
    {validation && <>
      <dl>
        <div><dt>Validation run</dt><dd>{validation.id ?? "Not recorded"}</dd></div>
        <div><dt>Review request</dt><dd>{validation.updateRequestId ?? "Current mission"}</dd></div>
        <div><dt>Checked at</dt><dd>{validation.checkedAt ? new Date(validation.checkedAt).toLocaleString() : "Not recorded"}</dd></div>
        <div><dt>Restriction revision</dt><dd>{validation.restrictionRevision || "Not recorded"}</dd></div>
      </dl>
      <p>{validation.message}</p>
      {!!validation.issues?.length && <ul>{validation.issues.map((issue, index) => <li key={index}>{issue}</li>)}</ul>}
      {!!validation.findings?.length && <div aria-label="Route findings in text"><p>Route findings remain available here if the map cannot load.</p><ul>{validation.findings.map((finding, index) => <li key={index}>
        <strong>{finding.waypointSequence != null ? `Waypoint ${finding.waypointSequence}` : finding.segmentFrom != null && finding.segmentTo != null ? `Segment ${finding.segmentFrom}–${finding.segmentTo}` : "Route"}</strong>: {finding.reason} · Restriction {finding.geofenceId}, revision {finding.revision} · {finding.rule}
      </li>)}</ul></div>}
      {missionId && validation.id && <a href={`/portal/validation-results?missionId=${encodeURIComponent(missionId)}&runId=${encodeURIComponent(validation.id)}`}>Open this validation run</a>}
    </>}
  </section>;
}
