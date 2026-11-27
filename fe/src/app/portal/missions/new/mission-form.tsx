"use client";

import { toVietnamDateTimeInput, vietnamInputToUtcIso } from "@/lib/portal/vietnam-time";

import Link from "next/link";
import { AlertCircle, AlertTriangle, CalendarClock } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { createMission, getAssignedUavs, getGeofences } from "@/lib/portal/client";
import type { AssignedUav, Geofence, MissionSuggestion, MissionWaypoint, MissionFence, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../module-state";
import MissionMap from "../mission-map";

import { appendLandingWaypoint, normalizeWaypointActions, waypointActionLabel } from "@/lib/maps/mission-waypoints";
import { withPlannedHome } from "@/lib/maps/planned-home";
import { missionSpeedError, routeError } from "@/lib/maps/planning-validation";
import { useUnsavedChanges } from "@/lib/maps/use-unsaved-changes";

import WaypointNavigationFields, { FirmwareDefaultsNotice } from "../waypoint-navigation-fields";
import AutomaticFlightFields, { defaultAutomaticLimits } from "../automatic-flight-fields";
import FlightModeFields, { type FlightMode } from "../flight-mode-fields";
import { automaticFlightError } from "@/lib/portal/automatic-flight-limits";
import MissionFenceFields, { useFenceDrawing } from "../mission-fence-fields";
import { missionFencesError, withMissionFences } from "@/lib/maps/mission-fences";
import DraftAnalysisPanel from "./draft-analysis-panel";
import MissionSuggestionPanel from "./mission-suggestion-panel";

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

export function describeInvalidMissionField(field: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement): string {
  if (field instanceof HTMLInputElement && field.name === "flight-mode") {
    return "Flight mode: Choose Manual operation or Automatic mission before saving.";
  }
  const label = field.dataset.fieldLabel
    || field.getAttribute("aria-label")?.split("·")[0]?.trim()
    || field.labels?.[0]?.childNodes[0]?.textContent?.split("·")[0]?.trim()
    || "Field";
  if (field.id === "mission-start-time" && field.validity.rangeUnderflow) {
    return "Start date & time: Flight start time must be in the future (GMT+7).";
  }
  if (field.id === "mission-end-time" && field.validity.rangeUnderflow) {
    return "Planned end date & time: End time must be later than the start time (GMT+7).";
  }
  if (field.validity.valueMissing) {
    return field instanceof HTMLSelectElement
      ? `${label}: Select an option to continue.`
      : `${label}: This field is required.`;
  }
  if (field instanceof HTMLInputElement && (field.validity.rangeUnderflow || field.validity.rangeOverflow)) {
    const min = field.min !== "" ? field.min : null;
    const max = field.max !== "" ? field.max : null;
    if (min !== null && max !== null) return `${label}: Enter a value between ${min} and ${max}.`;
    if (min !== null) return `${label}: Enter a value of at least ${min}.`;
    if (max !== null) return `${label}: Enter a value of at most ${max}.`;
  }
  if ((field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement) && field.validity.tooShort) {
    return `${label}: Enter at least ${field.minLength} characters.`;
  }
  if (field.validity.badInput) {
    return `${label}: Enter a valid number.`;
  }
  return `${label}: ${field.validationMessage}`;
}

export function syncFieldAriaInvalid(target: EventTarget | null, fromInvalidEvent = false) {
  if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement || target instanceof HTMLTextAreaElement)) return;
  if (fromInvalidEvent) {
    target.setAttribute("aria-invalid", "true");
    return;
  }
  if (target.validity.valid) {
    target.setAttribute("aria-invalid", "false");
    return;
  }
  try {
    target.setAttribute("aria-invalid", target.matches(":user-invalid") ? "true" : "false");
  } catch {
    target.setAttribute("aria-invalid", "true");
  }
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
  const [payloadKg, setPayloadKg] = useState("");
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [missionFences, setMissionFences] = useState<MissionFence[]>([]);
  const fenceDrawing = useFenceDrawing(missionFences, setMissionFences);
  const [waypoints, setWaypoints] = useState<MissionWaypoint[]>([]);
  const pending = useRef(false);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const errorRef = useRef<HTMLDivElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (error) { errorRef.current?.focus({ preventScroll: true }); errorRef.current?.scrollIntoView({ block: "center", behavior: "instant" }); }
  }, [error]);
  useEffect(() => {
    formRef.current?.querySelectorAll("input, select, textarea").forEach(el => {
      if (!el.hasAttribute("aria-invalid")) el.setAttribute("aria-invalid", "false");
    });
  }, [aircraft, waypoints.length, missionFences.length, automaticFlightRequested]);
  useUnsavedChanges(!saved && Boolean(missionFences.length || fenceDrawing.draft.length || flightMode || name || description || averageSpeed || maxSpeed || uavId || startTime || endTime || waypoints.length));
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
    if (latitude != null && longitude != null && fenceDrawing.point(longitude, latitude)) return;
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
  function applySuggestion(suggestion: MissionSuggestion) {
    const hasDraft = Boolean(flightMode || name || description || averageSpeed || maxSpeed || altitudeReference || waypoints.length);
    if (hasDraft && !window.confirm("Replace the current mission details with this suggestion? Your UAV, schedule and waypoints will be kept.")) return;

    if (suggestion.missionName.trim()) setName(suggestion.missionName.trim());
    if (suggestion.missionPurpose.trim()) setDescription(suggestion.missionPurpose.trim());
    if (suggestion.automaticFlightRequested != null) setFlightMode(suggestion.automaticFlightRequested ? "automatic" : "manual");
    if (["AGL", "MSL", "Relative"].includes(suggestion.altitudeReference)) {
      setAltitudeReference(suggestion.altitudeReference as "AGL" | "MSL" | "Relative");
    } else if (suggestion.automaticFlightRequested) {
      setAltitudeReference("Relative");
    }
    if (Number.isFinite(suggestion.averageSpeedMetersPerSecond) && Number(suggestion.averageSpeedMetersPerSecond) > 0) {
      setAverageSpeed(String(suggestion.averageSpeedMetersPerSecond));
    }
    if (Number.isFinite(suggestion.maxSpeedMetersPerSecond) && Number(suggestion.maxSpeedMetersPerSecond) > 0) {
      setMaxSpeed(String(suggestion.maxSpeedMetersPerSecond));
    }
    setError("");
    window.setTimeout(() => document.getElementById("mission-information")?.scrollIntoView({ behavior: "smooth", block: "start" }), 0);
  }
  async function submit(event: FormEvent) {
    event.preventDefault(); setError("");
    if (pending.current) return;
    if (flightMode === null) { setError("Choose Manual operation or Automatic mission before submitting."); return; }
    if (!name.trim()) { setError("Enter a mission name."); return; }
    if (!aircraft?.some(item => item.id === uavId && item.status.toLowerCase() === "active")) { setError("Select an active UAV assigned to you."); return; }
    const fenceError = missionFencesError(missionFences);
    if (fenceDrawing.drawing || fenceError) { setError(fenceError || "Finish or cancel fence drawing before saving."); return; }
    const normalizedWaypoints = withPlannedHome(withMissionFences(normalizeWaypointActions(waypoints), missionFences), null);
    const invalidRoute = routeError(normalizedWaypoints);
    if (invalidRoute) { setError(invalidRoute); return; }
    const start = new Date(`${startTime}+07:00`), end = new Date(`${endTime}+07:00`);
    if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start) { setError("Enter a valid schedule with the end time later than the start time."); return; }
    if (start.getTime() <= Date.now()) { setError("Flight start time must be in the future. Update the start date and time."); return; }
    const averageSpeedMetersPerSecond = Number(automaticFlightRequested && !averageSpeed.trim() ? "11.2" : averageSpeed);
    const maxSpeedMetersPerSecond = Number(automaticFlightRequested && !maxSpeed.trim() ? "15" : maxSpeed);
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
      router.push(`/portal/missions/${created.id}`);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Unable to create the mission."); }
    finally { pending.current = false; setSaving(false); }
  }
  if (loadError) return <ModuleError error={loadError} retry={load} />;
  if (!aircraft) return <ModuleLoading label="Loading mission resources" />;
  const activeAircraft = aircraft.filter(item => item.status.toLowerCase() === "active");
  const earliestStart = localDateTimeMinimum();
  const earliestEnd = startTime || earliestStart;
  const start = startTime ? Date.parse(`${startTime}+07:00`) : NaN;
  const end = endTime ? Date.parse(`${endTime}+07:00`) : NaN;
  const missing = [
    !activeAircraft.some(item => item.id === uavId) && "Select an active assigned UAV.",
    name.trim().length < 2 && "Enter a mission name of at least two characters.",
    !(start >= Date.parse(`${earliestStart}+07:00`)) && "Choose a future start date and time.",
    endTime && !(end > start) && "Choose an end time after the start or leave it blank for an estimate.",
    payloadKg && Number(payloadKg) < 0 && "Enter a non-negative payload or leave it blank.",
    routeError(waypoints),
  ].filter((message): message is string => Boolean(message));
  const analysisDraft = missing.length || !automaticFlightRequested ? null : {
    uavId, name: name.trim(), startTime: vietnamInputToUtcIso(startTime), endTime: endTime ? vietnamInputToUtcIso(endTime) : null,
    waypoints, automatic: flightMode === null ? null : automaticFlightRequested, limits: automaticFlightRequested ? automaticFlightLimits : null,
    altitudeReference: altitudeReference || null, cruise: averageSpeed ? Number(averageSpeed) : null,
    ceiling: maxSpeed ? Number(maxSpeed) : null, payloadKg: payloadKg ? Number(payloadKg) : null,
  };
  // Speed, ceiling, end time and limits are excluded so applying or editing them keeps the AI result on screen.
  const analysisContextKey = JSON.stringify({
    uavId, name: name.trim(), startTime, waypoints, automatic: flightMode === null ? null : automaticFlightRequested,
    altitudeReference: altitudeReference || null, payloadKg: payloadKg ? Number(payloadKg) : null,
  });
  return <>
    <header className="module-heading"><div><p className="module-kicker">PILOT / FLIGHT PLANNING</p><h1>Add Flight Route</h1><p>Select an aircraft, define the mission and place waypoints on the live map.</p></div><Link className="module-secondary module-link" href="/portal/missions">← MY MISSIONS</Link></header>
    <MissionSuggestionPanel disabled={saving} onApply={applySuggestion} />
    <form ref={formRef} className="mission-builder mission-create-builder" onSubmit={submit} onBlurCapture={event => syncFieldAriaInvalid(event.target)} onChangeCapture={event => { syncFieldAriaInvalid(event.target); if (error) setError(""); }} onInputCapture={event => { syncFieldAriaInvalid(event.target); if (error) setError(""); }} onInvalidCapture={event => {
      const field = event.target;
      if (!(field instanceof HTMLInputElement || field instanceof HTMLSelectElement || field instanceof HTMLTextAreaElement)) return;
      syncFieldAriaInvalid(field, true);
      // Native validation runs before onSubmit. Show the first blocked field in the form too.
      if (event.currentTarget.querySelector("input:invalid, select:invalid, textarea:invalid") !== field) return;
      setError(describeInvalidMissionField(field));
    }}>
      {error && <div ref={errorRef} className="mission-error-summary" role="alert" tabIndex={-1}><AlertCircle size={22} aria-hidden="true" /><div className="mission-error-copy"><strong>Mission could not be saved</strong><p>{error}</p></div><button type="button" className="mission-error-dismiss" aria-label="Dismiss error message" onClick={() => setError("")}>×</button></div>}
      <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: "contents" }}><legend className="sr-only">Mission and flight route</legend>
        <FlightModeFields value={flightMode} onChange={mode => {
          setFlightMode(mode);
          setError("");
          if (mode === "automatic") { setAverageSpeed("11.2"); setMaxSpeed("15"); setAltitudeReference("Relative"); }
        }} />
        <section className="mission-fields" id="mission-information">
          <div className="builder-section-title"><span>01</span><div><h2>Mission information</h2><p>Define the aircraft and operating window.</p></div></div>
          <div className="mission-field-grid">
            <label htmlFor="mission-uav"><span className="field-heading">ASSIGNED UAV <em>Required</em></span><select id="mission-uav" name="uavId" data-field-label="Assigned UAV" value={uavId} onChange={event => { setUavId(event.target.value); setError(""); }} required><option value="">Select an active UAV</option>{activeAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
            <label htmlFor="mission-name"><span className="field-heading">MISSION NAME <em>Required</em></span><input id="mission-name" name="name" data-field-label="Mission name" value={name} onChange={event => setName(event.target.value)} minLength={2} maxLength={200} required placeholder="Transmission Line Audit" /></label>
            <label htmlFor="mission-description" className="full-field"><span className="field-heading">DESCRIPTION <em className="optional">Optional</em></span><textarea id="mission-description" name="description" data-field-label="Description" value={description} onChange={event => setDescription(event.target.value)} maxLength={2000} placeholder="Describe the objective and operating notes…" /></label>
            <label htmlFor="mission-average-speed"><span className="field-heading">CRUISE SPEED (M/S) <em className={automaticFlightRequested ? "optional" : undefined}>{automaticFlightRequested ? "Optional override" : "Required"}</em></span><input id="mission-average-speed" name="averageSpeed" data-field-label="Cruise speed (m/s)" type="number" min={automaticFlightRequested ? "0.5" : "0.1"} max={automaticFlightRequested ? 15 : undefined} step="0.01" value={averageSpeed} onChange={event => setAverageSpeed(event.target.value)} placeholder={automaticFlightRequested ? "Optional — flight default 11.2" : "10.0"} required={!automaticFlightRequested} />{averageSpeed && Number.isFinite(Number(averageSpeed)) ? <small>{(Number(averageSpeed) * 3.6).toFixed(2)} km/h</small> : <small>Target horizontal flight speed</small>}</label>
            <label htmlFor="mission-max-speed"><span className="field-heading">SPEED CEILING (M/S) <em className={automaticFlightRequested ? "optional" : undefined}>{automaticFlightRequested ? "Optional override" : "Required"}</em></span><input id="mission-max-speed" name="maxSpeed" data-field-label="Speed ceiling (m/s)" type="number" min="0.1" max={automaticFlightRequested ? 15 : undefined} step="0.01" value={maxSpeed} onChange={event => setMaxSpeed(event.target.value)} placeholder={automaticFlightRequested ? "Optional — speed ceiling 15" : "15.0"} required={!automaticFlightRequested} />{maxSpeed && Number.isFinite(Number(maxSpeed)) ? <small>{(Number(maxSpeed) * 3.6).toFixed(2)} km/h</small> : <small>Must be greater than or equal to cruise speed</small>}</label>
            <div className="schedule-field"><label htmlFor="mission-start-time"><span className="field-heading">START DATE &amp; TIME <em>Required</em></span></label><div className="schedule-control"><CalendarClock size={17} aria-hidden="true" /><input id="mission-start-time" name="startTime" data-field-label="Start date & time" type="datetime-local" value={startTime} min={earliestStart} step={60} onClick={event => openDateTimePicker(event.currentTarget)} onChange={event => { setStartTime(event.target.value); setError(""); }} aria-describedby="mission-start-hint" required /></div><small id="mission-start-hint">Vietnam GMT+7 · Start must be in the future</small></div>
            <div className="schedule-field"><label htmlFor="mission-end-time"><span className="field-heading">PLANNED END DATE &amp; TIME <em>Required</em></span></label><div className="schedule-control"><CalendarClock size={17} aria-hidden="true" /><input id="mission-end-time" name="endTime" data-field-label="Planned end date & time" type="datetime-local" value={endTime} min={earliestEnd} step={60} onClick={event => openDateTimePicker(event.currentTarget)} onChange={event => { setEndTime(event.target.value); setError(""); }} aria-describedby="mission-end-hint" required /></div><small id="mission-end-hint">Vietnam GMT+7 · Booking limit; actual end is recorded after landing and disarm</small></div>
          </div>
          {activeAircraft.length === 0 && <p className="form-warning" role="status"><AlertTriangle size={17} aria-hidden="true" /><span>No active UAV is assigned to your pilot profile yet. Register and activate an aircraft before saving a mission.</span></p>}
        </section>
      {automaticFlightRequested && <FirmwareDefaultsNotice snapshot={aircraft.find(uav => uav.id === uavId)?.firmwareNavigationDefaults} />}
      {automaticFlightRequested && <AutomaticFlightFields value={automaticFlightLimits} onChange={setAutomaticFlightLimits} />}
      {automaticFlightRequested && <>
      <div className="mission-altitude-card">
        <div>
          <label htmlFor="draft-payload">Payload for this analysis (kg, optional)</label>
          <p>Optional payload mass used to check takeoff weight and payload limits during AI draft analysis.</p>
        </div>
        <input id="draft-payload" name="payloadKg" type="number" min="0" step="0.01" placeholder="0.0 kg" value={payloadKg} onChange={event => setPayloadKg(event.target.value)} style={{ width: "220px", maxWidth: "100%", minHeight: "42px", padding: "8px 12px", border: "1px solid var(--control-border, #8596ab)", borderRadius: "6px", background: "var(--surface-low, #f8fafc)", color: "var(--color-ink, #0d1c2e)", fontSize: "14.5px" }} />
      </div>
      <DraftAnalysisPanel key={analysisContextKey} draft={analysisDraft} missing={missing} disabled={saving}
        applyCruise={setAverageSpeed} applyCeiling={setMaxSpeed} applyEnd={setEndTime}
        applyAutomaticLimit={(field, value) => setAutomaticFlightLimits(current => ({ ...current, [field]: value }))}
        applyAllAutomaticLimits={recommended => setAutomaticFlightLimits(current => ({ ...current,
          takeoffSpeedMetersPerSecond: recommended.takeoffSpeedMetersPerSecond, climbSpeedMetersPerSecond: recommended.climbSpeedMetersPerSecond,
          descentSpeedMetersPerSecond: recommended.descentSpeedMetersPerSecond, landingSpeedMetersPerSecond: recommended.landingSpeedMetersPerSecond,
          maxAltitudeMeters: recommended.maxAltitudeMeters, maxWindMetersPerSecond: recommended.maxWindMetersPerSecond,
          maxGustMetersPerSecond: recommended.maxGustMetersPerSecond }))} />
      </>}
      <section className="route-builder">
        <div className="builder-section-title"><span>02</span><div><h2>Flight waypoints &amp; geofence</h2><p>{flightMode === "manual" ? "Manual operation: choose Polygon or Circular fence below to draw permitted flight zones on the map, and place your planned waypoints." : "Start with Takeoff/Home. Finish with Land at the last point, or Return/RTL to visit it and return to FC Home."}</p></div><button type="button" className="text-action" disabled={!waypoints.length} onClick={() => { if (window.confirm("Clear all waypoints?")) { setWaypoints([]); setError(""); } }}>CLEAR ROUTE</button></div>
        {geofenceWarning && <p className="form-warning" role="status"><AlertTriangle size={17} aria-hidden="true" /><span>{geofenceWarning}</span></p>}
        <MissionFenceFields fences={missionFences} onChange={fences => { setMissionFences(fences); setError(""); }} drawing={fenceDrawing.drawing} onDraw={shape => { fenceDrawing.begin(shape); setError(""); }} vertexCount={fenceDrawing.draft.length} onFinish={() => { fenceDrawing.finish(); setError(""); }} disabled={saving} />
        <MissionMap showUserLocation missionFences={missionFences} draftFence={fenceDrawing.draft} drawingFence={fenceDrawing.drawing} onFenceUpdate={fence => setMissionFences(current => current.map(f => f.id === fence.id ? fence : f))} plannedHome={null} waypoints={waypoints} geofences={geofences} onAdd={(lng, lat) => { addWaypoint(lng, lat); setError(""); }} onUpdate={(idx, patch) => { updateWaypoint(idx, patch); setError(""); }} readOnly={saving} />
        <div className="route-actions-row">
          <button type="button" className="module-secondary mission-add-waypoint" onClick={() => { addWaypoint(); setError(""); }}>+ ADD WAYPOINT MANUALLY</button>
          <span className="route-count-pill">{waypoints.length} {waypoints.length === 1 ? "waypoint" : "waypoints"}{waypoints.length < 2 ? " · minimum 2 required" : " · route ready"}</span>
        </div>
        <div className="waypoint-list">{waypoints.length === 0 ? <div className="waypoint-empty">Click on the map above to place your first waypoint (Takeoff/Home), or use <strong>+ Add waypoint manually</strong> to enter coordinates directly.</div> : waypoints.map((point, index) => {
          const endpointAction: WaypointAction | null = index === 0 ? "TakeOff" : null;
          return <article key={index}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE · required<input name={`waypointLatitude_${index + 1}`} data-field-label={`Waypoint ${index + 1} latitude`} type="number" min="-90" max="90" step="0.000001" value={Number.isFinite(point.latitude) ? point.latitude : ""} onChange={event => updateWaypoint(index, { latitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>LONGITUDE · required<input name={`waypointLongitude_${index + 1}`} data-field-label={`Waypoint ${index + 1} longitude`} type="number" min="-180" max="180" step="0.000001" value={Number.isFinite(point.longitude) ? point.longitude : ""} onChange={event => updateWaypoint(index, { longitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ALTITUDE (&gt;10 M) · required<input name={`waypointAltitude_${index + 1}`} data-field-label={`Waypoint ${index + 1} altitude (m)`} type="number" min="10.1" max={automaticFlightRequested ? automaticFlightLimits.maxAltitudeMeters : undefined} step="any" value={point.altitude != null && Number.isFinite(point.altitude) ? point.altitude : ""} onChange={event => updateWaypoint(index, { altitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ACTION · required<select name={`waypointAction_${index + 1}`} data-field-label={`Waypoint ${index + 1} action`} value={point.actionType} disabled={endpointAction !== null} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{endpointAction ? <option value={endpointAction}>{waypointActionLabel(endpointAction)}</option> : index === waypoints.length - 1 ? <><option value="Land">Land</option><option value="Return">Return/RTL</option></> : intermediateActions.map(action => <option key={action}>{action}</option>)}</select></label><div className="waypoint-order-actions"><button type="button" className="text-action" title="Move waypoint earlier" aria-label={`Move waypoint ${index + 1} earlier`} disabled={index === 0} onClick={() => moveWaypoint(index, -1)}>↑</button><button type="button" className="text-action" title="Move waypoint later" aria-label={`Move waypoint ${index + 1} later`} disabled={index === waypoints.length - 1} onClick={() => moveWaypoint(index, 1)}>↓</button><button type="button" className="waypoint-remove" title="Remove waypoint" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></div>{automaticFlightRequested && <WaypointNavigationFields point={point} maximumSpeed={Number(maxSpeed) || 3} onChange={navigation => updateWaypoint(index, { navigation })} />}</article>;
        })}</div>
      </section>
      <div className="mission-altitude-card">
        <div>
          <label htmlFor="mission-altitude-reference">Altitude reference</label>
          <p>{automaticFlightRequested ? "Automatic missions require Relative (height above takeoff Home)." : "Optional datum used to evaluate altitude restrictions along the route."}</p>
        </div>
        <select id="mission-altitude-reference" name="altitudeReference" data-field-label="Altitude reference" value={altitudeReference} onChange={event => setAltitudeReference(event.target.value as typeof altitudeReference)}><option value="">Not specified</option><option disabled={automaticFlightRequested} value="AGL">AGL — above ground</option><option disabled={automaticFlightRequested} value="MSL">MSL — mean sea level</option><option value="Relative">Relative — takeoff reference</option></select>
      </div>
    </fieldset>
    {error && <div className="mission-footer-error"><AlertCircle size={17} aria-hidden="true" /><span>{error}</span><button type="button" onClick={() => { errorRef.current?.focus({ preventScroll: true }); errorRef.current?.scrollIntoView({ block: "center", behavior: "smooth" }); }}>TOP ↑</button></div>}
    <footer className="builder-footer"><p><b>{waypoints.length}</b> WAYPOINTS{missionFences.length ? <> · <b>{missionFences.length}</b> FENCES</> : null} · Save a Draft, then submit it from Mission Details for validation and review.</p><div><Link className="module-secondary module-link" href="/portal/missions">CANCEL</Link><button type="submit" className="module-primary" disabled={saving || activeAircraft.length === 0}>{saving ? "SAVING…" : "SAVE DRAFT MISSION"}</button></div></footer>
  </form >
  </>;
}
