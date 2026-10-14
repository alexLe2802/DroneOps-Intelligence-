"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { getAssignedUav } from "@/lib/portal/client";
import type { AssignedUav } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../../module-state";
import UavForm from "../../uav-form";

export default function UavEditClient({ id }: { id: string }) {
  const [aircraft, setAircraft] = useState<AssignedUav | null | undefined>();
  const [error, setError] = useState<unknown>(null);
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
  if (aircraft === undefined) return <ModuleLoading label="Loading UAV information" />;
  if (aircraft === null) return <section className="module-state error"><span className="state-symbol">!</span><p className="module-kicker">HTTP 404</p><h2>UAV not found</h2><p>This aircraft does not exist or was removed.</p><Link className="module-primary module-link" href="/portal/uav">BACK TO UAV LIST</Link></section>;
  return <UavForm aircraft={aircraft} />;
}
