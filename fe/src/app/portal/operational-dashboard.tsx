"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ApiError } from "@/lib/auth/client";
import type { Account } from "@/lib/auth/types";
import {
  getAssignedUavs,
  getFlightHistory,
  getGeofences,
  getIncidents,
  getLiveMissions,
  getManagedUavs,
  getOperationalAnalytics,
} from "@/lib/portal/client";
import type {
  AssignedUav,
  FlightHistoryItem,
  Geofence,
  IncidentItem,
  LiveMission,
  OperationalAnalytics,
} from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";
import DashboardMap from "./dashboard-map";
import DashboardRouteWeather from "./dashboard-route-weather";
import { ModuleError, ModuleLoading } from "./module-state";
import PortalShell from "./portal-shell";

const EMPTY_MISSIONS: FlightHistoryItem[] = [];
const isActive = (status: string) => status.toLowerCase().replaceAll(" ", "") === "inprogress";
const formatDate = (value: string | null) => value ? formatVietnamDateTime(value) : "Not scheduled";
const statusKey = (value: string) => value.toLowerCase().replaceAll(" ", "");

function formatTelemetryAge(seconds: number | null) {
  if (seconds == null) return "No sample";
  if (seconds < 5) return "Live now";
  if (seconds < 60) return `${Math.round(seconds)} sec ago`;
  return `${Math.floor(seconds / 60)} min ago`;
}

type FleetSummary = { total: number; active: number };
type IncidentSummary = { items: IncidentItem[]; total: number };

