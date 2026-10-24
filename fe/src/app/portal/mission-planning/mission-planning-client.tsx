"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { getGeofences, getManagerMissions } from "@/lib/portal/client";
import type { Geofence, ManagerMission } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../module-state";

type MissionBucket = "all" | "draft" | "pending" | "approved" | "active" | "completed" | "cancelled";

const PAGE_SIZE = 10;

const normalized = (value: string) => value.toLowerCase().replaceAll(" ", "").replaceAll("_", "");
const bucketFor = (status: string): MissionBucket => {
  const value = normalized(status);
  if (value === "draft") return "draft";
  if (["pending", "pendingapproval", "pendingreview"].includes(value)) return "pending";
  if (["approved", "ready", "validated"].includes(value)) return "approved";
  if (["inprogress", "inflight", "active"].includes(value)) return "active";
  if (value === "completed") return "completed";
  if (["cancelled", "canceled", "rejected", "aborted", "failed"].includes(value)) return "cancelled";
  return "all";
};

const formatStatus = (value: string) => value.replace(/([a-z])([A-Z])/g, "$1 $2").replaceAll("_", " ");
const formatDate = (value: string | null) => value
  ? new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(new Date(value))
  : "Not scheduled";

const escapeCsv = (value: string | number | null) => `"${String(value ?? "").replaceAll('"', '""')}"`;
const initials = (value: string) => value.split(/\s+/).filter(Boolean).slice(-2).map(part => part[0]?.toUpperCase()).join("") || "--";
const missionDuration = (mission: ManagerMission) => {
  if (!mission.startTime || !mission.endTime) return "Not scheduled";
  const minutes = Math.max(0, Math.round((new Date(mission.endTime).getTime() - new Date(mission.startTime).getTime()) / 60000));
  return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes} min`;
};
const validationLabel = (mission: ManagerMission) => {
  const evidence = mission.pendingUpdate?.validation ?? mission.validation;
  if (evidence?.stale) return "Stale evidence";
  if (evidence?.incomplete) return "Incomplete";
  if (evidence) return evidence.isValid ? "Validation passed" : "Validation failed";
  return mission.flightReadiness ?? "Not validated";
};

const statCards: Array<{ bucket: MissionBucket; code: string; label: string; sublabel: string }> = [
  { bucket: "all", code: "ALL", label: "All Missions", sublabel: "Total registered" },
  { bucket: "draft", code: "DR", label: "Draft", sublabel: "Uncommitted" },
  { bucket: "pending", code: "RV", label: "Pending Review", sublabel: "Airspace check" },
  { bucket: "approved", code: "OK", label: "Approved / Ready", sublabel: "Cleared" },
  { bucket: "active", code: "LIVE", label: "In Progress", sublabel: "Live airborne" },
  { bucket: "completed", code: "LOG", label: "Completed", sublabel: "Logged" },
  { bucket: "cancelled", code: "X", label: "Cancelled", sublabel: "Aborted / denied" },
];

async function fetchRegistry() {
  const [history, geofences] = await Promise.allSettled([
    getManagerMissions(),
    getGeofences(),
  ]);
  if (history.status === "rejected") throw history.reason;
  return {
    missions: history.value,
    zones: geofences.status === "fulfilled" ? geofences.value : [],
  };
}

export default function MissionPlanningClient() {
  const [missions, setMissions] = useState<ManagerMission[] | null>(null);
  const [zones, setZones] = useState<Geofence[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const [bucket, setBucket] = useState<MissionBucket>("all");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [page, setPage] = useState(1);
  const [lastSynced, setLastSynced] = useState<Date | null>(null);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    setMissions(null);
    try {
      const result = await fetchRegistry();
      setMissions(result.missions);
      setZones(result.zones);
      setLastSynced(new Date());
    } catch (reason) {
      setError(reason);
    }
  }, []);

  useEffect(() => {
    let active = true;
    const refresh = () => {
      void fetchRegistry().then(result => {
        if (!active) return;
        setMissions(result.missions);
        setZones(result.zones);
        setLastSynced(new Date());
        setRefreshing(false);
      }).catch(reason => {
        if (!active) return;
        setError(reason);
        setRefreshing(false);
      });
    };
    refresh();
    const timer = window.setInterval(refresh, 30000);
    return () => { active = false; window.clearInterval(timer); };
  }, []);

  function refreshRegistry() {
    setRefreshing(true);
    void fetchRegistry().then(result => {
      setMissions(result.missions);
      setZones(result.zones);
      setLastSynced(new Date());
      setError(null);
    }).catch(setError).finally(() => setRefreshing(false));
  }

  const counts = useMemo(() => {
    const result: Record<MissionBucket, number> = { all: missions?.length ?? 0, draft: 0, pending: 0, approved: 0, active: 0, completed: 0, cancelled: 0 };
    missions?.forEach(item => {
      const itemBucket = bucketFor(item.status);
      if (itemBucket !== "all") result[itemBucket] += 1;
    });
    return result;
  }, [missions]);

  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase();
    const from = dateFrom ? new Date(`${dateFrom}T00:00:00`).getTime() : null;
    const to = dateTo ? new Date(`${dateTo}T23:59:59.999`).getTime() : null;
    return (missions ?? []).filter(item => {
      if (bucket !== "all" && bucketFor(item.status) !== bucket) return false;
      if (search && !`${item.id} ${item.name} ${item.uavCode ?? ""} ${item.uavName ?? ""} ${item.pilotName ?? ""} ${item.createdBy} ${item.status}`.toLowerCase().includes(search)) return false;
      const scheduled = item.startTime ? new Date(item.startTime).getTime() : null;
      if (from !== null && (scheduled === null || scheduled < from)) return false;
      if (to !== null && (scheduled === null || scheduled > to)) return false;
      return true;
    });
  }, [bucket, dateFrom, dateTo, missions, query]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const visible = filtered.slice((currentPage - 1) * PAGE_SIZE, currentPage * PAGE_SIZE);
  const pageWindowStart = Math.max(1, Math.min(currentPage - 2, Math.max(1, totalPages - 4)));
  const pageNumbers = Array.from({ length: Math.min(5, totalPages) }, (_, index) => pageWindowStart + index);

  function exportManifest() {
    const headers = ["Mission ID", "Mission", "UAV Code", "UAV", "Pilot", "Status", "Start", "End", "Waypoints", "Validation"];
    const rows = filtered.map(item => [item.id, item.name, item.uavCode, item.uavName, item.pilotName, item.status, item.startTime, item.endTime, item.waypoints.length, validationLabel(item)]);
    const csv = [headers, ...rows].map(row => row.map(escapeCsv).join(",")).join("\r\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `droneops-mission-manifest-${new Date().toISOString().slice(0, 10)}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  if (error) return <ModuleError error={error} retry={() => void load()} />;
  if (!missions) return <ModuleLoading label="Loading mission registry" />;

  const rangeStart = filtered.length ? (currentPage - 1) * PAGE_SIZE + 1 : 0;
  const rangeEnd = Math.min(currentPage * PAGE_SIZE, filtered.length);
  const activeCount = counts.active;
  const assignedCount = missions.filter(item => item.uavId).length;
  const validationAttentionCount = missions.filter(item => {
    const evidence = item.pendingUpdate?.validation ?? item.validation;
    return Boolean(evidence && (!evidence.isValid || evidence.stale || evidence.incomplete));
  }).length;

  return <div className="mission-registry">
    <header className="mission-registry-heading">
      <div><div className="mission-registry-eyebrow"><span>REGISTRY // UTM-04</span><b>SYS: READY <i /></b></div><h1>UAV Mission Plans &amp; Operational Registry</h1><p>Search, filter, schedule, and review autonomous flight missions across operational sectors.</p></div>
      <div className="mission-registry-actions">
        <button type="button" className="manifest-export" onClick={exportManifest} disabled={filtered.length === 0}><span aria-hidden="true">CSV</span><b>EXPORT<br />MANIFEST</b></button>
        <Link className="plan-mission" href="/portal/mission-approvals"><span aria-hidden="true">RV</span><b>Review Missions</b><small>APPROVAL QUEUE</small></Link>
      </div>
    </header>

    <section className="mission-status-grid" aria-label="Mission status summary">
      {statCards.map(card => <button type="button" key={card.bucket} className={`${card.bucket === "active" ? "in-flight" : card.bucket}${bucket === card.bucket ? " selected" : ""}`} onClick={() => { setBucket(card.bucket); setPage(1); }} aria-pressed={bucket === card.bucket}>
        <header><span>{card.sublabel}</span><i aria-hidden="true">{card.code}</i></header><div><strong>{card.label}</strong><b>{counts[card.bucket].toString().padStart(2, "0")}</b></div>
      </button>)}
    </section>

    <section className="mission-registry-filters" aria-label="Mission filters">
      <label className="mission-search"><span>MISSION LOOKUP</span><input value={query} onChange={event => { setQuery(event.target.value); setPage(1); }} placeholder="Search by mission code, name, UAV or pilot" /></label>
      <label className="mission-sector"><span>SECTOR</span><select aria-label="Operational sector"><option>All Operational Sectors</option></select></label>
      <label><span>FROM</span><input type="date" value={dateFrom} onChange={event => { setDateFrom(event.target.value); setPage(1); }} /></label>
      <label><span>TO</span><input type="date" value={dateTo} onChange={event => { setDateTo(event.target.value); setPage(1); }} /></label>
      <div className="mission-filter-actions"><button type="button" onClick={() => { setQuery(""); setBucket("all"); setDateFrom(""); setDateTo(""); setPage(1); }}>RESET</button><button type="button" onClick={refreshRegistry} disabled={refreshing}>{refreshing ? "SYNCING" : "REFRESH"}</button></div>
    </section>

    <section className="mission-manifest-panel">
      <header><div><b>ACTIVE MANIFEST <span>(SHOWING {filtered.length} RECORDS)</span></b><em>GEODESIC WGS-84</em></div><span>AUTO REFRESH: <strong>30s</strong>{lastSynced ? ` // ${lastSynced.toLocaleTimeString("en-GB")}` : ""}</span></header>
      <div className="mission-manifest-table-wrap"><table>
        <thead><tr><th>Mission ID</th><th>Mission Plan</th><th>Assigned UAV</th><th>Pilot in Command</th><th>Schedule</th><th>Route Profile</th><th>Validation State</th><th>Record</th></tr></thead>
        <tbody>{visible.map(item => {
          const itemBucket = bucketFor(item.status);
          const pendingReview = itemBucket === "pending";
          return <tr key={item.id} className={itemBucket === "cancelled" ? "registry-danger-row" : ""}>
            <td><b className="mission-code">#MS-{item.id.slice(0, 4).toUpperCase()}</b><small>ID {item.id.slice(-4).toUpperCase()}</small></td>
            <td><strong>{item.name}</strong><small>{item.description || "No mission brief provided"}</small></td>
            <td><b>{item.uavCode || "Unassigned"}</b><small>{item.uavName || "No aircraft record"}{item.uavStatus ? ` // ${item.uavStatus}` : ""}</small></td>
            <td><div className="registry-pilot"><i>{initials(item.pilotName ?? "")}</i><span><b>{item.pilotName || "Pilot unavailable"}</b><small>{item.createdBy ? `USER ${item.createdBy.slice(0, 8).toUpperCase()}` : "CREATOR UNAVAILABLE"}</small></span></div></td>
            <td><b>{formatDate(item.startTime)}</b><small>{item.endTime ? `END ${formatDate(item.endTime)}` : "END NOT SCHEDULED"}</small></td>
            <td><b>{item.waypoints.length} waypoint{item.waypoints.length === 1 ? "" : "s"}</b><small>{item.altitudeReference || "ALT REF N/A"}{" // "}{missionDuration(item)}</small></td>
            <td><span className={`registry-state ${itemBucket}`}>{formatStatus(item.status)}</span><small>{validationLabel(item)}</small></td>
            <td><Link href={pendingReview ? `/portal/mission-approvals?missionId=${encodeURIComponent(item.id)}` : `/portal/missions/${encodeURIComponent(item.id)}`}>{pendingReview ? "REVIEW" : "OPEN"} <span aria-hidden="true">&gt;</span></Link></td>
          </tr>;
        })}
        {visible.length === 0 && <tr><td className="mission-registry-empty" colSpan={8}>No mission records match the selected filters.</td></tr>}</tbody>
      </table></div>
      <footer><div className="manifest-range"><span>Showing <strong>{rangeStart}-{rangeEnd}</strong> of <strong>{filtered.length}</strong> flight plans</span><em><small>PAGE SIZE</small><strong>{PAGE_SIZE}</strong><small>/ PAGE</small></em></div><nav aria-label="Mission registry pages"><button type="button" aria-label="Previous page" disabled={currentPage === 1} onClick={() => setPage(value => Math.max(1, value - 1))}>&lt;</button>{pageNumbers.map(number => <button type="button" key={number} className={number === currentPage ? "current" : ""} aria-current={number === currentPage ? "page" : undefined} onClick={() => setPage(number)}>{number}</button>)}<button type="button" aria-label="Next page" disabled={currentPage === totalPages} onClick={() => setPage(value => Math.min(totalPages, value + 1))}>&gt;</button></nav></footer>
    </section>

    <section className="mission-registry-insights">
      <article><header><div><i aria-hidden="true">CL</i><h2>Corridor Load</h2></div><b>{activeCount ? "ACTIVE" : "NOMINAL"}</b></header><p>UTM Sector 04 currently handles {activeCount} active mission stream{activeCount === 1 ? "" : "s"} from the backend registry.</p><div className="registry-progress"><i style={{ width: `${Math.min(100, activeCount * 20)}%` }} /></div><footer><span>CHANNEL CAPACITY</span><b>{Math.min(100, activeCount * 20)}%</b></footer></article>
      <article><header><div><i aria-hidden="true">FA</i><h2>Fleet Assignment</h2></div><b>{assignedCount}/{missions.length}</b></header><p>{assignedCount} registered mission plan{assignedCount === 1 ? " has" : "s have"} an aircraft assignment in the current registry.</p><div className="registry-progress"><i style={{ width: `${missions.length ? Math.round(assignedCount / missions.length * 100) : 0}%` }} /></div><footer><span>ASSIGNED COVERAGE</span><b>{missions.length ? Math.round(assignedCount / missions.length * 100) : 0}%</b></footer></article>
      <article><header><div><i aria-hidden="true">GC</i><h2>Geofence Compliance</h2></div><b className={validationAttentionCount ? "alert" : ""}>{validationAttentionCount ? `${validationAttentionCount} ATTENTION` : "CLEAR"}</b></header><p>{zones.length} configured airspace zone{zones.length === 1 ? " is" : "s are"} available. {validationAttentionCount} mission plan{validationAttentionCount === 1 ? " requires" : "s require"} validation review.</p><footer><span>AIRSPACE REGISTRY</span><Link href="/portal/geofences">OPEN &gt;</Link></footer></article>
    </section>
  </div>;
}
