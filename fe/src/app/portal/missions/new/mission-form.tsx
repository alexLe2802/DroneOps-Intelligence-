"use client";

import Link from "next/link";
import { CalendarClock } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { createMission, getAssignedUavs, getGeofences } from "@/lib/portal/client";
import type { AssignedUav, Geofence, MissionWaypoint, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

import { routeError } from "@/lib/maps/planning-validation";
import { useUnsavedChanges } from "@/lib/maps/use-unsaved-changes";

const actions: WaypointAction[] = ["TakeOff", "FlyThrough", "Hover", "CapturePhoto", "Land"];

function localDateTimeMinimum() {
  const interval = 60_000;
  const now = new Date(Math.ceil(Date.now() / interval) * interval);
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function openDateTimePicker(input: HTMLInputElement) {
  try { input.showPicker(); }
  catch { input.focus(); }
}

export default function MissionForm() {
  const router = useRouter();
  const [aircraft, setAircraft] = useState<AssignedUav[] | null>(null);
  const [geofences, setGeofences] = useState<Geofence[]>([]);
  const [geofenceWarning, setGeofenceWarning] = useState("");
  const [loadError, setLoadError] = useState<unknown>(null);
  const [uavId, setUavId] = useState("");
  const [altitudeReference, setAltitudeReference] = useState<"AGL" | "MSL" | "Relative" | "">("");
  const operation = useRef<{ signature: string; id: string } | null>(null);
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
  function addWaypoint(longitude?: number, latitude?: number) {
    setWaypoints(current => {
      const last = current[current.length - 1];
      const nextLatitude = latitude ?? (last && Number.isFinite(last.latitude) ? last.latitude : 10.8231);
      const nextLongitude = longitude ?? (last && Number.isFinite(last.longitude) ? last.longitude : 106.6297);
      return [...current, {
        sequenceOrder: current.length + 1,
        latitude: Number(nextLatitude.toFixed(6)),
        longitude: Number(nextLongitude.toFixed(6)),
        altitude: 50,
        actionType: current.length === 0 ? "TakeOff" : "FlyThrough",
      }];
    });
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
    if (start.getTime() <= Date.now()) { setError("Flight start time must be in the future. Update the start date and time."); return; }
    pending.current = true;
    setSaving(true);
    try {
      const payload = { altitudeReference: altitudeReference || null, uavId, name: name.trim(), description: description.trim() || null, startTime: new Date(startTime).toISOString(), endTime: new Date(endTime).toISOString(), waypoints };
      const signature = JSON.stringify(payload);
      if (operation.current?.signature !== signature) operation.current = { signature, id: crypto.randomUUID() };
      const created = await createMission({ ...payload, operationId: operation.current.id });
      setSaved(true);
      router.push(`/portal/missions/${created.id}`);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to create the mission."); }
    finally { pending.current = false; setSaving(false); }
  }
  if (loadError) return <ModuleError error={loadError} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading mission resources" />;
  const activeAircraft = aircraft.filter(item => item.status.toLowerCase() === "active");
  const earliestStart = localDateTimeMinimum();
  const earliestEnd = startTime || earliestStart;
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / FLIGHT PLANNING</p><h1>Add Flight Route</h1><p>Select an aircraft, define the mission and place waypoints on the live map.</p></div><Link className="module-secondary module-link" href="/portal/missions">← MY MISSIONS</Link></header>
    <form className="mission-builder mission-create-builder" onSubmit={submit} onInvalidCapture={event => {
      const field = event.target;
      if (!(field instanceof HTMLInputElement || field instanceof HTMLSelectElement || field instanceof HTMLTextAreaElement)) return;
      // Native validation runs before onSubmit. Show the first blocked field in the form too.
      if (event.currentTarget.querySelector("input:invalid, select:invalid, textarea:invalid") !== field) return;
      const label = field.labels?.[0]?.textContent?.trim() || "Field";
      const message = field.id === "mission-start-time" && field.validity.rangeUnderflow
        ? "Flight start time must be in the future. Update the start date and time."
        : field.id === "mission-end-time" && field.validity.rangeUnderflow
          ? "End time must be later than the start time."
          : field.validationMessage;
      setError(`${label}: ${message}`);
    }}>
      <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: "contents" }}><legend className="sr-only">Mission and flight route</legend>
      <section className="mission-fields">
        <div className="builder-section-title"><span>01</span><div><h2>Mission information</h2><p>Define the aircraft and operating window.</p></div></div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="mission-field-grid">
          <label>ASSIGNED UAV<select value={uavId} onChange={event => setUavId(event.target.value)} required><option value="">Select an active UAV</option>{activeAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
          <label>MISSION NAME<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={200} required placeholder="Transmission Line Audit" /></label>
          <label className="full-field">DESCRIPTION<textarea value={description} onChange={event => setDescription(event.target.value)} maxLength={2000} placeholder="Describe the objective and operating notes…" /></label>
          <div className="schedule-field"><label htmlFor="mission-start-time">START DATE &amp; TIME</label><div className="schedule-control"><CalendarClock size={17} aria-hidden="true" /><input id="mission-start-time" type="datetime-local" value={startTime} min={earliestStart} step={60} onClick={event => openDateTimePicker(event.currentTarget)} onChange={event => setStartTime(event.target.value)} aria-describedby="mission-start-hint" required /></div><small id="mission-start-hint">LOCAL TIME / START MUST BE IN THE FUTURE</small></div>
          <div className="schedule-field"><label htmlFor="mission-end-time">END DATE &amp; TIME</label><div className="schedule-control"><CalendarClock size={17} aria-hidden="true" /><input id="mission-end-time" type="datetime-local" value={endTime} min={earliestEnd} step={60} onClick={event => openDateTimePicker(event.currentTarget)} onChange={event => setEndTime(event.target.value)} aria-describedby="mission-end-hint" required /></div><small id="mission-end-hint">MUST BE LATER THAN START</small></div>
        </div>
        {activeAircraft.length === 0 && <p className="form-warning">No active UAV is ready for mission planning.</p>}
      </section>
      <section className="route-builder">
        <div className="builder-section-title"><span>02</span><div><h2>Flight waypoints</h2><p>Click the map in flight order, then configure altitude and action.</p></div><button type="button" className="text-action" disabled={!waypoints.length} onClick={() => { if (window.confirm("Clear all waypoints?")) setWaypoints([]); }}>CLEAR ROUTE</button></div>
        {geofenceWarning && <p className="form-warning">{geofenceWarning}</p>}
        <MissionMap waypoints={waypoints} geofences={geofences} onAdd={addWaypoint} onUpdate={updateWaypoint} readOnly={saving} />
        <button type="button" className="module-secondary" onClick={() => addWaypoint()}>ADD WAYPOINT MANUALLY</button>
        <div className="waypoint-list">{waypoints.length === 0 ? <div className="waypoint-empty">Click the map to create a waypoint. You can then drag its marker or type exact coordinates below.</div> : waypoints.map((point, index) => <article key={index}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE<input type="number" min="-90" max="90" step="0.000001" value={Number.isFinite(point.latitude) ? point.latitude : ""} onChange={event => updateWaypoint(index, { latitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>LONGITUDE<input type="number" min="-180" max="180" step="0.000001" value={Number.isFinite(point.longitude) ? point.longitude : ""} onChange={event => updateWaypoint(index, { longitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ALTITUDE (M)<input type="number" min="0" step="any" value={point.altitude != null && Number.isFinite(point.altitude) ? point.altitude : ""} onChange={event => updateWaypoint(index, { altitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ACTION<select value={point.actionType} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{actions.map(action => <option key={action}>{action}</option>)}</select></label><button type="button" className="text-action" aria-label={`Move waypoint ${index + 1} earlier`} disabled={index === 0} onClick={() => moveWaypoint(index, -1)}>↑</button><button type="button" className="text-action" aria-label={`Move waypoint ${index + 1} later`} disabled={index === waypoints.length - 1} onClick={() => moveWaypoint(index, 1)}>↓</button><button type="button" className="waypoint-remove" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></article>)}</div>
      </section>
      </fieldset>
      <label>Altitude reference<select value={altitudeReference} onChange={event => setAltitudeReference(event.target.value as typeof altitudeReference)}><option value="">Not specified</option><option value="AGL">AGL — above ground</option><option value="MSL">MSL — mean sea level</option><option value="Relative">Relative — takeoff reference</option></select></label>
      <footer className="builder-footer"><p><b>{waypoints.length}</b> WAYPOINTS · Mandatory checks must pass before the mission is sent for manager review.</p><div><Link className="module-secondary module-link" href="/portal/missions">CANCEL</Link><button className="module-primary" disabled={saving || activeAircraft.length === 0}>{saving ? "VALIDATING…" : "CREATE & VALIDATE MISSION"}</button></div></footer>
    </form>
  </>;
}
