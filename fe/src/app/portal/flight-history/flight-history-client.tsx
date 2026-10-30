"use client";

import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { getFlightHistory, getOperationalAnalytics } from "@/lib/portal/client";
import type { FlightHistoryItem, OperationalAnalytics, PagedResponse } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import styles from "./flight-history.module.css";

const PAGE_SIZE = 8;
const terminalStatuses = ["", "Completed", "Aborted", "Failed", "Cancelled"];
const dateTime = (value: string | null) => {
  if (!value) return "Not recorded";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime())
    ? "Not recorded"
    : new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(parsed);
};
const reportLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2");
const percent = (value: number | null) => value == null ? "N/A" : `${value.toFixed(1)}%`;
const duration = (minutes: number) => {
  const safeMinutes = Number.isFinite(minutes) ? Math.max(0, minutes) : 0;
  return safeMinutes >= 60
    ? `${Math.floor(safeMinutes / 60)}h ${Math.round(safeMinutes % 60)}m`
    : `${Math.round(safeMinutes)}m`;
};

export default function FlightHistoryClient() {
  const [result, setResult] = useState<PagedResponse<FlightHistoryItem> | null>(null);
  const [analytics, setAnalytics] = useState<OperationalAnalytics | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [page, setPage] = useState(1);
  const [refreshing, setRefreshing] = useState(false);

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(searchInput.trim()); setPage(1); }, 300);
    return () => window.clearTimeout(timer);
  }, [searchInput]);

  const load = useCallback(async (showLoading = false) => {
    if (showLoading) setResult(null);
    else setRefreshing(true);
    setError(null);
    try {
      const filters = { pageNumber: page, pageSize: PAGE_SIZE, search: search || undefined, status: status || undefined, fromDate: fromDate || undefined, toDate: toDate || undefined };
      const [history, summary] = await Promise.all([
        getFlightHistory(filters),
        getOperationalAnalytics({ fromDate: filters.fromDate, toDate: filters.toDate }),
      ]);
      setResult(history);
      setAnalytics(summary);
    } catch (reason) { setError(reason); }
    finally { setRefreshing(false); }
  }, [fromDate, page, search, status, toDate]);

  useEffect(() => {
    const timer = window.setTimeout(() => void load(true), 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const pages = useMemo(() => {
    const total = result?.totalPages ?? 0;
    const start = Math.max(1, Math.min(page - 1, Math.max(1, total - 3)));
    return Array.from({ length: Math.min(4, total) }, (_, index) => start + index);
  }, [page, result?.totalPages]);

  function resetFilters() {
    setSearchInput(""); setSearch(""); setStatus(""); setFromDate(""); setToDate(""); setPage(1);
  }

  if (error) return <ModuleError error={error} retry={() => void load(true)} />;
  if (!result || !analytics) return <ModuleLoading label="Loading flight archive" />;

  const rangeStart = result.totalItems ? (result.pageNumber - 1) * result.pageSize + 1 : 0;
  const rangeEnd = Math.min(result.pageNumber * result.pageSize, result.totalItems);
  const averageDurationMinutes = analytics.reportsAvailable
    ? analytics.totalFlightHours * 60 / analytics.reportsAvailable
    : 0;
  return <div className={styles.archive}>
    <header className={styles.heading}>
      <div><p><i /> ARCHIVAL CERTIFICATION</p><h1>Flight History &amp; Reports</h1><span>Audit-ready repository of finalized missions, telemetry coverage, incidents, and post-flight reports.</span></div>
      <div className={styles.headingActions}><span><i /> BACKEND ARCHIVE ONLINE</span><button type="button" onClick={() => void load(false)} disabled={refreshing}>{refreshing ? "SYNCING ARCHIVE" : "REFRESH ARCHIVE"}</button></div>
    </header>

    <section className={styles.metrics} aria-label="Post-flight analytics">
      <article><span>TOTAL LOGGED FLIGHTS</span><b className={`${styles.metricIcon} ${styles.takeoffIcon}`} aria-hidden="true"><span>✈</span></b><strong>{analytics.totalFlights}<em> SORTIES</em></strong><small>{analytics.completedFlights} completed records</small><div className={styles.metricMeta}><b>ARCHIVE VOLUME</b><span>{analytics.totalIncidentsRecorded} incidents linked</span></div><i style={{ width: "100%" }} /></article>
      <article><span>AIRFRAME ACCRUED TIME</span><b className={styles.metricIcon} aria-hidden="true">◴</b><strong>{analytics.totalFlightHours.toFixed(1)}<em> HRS</em></strong><small>{analytics.totalDistanceKilometers.toFixed(1)} km reported</small><div className={styles.metricMeta}><b>AVG REPORTED</b><span>{duration(averageDurationMinutes)}</span></div><i style={{ width: `${Math.min(100, analytics.reportCoveragePercentage ?? 0)}%` }} /></article>
      <article><span>MISSION SUCCESS RATE</span><b className={styles.metricIcon} aria-hidden="true">◇</b><strong>{percent(analytics.successRatePercentage)}</strong><small>{analytics.abortedOrFailedFlights} diverted / aborted</small><div className={styles.metricMeta}><b>COMPLETED</b><span>{analytics.completedFlights} nominal</span></div><i style={{ width: `${analytics.successRatePercentage ?? 0}%` }} /></article>
      <article><span>REPORT COVERAGE</span><b className={styles.metricIcon} aria-hidden="true">▤</b><strong>{percent(analytics.reportCoveragePercentage)}</strong><small>{analytics.reportsAvailable} available / {analytics.reportsExcluded} excluded</small><div className={styles.metricMeta}><b>LEDGER STATE</b><span>{analytics.reportsAvailable ? "REPORTS INDEXED" : "AWAITING REPORTS"}</span></div><i style={{ width: `${analytics.reportCoveragePercentage ?? 0}%` }} /></article>
    </section>

    <section className={styles.filters} aria-label="Flight archive filters">
      <header><b><span aria-hidden="true">⌕</span> TELEMETRY &amp; MISSION FILTERING CONTROLS</b><button type="button" onClick={resetFilters}>RESET QUERY FILTERS</button></header>
      <div>
        <label className={styles.search}><span>MISSION / FLIGHT ID</span><input value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Search mission name or identifier" /></label>
        <label><span>FROM DATE</span><input type="date" value={fromDate} max={toDate || undefined} onChange={event => { setFromDate(event.target.value); setPage(1); }} /></label>
        <label><span>TO DATE</span><input type="date" value={toDate} min={fromDate || undefined} onChange={event => { setToDate(event.target.value); setPage(1); }} /></label>
        <label><span>MISSION OUTCOME</span><select value={status} onChange={event => { setStatus(event.target.value); setPage(1); }}>{terminalStatuses.map(value => <option key={value || "all"} value={value}>{value || "All outcomes"}</option>)}</select></label>
      </div>
      <footer><span>QUERY STATUS: <b>{result.totalItems} MATCHING FLIGHT RECORD{result.totalItems === 1 ? "" : "S"}</b></span><b>{refreshing ? "REFRESHING" : "BACKEND SYNCED"}</b></footer>
    </section>

    {result.items.length === 0 ? <ModuleEmpty title="No flights found" message="No mission matches the current filters." /> : <section className={styles.records} aria-label="Logged flight records">
      <header><h2>Logged Sorties <span>{result.totalItems} AVAILABLE</span></h2><small>SORT: DATE DESCENDING (UTC)</small></header>
      <div>{result.items.map(item => <FlightRecord key={item.missionId} item={item} />)}</div>
      <footer><span>Showing <b>{rangeStart}-{rangeEnd}</b> of <b>{result.totalItems}</b> completed flight records</span><nav aria-label="Flight history pages"><button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>&lt;</button>{pages.map(value => <button type="button" key={value} className={value === page ? styles.currentPage : ""} aria-current={value === page ? "page" : undefined} onClick={() => setPage(value)}>{value}</button>)}<button type="button" disabled={page >= result.totalPages} onClick={() => setPage(value => value + 1)}>&gt;</button></nav></footer>
    </section>}
  </div>;
}

function FlightRecord({ item }: { item: FlightHistoryItem }) {
  const missionStatus = item.status?.trim() || "Completed";
  const reportStatus = item.reportStatus?.trim() || (item.hasReport ? "Available" : "NotQueued");
  const reportAvailable = reportStatus !== "NotQueued";
  const statusKey = missionStatus.toLowerCase().replaceAll(" ", "");
  const isFailure = ["aborted", "failed", "cancelled"].includes(statusKey);
  const shortMissionId = item.missionId.slice(0, 8).toUpperCase();
  return <article className={`${styles.record} ${isFailure ? styles.recordDanger : ""}`}>
    <header><div><b>FLT-{shortMissionId}</b><code>MS-{item.missionId.slice(0, 4).toUpperCase()}</code><span className={`${styles.outcome} ${isFailure ? styles.danger : ""}`}>{missionStatus}</span></div><time>{dateTime(item.startTime)} UTC</time></header>
    <div className={styles.recordBody}>
      <div className={styles.recordIdentity}><h3>{item.missionName}</h3><dl><div><dt>AIRFRAME</dt><dd>{item.uavCode || "N/A"}<small>{item.uavName || "Aircraft unavailable"}</small></dd></div><div><dt>PIC / OPERATOR</dt><dd>{item.pilotName || "N/A"}<small>CERTIFIED FLIGHT CREW</small></dd></div><div><dt>DURATION</dt><dd>{duration(item.durationMinutes)}<small>RECORDED FLIGHT</small></dd></div><div><dt>INCIDENTS</dt><dd className={item.totalIncidents ? styles.alertText : ""}>{item.totalIncidents}<small>{item.totalIncidents ? "REVIEW REQUIRED" : "NOMINAL"}</small></dd></div><div><dt>REPORT REV.</dt><dd>{item.reportRevision ?? "--"}<small>{reportLabel(reportStatus)}</small></dd></div></dl></div>
      <div className={styles.reportState}><span className={`${styles.reportBadge} ${styles[reportStatus.toLowerCase()] ?? ""}`}>{reportLabel(reportStatus)}</span><small>{item.reportRevision ? `REVISION ${item.reportRevision}` : "NO REVISION"}{" // "}{item.telemetryCoverageComplete ? "TELEMETRY COVERED" : "COVERAGE INCOMPLETE"}</small>{reportAvailable ? <Link href={`/portal/flight-history/${encodeURIComponent(item.missionId)}`}>VIEW REPORT <ArrowRight size={14} strokeWidth={2} aria-hidden="true" /></Link> : <button type="button" disabled>REPORT NOT QUEUED</button>}</div>
    </div>
    <footer className={styles.recordTelemetry}><span><i className={item.telemetryCoverageComplete ? styles.nominalDot : styles.mutedDot} /> TELEMETRY: {item.telemetryCoverageComplete ? "COMPLETE" : "INCOMPLETE"}</span><span>END: {dateTime(item.endTime)}</span><span>INCIDENT LEDGER: {item.totalIncidents ? `${item.totalIncidents} LINKED` : "CLEAR"}</span><b>{reportAvailable ? "POST-FLIGHT RECORD AVAILABLE" : "REPORT GENERATION PENDING"}</b></footer>
  </article>;
}
