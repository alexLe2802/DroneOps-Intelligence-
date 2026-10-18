"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { getPilotMissions } from "@/lib/portal/client";
import type { PilotMission } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../module-state";

type MissionBucket = "all" | "draft" | "pending" | "approved" | "active" | "rejected" | "completed" | "cancelled";
type SortOrder = "newest" | "oldest" | "schedule" | "name";

const pageSize = 6;
const statusTabs: Array<{ bucket: MissionBucket; label: string; caption: string }> = [
  { bucket: "all", label: "All Missions", caption: "Full registry" },
  { bucket: "draft", label: "Draft", caption: "Action required" },
  { bucket: "pending", label: "In Review", caption: "Manager queue" },
  { bucket: "approved", label: "Approved", caption: "Ready to fly" },
  { bucket: "active", label: "In Progress", caption: "Flight active" },
  { bucket: "rejected", label: "Rejected", caption: "Revise plan" },
  { bucket: "completed", label: "Completed", caption: "Flight logged" },
  { bucket: "cancelled", label: "Cancelled", caption: "Record retained" },
];

const normalizedStatus = (value: string) => value.toLowerCase().replaceAll(" ", "");
const missionBucket = (status: string): Exclude<MissionBucket, "all"> => {
  const value = normalizedStatus(status);
  if (["submitted", "pending", "pendingapproval", "pendingreview"].includes(value)) return "pending";
  if (["approved", "ready"].includes(value)) return "approved";
  if (["inprogress", "inflight", "active"].includes(value)) return "active";
  if (value === "rejected") return "rejected";
  if (value === "completed") return "completed";
  if (["cancelled", "canceled", "aborted"].includes(value)) return "cancelled";
  return "draft";
};
const formatStatus = (value: string) => value.replace(/([A-Z])/g, " $1").trim();
const formatDate = (value: string | null) => value ? new Date(value).toLocaleString() : "Not scheduled";
const statusClass = (value: string) => value.toLowerCase().replaceAll(" ", "");