export default function OperationalDashboard({ account }: { account: Account }) {
  const manager = account.role === "operation_manager";
  const [analytics, setAnalytics] = useState<OperationalAnalytics | null>(null);
  const [missions, setMissions] = useState<FlightHistoryItem[] | null>(null);
  const [aircraft, setAircraft] = useState<AssignedUav[]>([]);
  const [fleet, setFleet] = useState<FleetSummary | null>(null);
  const [liveMissions, setLiveMissions] = useState<LiveMission[] | null>(null);
  const [incidents, setIncidents] = useState<IncidentSummary | null>(null);
  const [zones, setZones] = useState<Geofence[]>([]);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const [mapCursor, setMapCursor] = useState<{ latitude: number; longitude: number } | null>(null);

  const load = useCallback(async () => {
    setError(null);
    setWarnings([]);

    const fleetRequest: Promise<{ items: AssignedUav[]; summary: FleetSummary }> = manager
      ? Promise.all([
        getManagedUavs({ pageNumber: 1, pageSize: 6 }),
        getManagedUavs({ pageNumber: 1, pageSize: 1, status: "Active" }),
      ]).then(([all, active]) => ({
        items: all.items,
        summary: { total: all.totalItems, active: active.totalItems },
      }))
      : getAssignedUavs().then(items => ({
        items,
        summary: { total: items.length, active: items.filter(item => statusKey(item.status) === "active").length },
      }));

    const incidentRequest: Promise<IncidentSummary> = Promise.all([
      getIncidents({ pageNumber: 1, pageSize: 4, status: "Open" }),
      getIncidents({ pageNumber: 1, pageSize: 4, status: "Acknowledged" }),
    ]).then(([open, acknowledged]) => ({
      total: open.totalCount + acknowledged.totalCount,
      items: [...open.items, ...acknowledged.items]
        .sort((left, right) => Date.parse(right.createdAt) - Date.parse(left.createdAt))
        .slice(0, 4),
    }));

    const [summary, history, geofences, fleetResult, liveResult, incidentResult] = await Promise.allSettled([
      getOperationalAnalytics(),
      getFlightHistory({ pageSize: 100 }),
      getGeofences(),
      fleetRequest,
      getLiveMissions(),
      incidentRequest,
    ] as const);

    if (summary.status === "rejected" || history.status === "rejected") {
      const reason = summary.status === "rejected" ? summary.reason : history.status === "rejected" ? history.reason : null;
      const status = reason instanceof ApiError ? reason.status : 500;
      return setError(new ApiError("Dashboard data is unavailable. Try again shortly.", status));
    }

    setAnalytics(summary.value);
    setMissions(history.value.items);

    const nextWarnings: string[] = [];
    if (geofences.status === "fulfilled") setZones(geofences.value);
    else {
      setZones([]);
      nextWarnings.push("Geofence data is currently unavailable.");
    }

    if (fleetResult.status === "fulfilled") {
      setAircraft(fleetResult.value.items);
      setFleet(fleetResult.value.summary);
    } else {
      setAircraft([]);
      setFleet(null);
      nextWarnings.push(manager ? "Fleet readiness data is currently unavailable." : "Assigned UAV data is currently unavailable.");
    }

    if (liveResult.status === "fulfilled") setLiveMissions(liveResult.value);
    else {
      setLiveMissions(null);
      nextWarnings.push("Live mission and telemetry data is currently unavailable.");
    }

    if (incidentResult.status === "fulfilled") setIncidents(incidentResult.value);
    else {
      setIncidents(null);
      nextWarnings.push("Unresolved incident data is currently unavailable.");
    }
    setWarnings(nextWarnings);
  }, [manager]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const missionItems = missions ?? EMPTY_MISSIONS;
  const historyActiveMissions = useMemo(() => missionItems.filter(item => isActive(item.status)), [missionItems]);
  const weatherMissions = useMemo(() => {
    const activeIds = new Set(historyActiveMissions.map(item => item.missionId));
    return [...historyActiveMissions, ...missionItems.filter(item => !activeIds.has(item.missionId))].slice(0, 5);
  }, [historyActiveMissions, missionItems]);
  const filteredLive = useMemo(() => (liveMissions ?? []).filter(item =>
    `${item.missionName} ${item.missionId} ${item.uavCode ?? ""} ${item.pilotName ?? ""}`.toLowerCase().includes(query.trim().toLowerCase())), [liveMissions, query]);

  if (error) return <PortalShell account={account} dashboard><ModuleError error={error} retry={() => void load()} /></PortalShell>;
  if (!analytics || !missions) return <PortalShell account={account} dashboard><ModuleLoading label="Loading operational dashboard" /></PortalShell>;

  const pending = Math.max(analytics.totalFlights - analytics.completedFlights - analytics.abortedOrFailedFlights, 0);
  const unresolvedIncidents = incidents?.total ?? null;
  const hasIncidents = (unresolvedIncidents ?? 0) > 0;
  const onlineTelemetry = liveMissions?.filter(item => item.connectivityStatus === "Online").length ?? 0;
  const staleTelemetry = liveMissions?.filter(item => item.connectivityStatus === "Stale").length ?? 0;
  const offlineTelemetry = liveMissions?.filter(item => item.connectivityStatus === "Offline").length ?? 0;
  const telemetryPercent = liveMissions?.length ? Math.round((onlineTelemetry / liveMissions.length) * 100) : 0;
  const latestMission = missions[0];

  return <PortalShell account={account} dashboard>
    <section className={`critical-banner${hasIncidents ? "" : " clear"}`}>
      <span className="dash-icon">{hasIncidents ? "!" : "✓"}</span>
      <div>{hasIncidents ? "REQUIRES ACTION" : "AIRSPACE STATUS"}<br />LIVE INCIDENT REGISTRY</div>
      <p>{incidents === null
        ? "Incident status is temporarily unavailable. Other dashboard modules remain operational."
        : hasIncidents
          ? `${unresolvedIncidents} unresolved incident${unresolvedIncidents === 1 ? "" : "s"} require operational review.`
          : "No unresolved incidents require immediate attention."}</p>
      <div className="critical-banner-actions">
        <Link className="preset-button" href="/portal/flight-history">FLIGHT HISTORY</Link>
        {hasIncidents && <Link className="new-mission danger-button" href="/portal/incidents">OPEN INCIDENTS</Link>}
      </div>
    </section>

    {warnings.length > 0 && <section className="dashboard-service-warning" role="status"><b>PARTIAL BACKEND ACCESS</b>{warnings.map(message => <p key={message}>{message}</p>)}</section>}

    <section className="dashboard-heading">
      <div><h1>Mission &amp; Fleet Operations Hub</h1></div>
      <Link className="preset-button" href="/portal/geofences">⌁ SECTOR PRESETS</Link>
      <Link className="new-mission" href={manager ? "/portal/uav-management" : "/portal/missions/new"}>{manager ? "REVIEW UAV QUEUE" : "+ NEW MISSION DISPATCH"}</Link>
    </section>

    <section className="metric-grid" aria-label="Operational summary">
      <article><span>ACTIVE FLIGHTS</span><span className="dash-icon">⌁</span><strong>{liveMissions === null ? "—" : liveMissions.length}<small>LIVE MISSIONS</small></strong><p className={liveMissions?.length ? "ok" : ""}>{liveMissions === null ? "Live mission API unavailable" : liveMissions.length ? "● Tracking active operations" : "No active flight recorded"}</p></article>
      <article><span>MISSION OPERATIONS</span><span className="dash-icon">◇</span><strong>{analytics.totalFlights}<small>TOTAL</small></strong><div className="mini-stats"><b>{analytics.completedFlights}<small>DONE</small></b><b>{analytics.abortedOrFailedFlights}<small>FAILED</small></b><b>{pending}<small>PENDING</small></b></div></article>
      <article><span>AIRSPACE SAFETY</span><span className="dash-icon">△</span><strong className={hasIncidents ? "warn" : "ok"}>{unresolvedIncidents ?? "—"}<small>OPEN</small></strong><p>{zones.length} restriction records <em>{analytics.totalIncidentsRecorded} historical</em></p></article>
      <article><span>FLEET READINESS</span><span className="dash-icon">⌘</span><strong className="blue">{fleet?.active ?? "—"}<small>/ {fleet?.total ?? "—"} ACTIVE</small></strong><p>{fleet ? manager ? "Manager fleet registry" : "Assigned UAV registry" : "Fleet API unavailable"}</p></article>
      <article><span>TELEMETRY INGEST</span><span className="dash-icon">⌁</span><strong>{liveMissions === null ? "—" : onlineTelemetry}<small>{liveMissions === null ? "UNAVAILABLE" : `/ ${liveMissions.length} ONLINE`}</small></strong><p>{liveMissions === null ? "Live telemetry API unavailable" : `${staleTelemetry} stale · ${offlineTelemetry} offline`}</p><div className="progress"><i style={{ width: `${telemetryPercent}%` }} /></div></article>
    </section>

    <div className="operations-grid">
      <section className="map-panel">
        <header><b>◎ TAC-CANVAS // OPERATIONAL AIRSPACE</b><em>{mapCursor ? `${Math.abs(mapCursor.latitude).toFixed(5)}°${mapCursor.latitude >= 0 ? "N" : "S"}, ${Math.abs(mapCursor.longitude).toFixed(5)}°${mapCursor.longitude >= 0 ? "E" : "W"}` : "MOVE CURSOR OVER MAP"}</em></header>
        <DashboardMap geofences={zones} liveMissions={liveMissions ?? []} onCursorCoordinate={setMapCursor} />
        <footer><span>Layers: <b>Geofences + live mission telemetry</b></span><span><Link href="/portal/geofences">AIRSPACE REGISTRY</Link> · <Link href="/portal/live-monitoring">LIVE MONITORING →</Link></span></footer>
      </section>

      <aside className="insight-column">
        <section className="ai-panel">
          <header><span className="dash-icon" aria-hidden="true">◎</span><div><h2>AI Decision Engine</h2></div><b>AI</b></header>
          <article><div className="insight-mission-heading"><h3>{latestMission ? latestMission.missionName : "No mission context"}</h3><span className="insight-mission-status" data-status={latestMission ? statusKey(latestMission.status) : "idle"}>{latestMission?.status ?? "Idle"}</span></div><p>{latestMission ? `Latest mission record for ${latestMission.uavCode || "an unassigned UAV"}.` : "Create a mission before requesting an assessment."}</p><div className="insight-mission-actions"><span>{latestMission ? `${latestMission.totalIncidents} recorded incidents` : "No mission selected"}</span><Link className="module-link" href="/portal/ai-assessment">Open assessment</Link></div></article>
        </section>
        <section className="incident-panel">
          <header><div><h2>Unresolved Incidents</h2></div><Link href="/portal/incidents">View all</Link></header>
          {incidents?.items.length ? incidents.items.map(item => <article key={item.id}><div className="insight-event-meta"><b className="warn">{item.severity} · {item.status}</b><time>{formatDate(item.createdAt)}</time></div><h3><Link href={`/portal/incidents/${encodeURIComponent(item.id)}`}>{item.title}</Link></h3><p>{item.description || item.type}</p><div className="insight-event-footer"><span>{item.uavCode || "Unassigned UAV"}</span><span>{item.missionName}</span></div></article>) : <article><b className={incidents ? "ok" : "warn"}>{incidents ? "No unresolved incidents" : "Incident feed unavailable"}</b><h3>{incidents ? "Operations are clear" : "Unable to load the incident registry"}</h3><p>{incidents ? "The incident API returned no open or acknowledged records." : "Use Incident Management to retry the registry directly."}</p></article>}
        </section>
      </aside>
    </div>

    <section className="missions-panel">
      <header><div><span className="dash-icon">⌁</span><h2>Active Airborne Missions<span>Live missions and their latest telemetry connectivity</span></h2></div><label>⌕ <input value={query} onChange={event => setQuery(event.target.value)} placeholder="Filter active missions" /></label></header>
      <div className="table-wrap"><table><thead><tr><th>MISSION ID</th><th>UAV ASSET</th><th>ASSIGNED PILOT</th><th>FLIGHT PHASE</th><th>START</th><th>TELEMETRY</th><th>BATTERY</th><th>ACTION</th></tr></thead><tbody>
        {filteredLive.map(item => <tr key={item.missionId} className={item.connectivityStatus === "Online" ? "" : "danger-row"}><td><b className="blue">{item.missionName}</b><span>{item.missionId.slice(0, 8).toUpperCase()}</span></td><td><b>{item.uavCode || "—"}</b><span>{item.uavName || "Unassigned"}</span></td><td>{item.pilotName || "—"}</td><td><span className="phase">{item.missionStatus}</span><span>{item.connectivityStatus}</span></td><td>{formatDate(item.startTime)}</td><td>{formatTelemetryAge(item.dataAgeSeconds)}</td><td><b className={item.batteryPercent != null && item.batteryPercent <= 20 ? "warn" : "ok"}>{item.batteryPercent == null ? "—" : `${item.batteryPercent.toFixed(0)}%`}</b></td><td><Link href={`/portal/live-monitoring?missionId=${encodeURIComponent(item.missionId)}`}>MONITOR →</Link></td></tr>)}
        {filteredLive.length === 0 && <tr><td className="dashboard-table-empty" colSpan={8}>{liveMissions === null ? "Live mission data is currently unavailable." : query.trim() ? "No active mission matches this filter." : "No mission is currently airborne."}</td></tr>}
      </tbody></table></div>
    </section>

    <section className="bottom-grid">
      <DashboardRouteWeather key={weatherMissions.map(mission => mission.missionId).join(",")} missions={weatherMissions} />
      <article><header><b>{manager ? "GCS CREW ASSIGNMENTS" : "MY UAV ASSIGNMENT"}</b><span>{manager ? "LIVE OPERATIONS" : "FLEET REGISTRY"}</span></header>{manager
        ? liveMissions?.length
          ? <ul>{liveMissions.slice(0, 4).map(item => <li key={item.missionId}><i className={item.connectivityStatus === "Online" ? "" : "alert-dot"} /><span>{item.pilotName || "Unassigned pilot"} · {item.missionName}</span><b>{item.uavCode || "NO UAV"}</b></li>)}</ul>
          : <div className="dashboard-panel-empty"><span>◎</span><strong>{liveMissions === null ? "LIVE CREW DATA UNAVAILABLE" : "NO ACTIVE CREW ASSIGNMENT"}</strong><p>{liveMissions === null ? "The live mission service could not be reached." : "Crew assignments appear here when a mission becomes active."}</p></div>
        : <ul>{aircraft.length ? aircraft.slice(0, 3).map(item => <li key={item.id}><i /><span>{item.code} · {item.name}</span><b>{item.status}</b></li>) : <li><i className="alert-dot" /><span>No UAV assigned</span><b>—</b></li>}</ul>}<footer><span>{manager ? liveMissions === null ? "Live service unavailable" : `${liveMissions.length} active crew assignment${liveMissions.length === 1 ? "" : "s"}` : `${aircraft.length} assigned aircraft`}</span>{manager && <Link href="/portal/live-monitoring">OPEN LIVE MONITORING →</Link>}</footer></article>
    </section>
  </PortalShell>;
}
