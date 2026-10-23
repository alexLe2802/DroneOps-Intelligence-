"use client";
import { useEffect, useState } from "react";
import type { CreateMissionRequest, Geofence, MissionValidationResult } from "@/lib/portal/types";
import { getGeofences } from "@/lib/portal/client";
import { formatWeatherTime } from "@/lib/portal/weather-display";
import { planFieldChanges, waypointReviewRows } from "@/lib/portal/plan-review";
import MissionMap from "./mission-map";
import styles from "./mission-evidence.module.css";

export default function MissionPlanComparison({ current, proposed, validation, isInitial = false }: { isInitial?: boolean; current?: CreateMissionRequest | null; proposed?: CreateMissionRequest | null; validation?: MissionValidationResult | null }) {
  const [routeView, setRouteView] = useState<"current" | "proposed">("proposed");
  const [showMap, setShowMap] = useState(false);
  const [zones, setZones] = useState<Geofence[]>([]);
  useEffect(() => { let active = true; void getGeofences().then(value => { if (active) setZones(value); }).catch(() => {}); return () => { active = false; }; }, []);
  if (!proposed || !Array.isArray(proposed.waypoints) || (!isInitial && (!current || !Array.isArray(current.waypoints)))) {
    return <section className={`${styles.panel} ${styles.empty}`} role="status"><span className={styles.kicker}>PLAN COMPARISON</span><div className={styles.emptyIcon} aria-hidden="true">⇄</div><h2>Plans are not available yet</h2><p>We need both the saved plan and the submitted proposal to show the changes.</p><small>Refresh the review queue to try again. Approval remains disabled until the review details are available.</small></section>;
  }
  const saved = current ?? proposed;
  const changedFields = planFieldChanges(saved, proposed);
  const waypointRows = waypointReviewRows(isInitial ? [] : saved.waypoints, proposed.waypoints);
  const displayRoute = isInitial ? "proposed" : routeView;
  const fields = [
    ["Name", "name"], ["Description", "description"], ["UAV", "uavId"],
    ["Start", "startTime"], ["End", "endTime"], ["Altitude reference", "altitudeReference"],
  ] as const;
  const fieldValue = (plan: CreateMissionRequest, key: typeof fields[number][1]) =>
    key === "startTime" || key === "endTime" ? formatWeatherTime(plan[key]) : plan[key] || "Not specified";
  const waypointValue = (point: typeof proposed.waypoints[number] | undefined) => point
    ? `${point.latitude}, ${point.longitude} / ${point.altitude ?? "unknown"} m · ${point.actionType}` : "—";
  const passed = validation?.isValid === true && !validation.stale && !validation.incomplete;
  const hasFindings = !!validation?.findings?.length;
  const uncertain = !validation || validation.stale || validation.incomplete || validation.status === "Incomplete";
  return <section className={`${styles.panel} ${styles.comparison}`}>
    <header className={styles.heading}><div><span className={styles.kicker}>{isInitial ? "NEW MISSION / INITIAL REVIEW" : "PLAN COMPARISON"}</span><h2>{isInitial ? "Kế hoạch gửi duyệt" : "Review proposed changes"}</h2><p>{isInitial ? "Nhiệm vụ mới — chưa có kế hoạch trước đó để so sánh." : "Current: kế hoạch đang lưu. Proposed: kế hoạch đề nghị thay thế. Các mục thay đổi được đánh dấu bên dưới."}</p></div></header>
    {!isInitial && <p className={styles.changeSummary}>{changedFields.length || waypointRows.some(row => row.changed) ? `${changedFields.length} trường thông tin và ${waypointRows.filter(row => row.changed).length} waypoint thay đổi.` : "Không có thay đổi so với kế hoạch đang lưu."}</p>}
    <div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>Field</th>{!isInitial && <th>Current</th>}<th>{isInitial ? "Kế hoạch gửi duyệt" : "Proposed"}</th></tr></thead><tbody>
      {fields.map(([label, key]) => <tr key={key} className={!isInitial && changedFields.includes(key) ? styles.changedRow : undefined}><th>{label}{!isInitial && changedFields.includes(key) && <small className={styles.changeBadge}>Changed</small>}</th>{!isInitial && <td>{fieldValue(saved, key)}</td>}<td>{fieldValue(proposed, key)}</td></tr>)}
    </tbody></table></div>
    <div className={`${styles.routeStatus} ${passed ? styles.passed : styles.attention}`} role="status">
      <strong>{passed ? "Waypoints and route checks passed" : uncertain ? "Route verification needs attention" : hasFindings ? "Route has restriction findings" : "Mission checks have not passed"}</strong>
      <p>{passed ? "No blocking findings were reported for the submitted plan at the last check." : uncertain ? "The current evidence cannot confirm route clearance. Review missing data, restriction geometry or altitude references and revalidate." : "Review the findings below. The submitted route is preserved for the pilot to correct; the system does not reroute it automatically."}</p>
      <button type="button" disabled={!passed} onClick={() => setShowMap(value => !value)}>{passed ? showMap ? "Hide route map" : "Inspect route map" : "Route inspection required below"}</button>
    </div>
    {(!passed || showMap) && <><div className={styles.routeToolbar}><div role="group" aria-label="Route to inspect"><button type="button" aria-pressed={displayRoute === "proposed"} onClick={() => setRouteView("proposed")}>Submitted proposal</button>{!isInitial && <button type="button" aria-pressed={displayRoute === "current"} onClick={() => setRouteView("current")}>Saved plan</button>}</div><span>{displayRoute === "proposed" ? proposed.waypoints.length : saved.waypoints.length} waypoints</span></div>
      <MissionMap waypoints={displayRoute === "proposed" ? proposed.waypoints : saved.waypoints} geofences={zones} readOnly findings={displayRoute === "proposed" ? validation?.findings : undefined} />
      <p>{displayRoute === "proposed" ? "Submitted route · red markers and segments identify restriction findings. Incomplete findings require data review and do not establish a confirmed violation." : "Saved plan · the proposal validation applies to the submitted route, not this view."}</p></>}
    <details className={styles.waypointDetails}><summary>{isInitial ? "Xem waypoint của kế hoạch gửi duyệt" : "Compare waypoint coordinates, altitude and action"}</summary><div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>Waypoint</th>{!isInitial && <th>Current coordinates / altitude / action</th>}<th>{isInitial ? "Submitted coordinates / altitude / action" : "Proposed coordinates / altitude / action"}</th></tr></thead><tbody>{waypointRows.map(row => <tr key={row.sequence} className={!isInitial && row.changed ? styles.changedRow : undefined}><th>WP{row.sequence}{!isInitial && row.changed && <small className={styles.changeBadge}>{row.status}</small>}</th>{!isInitial && <td>{waypointValue(row.before)}</td>}<td>{waypointValue(row.after)}</td></tr>)}</tbody></table></div></details>
    <p>Validation: {validation?.status ?? "Incomplete"}. Checked: {formatWeatherTime(validation?.checkedAt)}</p>
    {validation?.findings?.map((finding, i) => <p role="alert" key={i}>{finding.reason} · Restriction revision {finding.revision}</p>)}
  </section>;
}
