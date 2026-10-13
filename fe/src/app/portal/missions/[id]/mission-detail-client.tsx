"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { getGeofences, getMission } from "@/lib/portal/client";
import type { Geofence, MissionDetail } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

const formatDate = (value: string | null) => value ? new Date(value).toLocaleString() : "Not scheduled";
const formatStatus = (value: string) => value.replace(/([A-Z])/g, " $1").trim();

export default function MissionDetailClient({ id }: { id: string }) {
  const [mission, setMission] = useState<MissionDetail | null>(null);
  const [geofences, setGeofences] = useState<Geofence[]>([]);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(() => {
    setMission(null);
    setError(null);
    void Promise.allSettled([getMission(id), getGeofences()]).then(([missionResult, geofenceResult]) => {
      if (missionResult.status === "rejected") {
        setError(missionResult.reason);
        return;
      }
      setMission(missionResult.value);
      setGeofences(geofenceResult.status === "fulfilled" ? geofenceResult.value : []);
    });
  }, [id]);

  useEffect(() => {
    let active = true;
    void Promise.allSettled([getMission(id), getGeofences()]).then(([missionResult, geofenceResult]) => {
      if (!active) return;
      if (missionResult.status === "rejected") {
        setError(missionResult.reason);
        return;
      }
      setMission(missionResult.value);
      setGeofences(geofenceResult.status === "fulfilled" ? geofenceResult.value : []);
    });
    return () => { active = false; };
  }, [id]);

  const duration = useMemo(() => {
    if (!mission?.startTime || !mission.endTime) return "Not available";
    const minutes = Math.max(0, Math.round((new Date(mission.endTime).getTime() - new Date(mission.startTime).getTime()) / 60000));
    return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes} min`;
  }, [mission]);

  if (error) return <ModuleError error={error} retry={load} />;
  if (!mission) return <ModuleLoading label="Loading mission details" />;

  return <>
    <header className="module-heading mission-detail-heading">
      <div>
        <p className="module-kicker">PILOT / MY MISSIONS / MISSION DETAIL</p>
        <div className="mission-title-line"><h1>{mission.name}</h1><span className={`status status-${mission.status.toLowerCase()}`}>{formatStatus(mission.status)}</span></div>
        <p>{mission.description ?? "No mission description has been provided."}</p>
      </div>
      <div className="heading-actions"><Link className="module-secondary module-link" href="/portal/missions">← MY MISSIONS</Link></div>
    </header>
    {mission.detailAvailable === false && <p className="api-pending-banner" role="status"><b>MISSION DETAIL API PENDING</b> Showing the available mission summary. Route and waypoint data will appear when the backend returns a response from GET /api/missions/{mission.id}.</p>}

    <section className="mission-detail-stats" aria-label="Mission summary">
      <article><span>MISSION VERSION</span><strong>V{mission.latestVersion.toString().padStart(2, "0")}</strong><small>{mission.id.slice(0, 8).toUpperCase()}</small></article>
      <article><span>ASSIGNED UAV</span><strong>{mission.uavCode ?? "UNASSIGNED"}</strong><small>{mission.uavId ? mission.uavId.slice(0, 8).toUpperCase() : "NO AIRCRAFT ID"}</small></article>
      <article><span>WAYPOINTS</span><strong>{mission.waypoints.length.toString().padStart(2, "0")}</strong><small>ROUTE NODES</small></article>
      <article><span>EST. DURATION</span><strong>{duration}</strong><small>PLANNED WINDOW</small></article>
    </section>

    <section className="mission-detail-layout">
      <div className="mission-route-panel">
        <header><div><p className="module-kicker">ROUTE PLAN</p><h2>Mission Flight Path</h2></div><span>{geofences.length} AIRSPACE ZONES</span></header>
        {mission.waypoints.length > 0 ? <MissionMap waypoints={mission.waypoints} geofences={geofences} readOnly /> : <div className="mission-route-empty"><span>⌁</span><h3>Route data unavailable</h3><p>No waypoint data was returned for this mission.</p></div>}
      </div>
      <aside className="mission-overview-panel">
        <header><p className="module-kicker">MISSION RECORD</p><h2>Plan Overview</h2></header>
        <dl>
          <div><dt>Status</dt><dd>{formatStatus(mission.status)}</dd></div>
          <div><dt>Start time</dt><dd>{formatDate(mission.startTime)}</dd></div>
          <div><dt>End time</dt><dd>{formatDate(mission.endTime)}</dd></div>
          <div><dt>Created</dt><dd>{mission.createdAt ? formatDate(mission.createdAt) : "Not provided"}</dd></div>
          <div><dt>Mission ID</dt><dd className="mission-id-value">{mission.id}</dd></div>
        </dl>
      </aside>
    </section>

    <section className="mission-waypoint-panel">
      <header><div><p className="module-kicker">ROUTE MANIFEST</p><h2>Waypoint Sequence</h2></div><span>{mission.waypoints.length} TOTAL</span></header>
      <div className="mission-waypoint-table-wrap">
        <table className="mission-waypoint-table">
          <thead><tr><th>SEQ</th><th>LATITUDE</th><th>LONGITUDE</th><th>ALTITUDE</th><th>ACTION</th></tr></thead>
          <tbody>{mission.waypoints.length > 0 ? mission.waypoints.map((point, index) => <tr key={`${point.sequenceOrder}-${index}`}><td><b>{String(point.sequenceOrder).padStart(2, "0")}</b></td><td>{point.latitude.toFixed(6)}</td><td>{point.longitude.toFixed(6)}</td><td>{point.altitude} m</td><td><span>{formatStatus(point.actionType)}</span></td></tr>) : <tr><td className="mission-waypoint-empty" colSpan={5}>Waypoint data is waiting for the mission detail API.</td></tr>}</tbody>
        </table>
      </div>
    </section>

    <section className="mission-review-grid">
      <article><span className="review-state pending">○</span><div><p className="module-kicker">VALIDATION</p><h2>Not submitted</h2><p>This draft has no route validation result yet.</p></div></article>
      <article><span className="review-state">—</span><div><p className="module-kicker">APPROVAL</p><h2>No decision recorded</h2><p>Approval evidence will appear after manager review.</p></div></article>
    </section>
  </>;
}
