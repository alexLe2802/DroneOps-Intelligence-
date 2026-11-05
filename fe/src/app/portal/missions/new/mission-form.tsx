"use client";

import { toVietnamDateTimeInput, vietnamInputToUtcIso } from "@/lib/portal/vietnam-time";

import Link from "next/link";
import { CalendarClock } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { cancelHighRiskMission, confirmHighRiskMission, createMission, getAssignedUavs, getGeofences } from "@/lib/portal/client";
import type { AssignedUav, Geofence, MissionDetail, MissionWaypoint, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";
import HighRiskConfirmation from "./high-risk-confirmation";

import { appendLandingWaypoint, normalizeWaypointActions } from "@/lib/maps/mission-waypoints";
import { missionSpeedError, routeError } from "@/lib/maps/planning-validation";
import { useUnsavedChanges } from "@/lib/maps/use-unsaved-changes";

import WaypointNavigationFields, { FirmwareDefaultsNotice } from "../waypoint-navigation-fields";
import AutomaticFlightFields, { defaultAutomaticLimits } from "../automatic-flight-fields";
import FlightModeFields, { type FlightMode } from "../flight-mode-fields";
import { automaticFlightError } from "@/lib/portal/automatic-flight-limits";

const intermediateActions: WaypointAction[] = ["FlyThrough", "Hover"];

function localDateTimeMinimum() {
  const interval = 60_000;
  const now = new Date(Math.ceil(Date.now() / interval) * interval);
  return toVietnamDateTimeInput(now);
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
  const [flightMode, setFlightMode] = useState<FlightMode | null>(null);
  const automaticFlightRequested = flightMode === "automatic";
  const [description, setDescription] = useState("");
  const [averageSpeed, setAverageSpeed] = useState("");
  const [maxSpeed, setMaxSpeed] = useState("");
  const [automaticFlightLimits, setAutomaticFlightLimits] = useState(defaultAutomaticLimits);
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [waypoints, setWaypoints] = useState<MissionWaypoint[]>([]);
  const pending = useRef(false);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [highRiskMission, setHighRiskMission] = useState<MissionDetail | null>(null);
  const [riskBusy, setRiskBusy] = useState<"confirm" | "cancel" | null>(null);
  const [riskError, setRiskError] = useState("");
  useUnsavedChanges(!saved && Boolean(flightMode || name || description || averageSpeed || maxSpeed || uavId || startTime || endTime || waypoints.length));
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
      return appendLandingWaypoint(current, {
        latitude: Number(nextLatitude.toFixed(6)),
        longitude: Number(nextLongitude.toFixed(6)),
        altitude: automaticFlightRequested ? Math.min(20, automaticFlightLimits.maxAltitudeMeters) : 50,
      });
    });
  }
  function updateWaypoint(index: number, patch: Partial<MissionWaypoint>) {
    setWaypoints(current => normalizeWaypointActions(current.map((point, currentIndex) => currentIndex === index ? { ...point, ...patch } : point)));
  }
  function removeWaypoint(index: number) {
    setWaypoints(current => normalizeWaypointActions(current.filter((_, currentIndex) => currentIndex !== index)));
  }
  function moveWaypoint(index: number, offset: number) {
    setWaypoints(current => {
      const next = [...current];
      [next[index], next[index + offset]] = [next[index + offset], next[index]];
      return normalizeWaypointActions(next);
    });
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); setError("");
    if (pending.current) return;
    if (flightMode === null) { setError("Choose Manual operation or Automatic mission before submitting."); return; }
    if (!name.trim()) { setError("Enter a mission name."); return; }
    if (!aircraft?.some(item => item.id === uavId && item.status.toLowerCase() === "active")) { setError("Select an active UAV assigned to you."); return; }
    const normalizedWaypoints = normalizeWaypointActions(waypoints);
    const invalidRoute = routeError(normalizedWaypoints);
    if (invalidRoute) { setError(invalidRoute); return; }
    const start = new Date(`${startTime}+07:00`), end = new Date(`${endTime}+07:00`);
    if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start) { setError("Enter a valid schedule with the end time later than the start time."); return; }
    if (start.getTime() <= Date.now()) { setError("Flight start time must be in the future. Update the start date and time."); return; }
    const averageSpeedMetersPerSecond = Number(automaticFlightRequested && !averageSpeed.trim() ? "3" : averageSpeed);
    const maxSpeedMetersPerSecond = Number(automaticFlightRequested && !maxSpeed.trim() ? "3" : maxSpeed);
    const invalidSpeed = missionSpeedError(averageSpeedMetersPerSecond, maxSpeedMetersPerSecond);
    if (invalidSpeed) { setError(invalidSpeed); return; }
    const invalidAutomatic = automaticFlightRequested ? automaticFlightError(altitudeReference, averageSpeedMetersPerSecond, maxSpeedMetersPerSecond, automaticFlightLimits, normalizedWaypoints) : null;
    if (invalidAutomatic) { setError(invalidAutomatic); return; }
    pending.current = true;
    setSaving(true);
    try {
      const payload = { automaticFlightRequested, automaticFlightLimits: automaticFlightRequested ? automaticFlightLimits : null, altitudeReference: altitudeReference || null, uavId, name: name.trim(), description: description.trim() || null, averageSpeedMetersPerSecond, maxSpeedMetersPerSecond, startTime: vietnamInputToUtcIso(startTime), endTime: vietnamInputToUtcIso(endTime), waypoints: normalizedWaypoints };
      const signature = JSON.stringify(payload);
      if (operation.current?.signature !== signature) operation.current = { signature, id: crypto.randomUUID() };
      const created = await createMission({ ...payload, operationId: operation.current.id });
      setSaved(true);
      if (created.status.trim().toLowerCase() === "pending confirm") {
        setHighRiskMission(created);
        return;
      }
      router.push(`/portal/missions/${created.id}`);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to create the mission."); }
    finally { pending.current = false; setSaving(false); }
  }
  async function confirmHighRisk() {
    if (!highRiskMission || riskBusy) return;
    setRiskBusy("confirm"); setRiskError("");
    try {
      await confirmHighRiskMission(highRiskMission.id);
      router.replace(`/portal/missions/${highRiskMission.id}`);
      router.refresh();
    } catch (reason) {
      setRiskError(reason instanceof Error ? reason.message : "Unable to confirm the high-risk mission.");
      setRiskBusy(null);
    }
  }
  async function cancelHighRisk() {
    if (!highRiskMission || riskBusy) return;
    setRiskBusy("cancel"); setRiskError("");
    try {
      await cancelHighRiskMission(highRiskMission.id);
      operation.current = null;
      setHighRiskMission(null);
      setSaved(false);
      setRiskBusy(null);
    } catch (reason) {
      setRiskError(reason instanceof Error ? reason.message : "Unable to cancel the high-risk mission.");
      setRiskBusy(null);
    }
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
      <FlightModeFields value={flightMode} onChange={mode => {
        setFlightMode(mode);
        if (mode === "automatic") { setAverageSpeed("3"); setMaxSpeed("3"); setAltitudeReference("Relative"); }
      }} />
      <section className="mission-fields">
        <div className="builder-section-title"><span>01</span><div><h2>Mission information</h2><p>Define the aircraft and operating window.</p></div></div>
        <div className="mission-field-grid">
          <label>ASSIGNED UAV · required<select value={uavId} onChange={event => setUavId(event.target.value)} required><option value="">Select an active UAV</option>{activeAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
          <label>MISSION NAME · required<input value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={200} required placeholder="Transmission Line Audit" /></label>
          <label className="full-field">DESCRIPTION · optional<textarea value={description} onChange={event => setDescription(event.target.value)} maxLength={2000} placeholder="Describe the objective and operating notes…" /></label>
          <label>CRUISE SPEED (M/S) · {automaticFlightRequested ? "optional override" : "required"}<input id="mission-average-speed" type="number" min={automaticFlightRequested ? "0.5" : "0.1"} max={automaticFlightRequested ? 5 : undefined} step="0.01" value={averageSpeed} onChange={event => setAverageSpeed(event.target.value)} placeholder={automaticFlightRequested ? "Optional — planning default 3" : "10.0"} required={!automaticFlightRequested} /><small>{averageSpeed ? `${(Number(averageSpeed) * 3.6).toFixed(2)} km/h` : ""}</small></label>
          <label>SPEED CEILING (M/S) · {automaticFlightRequested ? "optional override" : "required"}<input id="mission-max-speed" type="number" min="0.1" max={automaticFlightRequested ? 5 : undefined} step="0.01" value={maxSpeed} onChange={event => setMaxSpeed(event.target.value)} placeholder={automaticFlightRequested ? "Optional — planning ceiling 3" : "15.0"} required={!automaticFlightRequested} /><small>{maxSpeed ? `${(Number(maxSpeed) * 3.6).toFixed(2)} km/h` : ""}</small></label>
          <div className="schedule-field"><label htmlFor="mission-start-time">START DATE &amp; TIME · required</label><div className="schedule-control"><CalendarClock size={17} aria-hidden="true" /><input id="mission-start-time" type="datetime-local" value={startTime} min={earliestStart} step={60} onClick={event => openDateTimePicker(event.currentTarget)} onChange={event => setStartTime(event.target.value)} aria-describedby="mission-start-hint" required /></div><small id="mission-start-hint">VIETNAM GMT+7 / START MUST BE IN THE FUTURE</small></div>
          <div className="schedule-field"><label htmlFor="mission-end-time">END DATE &amp; TIME · required</label><div className="schedule-control"><CalendarClock size={17} aria-hidden="true" /><input id="mission-end-time" type="datetime-local" value={endTime} min={earliestEnd} step={60} onClick={event => openDateTimePicker(event.currentTarget)} onChange={event => setEndTime(event.target.value)} aria-describedby="mission-end-hint" required /></div><small id="mission-end-hint">VIETNAM GMT+7 / MUST BE LATER THAN START</small></div>
        </div>
        {activeAircraft.length === 0 && <p className="form-warning">No active UAV is ready for mission planning.</p>}
      </section>
      {automaticFlightRequested && <button type="button" className="module-secondary mission-speed-preset" onClick={() => { setAverageSpeed("2.77"); setMaxSpeed("2.77"); }}>USE ≤10 KM/H TARGET (2.77 M/S)</button>}
      {automaticFlightRequested && <FirmwareDefaultsNotice snapshot={aircraft.find(uav => uav.id === uavId)?.firmwareNavigationDefaults} />}
      {automaticFlightRequested && <AutomaticFlightFields value={automaticFlightLimits} onChange={setAutomaticFlightLimits} />}
      <section className="route-builder">
        <div className="builder-section-title"><span>02</span><div><h2>Flight waypoints</h2><p>Click the map in flight order, then configure altitude and action.</p></div><button type="button" className="text-action" disabled={!waypoints.length} onClick={() => { if (window.confirm("Clear all waypoints?")) setWaypoints([]); }}>CLEAR ROUTE</button></div>
        {geofenceWarning && <p className="form-warning">{geofenceWarning}</p>}
        <MissionMap waypoints={waypoints} geofences={geofences} onAdd={addWaypoint} onUpdate={updateWaypoint} readOnly={saving} />
        <button type="button" className="module-secondary mission-add-waypoint" onClick={() => addWaypoint()}>ADD WAYPOINT MANUALLY</button>
        <div className="waypoint-list">{waypoints.length === 0 ? <div className="waypoint-empty">Click the map to create a waypoint. You can then drag its marker or type exact coordinates below.</div> : waypoints.map((point, index) => {
          const endpointAction: WaypointAction | null = index === 0 ? "TakeOff" : index === waypoints.length - 1 ? "Land" : null;
          return <article key={index}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE · required<input type="number" min="-90" max="90" step="0.000001" value={Number.isFinite(point.latitude) ? point.latitude : ""} onChange={event => updateWaypoint(index, { latitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>LONGITUDE · required<input type="number" min="-180" max="180" step="0.000001" value={Number.isFinite(point.longitude) ? point.longitude : ""} onChange={event => updateWaypoint(index, { longitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ALTITUDE (&gt;10 M) · required<input type="number" min="10.1" max={automaticFlightRequested ? automaticFlightLimits.maxAltitudeMeters : undefined} step="any" value={point.altitude != null && Number.isFinite(point.altitude) ? point.altitude : ""} onChange={event => updateWaypoint(index, { altitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ACTION · required<select value={point.actionType} disabled={endpointAction !== null} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{endpointAction ? <option>{endpointAction}</option> : intermediateActions.map(action => <option key={action}>{action}</option>)}</select></label><div className="waypoint-order-actions"><button type="button" className="text-action" aria-label={`Move waypoint ${index + 1} earlier`} disabled={index === 0} onClick={() => moveWaypoint(index, -1)}>↑</button><button type="button" className="text-action" aria-label={`Move waypoint ${index + 1} later`} disabled={index === waypoints.length - 1} onClick={() => moveWaypoint(index, 1)}>↓</button><button type="button" className="waypoint-remove" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></div>{automaticFlightRequested && <WaypointNavigationFields point={point} maximumSpeed={Number(maxSpeed) || 3} onChange={navigation => updateWaypoint(index, { navigation })} />}</article>;
        })}</div>
      </section>
      </fieldset>
      <label className="mission-altitude-reference">Altitude reference<select value={altitudeReference} onChange={event => setAltitudeReference(event.target.value as typeof altitudeReference)}><option value="">Not specified</option><option disabled={automaticFlightRequested} value="AGL">AGL — above ground</option><option disabled={automaticFlightRequested} value="MSL">MSL — mean sea level</option><option value="Relative">Relative — takeoff reference</option></select></label>
      {error && <p className="form-error" role="alert" aria-live="assertive">{error}</p>}
      <footer className="builder-footer"><p><b>{waypoints.length}</b> WAYPOINTS · Mandatory checks must pass before the mission is sent for manager review.</p><div><Link className="module-secondary module-link" href="/portal/missions">CANCEL</Link><button className="module-primary" disabled={saving || activeAircraft.length === 0 || flightMode === null}>{saving ? "VALIDATING…" : "CREATE & VALIDATE MISSION"}</button></div></footer>
    </form>
    {highRiskMission && <HighRiskConfirmation mission={highRiskMission} busy={riskBusy} error={riskError} onConfirm={() => void confirmHighRisk()} onCancel={() => void cancelHighRisk()} />}
  </>;
}
