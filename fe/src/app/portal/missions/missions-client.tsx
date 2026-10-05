"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getPilotMissions } from "@/lib/portal/client";
import type { PilotMission } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

export default function MissionsClient() {
  const [missions, setMissions] = useState<PilotMission[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const load = useCallback(() => {
    setMissions(null); setError(null);
    void getPilotMissions().then(setMissions).catch(setError);
  }, []);
  useEffect(() => {
    let active = true;
    void getPilotMissions()
      .then(value => { if (active) setMissions(value); })
      .catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);
  const filtered = useMemo(() => missions?.filter(mission => `${mission.name} ${mission.uavCode ?? ""} ${mission.status}`.toLowerCase().includes(query.toLowerCase())) ?? [], [missions, query]);
  if (error) return <ModuleError error={error} retry={load} />;
  if (!missions) return <ModuleLoading label="Loading your missions" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / ASSIGNED SCOPE</p><h1>My Missions</h1><p>Plan and follow missions assigned to your account.</p></div><button className="module-primary" onClick={() => alert("Mission creation will connect to the backend mission API when available.")}>+ NEW MISSION</button></header>
    <section className="module-toolbar"><label>SEARCH MISSIONS<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Name, UAV or status" /></label><span>{filtered.length} / {missions.length} RECORDS</span></section>
    {filtered.length === 0 ? <ModuleEmpty title="No missions found" message={missions.length ? "Try a different search term." : "No mission has been assigned to your account yet."} /> : <section className="module-grid">{filtered.map(mission => <article className="module-card" key={mission.id}><header><span className={`status status-${mission.status.toLowerCase()}`}>{mission.status.replace(/([A-Z])/g, " $1").trim()}</span><small>V{mission.latestVersion.toString().padStart(2, "0")}</small></header><h2>{mission.name}</h2><p>{mission.description ?? "No description provided."}</p><dl><div><dt>Aircraft</dt><dd>{mission.uavCode ?? "Not assigned"}</dd></div><div><dt>Start</dt><dd>{mission.startTime ? new Date(mission.startTime).toLocaleString() : "Not scheduled"}</dd></div></dl><button className="module-secondary">OPEN MISSION →</button></article>)}</section>}
    <p className="mock-notice">MOCK DATA · This view is ready to switch to the Missions API contract.</p>
  </>;
}
