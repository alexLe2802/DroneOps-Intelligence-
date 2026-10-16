"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { createMission, getAssignedUavs, getGeofences } from "@/lib/portal/client";
import type { AssignedUav, Geofence, MissionWaypoint, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

import { routeError } from "@/lib/maps/planning-validation";
import { useUnsavedChanges } from "@/lib/maps/use-unsaved-changes";

const actions: WaypointAction[] = ["TakeOff", "FlyThrough", "Hover", "CapturePhoto", "Land"];

export default function MissionForm() {
  const router = useRouter();
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [geofences, setGeofences] = useState<Geofence[]>([]);
  const [geofenceWarning, setGeofenceWarning] = useState("");
  const [loadError, setLoadError] = useState<unknown>(null);
  const [uavId, setUavId] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [waypoints, setWaypoints] = useState<MissionWaypoint[]>([]);
  const pending = useRef(false);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  useUnsavedChanges(!saved && Boolean(name || description || uavId || startTime || endTime || waypoints.length));
  function load() {
    setAircraft(null); setLoadError(null);
    void Promise.allSettled([getAssignedUavs(), getGeofences()]).then(([uavs, zones]) => {
      if (uavs.status === "rejected") { setLoadError(uavs.reason); return; }
      setAircraft(uavs.value);
      if (zones.status === "fulfilled") { setGeofences(zones.value); setGeofenceWarning(""); }
      else { setGeofences([]); setGeofenceWarning("Restricted zones could not be loaded. Verify the route against airspace data before submission."); }
    });
  }
  useEffect(() => {
    let active = true;
    void Promise.allSettled([getAssignedUavs(), getGeofences()]).then(([uavs, zones]) => {
      if (!active) return;
      if (uavs.status === "rejected") { setLoadError(uavs.reason); return; }
      setAircraft(uavs.value);
      if (zones.status === "fulfilled") setGeofences(zones.value);
      else setGeofenceWarning("Restricted zones could not be loaded. Verify the route against airspace data before submission.");
    });
    return () => { active = false; };
  }, []);
  function addWaypoint(longitude: number, latitude: number) {
    setWaypoints(current => [...current, { sequenceOrder: current.length + 1, latitude: Number(latitude.toFixed(6)), longitude: Number(longitude.toFixed(6)), altitude: 50, actionType: current.length === 0 ? "TakeOff" : "FlyThrough" }]);
  }
  function updateWaypoint(index: number, patch: Partial<MissionWaypoint>) {
    setWaypoints(current => current.map((point, currentIndex) => currentIndex === index ? { ...point, ...patch } : point));
  }
  function removeWaypoint(index: number) {
    setWaypoints(current => current.filter((_, currentIndex) => currentIndex !== index).map((point, sequenceOrder) => ({ ...point, sequenceOrder: sequenceOrder + 1 })));
  }
  function moveWaypoint(index: number, offset: number) {
    setWaypoints(current => {
      const next = [...current];
      [next[index], next[index + offset]] = [next[index + offset], next[index]];
      return next.map((point, i) => ({ ...point, sequenceOrder: i + 1 }));
    });
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); setError("");
    if (pending.current) return;
    if (!name.trim()) { setError("Enter a mission name."); return; }
    if (!aircraft?.some(item => item.id === uavId && item.status.toLowerCase() === "active")) { setError("Select an active UAV assigned to you."); return; }
    const invalidRoute = routeError(waypoints);
    if (invalidRoute) { setError(invalidRoute); return; }
    const start = new Date(startTime), end = new Date(endTime);
    if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start) { setError("Enter a valid schedule with the end time later than the start time."); return; }
    pending.current = true;
    setSaving(true);
    try {
      await createMission({ uavId, name: name.trim(), description: description.trim() || null, startTime: new Date(startTime).toISOString(), endTime: new Date(endTime).toISOString(), waypoints });
      setSaved(true);
      router.push("/portal/missions");
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to create the mission."); }
    finally { pending.current = false; setSaving(false); }
  }
  if (loadError) return <ModuleError error={loadError} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading mission resources" />;
  const approvedAircraft = aircraft.filter(item => item.status.toLowerCase() === "active");
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / FLIGHT PLANNING</p><h1>Add Flight Route</h1><p>Select an aircraft, define the mission and place waypoints on the live map.</p></div><Link className="module-secondary module-link" href="/portal/missions">← MY MISSIONS</Link></header>
    <form className="mission-builder mission-create-builder" onSubmit={submit}>
      <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: "contents" }}><legend className="sr-only">Mission and flight route</legend>
      <section className="mission-fields">
        <div className="builder-section-title"><span>01</span><div><h2>Mission information</h2><p>Define the aircraft and operating window.</p></div></div>
        <div className="mission-field-grid">
          <label>ASSIGNED UAV<select value={uavId} onChange={event => setUavId(event.target.value)} required><option value="">Select an approved UAV</option>{approvedAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
          <label>MISSION NAME<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={200} required placeholder="Transmission Line Audit" /></label>
          <label className="full-field">DESCRIPTION<textarea value={description} onChange={event => setDescription(event.target.value)} maxLength={2000} placeholder="Describe the objective and operating notes…" /></label>
          <label>START TIME<input type="datetime-local" value={startTime} onChange={event => setStartTime(event.target.value)} required /></label>
          <label>END TIME<input type="datetime-local" value={endTime} onChange={event => setEndTime(event.target.value)} required /></label>
        </div>
        {approvedAircraft.length === 0 && <p className="form-warning">No approved and available UAV is ready for mission planning.</p>}
      </section>
      <section className="route-builder">
        <div className="builder-section-title"><span>02</span><div><h2>Flight waypoints</h2><p>Click the map in flight order, then configure altitude and action.</p></div><button type="button" className="text-action" disabled={!waypoints.length} onClick={() => { if (window.confirm("Clear all waypoints?")) setWaypoints([]); }}>CLEAR ROUTE</button></div>
        {geofenceWarning && <p className="form-warning">{geofenceWarning}</p>}
        <MissionMap waypoints={waypoints} geofences={geofences} onAdd={addWaypoint} onUpdate={updateWaypoint} readOnly={saving} />
        <button type="button" className="module-secondary" onClick={() => addWaypoint(106.6297, 10.8231)}>ADD WAYPOINT MANUALLY</button>
        <div className="waypoint-list">{waypoints.length === 0 ? <div className="waypoint-empty">Click the map to create a waypoint. You can then drag its marker or type exact coordinates below.</div> : waypoints.map((point, index) => <article key={index}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE<input type="number" min="-90" max="90" step="0.000001" value={Number.isFinite(point.latitude) ? point.latitude : ""} onChange={event => updateWaypoint(index, { latitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>LONGITUDE<input type="number" min="-180" max="180" step="0.000001" value={Number.isFinite(point.longitude) ? point.longitude : ""} onChange={event => updateWaypoint(index, { longitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ALTITUDE (M)<input type="number" min="0" step="any" value={Number.isFinite(point.altitude) ? point.altitude : ""} onChange={event => updateWaypoint(index, { altitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ACTION<select value={point.actionType} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{actions.map(action => <option key={action}>{action}</option>)}</select></label><button type="button" className="text-action" aria-label={`Move waypoint ${index + 1} earlier`} disabled={index === 0} onClick={() => moveWaypoint(index, -1)}>↑</button><button type="button" className="text-action" aria-label={`Move waypoint ${index + 1} later`} disabled={index === waypoints.length - 1} onClick={() => moveWaypoint(index, 1)}>↓</button><button type="button" className="waypoint-remove" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></article>)}</div>
      </section>
      </fieldset>
      {error && <p className="form-error" role="alert">{error}</p>}
      <footer className="builder-footer"><p><b>{waypoints.length}</b> WAYPOINTS · Mission will be saved as a draft.</p><div><Link className="module-secondary module-link" href="/portal/missions">CANCEL</Link><button className="module-primary" disabled={saving || approvedAircraft.length === 0}>{saving ? "CREATING…" : "CREATE MISSION"}</button></div></footer>
    </form>
  </>;
}
