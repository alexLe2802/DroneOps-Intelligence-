"use client";

import { apiInstant } from "@/lib/portal/vietnam-time";

import Link from "next/link";
import AnalyticsDetails from "../analytics-details";
import { archiveHref, archiveQuery } from "@/lib/portal/analytics-scope";
import { ArrowRight } from "lucide-react";
import { useEffect, useState } from "react";
import { getFlightHistory, getOperationalAnalytics, getAssignedUavs, getManagedUavs } from "@/lib/portal/client";
import type { FlightHistoryItem, OperationalAnalytics, PagedResponse } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import styles from "./flight-history.module.css";

const PAGE_SIZE = 8;
const terminalStatuses = ["", "Completed", "Aborted", "Failed", "Cancelled"];
const dateTime = (value: string | null) => {
  if (!value) return "Not recorded";
  const parsed = apiInstant(value);
  return Number.isNaN(parsed.getTime())
    ? "Not recorded"
    : new Intl.DateTimeFormat("en-GB", { timeZone: "Asia/Ho_Chi_Minh", day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(parsed);
};
const reportLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2");
const duration = (minutes: number) => {
  const safeMinutes = Number.isFinite(minutes) ? Math.max(0, minutes) : 0;
  return safeMinutes >= 60
    ? `${Math.floor(safeMinutes / 60)}h ${Math.round(safeMinutes % 60)}m`
    : `${Math.round(safeMinutes)}m`;
};

export default function FlightHistoryClient({ initialQuery = "", role = "uav_operator", sourceView = false }: { initialQuery?: string; role?: string; sourceView?: boolean }) {
  const params = new URLSearchParams(initialQuery);
  const rawPage = params.get("page") || "1";
  const page = /^[1-9][0-9]{0,5}$/.test(rawPage) ? Number(rawPage) : 1;
  const scope = archiveQuery(params);
  const [result, setResult] = useState<PagedResponse<FlightHistoryItem> | null>(null);
  const [analytics, setAnalytics] = useState<OperationalAnalytics | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [aircraft, setAircraft] = useState<Array<{ id: string; code: string; name: string }>>([]);
  const [aircraftError, setAircraftError] = useState("");
  const [refresh, setRefresh] = useState(0);
  useEffect(() => {
    let active = true;
    const query = new URLSearchParams(scope);
    const filters = { pageNumber: page, pageSize: PAGE_SIZE, search: query.get("search") || undefined, status: query.get("status") || undefined, uavId: query.get("uavId") || undefined, missionId: query.get("missionId") || undefined, fromDate: query.get("fromDate") || undefined, toDate: query.get("toDate") || undefined };
    void Promise.all([getFlightHistory(filters), getOperationalAnalytics(filters)]).then(([history, summary]) => {
      if (active) { setResult(history); setAnalytics(summary); setError(null); }
    }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, [page, scope, refresh]);
  useEffect(() => {
    let active = true;
    async function readAircraft() {
      if (role !== "operation_manager") return getAssignedUavs();
      const first = await getManagedUavs({ pageSize: 100 });
      const all = [...first.items];
      for(let pageNumber=2;pageNumber<=first.totalPages;pageNumber++) {
        if (!active) break;
        all.push(...(await getManagedUavs({ pageSize:100,pageNumber })).items);
      }
      return all;
    }
    void readAircraft().then(items => { if(active) setAircraft(items); }).catch(() => { if(active) setAircraftError("Aircraft options unavailable. You can still enter an exact UAV ID."); });
    return () => { active = false; };
  }, [role]);
  const base = sourceView ? "/portal/flight-history/sources" : "/portal/flight-history";
  function pageHref(value: number) { const p = new URLSearchParams(scope); p.set("page",String(value)); return archiveHref(p,base); }
  if (error) return <ModuleError error={error} retry={() => setRefresh(v=>v+1)} />;
  if (!result || !analytics) return <ModuleLoading label="Loading flight archive" />;
  return <div className={styles.archive}>
    <header className={styles.heading}><div><p>FLIGHTS / RECORDED EVIDENCE</p><h1>{sourceView ? "Analytics source flights" : "Flight History & Reports"}</h1><span>Inspect recorded flights, report coverage and their source evidence.</span></div><button type="button" onClick={()=>setRefresh(v=>v+1)}>Refresh records</button></header>
    <form className={styles.filters} method="get">
      <header><b>Filter start-time window (UTC)</b><Link href={base}>Reset filters</Link></header>
      <div>
        <label htmlFor="archive-search">Search mission<input id="archive-search" name="search" defaultValue={params.get("search") || ""} placeholder="Mission name" /></label>
        <label htmlFor="archive-from">From date (UTC)<input id="archive-from" name="fromDate" type="date" defaultValue={params.get("fromDate") || ""} /></label>
        <label htmlFor="archive-to">To date (UTC)<input id="archive-to" name="toDate" type="date" defaultValue={params.get("toDate") || ""} /></label>
        <div className={styles.filterField}><label htmlFor="archive-uav">UAV</label>{aircraftError ? <input id="archive-uav" name="uavId" defaultValue={params.get("uavId") || ""} /> : <select id="archive-uav" name="uavId" defaultValue={params.get("uavId") || ""}><option value="">All authorized UAVs</option>{params.get("uavId") && !aircraft.some(a=>a.id===params.get("uavId")) && <option value={params.get("uavId")!}>Selected UAV: {params.get("uavId")}</option>}{aircraft.map(a=><option key={a.id} value={a.id}>{a.code} · {a.name}</option>)}</select>}</div>
        <label htmlFor="archive-mission">Exact mission ID<input id="archive-mission" name="missionId" defaultValue={params.get("missionId") || ""} placeholder="Optional mission UUID" /></label>
        <label htmlFor="archive-status">History status<select id="archive-status" name="status" defaultValue={params.get("status") || ""}>{terminalStatuses.map(value=><option key={value} value={value}>{value || "All statuses"}</option>)}</select></label>
      </div><footer><button type="submit" className="module-primary">Apply filters</button><span>{result.totalItems} matching records. Search/status filter the registry; analytics includes all started flights in the selected date/UAV/mission scope.</span></footer>
      {aircraftError && <p role="status">{aircraftError}</p>}
    </form>
    {!sourceView && <AnalyticsDetails data={analytics} scope={scope} />}
    {sourceView && <p><Link href={archiveHref(params)}>Back to scoped analytics</Link> · Select a flight to inspect its immutable route, telemetry and incidents. The registry may include unstarted mission records; only started flights enter the analytics denominator.</p>}
    {result.items.length === 0 ? <ModuleEmpty title="No records found" message="No mission matches the current filters." /> : <section className={styles.records} aria-label="Flight records">
      <header><h2>Flight registry</h2><small>{result.totalItems} matching records</small></header>
      <div>{result.items.map(item=><FlightRecord key={item.missionId} item={item} scope={scope} sourceView={sourceView} />)}</div>
      <footer><span>Page {result.pageNumber} of {result.totalPages}</span><nav aria-label="Flight history pages">{page>1 && <Link href={pageHref(page-1)}>Previous page</Link>}{page<result.totalPages && <Link href={pageHref(page+1)}>Next page</Link>}</nav></footer>
    </section>}
  </div>;
}

function FlightRecord({ item, scope, sourceView }: { item: FlightHistoryItem; scope: string; sourceView: boolean }) {
  const missionStatus = item.status?.trim() || "Completed";
  const reportStatus = item.reportStatus?.trim() || (item.hasReport ? "Available" : "NotQueued");
  const reportAvailable = reportStatus !== "NotQueued";
  const statusKey = missionStatus.toLowerCase().replaceAll(" ", "");
  const isFailure = ["aborted", "failed", "cancelled"].includes(statusKey);
  const shortMissionId = item.missionId.slice(0, 8).toUpperCase();
  return <article className={`${styles.record} ${isFailure ? styles.recordDanger : ""}`}>
    <header><div><b>FLT-{shortMissionId}</b><code>MS-{item.missionId.slice(0, 4).toUpperCase()}</code><span className={`${styles.outcome} ${isFailure ? styles.danger : ""}`}>{missionStatus}</span></div><time>{dateTime(item.startTime)} GMT+7</time></header>
    <div className={styles.recordBody}>
      <div className={styles.recordIdentity}><h3>{item.missionName}</h3><dl><div><dt>AIRFRAME</dt><dd>{item.uavCode || "N/A"}<small>{item.uavName || "Aircraft unavailable"}</small></dd></div><div><dt>PIC / OPERATOR</dt><dd>{item.pilotName || "N/A"}<small>ASSIGNED OPERATOR</small></dd></div><div><dt>DURATION</dt><dd>{duration(item.durationMinutes)}<small>{item.hasReport ? "REPORT DURATION" : "SCHEDULED DURATION"}</small></dd></div><div><dt>INCIDENTS</dt><dd className={item.totalIncidents ? styles.alertText : ""}>{item.totalIncidents}<small>{item.totalIncidents ? "REVIEW REQUIRED" : "NO LINKED INCIDENTS"}</small></dd></div><div><dt>REPORT REV.</dt><dd>{item.reportRevision ?? "--"}<small>{reportLabel(reportStatus)}</small></dd></div></dl></div>
      <div className={styles.reportState}><span className={`${styles.reportBadge} ${styles[reportStatus.toLowerCase()] ?? ""}`}>{reportLabel(reportStatus)}</span><small>{item.reportRevision ? `REVISION ${item.reportRevision}` : "NO REVISION"}{" // "}{item.telemetryCoverageComplete ? "TELEMETRY COVERED" : "COVERAGE INCOMPLETE"}</small>{reportAvailable ? <Link href={archiveHref(new URLSearchParams(scope), `/portal/flight-history/${encodeURIComponent(item.missionId)}`)}>VIEW REPORT <ArrowRight size={14} strokeWidth={2} aria-hidden="true" /></Link> : <Link href={archiveHref(new URLSearchParams(scope), `/portal/missions/${encodeURIComponent(item.missionId)}`)}>VIEW MISSION</Link>}</div>
    </div>
    <footer className={styles.recordTelemetry}>{sourceView && <Link href={archiveHref(new URLSearchParams(scope), item.hasReport ? `/portal/flight-history/${encodeURIComponent(item.missionId)}` : `/portal/missions/${encodeURIComponent(item.missionId)}`)}>{item.hasReport ? "Inspect source evidence" : "Inspect mission record (report unavailable)"}</Link>}<span><i className={item.telemetryCoverageComplete ? styles.nominalDot : styles.mutedDot} /> TELEMETRY: {item.telemetryCoverageComplete ? "COMPLETE" : "INCOMPLETE"}</span><span>END: {dateTime(item.endTime)}</span><span>INCIDENT LEDGER: {item.totalIncidents ? `${item.totalIncidents} LINKED` : "CLEAR"}</span><b>{reportAvailable ? "REPORT STATE RECORDED" : "NO QUEUED REPORT"}</b></footer>
  </article>;
}
