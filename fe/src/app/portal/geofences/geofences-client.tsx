"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { getGeofences } from "@/lib/portal/client";
import type { Geofence } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import { ApiError } from "@/lib/auth/client";
import { describeGeofence } from "@/lib/maps/geofences";

export default function GeofencesClient() {
  const [zones, setZones] = useState<Geofence[] | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const load = useCallback(() => {
    setError(null); setZones(null);
    void getGeofences().then(setZones).catch(setError);
  }, []);
  useEffect(() => { let active = true; void getGeofences().then(value => { if (active) setZones(value); }).catch(reason => { if (active) setError(reason); }); return () => { active = false; }; }, []);
  const filtered = useMemo(() => zones?.filter(zone => `${zone.name} ${zone.description ?? ""}`.toLowerCase().includes(query.toLowerCase())) ?? [], [zones, query]);
  if (error instanceof ApiError && error.status === 403) return <section className="module-state error" role="alert"><span className="state-symbol">!</span><p className="module-kicker">ROLE POLICY MISMATCH</p><h2>Fleet & Restrictions is waiting for backend access</h2><p>Your web role is <b>operations_manager</b>, while the Geofences controller currently accepts only <b>Pilot, Admin, SystemAdmin</b>. The frontend cannot safely bypass backend authorization.</p><button className="module-primary" onClick={load}>TRY AGAIN</button></section>;
  if (error) return <ModuleError error={error} retry={load} />;
  if (!zones) return <ModuleLoading label="Loading controlled airspace" />;
  return <>
    <header className="module-heading"><div><p className="module-kicker">AIRSPACE / GIS REGISTRY</p><h1>Operational Airspace & Geofences</h1><p>Authoritative restricted zones currently configured by the backend.</p></div></header>
    <section className="module-toolbar"><label>SEARCH AIRSPACE<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Zone name or description" /></label><span>{filtered.length} / {zones.length} ZONES</span></section>
    {filtered.length === 0 ? <ModuleEmpty title="No geofences found" message={zones.length ? "Try a different search term." : "No controlled airspace has been configured."} /> : <section className="geofence-grid">{filtered.map(zone => {
      const info = describeGeofence(zone);
      return <article className={`geofence-card geofence-${info.category}`} key={zone.id}><header><span>{info.label}</span><small>{zone.coordinates.length} VERTICES</small></header><h2>{zone.name}</h2><strong className="geofence-limit">{info.limitLabel}</strong><p>{zone.description ?? "No operational notes provided."}</p><div className="coordinate-list">{zone.coordinates.slice(0, 4).map((point, index) => <code key={`${point.latitude}-${point.longitude}-${index}`}>{String(index + 1).padStart(2, "0")} · {point.latitude.toFixed(5)}, {point.longitude.toFixed(5)}</code>)}{zone.coordinates.length > 4 && <code>+ {zone.coordinates.length - 4} MORE VERTICES</code>}</div><footer>CREATED {new Date(zone.createdAt).toLocaleDateString()}</footer></article>;
    })}</section>}
  </>;
}
