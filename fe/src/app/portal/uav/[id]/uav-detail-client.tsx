"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { getAssignedUav } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";

export default function UavDetailClient({ id }: { id: string }) {
  const searchParams = useSearchParams();
  const [aircraft, setAircraft] = useState<AssignedUav | null | undefined>();
  const [error, setError] = useState<unknown>(null);
  const [pendingAction, setPendingAction] = useState("");
  const load = useCallback(() => {
    setAircraft(undefined); setError(null);
    void getAssignedUav(id).then(setAircraft).catch(setError);
  }, [id]);
  useEffect(() => {
    let active = true;
    void getAssignedUav(id).then(value => { if (active) setAircraft(value); }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, [id]);
  if (error) return <ModuleError error={error} retry={load} />;
  if (aircraft === undefined) return <ModuleLoading label="Loading UAV details" />;
  if (aircraft === null) return <section className="module-state error"><span className="state-symbol">!</span><p className="module-kicker">HTTP 404</p><h2>UAV not found</h2><p>This aircraft does not exist or was removed.</p><Link className="module-primary module-link" href="/portal/uav">BACK TO UAV LIST</Link></section>;
  const submitted = searchParams.get("submitted");
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / UAV DETAILS</p><h1>{aircraft.code}</h1><p>Registration and operational status from the backend registry.</p></div><div className="heading-actions"><Link className="module-secondary module-link" href="/portal/uav">← ALL UAV</Link></div></header>
    {submitted && <p className="approval-banner" role="status">{submitted === "create" ? "UAV registration submitted." : "UAV changes submitted."} Waiting for admin approval.</p>}
    {pendingAction && <p className="api-pending-banner" role="status"><b>{pendingAction}</b> cannot be submitted yet because the backend currently exposes only list and create APIs for pilots.</p>}
    <section className="uav-detail">
      <div className="uav-visual"><span>⌁</span><b>{aircraft.code}</b></div>
      <div className="uav-detail-info"><header><span className={`status status-${aircraft.status.toLowerCase()}`}>{aircraft.status}</span><small className={aircraft.status.toLowerCase() === "active" ? "approval-approved" : "approval-pending"}>{aircraft.status.toLowerCase() === "active" ? "ADMIN APPROVED" : "AWAITING ADMIN ACTION"}</small></header><h2>{aircraft.name}</h2><p>{aircraft.model ?? "Model not specified"}</p><dl><div><dt>Code</dt><dd>{aircraft.code}</dd></div><div><dt>Name</dt><dd>{aircraft.name}</dd></div><div><dt>Model</dt><dd>{aircraft.model ?? "Not specified"}</dd></div><div><dt>Status</dt><dd>{aircraft.status}</dd></div><div><dt>Created at</dt><dd>{aircraft.createdAt.slice(0, 10)}</dd></div><div><dt>Pilot</dt><dd>{aircraft.pilotName || "Current pilot"}</dd></div></dl><div className="uav-actions"><button className="module-secondary pending-action" type="button" onClick={() => setPendingAction(`Edit ${aircraft.code}`)}>EDIT · API PENDING</button><button className="module-danger pending-action" type="button" onClick={() => setPendingAction(`Delete ${aircraft.code}`)}>DELETE · API PENDING</button></div></div>
    </section>
  </>;
}
