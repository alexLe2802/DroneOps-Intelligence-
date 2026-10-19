"use client";

import { useCallback, useEffect, useState } from "react";
import { getFlightHistory, getOperationalAnalytics } from "@/lib/portal/client";
import type { FlightHistoryItem, OperationalAnalytics } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

export default function FlightHistoryClient() {
  const [items, setItems] = useState<FlightHistoryItem[] | null>(null);
  const [analytics, setAnalytics] = useState<OperationalAnalytics | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const load = useCallback(async () => {
    setError(null);
    try {
      const [history, summary] = await Promise.all([
        getFlightHistory({ pageSize: 100, search: search.trim() || undefined, status: status || undefined }),
        getOperationalAnalytics(),
      ]);
      setItems(history.items);
      setAnalytics(summary);
    } catch (reason) { setError(reason); }
  }, [search, status]);
  useEffect(() => {
    const timer = window.setTimeout(() => void load(), 0);
    return () => window.clearTimeout(timer);
  }, [load]);
  if (error) return <ModuleError error={error} retry={() => void load()} />;
  if (!items || !analytics) return <ModuleLoading label="Loading flight archive" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">OPERATIONS / POST-FLIGHT</p><h1>Flight History & Reports</h1><p>Completed and scheduled missions retrieved from the operational database.</p></div></header>
    <section className="history-stats">
      <article><span>TOTAL FLIGHTS</span><strong>{analytics.totalFlights}</strong></article>
      <article><span>COMPLETED</span><strong>{analytics.completedFlights}</strong></article>
      <article><span>SUCCESS RATE</span><strong>{analytics.successRatePercentage.toFixed(1)}%</strong></article>
      <article><span>FLIGHT HOURS</span><strong>{analytics.totalFlightHours.toFixed(1)}</strong></article>
      <article><span>INCIDENTS</span><strong>{analytics.totalIncidentsRecorded}</strong></article>
    </section>
    <section className="module-toolbar history-toolbar">
      <label>SEARCH MISSIONS<input value={search} onChange={event => setSearch(event.target.value)} placeholder="Mission name" /></label>
      <label>STATUS<select value={status} onChange={event => setStatus(event.target.value)}><option value="">All statuses</option><option>Draft</option><option>InProgress</option><option>Completed</option><option>Aborted</option><option>Failed</option></select></label>
      <span>{items.length} RECORDS</span>
    </section>
    {items.length === 0 ? <ModuleEmpty title="No flights found" message="No mission matches the current filters." /> : <section className="history-table-wrap"><table className="history-table"><thead><tr><th>Mission</th><th>Aircraft</th><th>Pilot</th><th>Window</th><th>Duration</th><th>Incidents</th><th>Report</th><th>Status</th></tr></thead><tbody>{items.map(item => <tr key={item.missionId}><td><strong>{item.missionName}</strong><small>{item.missionId.slice(0, 8).toUpperCase()}</small></td><td>{item.uavCode || "—"}<small>{item.uavName}</small></td><td>{item.pilotName || "—"}</td><td>{item.startTime ? new Date(item.startTime).toLocaleString() : "Not scheduled"}<small>{item.endTime ? `to ${new Date(item.endTime).toLocaleString()}` : ""}</small></td><td>{item.durationMinutes.toFixed(0)} min</td><td className={item.totalIncidents ? "history-alert" : ""}>{item.totalIncidents}</td><td>{item.hasReport ? "AVAILABLE" : "PENDING"}</td><td><span className={`status status-${item.status.toLowerCase()}`}>{item.status}</span></td></tr>)}</tbody></table></section>}
  </>;
}
