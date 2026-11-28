"use client";

import { toVietnamDateTimeInput, vietnamInputToUtcIso } from "@/lib/portal/vietnam-time";

import Link from "next/link";
import { AlertCircle, Sparkles } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type MouseEvent } from "react";
import { describeInvalidMissionField, syncFieldAriaInvalid } from "../../new/mission-form";
import MissionSuggestionPanel from "../../new/mission-suggestion-panel";
import { getAssignedUavs, getManagedUavs, getGeofences, getMission, updateMission } from "@/lib/portal/client";
import { missionActions } from "@/lib/portal/mission-review";
import type { AccountRole } from "@/lib/auth/types";
import type { AssignedUav, Geofence, MissionDetail, MissionSuggestion, MissionWaypoint, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../../module-state";
import MissionMap from "../../mission-map";
import { appendLandingWaypoint, normalizeWaypointActions, waypointActionLabel } from "@/lib/maps/mission-waypoints";
import { withPlannedHome } from "@/lib/maps/planned-home";
import { missionSpeedError, routeError } from "@/lib/maps/planning-validation";

import WaypointNavigationFields, { FirmwareDefaultsNotice } from "../../waypoint-navigation-fields";
import AutomaticFlightFields, { defaultAutomaticLimits } from "../../automatic-flight-fields";
import FlightModeFields from "../../flight-mode-fields";
import { automaticFlightError } from "@/lib/portal/automatic-flight-limits";
import type { AutomaticFlightLimits } from "@/lib/portal/types";

import MissionFenceFields, { useFenceDrawing } from "../../mission-fence-fields";
import { missionFencesError, withMissionFences } from "@/lib/maps/mission-fences";
import type { MissionFence } from "@/lib/portal/types";

const intermediateActions: WaypointAction[] = ["FlyThrough", "Hover"];

const toLocalInput = (value: string | null) => {
  if (!value) return "";
  return toVietnamDateTimeInput(value);
};

type EditorValue = {
  missionFences: MissionFence[];
  automaticFlightRequested: boolean;
  automaticFlightLimits: AutomaticFlightLimits;
  altitudeReference: "AGL" | "MSL" | "Relative" | "";
  uavId: string;
  name: string;
  description: string;
  averageSpeedMetersPerSecond: string;
  maxSpeedMetersPerSecond: string;
  startTime: string;
  endTime: string;
  waypoints: MissionWaypoint[];
};

const editorValue = (saved: MissionDetail): EditorValue => {
  const mission = saved.pendingUpdate ? { ...saved, ...saved.pendingUpdate.proposedPlan } : saved; return ({
    altitudeReference: (mission.pendingUpdate?.proposedPlan.altitudeReference ?? mission.altitudeReference ?? "") as EditorValue["altitudeReference"],
    automaticFlightRequested: mission.automaticFlightRequested ?? false,
    automaticFlightLimits: mission.automaticFlightLimits ?? defaultAutomaticLimits(),
    missionFences: mission.waypoints.find(p => p.sequenceOrder === 1)?.navigation?.missionFences ?? [],
    uavId: mission.uavId ?? "",
    name: mission.name,
    description: mission.description ?? "",
    averageSpeedMetersPerSecond: mission.averageSpeedMetersPerSecond?.toString() ?? "",
    maxSpeedMetersPerSecond: mission.maxSpeedMetersPerSecond?.toString() ?? "",
    startTime: toLocalInput(mission.startTime),
    endTime: toLocalInput(mission.endTime),
    waypoints: normalizeWaypointActions(mission.waypoints.map(point => ({ ...point }))),
  });
};

export default function MissionEditClient({ id, actorId, role }: { id: string; actorId: string; role: AccountRole }) {
  const [mission, setMission] = useState<MissionDetail | null>(null);
  const [aircraft, setAircraft] = useState<AssignedUav[]>([]);
  const [geofences, setGeofences] = useState<Geofence[]>([]);
  const [value, setValue] = useState<EditorValue | null>(null);
  const fenceDrawing = useFenceDrawing(value?.missionFences ?? [], fences => patchValue({ missionFences: fences }));
  const [initialValue, setInitialValue] = useState("");
  const [loadError, setLoadError] = useState<unknown>(null);
  const [formError, setFormError] = useState("");
  const errorRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (formError) { errorRef.current?.focus({ preventScroll: true }); errorRef.current?.scrollIntoView({ block: "center", behavior: "instant" }); }
  }, [formError]);
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);
  const [aiOpen, setAiOpen] = useState(false);
  const savePending = useRef(false);
  const operation = useRef<{ signature: string; id: string } | null>(null);

  const load = useCallback(() => {
    setMission(null);
    setValue(null);
    setLoadError(null);
    void Promise.allSettled([getMission(id), (role === "operation_manager" ? getManagedUavs({ pageSize: 100 }).then(result => result.items) : getAssignedUavs()), getGeofences()]).then(([missionResult, uavResult, zoneResult]) => {
      if (missionResult.status === "rejected") {
        setLoadError(missionResult.reason);
        return;
      }
      const next = editorValue(missionResult.value);
      setMission(missionResult.value);
      setValue(next);
      setInitialValue(JSON.stringify(next));
      setAircraft(uavResult.status === "fulfilled" ? uavResult.value : []);
      setGeofences(zoneResult.status === "fulfilled" ? zoneResult.value : []);
    });
  }, [id, role]);

  useEffect(() => {
    let active = true;
    void Promise.allSettled([getMission(id), (role === "operation_manager" ? getManagedUavs({ pageSize: 100 }).then(result => result.items) : getAssignedUavs()), getGeofences()]).then(([missionResult, uavResult, zoneResult]) => {
      if (!active) return;
      if (missionResult.status === "rejected") {
        setLoadError(missionResult.reason);
        return;
      }
      const next = editorValue(missionResult.value);
      setMission(missionResult.value);
      setValue(next);
      setInitialValue(JSON.stringify(next));
      setAircraft(uavResult.status === "fulfilled" ? uavResult.value : []);
      setGeofences(zoneResult.status === "fulfilled" ? zoneResult.value : []);
    });
    return () => { active = false; };
  }, [id, role]);

  const dirty = useMemo(() => Boolean(value && initialValue && JSON.stringify(value) !== initialValue), [initialValue, value]);

  useEffect(() => {
    if (!dirty) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener("beforeunload", warn);
    return () => window.removeEventListener("beforeunload", warn);
  }, [dirty]);

  function patchValue(patch: Partial<EditorValue>) {
    setValue(current => current ? { ...current, ...patch } : current);
    setNotice("");
    setFormError("");
  }

  function addWaypoint(longitude: number, latitude: number) {
    if (!value) return;
    if (fenceDrawing.point(longitude, latitude)) return;
    patchValue({ waypoints: appendLandingWaypoint(value.waypoints, { latitude: Number(latitude.toFixed(6)), longitude: Number(longitude.toFixed(6)), altitude: value.automaticFlightRequested ? 20 : 50 }) });
  }

  function updateWaypoint(index: number, patch: Partial<MissionWaypoint>) {
    if (!value) return;
    patchValue({ waypoints: normalizeWaypointActions(value.waypoints.map((point, current) => current === index ? { ...point, ...patch } : point)) });
  }

  function removeWaypoint(index: number) {
    if (!value) return;
    patchValue({ waypoints: normalizeWaypointActions(value.waypoints.filter((_, current) => current !== index)) });
  }

  function moveWaypoint(index: number, direction: -1 | 1) {
    if (!value) return;
    const target = index + direction;
    if (target < 0 || target >= value.waypoints.length) return;
    const next = [...value.waypoints];
    [next[index], next[target]] = [next[target], next[index]];
    patchValue({ waypoints: normalizeWaypointActions(next) });
  }

  function applySuggestion(suggestion: MissionSuggestion) {
    if (!value) return;
    if (!window.confirm("Replace the current mission details with this AI suggestion? Your assigned UAV, schedule, geofences and waypoints will be kept.")) return;

    const patch: Partial<EditorValue> = {};
    if (suggestion.missionName.trim()) patch.name = suggestion.missionName.trim();
    if (suggestion.missionPurpose.trim()) patch.description = suggestion.missionPurpose.trim();
    if (suggestion.automaticFlightRequested != null) {
      patch.automaticFlightRequested = suggestion.automaticFlightRequested;
      if (suggestion.automaticFlightRequested && !value.automaticFlightRequested) {
        patch.automaticFlightLimits = defaultAutomaticLimits();
      }
    }
    if (["AGL", "MSL", "Relative"].includes(suggestion.altitudeReference)) {
      patch.altitudeReference = suggestion.altitudeReference as "AGL" | "MSL" | "Relative";
    } else if (suggestion.automaticFlightRequested) {
      patch.altitudeReference = "Relative";
    }
    if (Number.isFinite(suggestion.averageSpeedMetersPerSecond) && Number(suggestion.averageSpeedMetersPerSecond) > 0) {
      patch.averageSpeedMetersPerSecond = String(suggestion.averageSpeedMetersPerSecond);
    }
    if (Number.isFinite(suggestion.maxSpeedMetersPerSecond) && Number(suggestion.maxSpeedMetersPerSecond) > 0) {
      patch.maxSpeedMetersPerSecond = String(suggestion.maxSpeedMetersPerSecond);
    }

    patchValue(patch);
    setNotice("AI suggestion applied to mission details. Review the fields and save to send for manager review.");
    window.setTimeout(() => document.getElementById("mission-edit-plan-info")?.scrollIntoView({ behavior: "smooth", block: "start" }), 0);
  }

  function resetChanges() {
    if (!mission) return;
    setValue(editorValue(mission));
    setFormError("");
    setNotice("");
  }

  function leave(event: MouseEvent<HTMLAnchorElement>) {
    if (dirty && !window.confirm("Discard the unsaved mission changes?")) event.preventDefault();
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!value || !mission || !missionActions(mission, role, actorId).edit || savePending.current) return;
    setFormError("");
    setNotice("");
    const fenceError = missionFencesError(value.missionFences);
    if (fenceDrawing.drawing || fenceError) { setFormError(fenceError || "Finish or cancel fence drawing before saving."); return; }
    const normalizedWaypoints = withPlannedHome(withMissionFences(normalizeWaypointActions(value.waypoints), value.missionFences), null);
    const invalidRoute = routeError(normalizedWaypoints);
    if (invalidRoute) { setFormError(invalidRoute); return; }
    if (new Date(value.endTime) <= new Date(value.startTime)) { setFormError("End time must be later than start time."); return; }
    const averageSpeedMetersPerSecond = Number(value.automaticFlightRequested && !value.averageSpeedMetersPerSecond.trim() ? (value.automaticFlightLimits.policyVersion === "multicopter-envelope-v2" ? "11.2" : "3") : value.averageSpeedMetersPerSecond);
    const maxSpeedMetersPerSecond = Number(value.automaticFlightRequested && !value.maxSpeedMetersPerSecond.trim() ? (value.automaticFlightLimits.policyVersion === "multicopter-envelope-v2" ? "15" : "3") : value.maxSpeedMetersPerSecond);
    const invalidSpeed = missionSpeedError(averageSpeedMetersPerSecond, maxSpeedMetersPerSecond);
    if (invalidSpeed) { setFormError(invalidSpeed); return; }
    const invalidAutomatic = value.automaticFlightRequested ? automaticFlightError(value.altitudeReference, averageSpeedMetersPerSecond, maxSpeedMetersPerSecond, value.automaticFlightLimits, normalizedWaypoints) : null;
    if (invalidAutomatic) { setFormError(invalidAutomatic); return; }
    savePending.current = true;
    setSaving(true);
    try {
      const payload = {
        automaticFlightRequested: value.automaticFlightRequested,
        automaticFlightLimits: value.automaticFlightRequested ? value.automaticFlightLimits : null,
        altitudeReference: value.altitudeReference || null,
        uavId: value.uavId,
        name: value.name.trim(),
        description: value.description.trim() || null,
        averageSpeedMetersPerSecond,
        maxSpeedMetersPerSecond,
        startTime: vietnamInputToUtcIso(value.startTime),
        endTime: vietnamInputToUtcIso(value.endTime),
        waypoints: normalizedWaypoints,
      };
      const signature = JSON.stringify(payload);
      if (operation.current?.signature !== signature) operation.current = { signature, id: crypto.randomUUID() };
      const saved = await updateMission(id, { ...payload, operationId: operation.current.id, expectedPlanHash: mission.planHash, expectedPendingRequestId: mission.pendingUpdate?.id ?? null });
      const next = editorValue(saved);
      setMission(saved);
      setValue(next);
      setInitialValue(JSON.stringify(next));
      setNotice("Proposed changes sent for manager review. The current mission has not changed.");
    } catch (reason) {
      setFormError(reason instanceof Error ? reason.message : "Unable to validate and save this mission.");
    } finally {
      savePending.current = false;
      setSaving(false);
    }
  }

  if (loadError) return <ModuleError error={loadError} retry={load} />;
  if (!mission || !value) return <ModuleLoading label="Loading mission editor" />;

  const canEdit = missionActions(mission, role, actorId).edit;
  if (!canEdit) return <section className="module-state error"><span className="state-symbol">!</span><p className="module-kicker">MISSION LOCKED</p><h2>This plan cannot be edited</h2><p>Only pre-flight missions can be proposed for editing. The current mission status is {mission.status}.</p><Link className="module-primary module-link" href={`/portal/missions/${id}`}>BACK TO MISSION</Link></section>;

  const approvedAircraft = aircraft.filter(item => item.status.toLowerCase() === "active" || item.id === mission.uavId);
  const canUseAi = role !== "operation_manager";
  const initialAiPrompt = [value.name.trim(), value.description.trim()].filter(Boolean).join(" — ");

  return <>
    <header className="module-heading mission-edit-heading">
      <div><p className="module-kicker">PILOT / MY MISSIONS / EDIT PLAN</p><div className="mission-title-line"><h1>Edit Mission Plan</h1><span className={`status status-${mission.status.toLowerCase().replaceAll(" ", "")}`}>{mission.status}</span></div><p>Update mission details and refine the planned flight path.</p></div>
      <div className="heading-actions">
        <span className={`edit-dirty-state${dirty ? " changed" : ""}`}>{dirty ? "UNSAVED CHANGES" : "NO CHANGES"}</span>
        {canUseAi && <button type="button" className="module-secondary" aria-expanded={aiOpen} aria-controls="mission-edit-ai-panel" disabled={saving} onClick={() => setAiOpen(open => !open)} style={{ display: "inline-flex", alignItems: "center", gap: 6, width: "auto" }}><Sparkles size={15} aria-hidden="true" />{aiOpen ? "HIDE AI ASSISTANT" : "AI ASSIST"}</button>}
        <Link className="module-secondary module-link" href={`/portal/missions/${id}`} onClick={leave}>BACK TO MISSION</Link>
      </div>
    </header>
    {mission.pendingUpdate && <div className="mission-pending-banner" role="status">
      <span className="mission-pending-badge">PENDING PROPOSAL</span>
      <p>Editing the pending proposal. The current mission remains unchanged until approval.</p>
    </div>}
    {canUseAi && aiOpen && <div id="mission-edit-ai-panel"><MissionSuggestionPanel disabled={saving} initialPrompt={initialAiPrompt} onApply={applySuggestion} /></div>}
    <form className="mission-builder mission-edit-form" onSubmit={submit} onBlurCapture={event => syncFieldAriaInvalid(event.target)} onChangeCapture={event => { syncFieldAriaInvalid(event.target); if (formError) setFormError(""); }} onInputCapture={event => { syncFieldAriaInvalid(event.target); if (formError) setFormError(""); }} onInvalidCapture={event => {
      const field = event.target;
      if (!(field instanceof HTMLInputElement || field instanceof HTMLSelectElement || field instanceof HTMLTextAreaElement)) return;
      syncFieldAriaInvalid(field, true);
      if (event.currentTarget.querySelector("input:invalid, select:invalid, textarea:invalid") !== field) return;
      setFormError(describeInvalidMissionField(field));
    }}>
      {formError && <div ref={errorRef} className="mission-error-summary" role="alert" tabIndex={-1}><AlertCircle size={22} aria-hidden="true" /><div className="mission-error-copy"><strong>Changes could not be saved</strong><p>{formError}</p></div><button type="button" className="mission-error-dismiss" aria-label="Dismiss error message" onClick={() => setFormError("")}>×</button></div>}
      <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: "contents" }}><legend className="sr-only">Mission and flight route</legend>
        <FlightModeFields value={value.automaticFlightRequested ? "automatic" : "manual"} onChange={mode => patchValue(mode === "automatic"
          ? { automaticFlightRequested: true, averageSpeedMetersPerSecond: "11.2", maxSpeedMetersPerSecond: "15", automaticFlightLimits: defaultAutomaticLimits(), altitudeReference: "Relative" }
          : { automaticFlightRequested: false })} />
        <section className="mission-fields" id="mission-edit-plan-info">
          <div className="builder-section-title"><span>01</span><div><h2>Plan configuration</h2><p>Revise the assigned aircraft, schedule and mission brief.</p></div><button type="button" className="text-action" disabled={!dirty} onClick={resetChanges}>RESET CHANGES</button></div>
          <div className="mission-field-grid">
            <label><span className="field-heading">ASSIGNED UAV <em>Required</em></span><select data-field-label="Assigned UAV" value={value.uavId} onChange={event => patchValue({ uavId: event.target.value })} required><option value="">Select an active UAV</option>{approvedAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
            <label><span className="field-heading">MISSION NAME <em>Required</em></span><input data-field-label="Mission name" value={value.name} onChange={event => patchValue({ name: event.target.value })} minLength={2} maxLength={200} required /></label>
            <label className="full-field"><span className="field-heading">DESCRIPTION <em className="optional">Optional</em></span><textarea data-field-label="Description" value={value.description} onChange={event => patchValue({ description: event.target.value })} maxLength={2000} /></label>
            <label><span className="field-heading">CRUISE SPEED (M/S) <em className={value.automaticFlightRequested ? "optional" : undefined}>{value.automaticFlightRequested ? "Optional override" : "Required"}</em></span><input data-field-label="Cruise speed (m/s)" type="number" min="0.1" step="0.01" max={value.automaticFlightRequested ? (value.automaticFlightLimits.policyVersion === "multicopter-envelope-v2" ? 15 : 5) : undefined} value={value.averageSpeedMetersPerSecond} onChange={event => patchValue({ averageSpeedMetersPerSecond: event.target.value })} required={!value.automaticFlightRequested} /></label>
            <label><span className="field-heading">SPEED CEILING (M/S) <em className={value.automaticFlightRequested ? "optional" : undefined}>{value.automaticFlightRequested ? "Optional override" : "Required"}</em></span><input data-field-label="Speed ceiling (m/s)" type="number" min="0.1" step="0.01" max={value.automaticFlightRequested ? (value.automaticFlightLimits.policyVersion === "multicopter-envelope-v2" ? 15 : 5) : undefined} value={value.maxSpeedMetersPerSecond} onChange={event => patchValue({ maxSpeedMetersPerSecond: event.target.value })} required={!value.automaticFlightRequested} /></label>
            <label><span className="field-heading">START TIME (GMT+7) <em>Required</em></span><input data-field-label="Start date & time" type="datetime-local" value={value.startTime} onChange={event => patchValue({ startTime: event.target.value })} required /></label>
            <label><span className="field-heading">END TIME (GMT+7) <em>Required</em></span><input data-field-label="Planned end date & time" type="datetime-local" value={value.endTime} onChange={event => patchValue({ endTime: event.target.value })} required /></label>
          </div>
        </section>

        {value.automaticFlightRequested && <FirmwareDefaultsNotice snapshot={aircraft.find(uav => uav.id === value.uavId)?.firmwareNavigationDefaults} />}
        {value.automaticFlightRequested && <AutomaticFlightFields value={value.automaticFlightLimits} onChange={automaticFlightLimits => patchValue({ automaticFlightLimits })} />}
        <section className="route-builder">
          <div className="builder-section-title"><span>02</span><div><h2>Route, geofence and waypoint sequence</h2><p>{!value.automaticFlightRequested ? "Manual operation: choose Polygon or Circular fence below to draw permitted flight zones on the map, and refine the planned waypoints." : "Start with Takeoff/Home. Finish with Land at the last point, or Return/RTL to visit it and return to FC Home."}</p></div><button type="button" className="text-action" disabled={!value.waypoints.length} onClick={() => patchValue({ waypoints: [] })}>CLEAR ROUTE</button></div>
          <MissionFenceFields fences={value.missionFences} onChange={fences => patchValue({ missionFences: fences })} drawing={fenceDrawing.drawing} onDraw={shape => { fenceDrawing.begin(shape); }} vertexCount={fenceDrawing.draft.length} onFinish={fenceDrawing.finish} disabled={saving} />
          <MissionMap missionFences={value.missionFences} draftFence={fenceDrawing.draft} drawingFence={fenceDrawing.drawing} onFenceUpdate={fence => patchValue({ missionFences: value.missionFences.map(f => f.id === fence.id ? fence : f) })} plannedHome={null} waypoints={value.waypoints} geofences={geofences} onAdd={addWaypoint} onUpdate={updateWaypoint} readOnly={saving} />
          <div className="waypoint-list mission-edit-waypoint-list">{value.waypoints.length === 0 ? <div className="waypoint-empty">Click the map above to add the first route waypoint.</div> : value.waypoints.map((point, index) => {
            const endpointAction: WaypointAction | null = index === 0 ? "TakeOff" : null;
            return <article key={`${point.sequenceOrder}-${index}`}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE · required<input data-field-label={`Waypoint ${index + 1} latitude`} type="number" min="-90" max="90" step="0.000001" value={Number.isFinite(point.latitude) ? point.latitude : ""} onChange={event => updateWaypoint(index, { latitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>LONGITUDE · required<input data-field-label={`Waypoint ${index + 1} longitude`} type="number" min="-180" max="180" step="0.000001" value={Number.isFinite(point.longitude) ? point.longitude : ""} onChange={event => updateWaypoint(index, { longitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ALTITUDE (&gt;10 M) · required<input data-field-label={`Waypoint ${index + 1} altitude (m)`} type="number" min="10.1" max={value.automaticFlightRequested ? value.automaticFlightLimits.maxAltitudeMeters : undefined} step="any" value={point.altitude != null && Number.isFinite(point.altitude) ? point.altitude : ""} onChange={event => updateWaypoint(index, { altitude: event.target.value === "" ? NaN : Number(event.target.value) })} required /></label><label>ACTION · required<select data-field-label={`Waypoint ${index + 1} action`} value={point.actionType} disabled={endpointAction !== null} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{endpointAction ? <option value={endpointAction}>{waypointActionLabel(endpointAction)}</option> : index === value.waypoints.length - 1 ? <><option value="Land">Land</option><option value="Return">Return/RTL</option></> : <>{point.actionType === "CapturePhoto" && <option value="CapturePhoto" disabled>Camera action deferred — select another action</option>}{intermediateActions.map(action => <option key={action}>{action}</option>)}</>}</select></label><div className="waypoint-order-actions"><button type="button" title="Move waypoint up" aria-label={`Move waypoint ${index + 1} up`} disabled={index === 0} onClick={() => moveWaypoint(index, -1)}>↑</button><button type="button" title="Move waypoint down" aria-label={`Move waypoint ${index + 1} down`} disabled={index === value.waypoints.length - 1} onClick={() => moveWaypoint(index, 1)}>↓</button><button type="button" className="waypoint-remove" title="Remove waypoint" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></div>{value.automaticFlightRequested && <WaypointNavigationFields point={point} maximumSpeed={Number(value.maxSpeedMetersPerSecond) || 3} onChange={navigation => updateWaypoint(index, { navigation })} />}</article>;
          })}</div>
        </section>
        <div className="mission-altitude-card">
          <div>
            <label htmlFor="mission-edit-altitude-reference">Altitude reference</label>
            <p>{value.automaticFlightRequested ? "Automatic missions require Relative (height above takeoff Home)." : "Optional datum used to evaluate altitude restrictions along the route."}</p>
          </div>
          <select id="mission-edit-altitude-reference" data-field-label="Altitude reference" value={value.altitudeReference} onChange={event => patchValue({ altitudeReference: event.target.value as EditorValue["altitudeReference"] })}><option value="">Not specified</option><option disabled={value.automaticFlightRequested} value="AGL">AGL — above ground</option><option disabled={value.automaticFlightRequested} value="MSL">MSL — mean sea level</option><option value="Relative">Relative — takeoff reference</option></select>
        </div>
      </fieldset>
      {formError && <div className="mission-footer-error"><AlertCircle size={17} aria-hidden="true" /><span>{formError}</span><button type="button" onClick={() => { errorRef.current?.focus({ preventScroll: true }); errorRef.current?.scrollIntoView({ block: "center", behavior: "smooth" }); }}>TOP ↑</button></div>}
      {notice && <p className="mission-edit-notice" role="status" aria-live="polite">{notice}</p>}
      <footer className="builder-footer"><p><b>{value.waypoints.length}</b> WAYPOINTS{value.missionFences.length ? <> · <b>{value.missionFences.length}</b> FENCES</> : null} · {dirty ? "CHANGES PENDING" : "UNCHANGED"}</p><div><Link className="module-secondary module-link" href={`/portal/missions/${id}`} onClick={leave}>CANCEL</Link><button type="submit" className="module-primary" disabled={!dirty || saving}>{saving ? "VALIDATING..." : "SAVE & SEND FOR REVIEW"}</button></div></footer>
    </form>
  </>;
}
