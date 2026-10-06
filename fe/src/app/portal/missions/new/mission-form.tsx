"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useState, type FormEvent } from "react";
import { createMission, getAssignedUavs } from "@/lib/portal/client";
import type { AssignedUav, MissionWaypoint, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

const actions: WaypointAction[] = ["FlyThrough", "Hover", "Photo", "Land"];

export default function MissionForm() {
  const router = useRouter();
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const [uavId, setUavId] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [waypoints, setWaypoints] = useState<MissionWaypoint[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  function load() {
    setAircraft(null); setLoadError(null);
    void getAssignedUavs().then(setAircraft).catch(setLoadError);
  }
  useEffect(() => {
    let active = true;
    void getAssignedUavs().then(value => { if (active) setAircraft(value); }).catch(reason => { if (active) setLoadError(reason); });
    return () => { active = false; };
  }, []);
  function addWaypoint(longitude: number, latitude: number) {
    setWaypoints(current => [...current, { sequenceOrder: current.length, latitude: Number(latitude.toFixed(6)), longitude: Number(longitude.toFixed(6)), altitude: 50, actionType: "FlyThrough" }]);
  }
  function updateWaypoint(index: number, patch: Partial<MissionWaypoint>) {
    setWaypoints(current => current.map((point, currentIndex) => currentIndex === index ? { ...point, ...patch } : point));
  }
  function removeWaypoint(index: number) {
    setWaypoints(current => current.filter((_, currentIndex) => currentIndex !== index).map((point, sequenceOrder) => ({ ...point, sequenceOrder })));
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); setError("");
    if (waypoints.length < 2) { setError("Add at least two waypoints to define a flight route."); return; }
    if (new Date(endTime) <= new Date(startTime)) { setError("End time must be later than start time."); return; }
    if (waypoints.some(point => point.altitude < 0 || point.altitude > 500)) { setError("Waypoint altitude must be between 0 and 500 metres."); return; }
    setSaving(true);
    try {
      await createMission({ uavId, name: name.trim(), description: description.trim() || null, startTime: new Date(startTime).toISOString(), endTime: new Date(endTime).toISOString(), waypoints });
      router.push("/portal/missions");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to create the mission."); }
    finally { setSaving(false); }
  }
  if (loadError) return <ModuleError error={loadError} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading mission resources" />;
  const approvedAircraft = aircraft.filter(item => item.approvalStatus === "Approved" && item.status !== "Maintenance" && item.status !== "InFlight");
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / FLIGHT PLANNING</p><h1>Add Flight Route</h1><p>Select an aircraft, define the mission and place waypoints on the live map.</p></div><Link className="module-secondary module-link" href="/portal/missions">← MY MISSIONS</Link></header>
    <form className="mission-builder" onSubmit={submit}>
      <section className="mission-fields">
        <div className="builder-section-title"><span>01</span><div><h2>Mission information</h2><p>Define the aircraft and operating window.</p></div></div>
        <div className="mission-field-grid">
          <label>ASSIGNED UAV<select value={uavId} onChange={event => setUavId(event.target.value)} required><option value="">Select an approved UAV</option>{approvedAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
          <label>MISSION NAME<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={100} required placeholder="Transmission Line Audit" /></label>
          <label className="full-field">DESCRIPTION<textarea value={description} onChange={event => setDescription(event.target.value)} maxLength={500} placeholder="Describe the objective and operating notes…" /></label>
          <label>START TIME<input type="datetime-local" value={startTime} onChange={event => setStartTime(event.target.value)} required /></label>
          <label>END TIME<input type="datetime-local" value={endTime} onChange={event => setEndTime(event.target.value)} required /></label>
        </div>
        {approvedAircraft.length === 0 && <p className="form-warning">No approved and available UAV is ready for mission planning.</p>}
      </section>
      <section className="route-builder">
        <div className="builder-section-title"><span>02</span><div><h2>Flight waypoints</h2><p>Click the map in flight order, then configure altitude and action.</p></div><button type="button" className="text-action" disabled={!waypoints.length} onClick={() => setWaypoints([])}>CLEAR ROUTE</button></div>
        <MissionMap waypoints={waypoints} onAdd={addWaypoint} />
        <div className="waypoint-list">{waypoints.length === 0 ? <div className="waypoint-empty">No waypoints yet. Click anywhere on the map to start the route.</div> : waypoints.map((point, index) => <article key={index}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE<input type="number" step="0.000001" value={point.latitude} onChange={event => updateWaypoint(index, { latitude: Number(event.target.value) })} required /></label><label>LONGITUDE<input type="number" step="0.000001" value={point.longitude} onChange={event => updateWaypoint(index, { longitude: Number(event.target.value) })} required /></label><label>ALTITUDE (M)<input type="number" min="0" max="500" value={point.altitude} onChange={event => updateWaypoint(index, { altitude: Number(event.target.value) })} required /></label><label>ACTION<select value={point.actionType} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{actions.map(action => <option key={action}>{action}</option>)}</select></label><button type="button" className="waypoint-remove" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></article>)}</div>
      </section>
      {error && <p className="form-error" role="alert">{error}</p>}
      <footer className="builder-footer"><p><b>{waypoints.length}</b> WAYPOINTS · Mission will be saved as a draft.</p><div><Link className="module-secondary module-link" href="/portal/missions">CANCEL</Link><button className="module-primary" disabled={saving || approvedAircraft.length === 0}>{saving ? "CREATING…" : "CREATE MISSION"}</button></div></footer>
    </form>
    <p className="mock-notice">MOCK API · Map tiles are provided by OpenFreeMap for development.</p>
  </>;
}
