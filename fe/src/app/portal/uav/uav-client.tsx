"use client";

import { useCallback, useEffect, useState } from "react";
import { getAssignedUavs } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";

export default function UavClient() {
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const load = useCallback(() => {
    setAircraft(null); setError(null);
    void getAssignedUavs().then(setAircraft).catch(setError);
  }, []);
  useEffect(() => {
    let active = true;
    void getAssignedUavs()
      .then(value => { if (active) setAircraft(value); })
      .catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);
  if (error) return <ModuleError error={error} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading assigned aircraft" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / AIRCRAFT ACCESS</p><h1>Assigned UAV</h1><p>Readiness and latest telemetry for aircraft assigned to you.</p></div></header>
    {aircraft.length === 0 ? <ModuleEmpty title="No UAV assigned" message="Your Operations Manager has not assigned an aircraft yet." /> : <section className="uav-grid">{aircraft.map(uav => <article className="uav-card" key={uav.id}><div className="uav-visual"><span>⌁</span><b>{uav.code}</b></div><div><header><span className={`status status-${uav.status.toLowerCase()}`}>{uav.status}</span><small>ASSIGNED AIRFRAME</small></header><h2>{uav.name}</h2><p>{uav.model ?? "Model not specified"}</p><div className="uav-metrics"><div><span>BATTERY</span><strong>{uav.batteryPercent ?? "--"}%</strong></div><div><span>LAST TELEMETRY</span><strong>{uav.lastTelemetryAt ? new Date(uav.lastTelemetryAt).toLocaleTimeString() : "Unavailable"}</strong></div><div><span>MISSION</span><strong>{uav.assignedMissionId?.toUpperCase() ?? "STANDBY"}</strong></div></div></div></article>)}</section>}
    <p className="mock-notice">MOCK DATA · UAV management remains read-only for the Pilot role.</p>
  </>;
}
