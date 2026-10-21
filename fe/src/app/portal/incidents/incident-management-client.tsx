"use client";

import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import type { Account } from "@/lib/auth/types";
import { getIncident, getIncidents } from "@/lib/portal/client";
import type { IncidentDetail, IncidentItem, PagedIncidents } from "@/lib/portal/types";
import IncidentMap from "./incident-map";
import styles from "./incident-management.module.css";

const PAGE_SIZE = 6;
const EMPTY_PAGE: PagedIncidents = { items: [], totalCount: 0, pageNumber: 1, pageSize: PAGE_SIZE, totalPages: 0 };
const SEVERITIES = ["", "Critical", "High", "Medium", "Low"];
const STATUSES = ["", "Open", "Acknowledged", "Resolved"];
const TYPES = ["", "LowBattery", "AltitudeExceeded", "SpeedExceeded", "GeofenceBreach", "CommunicationLoss"];

type Summary = { total: number; open: number; acknowledged: number; resolved: number };

const keyOf = (value: string | null | undefined) => (value ?? "unknown").replace(/\s+/g, "").toLowerCase();
const displayLabel = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/_/g, " ");
const apiDate = (value: string, endOfDay = false) => value ? `${value}T${endOfDay ? "23:59:59.999" : "00:00:00.000"}Z` : undefined;

function formatDate(value: string | null | undefined, includeTime = true) {
  if (!value) return "Unavailable";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Unavailable";
  return new Intl.DateTimeFormat("en-GB", {
    day: "2-digit", month: "short", year: "numeric",
    ...(includeTime ? { hour: "2-digit", minute: "2-digit", second: "2-digit" } : {}),
  }).format(date);
}

