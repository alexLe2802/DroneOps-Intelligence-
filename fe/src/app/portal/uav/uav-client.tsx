"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { getAssignedUavs } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

export default function UavClient() {
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const [pendingAction, setPendingAction] = useState("");
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
  if (error) return <ModuleError error={error} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading assigned aircraft" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / ASSIGNED AIRCRAFT</p><h1>Assigned UAV</h1><p>View aircraft assigned to your pilot account. Record changes are restricted to Operations Managers.</p></div></header>
    {pendingAction && <p className="api-pending-banner" role="status"><b>{pendingAction}</b> is defined by SRS UC-02 for Operations Managers. The backend endpoint and role policy are pending implementation.</p>}
    <section className="module-toolbar"><label>SEARCH UAV<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Code, name, model or status" /></label><span>{filtered.length} / {aircraft.length} AIRCRAFT</span></section>
    {filtered.length === 0 ? <ModuleEmpty title="No UAV found" message={aircraft.length ? "Try a different search term." : "No UAV is assigned to your account."} /> : <section className="uav-grid">{filtered.map(uav => <article className="uav-card" key={uav.id}><div className="uav-visual"><span>⌁</span><b>{uav.code}</b></div><div><header><span className={`status status-${uav.status.toLowerCase()}`}>{uav.status}</span><small className={uav.status.toLowerCase() === "active" ? "approval-approved" : "approval-pending"}>{uav.status.toLowerCase() === "active" ? "ADMIN APPROVED" : "AWAITING ADMIN ACTION"}</small></header><h2>{uav.name}</h2><p>{uav.model ?? "Model not specified"}</p><div className="uav-metrics"><div><span>PILOT</span><strong>{uav.pilotName || "Current pilot"}</strong></div><div><span>CREATED</span><strong>{uav.createdAt.slice(0, 10)}</strong></div><div><span>STATE</span><strong>{uav.status.toUpperCase()}</strong></div></div><div className="uav-actions"><Link className="module-secondary module-link" href={`/portal/uav/${uav.id}`}>VIEW DETAILS →</Link><button className="module-secondary srs-manager-action" type="button" onClick={() => setPendingAction(`Edit ${uav.code}`)}>EDIT · MANAGER</button><button className="module-danger srs-manager-action" type="button" onClick={() => setPendingAction(`Deactivate ${uav.code}`)}>DEACTIVATE · MANAGER</button></div></div></article>)}</section>}
  </>;
}
