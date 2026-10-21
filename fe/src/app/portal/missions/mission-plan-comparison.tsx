"use client";
import { useEffect, useState } from "react";
import type { CreateMissionRequest, Geofence, MissionValidationResult } from "@/lib/portal/types";
import { getGeofences } from "@/lib/portal/client";
import MissionMap from "./mission-map";
import styles from "./mission-evidence.module.css";

export default function MissionPlanComparison({ current, proposed, validation }: { current?: CreateMissionRequest | null; proposed?: CreateMissionRequest | null; validation?: MissionValidationResult | null }) {
  const [routeView, setRouteView] = useState<"current" | "proposed">("proposed");
  const [showMap, setShowMap] = useState(false);
  const [zones, setZones] = useState<Geofence[]>([]);
  useEffect(() => { let active = true; void getGeofences().then(value => { if (active) setZones(value); }).catch(() => {}); return () => { active = false; }; }, []);
  if (!current || !proposed || !Array.isArray(current.waypoints) || !Array.isArray(proposed.waypoints)) {
    return <section className={`${styles.panel} ${styles.empty}`} role="status"><span className={styles.kicker}>PLAN COMPARISON</span><div className={styles.emptyIcon} aria-hidden="true">⇄</div><h2>Plans are not available yet</h2><p>We need both the saved plan and the submitted proposal to show the changes.</p><small>Refresh the review queue to try again. Approval remains disabled until the review details are available.</small></section>;
  }
  const passed = validation?.isValid === true && !validation.stale && !validation.incomplete;
  const hasFindings = !!validation?.findings?.length;
  const uncertain = !validation || validation.stale || validation.incomplete || validation.status === "Incomplete";
  return <section className={`${styles.panel} ${styles.comparison}`}>
    <header className={styles.heading}><div><span className={styles.kicker}>PLAN COMPARISON</span><h2>Review proposed changes</h2><p>The saved plan stays active until this proposal is approved.</p></div></header>
    <div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>Field</th><th>Current</th><th>Proposed</th></tr></thead><tbody>
      {([['Name', current.name, proposed.name], ['Description', current.description, proposed.description], ['UAV', current.uavId, proposed.uavId], ['Start', current.startTime, proposed.startTime], ['End', current.endTime, proposed.endTime], ['Altitude reference', current.altitudeReference, proposed.altitudeReference]]).map(([label, old, next]) => <tr key={label}><th>{label}</th><td>{old || "Not specified"}</td><td>{next || "Not specified"}</td></tr>)}
    </tbody></table></div>
    <div className={`${styles.routeStatus} ${passed ? styles.passed : styles.attention}`} role="status">
      <strong>{passed ? "Waypoints and route checks passed" : uncertain ? "Route verification needs attention" : hasFindings ? "Route has restriction findings" : "Mission checks have not passed"}</strong>
      <p>{passed ? "No blocking findings were reported for the submitted plan at the last check." : uncertain ? "The current evidence cannot confirm route clearance. Review missing data, restriction geometry or altitude references and revalidate." : "Review the findings below. The submitted route is preserved for the pilot to correct; the system does not reroute it automatically."}</p>
      <button type="button" disabled={!passed} onClick={() => setShowMap(value => !value)}>{passed ? showMap ? "Hide route map" : "Inspect route map" : "Route inspection required below"}</button>
    </div>
    {(!passed || showMap) && <><div className={styles.routeToolbar}><div role="group" aria-label="Route to inspect"><button type="button" aria-pressed={routeView === "proposed"} onClick={() => setRouteView("proposed")}>Submitted proposal</button><button type="button" aria-pressed={routeView === "current"} onClick={() => setRouteView("current")}>Saved plan</button></div><span>{routeView === "proposed" ? proposed.waypoints.length : current.waypoints.length} waypoints</span></div>
      <MissionMap waypoints={routeView === "proposed" ? proposed.waypoints : current.waypoints} geofences={zones} readOnly findings={routeView === "proposed" ? validation?.findings : undefined} />
      <p>{routeView === "proposed" ? "Submitted route · red markers and segments identify restriction findings. Incomplete findings require data review and do not establish a confirmed violation." : "Saved plan · the proposal validation applies to the submitted route, not this view."}</p></>}
    <details className={styles.waypointDetails}><summary>Compare waypoint coordinates and altitude</summary><div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>Waypoint</th><th>Current coordinates / altitude</th><th>Proposed coordinates / altitude</th></tr></thead><tbody>{Array.from({ length: Math.max(current.waypoints.length, proposed.waypoints.length) }, (_, i) => <tr key={i}><th>WP{i + 1}</th><td>{current.waypoints[i] ? `${current.waypoints[i].latitude}, ${current.waypoints[i].longitude} / ${current.waypoints[i].altitude ?? "unknown"} m` : "—"}</td><td>{proposed.waypoints[i] ? `${proposed.waypoints[i].latitude}, ${proposed.waypoints[i].longitude} / ${proposed.waypoints[i].altitude ?? "unknown"} m` : "Removed"}</td></tr>)}</tbody></table></div></details>
    <p>Validation: {validation?.status ?? "Incomplete"}. Checked: {validation?.checkedAt ?? "Not checked"}</p>
    {validation?.findings?.map((finding, i) => <p role="alert" key={i}>{finding.reason} · Restriction revision {finding.revision}</p>)}
  </section>;
}
