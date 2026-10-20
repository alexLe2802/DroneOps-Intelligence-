"use client";
import { useEffect, useState } from "react";
import type { CreateMissionRequest, Geofence, MissionValidationResult } from "@/lib/portal/types";
import { getGeofences } from "@/lib/portal/client";
import MissionMap from "./mission-map";

export default function MissionPlanComparison({ current, proposed, validation }: { current: CreateMissionRequest; proposed: CreateMissionRequest; validation?: MissionValidationResult | null }) {
  const [zones, setZones] = useState<Geofence[]>([]);
  useEffect(() => { let active = true; void getGeofences().then(value => { if (active) setZones(value); }).catch(() => {}); return () => { active = false; }; }, []);
  return <section className="mission-waypoint-panel">
    <h2>Current mission / Proposed changes</h2><p>The saved mission remains unchanged until this request is approved.</p>
    <div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>Field</th><th>Current</th><th>Proposed</th></tr></thead><tbody>
      {([['Name', current.name, proposed.name], ['Description', current.description, proposed.description], ['UAV', current.uavId, proposed.uavId], ['Start', current.startTime, proposed.startTime], ['End', current.endTime, proposed.endTime], ['Altitude reference', current.altitudeReference, proposed.altitudeReference]]).map(([label, old, next]) => <tr key={label}><th>{label}</th><td>{old || "Not specified"}</td><td>{next || "Not specified"}</td></tr>)}
    </tbody></table></div>
    <h3>Current route</h3><MissionMap waypoints={current.waypoints} geofences={zones} readOnly />
    <h3>Proposed route</h3><MissionMap waypoints={proposed.waypoints} geofences={zones} readOnly findings={validation?.findings} />
    <div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>Waypoint</th><th>Current coordinates / altitude</th><th>Proposed coordinates / altitude</th></tr></thead><tbody>{Array.from({ length: Math.max(current.waypoints.length, proposed.waypoints.length) }, (_, i) => <tr key={i}><th>WP{i + 1}</th><td>{current.waypoints[i] ? `${current.waypoints[i].latitude}, ${current.waypoints[i].longitude} / ${current.waypoints[i].altitude ?? "unknown"} m` : "—"}</td><td>{proposed.waypoints[i] ? `${proposed.waypoints[i].latitude}, ${proposed.waypoints[i].longitude} / ${proposed.waypoints[i].altitude ?? "unknown"} m` : "Removed"}</td></tr>)}</tbody></table></div>
    <p>Validation: {validation?.status ?? "Incomplete"}. Checked: {validation?.checkedAt ?? "Not checked"}</p>
    {validation?.findings?.map((finding, i) => <p role="alert" key={i}>{finding.reason} · Restriction revision {finding.revision}</p>)}
  </section>;
}
