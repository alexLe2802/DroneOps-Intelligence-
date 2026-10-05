"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { deleteUav, getAssignedUav } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";

export default function UavDetailClient({ id }: { id: string }) {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [aircraft, setAircraft] = useState<AssignedUav | null | undefined>();
  const [error, setError] = useState<unknown>(null);
  const [deleting, setDeleting] = useState(false);
  const load = useCallback(() => {
    setAircraft(undefined); setError(null);
    void getAssignedUav(id).then(setAircraft).catch(setError);
  }, [id]);
  useEffect(() => {
    let active = true;
    void getAssignedUav(id).then(value => { if (active) setAircraft(value); }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, [id]);
  async function remove() {
    if (!aircraft || !window.confirm(`Delete ${aircraft.code} from your UAV list?`)) return;
    setDeleting(true);
    try { await deleteUav(aircraft.id); router.replace("/portal/uav"); }
    catch (reason) { setError(reason); setDeleting(false); }
  }
  if (error) return <ModuleError error={error} retry={load} />;
  if (aircraft === undefined) return <ModuleLoading label="Loading UAV details" />;
  if (aircraft === null) return <section className="module-state error"><span className="state-symbol">!</span><p className="module-kicker">HTTP 404</p><h2>UAV not found</h2><p>This aircraft does not exist or was removed.</p><Link className="module-primary module-link" href="/portal/uav">BACK TO UAV LIST</Link></section>;
  const submitted = searchParams.get("submitted");
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / UAV DETAILS</p><h1>{aircraft.code}</h1><p>Registration, operational status and assignment information.</p></div><div className="heading-actions"><Link className="module-secondary module-link" href="/portal/uav">← ALL UAV</Link><Link className="module-primary module-link" href={`/portal/uav/${aircraft.id}/edit`}>EDIT UAV</Link></div></header>
    {submitted && <p className="approval-banner" role="status">{submitted === "create" ? "UAV registration submitted." : "UAV changes submitted."} Waiting for admin approval.</p>}
    <section className="uav-detail">
      <div className="uav-visual"><span>⌁</span><b>{aircraft.code}</b></div>
      <div className="uav-detail-info"><header><span className={`status status-${aircraft.status.toLowerCase()}`}>{aircraft.status}</span><small className={aircraft.approvalStatus === "Approved" ? "approval-approved" : "approval-pending"}>{aircraft.approvalStatus === "Approved" ? "ADMIN APPROVED" : "PENDING ADMIN APPROVAL"}</small></header><h2>{aircraft.name}</h2><p>{aircraft.model ?? "Model not specified"}</p><dl><div><dt>Code</dt><dd>{aircraft.code}</dd></div><div><dt>Name</dt><dd>{aircraft.name}</dd></div><div><dt>Model</dt><dd>{aircraft.model ?? "Not specified"}</dd></div><div><dt>Status</dt><dd>{aircraft.status}</dd></div><div><dt>Created at</dt><dd>{aircraft.createdAt.slice(0, 10)}</dd></div><div><dt>Assigned mission</dt><dd>{aircraft.assignedMissionId?.toUpperCase() ?? "None"}</dd></div></dl><button className="module-danger" disabled={deleting} onClick={() => void remove()}>{deleting ? "DELETING…" : "DELETE UAV"}</button></div>
    </section>
    <p className="mock-notice">MOCK API · Detail fields follow the current backend UAV entity contract.</p>
  </>;
}
