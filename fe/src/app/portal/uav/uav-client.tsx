"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { deleteUav, getAssignedUavs } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

export default function UavClient() {
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const [deleting, setDeleting] = useState<string | null>(null);
  const load = useCallback(() => {
    setAircraft(null); setError(null);
    void getAssignedUavs().then(setAircraft).catch(setError);
  }, []);
  useEffect(() => {
    let active = true;
    void getAssignedUavs().then(value => { if (active) setAircraft(value); }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);
  const filtered = useMemo(() => aircraft?.filter(item => `${item.code} ${item.name} ${item.model ?? ""} ${item.status}`.toLowerCase().includes(query.toLowerCase())) ?? [], [aircraft, query]);
  async function remove(item: AssignedUav) {
    if (!window.confirm(`Delete ${item.code} from your UAV list?`)) return;
    setDeleting(item.id); setError(null);
    try { await deleteUav(item.id); setAircraft(current => current?.filter(value => value.id !== item.id) ?? []); }
    catch (reason) { setError(reason); }
    finally { setDeleting(null); }
  }
  if (error) return <ModuleError error={error} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading assigned aircraft" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / AIRCRAFT ACCESS</p><h1>Manage UAV</h1><p>View and manage UAVs registered to your pilot account.</p></div><Link className="module-primary module-link" href="/portal/uav/new">+ ADD UAV</Link></header>
    <section className="module-toolbar"><label>SEARCH UAV<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Code, name, model or status" /></label><span>{filtered.length} / {aircraft.length} AIRCRAFT</span></section>
    {filtered.length === 0 ? <ModuleEmpty title="No UAV found" message={aircraft.length ? "Try a different search term." : "Add your first UAV to submit it for admin approval."} /> : <section className="uav-grid">{filtered.map(uav => <article className="uav-card" key={uav.id}><div className="uav-visual"><span>⌁</span><b>{uav.code}</b></div><div><header><span className={`status status-${uav.status.toLowerCase()}`}>{uav.status}</span><small className={uav.approvalStatus === "Approved" ? "approval-approved" : "approval-pending"}>{uav.approvalStatus === "Approved" ? "ADMIN APPROVED" : "PENDING ADMIN APPROVAL"}</small></header><h2>{uav.name}</h2><p>{uav.model ?? "Model not specified"}</p><div className="uav-metrics"><div><span>BATTERY</span><strong>{uav.batteryPercent ?? "--"}{uav.batteryPercent === null ? "" : "%"}</strong></div><div><span>CREATED</span><strong>{uav.createdAt.slice(0, 10)}</strong></div><div><span>MISSION</span><strong>{uav.assignedMissionId?.toUpperCase() ?? "STANDBY"}</strong></div></div><div className="uav-actions"><Link className="module-secondary module-link" href={`/portal/uav/${uav.id}`}>VIEW DETAILS →</Link><Link className="module-secondary module-link" href={`/portal/uav/${uav.id}/edit`}>EDIT</Link><button className="module-danger" disabled={deleting === uav.id} onClick={() => void remove(uav)}>{deleting === uav.id ? "DELETING…" : "DELETE"}</button></div></div></article>)}</section>}
    <p className="mock-notice">MOCK API · New and updated UAV information is marked pending until the backend approval API is available.</p>
  </>;
}