function relativeAge(value: string) {
  const elapsed = Date.now() - new Date(value).getTime();
  if (!Number.isFinite(elapsed) || elapsed < 0) return "just now";
  const minutes = Math.floor(elapsed / 60000);
  if (minutes < 1) return "less than a minute ago";
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ${minutes % 60}m ago`;
  return `${Math.floor(hours / 24)}d ago`;
}

function evidenceEntries(value: unknown): Array<[string, string]> {
  if (value === null || value === undefined) return [];
  if (typeof value !== "object" || Array.isArray(value)) return [["Evidence", String(value)]];
  return Object.entries(value as Record<string, unknown>).slice(0, 10).map(([label, item]) => {
    const rendered = item !== null && typeof item === "object" ? JSON.stringify(item) : String(item ?? "Unavailable");
    return [displayLabel(label), rendered];
  });
}

export default function IncidentManagementClient({ account }: { account: Account }) {
  const [data, setData] = useState<PagedIncidents>(EMPTY_PAGE);
  const [summary, setSummary] = useState<Summary>({ total: 0, open: 0, acknowledged: 0, resolved: 0 });
  const [page, setPage] = useState(1);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");
  const [severity, setSeverity] = useState("");
  const [status, setStatus] = useState("");
  const [type, setType] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [selectedId, setSelectedId] = useState("");
  const [detail, setDetail] = useState<IncidentDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState("");
  const [detailError, setDetailError] = useState("");
  const [refreshToken, setRefreshToken] = useState(0);
  const [dismissedPriorityId, setDismissedPriorityId] = useState("");

  const loadSummary = useCallback(async () => {
    const [all, open, acknowledged, resolved] = await Promise.all([
      getIncidents({ pageSize: 1 }),
      getIncidents({ pageSize: 1, status: "Open" }),
      getIncidents({ pageSize: 1, status: "Acknowledged" }),
      getIncidents({ pageSize: 1, status: "Resolved" }),
    ]);
    setSummary({ total: all.totalCount, open: open.totalCount, acknowledged: acknowledged.totalCount, resolved: resolved.totalCount });
  }, []);

  const loadList = useCallback(async () => {
    setLoading(true);
    setError("");
    try {
      const result = await getIncidents({
        pageNumber: page,
        pageSize: PAGE_SIZE,
        search,
        severity,
        status,
        type,
        fromDate: apiDate(fromDate),
        toDate: apiDate(toDate, true),
      });
      setData(result);
      setSelectedId(current => result.items.some(item => item.id === current) ? current : result.items[0]?.id ?? "");
    } catch (reason) {
      setData({ ...EMPTY_PAGE, pageNumber: page });
      setSelectedId("");
      setError(reason instanceof Error ? reason.message : "Unable to load incident registry.");
    } finally {
      setLoading(false);
    }
  }, [fromDate, page, search, severity, status, toDate, type]);

  useEffect(() => {
    const task = window.setTimeout(() => { void loadList(); }, 0);
    return () => window.clearTimeout(task);
  }, [loadList, refreshToken]);
  useEffect(() => {
    const task = window.setTimeout(() => {
      void loadSummary().catch(reason => setError(reason instanceof Error ? reason.message : "Unable to load incident summary."));
    }, 0);
    return () => window.clearTimeout(task);
  }, [loadSummary, refreshToken]);

  useEffect(() => {
    let active = true;
    const task = window.setTimeout(() => {
      if (!selectedId) {
        setDetail(null);
        setDetailError("");
        return;
      }
      setDetailLoading(true);
      setDetailError("");
      void getIncident(selectedId).then(result => {
        if (active) setDetail(result);
      }).catch(reason => {
        if (!active) return;
        setDetail(null);
        setDetailError(reason instanceof Error ? reason.message : "Unable to load incident evidence.");
      }).finally(() => { if (active) setDetailLoading(false); });
    }, 0);
    return () => { active = false; window.clearTimeout(task); };
  }, [selectedId, refreshToken]);

  const selectedItem = useMemo(() => data.items.find(item => item.id === selectedId) ?? null, [data.items, selectedId]);
  const selected = detail ?? selectedItem;
  const evidence = evidenceEntries(detail?.evidence);
  const roleTitle = account.role === "operation_manager" ? "MANAGER SCOPE / ALL MISSION INCIDENTS" : "PILOT SCOPE / OWN MISSIONS";
  const priorityIncident = data.items.find(item => keyOf(item.status) === "open" && ["critical", "high"].includes(keyOf(item.severity))) ?? null;
  const activeFilterCount = [search, severity, status, type, fromDate, toDate].filter(Boolean).length;

  function applySearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPage(1);
    setSearch(searchInput.trim());
  }

  function resetFilters() {
    setSearchInput("");
    setSearch("");
    setSeverity("");
    setStatus("");
    setType("");
    setFromDate("");
    setToDate("");
    setPage(1);
  }

  function selectStatus(nextStatus: string) {
    setStatus(nextStatus);
    setPage(1);
  }

  return <div className={styles.registry}>
    <header className={styles.heading}>
      <div>
        <span className={styles.eyebrow}>BR-09 TELEMETRY WATCHDOG / SCR-11 / UC-15</span>
        <h1>Real-Time Anomaly &amp; Incident Log</h1>
        <p>Review mission anomalies, frozen telemetry evidence, spatial fixes, and handling history.</p>
      </div>
      <div className={styles.headingTools}>
        <span><i /> API LINK ACTIVE</span>
        <b>{roleTitle}</b>
        <button type="button" onClick={() => setRefreshToken(value => value + 1)} disabled={loading}>REFRESH REGISTRY</button>
      </div>
    </header>

    {priorityIncident && priorityIncident.id !== dismissedPriorityId && <section className={styles.priorityBanner} aria-label="Priority incident alert">
      <div className={styles.prioritySignal}><i /><span>PRIORITY INCIDENT</span></div>
      <div className={styles.priorityMessage}><b>{priorityIncident.title}</b><span>{priorityIncident.uavCode ?? "UNASSIGNED UAV"} / {priorityIncident.missionName || "MISSION UNAVAILABLE"} / {relativeAge(priorityIncident.createdAt).toUpperCase()}</span></div>
      <StatusBadge value={priorityIncident.severity} />
      <button type="button" onClick={() => setSelectedId(priorityIncident.id)}>INSPECT RECORD</button>
      <button type="button" className={styles.dismissPriority} aria-label="Dismiss priority incident alert" title="Dismiss alert" onClick={() => setDismissedPriorityId(priorityIncident.id)}>&times;</button>
    </section>}

    {error && <div className={styles.errorBanner} role="alert"><span>{error}</span><button type="button" onClick={() => setRefreshToken(value => value + 1)}>RETRY</button></div>}

    <section className={styles.summaryGrid} aria-label="Incident summary">
      <SummaryCard label="TOTAL RECORDED" value={summary.total} tone="cyan" note="Visible in your access scope" active={!status} onSelect={() => selectStatus("")} />
      <SummaryCard label="ACTIVE / UNRESOLVED" value={summary.open} tone="danger" note="Requires operational review" active={status === "Open"} onSelect={() => selectStatus("Open")} />
      <SummaryCard label="ACKNOWLEDGED" value={summary.acknowledged} tone="violet" note="Handling in progress" active={status === "Acknowledged"} onSelect={() => selectStatus("Acknowledged")} />
      <SummaryCard label="RESOLVED / CLOSED" value={summary.resolved} tone="green" note="Mitigation recorded" active={status === "Resolved"} onSelect={() => selectStatus("Resolved")} />
    </section>

    <section className={styles.filterPanel} aria-label="Incident filters">
      <div className={styles.filterPrimary}>
        <form onSubmit={applySearch} className={styles.searchForm}>
          <label><span>SEARCH REGISTRY</span><input value={searchInput} onChange={event => setSearchInput(event.target.value)} placeholder="Incident title or description" /></label>
          <button type="submit">SEARCH</button>
        </form>
        <label className={styles.typeFilter}><span>ANOMALY TYPE</span><select value={type} onChange={event => { setType(event.target.value); setPage(1); }}>{TYPES.map(item => <option key={item || "all"} value={item}>{item ? displayLabel(item) : "All anomaly types"}</option>)}</select></label>
        <div className={styles.dateRange}>
          <label><span>FROM</span><input type="date" value={fromDate} max={toDate || undefined} onChange={event => { setFromDate(event.target.value); setPage(1); }} /></label>
          <label><span>TO</span><input type="date" value={toDate} min={fromDate || undefined} onChange={event => { setToDate(event.target.value); setPage(1); }} /></label>
        </div>
      </div>
      <div className={styles.filterMatrix}>
        <FilterGroup label="SEVERITY" value={severity} options={SEVERITIES} allLabel="ALL" onChange={next => { setSeverity(next); setPage(1); }} />
        <FilterGroup label="STATE" value={status} options={STATUSES} allLabel="ALL" onChange={selectStatus} />
        <div className={styles.filterActions}><span><b>{activeFilterCount}</b> ACTIVE FILTER{activeFilterCount === 1 ? "" : "S"}</span><button type="button" onClick={resetFilters} disabled={!activeFilterCount}>CLEAR FILTERS</button></div>
      </div>
    </section>

    <section className={styles.tablePanel} aria-busy={loading}>
      <header className={styles.tableHeader}>
        <div><span>ACTIVE INCIDENT REGISTRY</span><b>{data.totalCount} RECORD{data.totalCount === 1 ? "" : "S"}</b></div>
        <small>SELECT A RECORD TO INSPECT SPATIAL AND TELEMETRY EVIDENCE</small>
      </header>
      <div className={styles.columnHeader}><span>INCIDENT / SEVERITY</span><span>ASSET &amp; MISSION</span><span>TRIGGER SNAPSHOT</span><span>TIMESTAMP</span><span>STATUS</span><span>ACTION</span></div>
      {loading ? <RegistryState title="LOADING INCIDENT CHANNEL" description="Retrieving scoped incident records from the backend." /> : data.items.length ? <div className={styles.incidentRows}>
        {data.items.map(item => <IncidentRow key={item.id} item={item} selected={item.id === selectedId} onSelect={() => setSelectedId(item.id)} />)}
      </div> : <RegistryState title={activeFilterCount ? "NO MATCHING INCIDENTS" : "INCIDENT REGISTRY CLEAR"} description={activeFilterCount ? "No records match the current search and filter combination." : "No incidents have been recorded in your current access scope."} />}
      <footer className={styles.pagination}>
        <span>SHOWING <b>{data.items.length ? (data.pageNumber - 1) * data.pageSize + 1 : 0}-{Math.min(data.pageNumber * data.pageSize, data.totalCount)}</b> OF <b>{data.totalCount}</b></span>
        <div><button type="button" onClick={() => setPage(value => Math.max(1, value - 1))} disabled={page <= 1 || loading} aria-label="Previous page">&#8249;</button><b>PAGE {data.pageNumber || 1} / {Math.max(1, data.totalPages)}</b><button type="button" onClick={() => setPage(value => value + 1)} disabled={page >= data.totalPages || loading} aria-label="Next page">&#8250;</button></div>
      </footer>
    </section>

    <section className={styles.detailGrid}>
      <div className={styles.mapPanel}>
        <header><div><span>INCIDENT SPATIAL GROUND TRUTH</span><h2>{selected?.uavCode ?? "No asset selected"}</h2></div><b>{selected ? displayLabel(selected.type).toUpperCase() : "NO RECORD"}</b></header>
        <IncidentMap latitude={selected?.latitude ?? null} longitude={selected?.longitude ?? null} uavCode={selected?.uavCode ?? null} />
      </div>
      <aside className={styles.evidencePanel}>
        <header><div><span>FROZEN TELEMETRY EVIDENCE</span><h2>{selected?.title ?? "Select an incident"}</h2></div>{selected && <StatusBadge value={selected.status} />}</header>
        {detailLoading ? <RegistryState title="READING EVIDENCE" description="Loading the immutable incident snapshot." compact /> : detailError ? <div className={styles.detailError} role="alert">{detailError}</div> : selected ? <>
          <dl className={styles.detailFacts}>
            <div><dt>INCIDENT ID</dt><dd>{selected.id}</dd></div>
            <div><dt>MISSION</dt><dd>{selected.missionName || selected.missionId}</dd></div>
            <div><dt>UAV PLATFORM</dt><dd>{[selected.uavCode, selected.uavName].filter(Boolean).join(" / ") || "Unassigned"}</dd></div>
            <div><dt>DETECTED</dt><dd>{formatDate(selected.createdAt)}</dd></div>
            <div><dt>APPLIED RULE</dt><dd>{detail?.appliedRule ?? "Unavailable"}</dd></div>
            <div><dt>THRESHOLD</dt><dd>{detail?.threshold ?? "Unavailable"}</dd></div>
          </dl>
          <p className={styles.description}>{selected.description ?? "No incident description was supplied."}</p>
          <div className={styles.evidenceList}>{evidence.length ? evidence.map(([label, value]) => <div key={label}><span>{label}</span><code>{value}</code></div>) : <div className={styles.noEvidence}>No structured telemetry evidence was returned.</div>}</div>
          <div className={styles.auditTrail}>
            <h3>HANDLING AUDIT</h3>
            <div><i className={styles.auditCreated} /><span><b>Incident created</b><small>{formatDate(selected.createdAt)}</small></span></div>
            <div><i className={selected.acknowledgedAt ? styles.auditDone : undefined} /><span><b>{selected.acknowledgedAt ? `Acknowledged by ${detail?.acknowledgedByName ?? "authorized operator"}` : "Awaiting acknowledgement"}</b><small>{selected.acknowledgedAt ? formatDate(selected.acknowledgedAt) : "No backend action recorded"}</small></span></div>
            <div><i className={selected.resolvedAt ? styles.auditDone : undefined} /><span><b>{selected.resolvedAt ? `Resolved by ${detail?.resolvedByName ?? "authorized operator"}` : "Resolution pending"}</b><small>{selected.resolvedAt ? formatDate(selected.resolvedAt) : "No backend action recorded"}</small></span></div>
          </div>
          {account.role === "operation_manager" && keyOf(selected.status) !== "resolved" && <div className={styles.pendingActions}><button type="button" disabled>{keyOf(selected.status) === "open" ? "ACKNOWLEDGE INCIDENT" : "RESOLVE INCIDENT"}</button><span>Action endpoint is not available from the backend yet.</span></div>}
        </> : <RegistryState title="NO INCIDENT SELECTED" description="Choose a registry row to inspect its evidence." compact />}
      </aside>
    </section>
  </div>;
}

function SummaryCard({ label, value, tone, note, active, onSelect }: { label: string; value: number; tone: "cyan" | "danger" | "violet" | "green"; note: string; active: boolean; onSelect: () => void }) {
  return <button type="button" className={`${styles.summaryCard} ${styles[tone]} ${active ? styles.summaryActive : ""}`} aria-pressed={active} onClick={onSelect}><span>{label}</span><div><strong>{String(value).padStart(2, "0")}</strong><i /></div><small>{note}</small></button>;
}

function FilterGroup({ label, value, options, allLabel, onChange }: { label: string; value: string; options: string[]; allLabel: string; onChange: (value: string) => void }) {
  return <div className={styles.filterGroup}><span>{label}</span><div>{options.map(option => <button key={option || "all"} type="button" className={value === option ? styles.filterActive : ""} aria-pressed={value === option} onClick={() => onChange(option)}>{option || allLabel}</button>)}</div></div>;
}

function StatusBadge({ value }: { value: string }) {
  return <span className={`${styles.statusBadge} ${styles[`status${keyOf(value)}`] ?? ""}`}>{value.toUpperCase()}</span>;
}

function IncidentRow({ item, selected, onSelect }: { item: IncidentItem; selected: boolean; onSelect: () => void }) {
  const severityKey = keyOf(item.severity);
  return <article className={`${styles.incidentRow} ${selected ? styles.selected : ""}`} role="button" tabIndex={0} aria-pressed={selected} onClick={onSelect} onKeyDown={event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onSelect(); } }}>
    <div className={styles.incidentIdentity}><span className={`${styles.severityLine} ${styles[`severity${severityKey}`] ?? ""}`} /><div><small>{item.id.slice(0, 8).toUpperCase()}</small><h3>{item.title}</h3><b className={styles[`severity${severityKey}`]}>{item.severity.toUpperCase()}</b></div></div>
    <div className={styles.assetCell}><strong>{item.uavCode ?? "UNASSIGNED"}</strong><span>{item.uavName ?? "UAV name unavailable"}</span><small>MISSION: {item.missionName || item.missionId.slice(0, 8)}</small></div>
    <div className={styles.triggerCell}><b>{displayLabel(item.type)}</b><span>{item.description ?? "No description supplied"}</span><small>{item.latitude !== null && item.longitude !== null ? `${item.latitude.toFixed(4)}, ${item.longitude.toFixed(4)}` : "POSITION UNAVAILABLE"}</small></div>
    <div className={styles.timeCell}><strong>{formatDate(item.createdAt)}</strong><span>{relativeAge(item.createdAt)}</span></div>
    <div className={styles.statusCell}><StatusBadge value={item.status} /><small>{item.resolvedAt ? `CLOSED ${formatDate(item.resolvedAt, false)}` : item.acknowledgedAt ? `ACK ${formatDate(item.acknowledgedAt, false)}` : "UNACKNOWLEDGED"}</small></div>
    <div className={styles.rowAction}><button type="button" onClick={event => { event.stopPropagation(); onSelect(); }}>{selected ? "VIEWING" : "VIEW DETAILS"}</button></div>
  </article>;
}

function RegistryState({ title, description, compact = false }: { title: string; description: string; compact?: boolean }) {
  const loading = title.startsWith("LOADING") || title.startsWith("READING");
  return <div className={`${styles.registryState} ${compact ? styles.compact : ""}`}>{loading && <i />}<b>{title}</b><span>{description}</span></div>;
}
