"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import type { Account } from "@/lib/auth/types";
import { getAssignedUavs, getFlightHistory, getGeofences, getOperationalAnalytics } from "@/lib/portal/client";
import type { AssignedUav, FlightHistoryItem, Geofence, OperationalAnalytics } from "@/lib/portal/types";
import DashboardMap from "./dashboard-map";
import DashboardRouteWeather from "./dashboard-route-weather";
import { ModuleError, ModuleLoading } from "./module-state";
import PortalShell from "./portal-shell";

type Feed = "live" | "gcs" | "simulation";
const EMPTY_MISSIONS: FlightHistoryItem[] = [];

const isActive = (status: string) => status.toLowerCase().replaceAll(" ", "") === "inprogress";
const formatDate = (value: string | null) => value ? new Date(value).toLocaleString() : "Not scheduled";

export default function OperationalDashboard({ account }: { account: Account }) {
  const manager = account.role === "operation_manager";
  const [analytics, setAnalytics] = useState<OperationalAnalytics | null>(null);
  const [missions, setMissions] = useState<FlightHistoryItem[] | null>(null);
  const [aircraft, setAircraft] = useState<AssignedUav[]>([]);
  const [zones, setZones] = useState<Geofence[]>([]);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [feed, setFeed] = useState<Feed>("live");
  const [query, setQuery] = useState("");
  const [mapCursor, setMapCursor] = useState<{ latitude: number; longitude: number } | null>(null);

  const load = useCallback(async () => {
    setError(null);
    const results = await Promise.allSettled([
      getOperationalAnalytics(),
      getFlightHistory({ pageSize: 100 }),
      getGeofences(),
      manager ? Promise.resolve([] as AssignedUav[]) : getAssignedUavs(),
    ]);
    const [summary, history, geofences, uavs] = results;
    if (summary.status === "rejected") return setError(summary.reason);
    if (history.status === "rejected") return setError(history.reason);
    setAnalytics(summary.value);
    setMissions(history.value.items);
    setZones(geofences.status === "fulfilled" ? geofences.value : []);
    setAircraft(uavs.status === "fulfilled" ? uavs.value : []);
    const nextWarnings: string[] = [];
    if (geofences.status === "rejected") nextWarnings.push("Geofence API is currently unavailable for this session.");
    if (uavs.status === "rejected") nextWarnings.push("Assigned UAV API is currently unavailable for this session.");
    setWarnings(nextWarnings);
  }, [manager]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const missionItems = missions ?? EMPTY_MISSIONS;
  const activeMissions = useMemo(() => missionItems.filter(item => isActive(item.status)), [missionItems]);
  const weatherMissions = useMemo(() => {
    const activeIds = new Set(activeMissions.map(item => item.missionId));
    return [...activeMissions, ...missionItems.filter(item => !activeIds.has(item.missionId))].slice(0, 5);
  }, [activeMissions, missionItems]);
  const filteredActive = useMemo(() => activeMissions.filter(item =>
    `${item.missionName} ${item.missionId} ${item.uavCode} ${item.pilotName}`.toLowerCase().includes(query.toLowerCase())), [activeMissions, query]);
  const incidentMissions = useMemo(() => missionItems.filter(item => item.totalIncidents > 0).slice(0, 2), [missionItems]);

  if (error) return <PortalShell account={account} dashboard><ModuleError error={error} retry={() => void load()} /></PortalShell>;
  if (!analytics || !missions) return <PortalShell account={account} dashboard><ModuleLoading label="Loading operational dashboard" /></PortalShell>;

  const pending = Math.max(analytics.totalFlights - analytics.completedFlights - analytics.abortedOrFailedFlights, 0);
  const activeUavs = aircraft.filter(item => item.status.toLowerCase() === "active").length;
  const hasIncidents = analytics.totalIncidentsRecorded > 0;
  const latestMission = missions[0];

  return <PortalShell account={account} dashboard>
    <section className={`critical-banner${hasIncidents ? "" : " clear"}`}>
      <span className="dash-icon">{hasIncidents ? "!" : "✓"}</span>
      <div>{hasIncidents ? "CRITICAL ADVISORY" : "AIRSPACE STATUS"}<br />BACKEND RECORDS</div>
      <p>{hasIncidents ? `${analytics.totalIncidentsRecorded} incident${analytics.totalIncidentsRecorded === 1 ? "" : "s"} recorded across mission history.` : "No recorded incidents require immediate attention."}</p>
      <Link className="preset-button" href="/portal/flight-history">VIEW RECORDS</Link>
      {hasIncidents && <Link className="new-mission danger-button" href="/portal/flight-history">OPEN INCIDENTS</Link>}
    </section>

    {warnings.length > 0 && <section className="dashboard-service-warning" role="status"><b>PARTIAL BACKEND ACCESS</b>{warnings.map(message => <p key={message}>{message}</p>)}</section>}

    <section className="dashboard-heading">
      <div><p>◎ SECTOR SGN-04 TECH PARK OPERATIONAL THEATER</p><h1>Mission &amp; Fleet Operations Hub</h1></div>
      <div className="feed-tabs" aria-label="Dashboard data mode">
        <button className={feed === "live" ? "active" : ""} onClick={() => setFeed("live")}>LIVE FEED</button>
        <button className={feed === "gcs" ? "active" : ""} onClick={() => setFeed("gcs")}>GCS SYNC</button>
        <button className={feed === "simulation" ? "active" : ""} onClick={() => setFeed("simulation")}>SIMULATION</button>
      </div>
      <Link className="preset-button" href="/portal/geofences">⌁ SECTOR PRESETS</Link>
      <Link className="new-mission" href={manager ? "/portal/uav-management" : "/portal/missions/new"}>{manager ? "REVIEW UAV QUEUE" : "+ NEW MISSION DISPATCH"}</Link>
    </section>

    <section className="metric-grid" aria-label="Operational summary">
      <article><span>ACTIVE FLIGHTS</span><span className="dash-icon">⌁</span><strong>{activeMissions.length}<small>/ {missions.length} IN VIEW</small></strong><p className={activeMissions.length ? "ok" : ""}>{activeMissions.length ? "● Mission status: InProgress" : "No active flight recorded"}</p></article>
      <article><span>MISSION OPERATIONS</span><span className="dash-icon">◇</span><strong>{analytics.totalFlights}<small>TOTAL</small></strong><div className="mini-stats"><b>{analytics.completedFlights}<small>DONE</small></b><b>{analytics.abortedOrFailedFlights}<small>FAILED</small></b><b>{pending}<small>PENDING</small></b></div></article>
      <article><span>AIRSPACE SAFETY</span><span className="dash-icon">△</span><strong className={hasIncidents ? "warn" : "ok"}>{analytics.totalIncidentsRecorded}<small>INCIDENTS</small></strong><p>{zones.length} restriction records <em>{analytics.successRatePercentage == null ? "No success rate" : `${analytics.successRatePercentage.toFixed(1)}% success`}</em></p></article>
      <article><span>FLEET READINESS</span><span className="dash-icon">⌘</span><strong className="blue">{manager ? "—" : activeUavs}<small>{manager ? "API PENDING" : `/ ${aircraft.length} ACTIVE`}</small></strong><p>{manager ? "Fleet-wide UAV endpoint unavailable" : "Assigned UAV API"}</p></article>
      <article><span>TELEMETRY INGEST</span><span className="dash-icon">⌁</span><strong>—<small>NO LIVE FEED</small></strong><p>Live telemetry API unavailable</p><div className="progress"><i style={{ width: "0%" }} /></div></article>
    </section>

    <div className="operations-grid">
      <section className="map-panel">
        <header><b>◎ TAC-CANVAS // OPERATIONAL AIRSPACE</b><em>{mapCursor ? `${Math.abs(mapCursor.latitude).toFixed(5)}°${mapCursor.latitude >= 0 ? "N" : "S"}, ${Math.abs(mapCursor.longitude).toFixed(5)}°${mapCursor.longitude >= 0 ? "E" : "W"}` : "MOVE CURSOR OVER MAP"}</em><span>BACKEND GEOFENCES</span></header>
        <DashboardMap geofences={zones} onCursorCoordinate={setMapCursor} />
        <footer><span>Airspace layer: <b>DroneOps Geofences API</b> · {zones.length} zones</span><span><Link href="/portal/geofences">OPEN AIRSPACE REGISTRY</Link> · <Link href="/portal/flight-history">FLIGHT HISTORY →</Link></span></footer>
      </section>

      <aside className="insight-column">
        <section className="ai-panel">
          <header><span className="dash-icon">◎</span><div><h2>AI Decision Engine</h2><span>GEMINI ASSESSMENT SERVICE</span></div><b>AI</b></header>
          <article><h3>{latestMission ? latestMission.missionName : "NO MISSION CONTEXT"}<small>{latestMission?.status ?? "IDLE"}</small></h3><p>{latestMission ? `Latest backend mission record for ${latestMission.uavCode || "an unassigned UAV"}.` : "Create a mission before requesting an assessment."}</p><div><span>{latestMission ? `${latestMission.totalIncidents} recorded incidents` : "No mission selected"}</span><Link className="module-link" href="/portal/ai-assessment">OPEN ASSESSMENT →</Link></div></article>
        </section>
        <section className="incident-panel">
          <header><div><h2>Critical Incidents &amp; Approvals</h2><span>RECORDED MISSION EVENTS</span></div><Link href="/portal/flight-history">VIEW ALL</Link></header>
          {incidentMissions.length ? incidentMissions.map(item => <article key={item.missionId}><time>{formatDate(item.startTime)}</time><b className="warn">{item.totalIncidents} INCIDENT{item.totalIncidents === 1 ? "" : "S"}</b><h3>{item.missionName}</h3><p>{item.uavCode || "Unassigned UAV"} · {item.status}</p></article>) : <article><b className="ok">NO CRITICAL RECORDS</b><h3>Operational history is clear</h3><p>The Flight History API returned no mission incidents.</p></article>}
        </section>
      </aside>
    </div>

    <section className="missions-panel">
      <header><div><span className="dash-icon">⌁</span><h2>Active Airborne Missions<span>Mission records currently marked InProgress by Flight History API</span></h2></div><label>⌕ <input value={query} onChange={event => setQuery(event.target.value)} placeholder="Filter active missions" /></label></header>
      <div className="table-wrap"><table><thead><tr><th>MISSION ID</th><th>UAV ASSET</th><th>ASSIGNED PILOT</th><th>FLIGHT PHASE</th><th>START</th><th>DURATION</th><th>INCIDENTS</th><th>ACTION</th></tr></thead><tbody>
        {filteredActive.map(item => <tr key={item.missionId} className={item.totalIncidents ? "danger-row" : ""}><td><b className="blue">{item.missionName}</b><span>{item.missionId.slice(0, 8).toUpperCase()}</span></td><td><b>{item.uavCode || "—"}</b><span>{item.uavName || "Unassigned"}</span></td><td>{item.pilotName || "—"}</td><td><span className="phase">{item.status}</span></td><td>{formatDate(item.startTime)}</td><td>{item.durationMinutes.toFixed(0)} min</td><td><b className={item.totalIncidents ? "warn" : "ok"}>{item.totalIncidents}</b></td><td><Link href="/portal/flight-history">OPEN RECORD →</Link></td></tr>)}
        {filteredActive.length === 0 && <tr><td className="dashboard-table-empty" colSpan={8}>No mission currently has InProgress status.</td></tr>}
      </tbody></table></div>
    </section>

    <section className="bottom-grid">
      <DashboardRouteWeather missions={weatherMissions} />
      <article><header><b>{manager ? "GCS CREW ASSIGNMENTS" : "MY UAV ASSIGNMENT"}</b>{manager && <span>API PENDING</span>}</header>{manager ? <div className="dashboard-panel-empty"><span>◎</span><strong>CREW ROSTER UNAVAILABLE</strong><p>The backend does not expose live crew assignments.</p></div> : <ul>{aircraft.length ? aircraft.slice(0, 3).map(item => <li key={item.id}><i /><span>{item.code} · {item.name}</span><b>{item.status}</b></li>) : <li><i className="alert-dot" /><span>No UAV assigned</span><b>—</b></li>}</ul>}<footer><span>{manager ? "Awaiting crew API" : `${aircraft.length} assigned aircraft`}</span></footer></article>
    </section>
  </PortalShell>;
}
