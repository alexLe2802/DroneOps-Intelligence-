"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import PortalShell from "./portal-shell";
import type { Account } from "@/lib/auth/types";
import { getAssignedUavs, getFlightHistory, getGeofences, getOperationalAnalytics } from "@/lib/portal/client";
import type { AssignedUav, FlightHistoryItem, Geofence, OperationalAnalytics } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "./module-state";
import DashboardMap from "./dashboard-map";

export default function OperationalDashboard({ account }: { account: Account }) {
  const [analytics, setAnalytics] = useState<OperationalAnalytics | null>(null);
  const [missions, setMissions] = useState<FlightHistoryItem[] | null>(null);
  const [aircraft, setAircraft] = useState<AssignedUav[]>([]);
  const [zones, setZones] = useState<Geofence[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [serviceWarnings, setServiceWarnings] = useState<string[]>([]);
  const [query, setQuery] = useState("");
  const manager = account.role === "operations_manager";
  const load = useCallback(async () => {
    setError(null);
    try {
      const [summary, history, geofences, uavs] = await Promise.allSettled([
        getOperationalAnalytics(), getFlightHistory({ pageSize: 25 }), getGeofences(),
        manager ? Promise.resolve([]) : getAssignedUavs(),
      ]);
      if (summary.status === "rejected") throw summary.reason;
      if (history.status === "rejected") throw history.reason;
      setAnalytics(summary.value); setMissions(history.value.items);
      setZones(geofences.status === "fulfilled" ? geofences.value : []);
      setAircraft(uavs.status === "fulfilled" ? uavs.value : []);
      const warnings: string[] = [];
      if (geofences.status === "rejected") warnings.push("Geofences are unavailable because the backend role policy does not allow this web-session role.");
      if (uavs.status === "rejected") warnings.push("Assigned UAVs are unavailable because the backend role policy does not allow this web-session role.");
      setServiceWarnings(warnings);
    } catch (reason) { setError(reason); }
  }, [manager]);
  useEffect(() => { void load(); }, [load]);
  const filtered = useMemo(() => missions?.filter(item => `${item.missionName} ${item.uavCode} ${item.pilotName} ${item.status}`.toLowerCase().includes(query.toLowerCase())) ?? [], [missions, query]);
  if (error) return <PortalShell account={account} dashboard><ModuleError error={error} retry={() => void load()} /></PortalShell>;
  if (!analytics || !missions) return <PortalShell account={account} dashboard><ModuleLoading label="Loading operational dashboard" /></PortalShell>;
  const activeFlights = missions.filter(item => item.status.toLowerCase() === "inprogress").length;
  const activeUavs = aircraft.filter(item => item.status.toLowerCase() === "active").length;
  return <PortalShell account={account} dashboard>
    {serviceWarnings.length > 0 && <section className="dashboard-service-warning" role="status"><b>PARTIAL BACKEND ACCESS</b>{serviceWarnings.map(message => <p key={message}>{message}</p>)}</section>}
    <section className="dashboard-heading"><div><p>◎ {manager ? "MANAGED OPERATIONAL SCOPE" : "OWN / ASSIGNED SCOPE"}</p><h1>{manager ? <>Operations Manager<br />Command Center</> : <>My Mission Operations<br />Workspace</>}</h1><span className="scope-note">Live values below are retrieved from the DroneOps backend.</span></div><Link className="preset-button" href="/portal/geofences">⌁ AIRSPACE REGISTRY</Link>{!manager && <Link className="new-mission" href="/portal/missions/new">⊕ NEW MISSION</Link>}</section>
    <section className="metric-grid" aria-label="Operational summary">
      <article><span>ACTIVE FLIGHTS</span><strong>{activeFlights}<small>IN PROGRESS</small></strong><p className={activeFlights ? "ok" : ""}>{activeFlights ? "● Mission activity detected" : "No active flight recorded"}</p></article>
      <article><span>TOTAL FLIGHTS</span><strong>{analytics.totalFlights}<small>RECORDED</small></strong><div className="mini-stats"><b>{analytics.completedFlights}<small>DONE</small></b><b>{analytics.abortedOrFailedFlights}<small>FAILED</small></b></div></article>
      <article><span>SUCCESS RATE</span><strong className="blue">{analytics.successRatePercentage.toFixed(1)}%<small>RECORDED</small></strong><p>{analytics.totalFlightHours.toFixed(1)} flight hours</p></article>
      <article><span>{manager ? "CONTROLLED AIRSPACE" : "ASSIGNED UAV"}</span><strong className="ok">{serviceWarnings.length ? "—" : manager ? zones.length : activeUavs}<small>{manager ? "GEOFENCES" : `${aircraft.length} TOTAL`}</small></strong><p>{serviceWarnings.length ? "Role access pending" : manager ? "Configured restriction records" : "Approved aircraft available"}</p></article>
      <article><span>INCIDENTS</span><strong className={analytics.totalIncidentsRecorded ? "warn" : "ok"}>{analytics.totalIncidentsRecorded}<small>RECORDED</small></strong><p>{analytics.totalDistanceKilometers.toFixed(1)} km total distance</p></article>
    </section>
    <div className="operations-grid dashboard-data-grid">
      <section className="map-panel"><header><b>OPERATIONAL MAP // HO CHI MINH CITY</b><em>BASEMAP ACTIVE · LIVE LAYER PENDING</em></header><DashboardMap /><footer><span>Map tiles: OpenFreeMap · UAV positions require a fleet-wide telemetry endpoint</span><Link href="/portal/flight-history">OPEN RECORDED FLIGHT HISTORY →</Link></footer></section>
      <aside className="insight-column"><section className="ai-panel"><header><div><h2>Operational data coverage</h2><span>BACKEND SYNCHRONIZED</span></div></header><article><h3>MISSION HISTORY</h3><p>{missions.length} recent records loaded for this account scope.</p></article><article><h3>AIRSPACE RESTRICTIONS</h3><p>{serviceWarnings.length ? "Waiting for backend role-policy alignment." : `${zones.length} configured geofences are available for route review.`}</p><Link className="module-secondary module-link" href="/portal/geofences">VIEW GEOFENCES</Link></article></section></aside>
    </div>
    <section className="missions-panel"><header><div><span className="dash-icon">⌁</span><h2>Mission Registry<span>Authorized records from Flight History API</span></h2></div><label>⌕ <input value={query} onChange={event => setQuery(event.target.value)} placeholder="Filter by mission, UAV, pilot or status" /></label></header><div className="table-wrap"><table><thead><tr><th>MISSION</th><th>UAV ASSET</th><th>PILOT</th><th>START</th><th>DURATION</th><th>INCIDENTS</th><th>STATUS</th><th>REPORT</th></tr></thead><tbody>{filtered.map(item => <tr key={item.missionId}><td><b className="blue">{item.missionName}</b><span>{item.missionId.slice(0, 8).toUpperCase()}</span></td><td><b>{item.uavCode || "—"}</b><span>{item.uavName}</span></td><td>{item.pilotName || "—"}</td><td>{item.startTime ? new Date(item.startTime).toLocaleString() : "Not scheduled"}</td><td>{item.durationMinutes.toFixed(0)} min</td><td><b className={item.totalIncidents ? "warn" : "ok"}>{item.totalIncidents}</b></td><td><span className={`status status-${item.status.toLowerCase()}`}>{item.status}</span></td><td>{item.hasReport ? "AVAILABLE" : "PENDING"}</td></tr>)}</tbody></table>{filtered.length === 0 && <p className="dashboard-table-empty">No mission matches this view.</p>}</div></section>
  </PortalShell>;
}