export default function MissionsClient() {
  const [missions, setMissions] = useState<PilotMission[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const [bucket, setBucket] = useState<MissionBucket>("all");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [sortOrder, setSortOrder] = useState<SortOrder>("newest");
  const [page, setPage] = useState(1);

  const load = useCallback(() => {
    setMissions(null);
    setError(null);
    void getPilotMissions().then(setMissions).catch(setError);
  }, []);

  useEffect(() => {
    let active = true;
    void getPilotMissions()
      .then(value => { if (active) setMissions(value); })
      .catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);

  const counts = useMemo(() => {
    const result: Record<MissionBucket, number> = { all: missions?.length ?? 0, draft: 0, pending: 0, approved: 0, active: 0, rejected: 0, completed: 0, cancelled: 0 };
    missions?.forEach(mission => { result[missionBucket(mission.status)] += 1; });
    return result;
  }, [missions]);

  const filtered = useMemo(() => {
    const cleanQuery = query.trim().toLowerCase();
    const from = dateFrom ? new Date(`${dateFrom}T00:00:00`).getTime() : null;
    const to = dateTo ? new Date(`${dateTo}T23:59:59.999`).getTime() : null;
    const result = (missions ?? []).filter(mission => {
      if (bucket !== "all" && missionBucket(mission.status) !== bucket) return false;
      if (cleanQuery && !`${mission.name} ${mission.description ?? ""} ${mission.uavCode ?? ""} ${mission.status}`.toLowerCase().includes(cleanQuery)) return false;
      const missionDate = new Date(mission.startTime ?? mission.createdAt).getTime();
      if (from !== null && (!Number.isFinite(missionDate) || missionDate < from)) return false;
      if (to !== null && (!Number.isFinite(missionDate) || missionDate > to)) return false;
      return true;
    });
    return result.sort((left, right) => {
      if (sortOrder === "name") return left.name.localeCompare(right.name);
      const leftTime = new Date(sortOrder === "schedule" ? left.startTime ?? left.createdAt : left.createdAt).getTime() || 0;
      const rightTime = new Date(sortOrder === "schedule" ? right.startTime ?? right.createdAt : right.createdAt).getTime() || 0;
      return sortOrder === "oldest" ? leftTime - rightTime : rightTime - leftTime;
    });
  }, [bucket, dateFrom, dateTo, missions, query, sortOrder]);

  if (error) return <ModuleError error={error} retry={load} />;
  if (!missions) return <ModuleLoading label="Loading your missions" />;

  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize));
  const safePage = Math.min(page, totalPages);
  const startIndex = (safePage - 1) * pageSize;
  const visible = filtered.slice(startIndex, startIndex + pageSize);
  const hasFilters = Boolean(query || dateFrom || dateTo || bucket !== "all" || sortOrder !== "newest");

  function clearFilters() {
    setQuery("");
    setBucket("all");
    setDateFrom("");
    setDateTo("");
    setSortOrder("newest");
    setPage(1);
  }

  return <div className="pilot-mission-registry">
    <header className="module-heading pilot-mission-heading"><div><p className="module-kicker">PILOT / ASSIGNED SCOPE</p><h1>My Missions</h1><p>Plan, review and follow every mission owned by your account.</p></div><Link className="module-primary module-link" href="/portal/missions/new">+ NEW MISSION</Link></header>

    <nav className="pilot-mission-statuses" aria-label="Filter missions by status">
      {statusTabs.map(tab => <button type="button" key={tab.bucket} className={`${tab.bucket}${bucket === tab.bucket ? " selected" : ""}`} aria-pressed={bucket === tab.bucket} onClick={() => { setBucket(tab.bucket); setPage(1); }}><span>{tab.label}</span><strong>{String(counts[tab.bucket]).padStart(2, "0")}</strong><small>{tab.caption}</small></button>)}
    </nav>

    <section className="pilot-mission-filters" aria-label="Mission registry filters">
      <label className="pilot-mission-search">SEARCH MISSIONS<input value={query} onChange={event => { setQuery(event.target.value); setPage(1); }} placeholder="Mission name, UAV or status" /></label>
      <label>FROM DATE<input type="date" value={dateFrom} onChange={event => { setDateFrom(event.target.value); setPage(1); }} /></label>
      <label>TO DATE<input type="date" value={dateTo} min={dateFrom || undefined} onChange={event => { setDateTo(event.target.value); setPage(1); }} /></label>
      <label>SORT BY<select value={sortOrder} onChange={event => { setSortOrder(event.target.value as SortOrder); setPage(1); }}><option value="newest">Newest created</option><option value="oldest">Oldest created</option><option value="schedule">Next schedule</option><option value="name">Mission name</option></select></label>
      <button type="button" disabled={!hasFilters} onClick={clearFilters}>RESET FILTERS</button>
    </section>

    <section className="pilot-mission-manifest">
      <header><div><b>MISSION REGISTRY</b><span>OWNED SCOPE</span></div><div><strong>{filtered.length}</strong> MATCHING / {missions.length} TOTAL</div></header>
      <div className="pilot-mission-table-wrap">
        <table>
          <thead><tr><th>MISSION PLAN</th><th>ASSIGNED UAV</th><th>SCHEDULE</th><th>STATUS</th><th>VERSION</th><th>ACTIONS</th></tr></thead>
          <tbody>{visible.length ? visible.map(mission => {
            const editable = ["draft", "rejected"].includes(normalizedStatus(mission.status));
            const missionState = missionBucket(mission.status);
            return <tr key={mission.id}>
              <td><strong>{mission.name}</strong><small>{mission.description || "No mission description"}</small><code>#{mission.id.slice(0, 8).toUpperCase()}</code></td>
              <td><b>{mission.uavCode ?? "UNASSIGNED"}</b><small>{mission.uavId ? mission.uavId.slice(0, 8).toUpperCase() : "NO AIRCRAFT ID"}</small></td>
              <td><b>{formatDate(mission.startTime)}</b><small>{mission.endTime ? `Ends ${formatDate(mission.endTime)}` : "No end time"}</small></td>
              <td><span className={`status status-${statusClass(mission.status)}`}>{formatStatus(mission.status)}</span><small>{missionState === "draft" ? "Validation required" : missionState === "pending" ? "Manager review" : missionState === "active" ? "Flight active" : missionState === "rejected" ? "Revision required" : "Lifecycle recorded"}</small></td>
              <td><b>V{String(mission.latestVersion).padStart(2, "0")}</b><small>Current plan</small></td>
              <td><div className="pilot-mission-actions"><Link href={`/portal/missions/${mission.id}`}>OPEN</Link>{editable && <Link className="edit" href={`/portal/missions/${mission.id}/edit`}>EDIT</Link>}</div></td>
            </tr>;
          }) : <tr><td className="pilot-mission-empty" colSpan={6}><strong>{missions.length ? "No missions match these filters" : "No missions in your registry"}</strong><span>{missions.length ? "Adjust the current status, date or search filters." : "Create a draft mission to begin planning a flight route."}</span>{hasFilters && <button type="button" onClick={clearFilters}>CLEAR FILTERS</button>}</td></tr>}</tbody>
        </table>
      </div>
      <footer><span>{filtered.length ? `SHOWING ${startIndex + 1}-${Math.min(startIndex + pageSize, filtered.length)} OF ${filtered.length}` : "SHOWING 0 RECORDS"}</span><div><button type="button" aria-label="Previous page" title="Previous page" disabled={safePage === 1} onClick={() => setPage(current => Math.max(1, current - 1))}>&lt;</button><b>{safePage}</b><span>/ {totalPages}</span><button type="button" aria-label="Next page" title="Next page" disabled={safePage === totalPages} onClick={() => setPage(current => Math.min(totalPages, current + 1))}>&gt;</button></div></footer>
    </section>
  </div>;
}
