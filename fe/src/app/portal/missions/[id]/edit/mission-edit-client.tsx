"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent, type MouseEvent } from "react";
import { getAssignedUavs, getGeofences, getMission, updateMission } from "@/lib/portal/client";
import type { AssignedUav, Geofence, MissionDetail, MissionWaypoint, WaypointAction } from "@/lib/portal/types";
import { ModuleError, ModuleLoading } from "../../../module-state";
import MissionMap from "../../mission-map";

const actions: WaypointAction[] = ["TakeOff", "FlyThrough", "Hover", "CapturePhoto", "Land"];
const editableStatuses = new Set(["draft", "rejected", "pending", "pendingreview", "submitted", "approved"]);

const toLocalInput = (value: string | null) => {
  if (!value) return "";
  const date = new Date(value);
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 16);
};

type EditorValue = {
  altitudeReference: "AGL" | "MSL" | "Relative" | "";
  uavId: string;
  name: string;
  description: string;
  startTime: string;
  endTime: string;
  waypoints: MissionWaypoint[];
};

const editorValue = (saved: MissionDetail): EditorValue => { const mission = saved.pendingUpdate ? { ...saved, ...saved.pendingUpdate.proposedPlan } : saved; return ({
  altitudeReference: (mission.pendingUpdate?.proposedPlan.altitudeReference ?? mission.altitudeReference ?? "") as EditorValue["altitudeReference"],
  uavId: mission.uavId ?? "",
  name: mission.name,
  description: mission.description ?? "",
  startTime: toLocalInput(mission.startTime),
  endTime: toLocalInput(mission.endTime),
  waypoints: mission.waypoints.map(point => ({ ...point })),
}); };

export default function MissionEditClient({ id }: { id: string }) {
  const [mission, setMission] = useState<MissionDetail | null>(null);
  const [aircraft, setAircraft] = useState<AssignedUav[]>([]);
  const [geofences, setGeofences] = useState<Geofence[]>([]);
  const [value, setValue] = useState<EditorValue | null>(null);
  const [initialValue, setInitialValue] = useState("");
  const [loadError, setLoadError] = useState<unknown>(null);
  const [formError, setFormError] = useState("");
  const [notice, setNotice] = useState("");
  const [saving, setSaving] = useState(false);
  const savePending = useRef(false);
  const operation = useRef<{ signature: string; id: string } | null>(null);

  const load = useCallback(() => {
    setMission(null);
    setValue(null);
    setLoadError(null);
    void Promise.allSettled([getMission(id), getAssignedUavs(), getGeofences()]).then(([missionResult, uavResult, zoneResult]) => {
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
  }, [id]);

  useEffect(() => {
    let active = true;
    void Promise.allSettled([getMission(id), getAssignedUavs(), getGeofences()]).then(([missionResult, uavResult, zoneResult]) => {
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
  }, [id]);

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
    patchValue({ waypoints: [...value.waypoints, { sequenceOrder: value.waypoints.length + 1, latitude: Number(latitude.toFixed(6)), longitude: Number(longitude.toFixed(6)), altitude: 50, actionType: value.waypoints.length ? "FlyThrough" : "TakeOff" }] });
  }

  function updateWaypoint(index: number, patch: Partial<MissionWaypoint>) {
    if (!value) return;
    patchValue({ waypoints: value.waypoints.map((point, current) => current === index ? { ...point, ...patch } : point) });
  }

  function removeWaypoint(index: number) {
    if (!value) return;
    patchValue({ waypoints: value.waypoints.filter((_, current) => current !== index).map((point, current) => ({ ...point, sequenceOrder: current + 1 })) });
  }

  function moveWaypoint(index: number, direction: -1 | 1) {
    if (!value) return;
    const target = index + direction;
    if (target < 0 || target >= value.waypoints.length) return;
    const next = [...value.waypoints];
    [next[index], next[target]] = [next[target], next[index]];
    patchValue({ waypoints: next.map((point, current) => ({ ...point, sequenceOrder: current + 1 })) });
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
    if (!value || savePending.current) return;
    setFormError("");
    setNotice("");
    if (value.waypoints.length < 2) { setFormError("Keep at least two waypoints in the flight route."); return; }
    if (new Date(value.endTime) <= new Date(value.startTime)) { setFormError("End time must be later than start time."); return; }
    if (value.waypoints.some(point => point.latitude < -90 || point.latitude > 90 || point.longitude < -180 || point.longitude > 180)) { setFormError("A waypoint is outside the valid latitude or longitude range."); return; }
    if (value.waypoints.some(point => point.altitude != null && point.altitude < 0)) { setFormError("Waypoint altitude must not be negative."); return; }
    savePending.current = true;
    setSaving(true);
    try {
      const payload = {
        altitudeReference: value.altitudeReference || null,
        uavId: value.uavId,
        name: value.name.trim(),
        description: value.description.trim() || null,
        startTime: new Date(value.startTime).toISOString(),
        endTime: new Date(value.endTime).toISOString(),
        waypoints: value.waypoints.map((point, index) => ({ ...point, sequenceOrder: index + 1 })),
      };
      const signature = JSON.stringify(payload);
      if (operation.current?.signature !== signature) operation.current = { signature, id: crypto.randomUUID() };
      const saved = await updateMission(id, { ...payload, operationId: operation.current.id });
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

  const canEdit = editableStatuses.has(mission.status.toLowerCase().replaceAll(" ", ""));
  if (!canEdit) return <section className="module-state error"><span className="state-symbol">!</span><p className="module-kicker">MISSION LOCKED</p><h2>This plan cannot be edited</h2><p>Only pre-flight missions can be proposed for editing. The current mission status is {mission.status}.</p><Link className="module-primary module-link" href={`/portal/missions/${id}`}>BACK TO MISSION</Link></section>;

  const approvedAircraft = aircraft.filter(item => item.status.toLowerCase() === "active" || item.id === mission.uavId);

  return <>
    <header className="module-heading mission-edit-heading">
      <div><p className="module-kicker">PILOT / MY MISSIONS / EDIT PLAN</p><div className="mission-title-line"><h1>Edit Mission Plan</h1><span className={`status status-${mission.status.toLowerCase()}`}>{mission.status}</span></div><p>Update mission details and refine the planned flight path.</p></div>
      <div className="heading-actions"><span className={`edit-dirty-state${dirty ? " changed" : ""}`}>{dirty ? "UNSAVED CHANGES" : "NO CHANGES"}</span><Link className="module-secondary module-link" href={`/portal/missions/${id}`} onClick={leave}>BACK TO MISSION</Link></div>
    </header>
    <form className="mission-builder mission-edit-form" onSubmit={submit}>
      <fieldset disabled={saving} style={{ border: 0, padding: 0, margin: 0, minWidth: 0, display: "contents" }}><legend className="sr-only">Mission and flight route</legend>
      <section className="mission-fields">
        <div className="builder-section-title"><span>01</span><div><h2>Plan configuration</h2><p>Revise the assigned aircraft, schedule and mission brief.</p></div><button type="button" className="text-action" disabled={!dirty} onClick={resetChanges}>RESET CHANGES</button></div>
        <div className="mission-field-grid">
          <label>ASSIGNED UAV<select value={value.uavId} onChange={event => patchValue({ uavId: event.target.value })} required><option value="">Select an active UAV</option>{approvedAircraft.map(item => <option key={item.id} value={item.id}>{item.code} · {item.name}</option>)}</select></label>
          <label>MISSION NAME<input value={value.name} onChange={event => patchValue({ name: event.target.value })} minLength={2} maxLength={200} required /></label>
          <label className="full-field">DESCRIPTION<textarea value={value.description} onChange={event => patchValue({ description: event.target.value })} maxLength={2000} /></label>
          <label>START TIME<input type="datetime-local" value={value.startTime} onChange={event => patchValue({ startTime: event.target.value })} required /></label>
          <label>END TIME<input type="datetime-local" value={value.endTime} onChange={event => patchValue({ endTime: event.target.value })} required /></label>
        </div>
      </section>

      <section className="route-builder">
        <div className="builder-section-title"><span>02</span><div><h2>Route and waypoint sequence</h2><p>Drag route nodes, click the map to add one, or edit exact values below.</p></div><button type="button" className="text-action" disabled={!value.waypoints.length} onClick={() => patchValue({ waypoints: [] })}>CLEAR ROUTE</button></div>
        <MissionMap waypoints={value.waypoints} geofences={geofences} onAdd={addWaypoint} onUpdate={updateWaypoint} readOnly={saving} />
        <div className="waypoint-list mission-edit-waypoint-list">{value.waypoints.length === 0 ? <div className="waypoint-empty">Click the map to add the first route waypoint.</div> : value.waypoints.map((point, index) => <article key={`${point.sequenceOrder}-${index}`}><b>{String(index + 1).padStart(2, "0")}</b><label>LATITUDE<input type="number" min="-90" max="90" step="0.000001" value={point.latitude} onChange={event => updateWaypoint(index, { latitude: Number(event.target.value) })} required /></label><label>LONGITUDE<input type="number" min="-180" max="180" step="0.000001" value={point.longitude} onChange={event => updateWaypoint(index, { longitude: Number(event.target.value) })} required /></label><label>ALTITUDE (M)<input type="number" min="0" max="500" value={point.altitude ?? ""} onChange={event => updateWaypoint(index, { altitude: Number(event.target.value) })} required /></label><label>ACTION<select value={point.actionType} onChange={event => updateWaypoint(index, { actionType: event.target.value as WaypointAction })}>{actions.map(action => <option key={action}>{action}</option>)}</select></label><div className="waypoint-order-actions"><button type="button" title="Move waypoint up" aria-label={`Move waypoint ${index + 1} up`} disabled={index === 0} onClick={() => moveWaypoint(index, -1)}>↑</button><button type="button" title="Move waypoint down" aria-label={`Move waypoint ${index + 1} down`} disabled={index === value.waypoints.length - 1} onClick={() => moveWaypoint(index, 1)}>↓</button><button type="button" className="waypoint-remove" title="Remove waypoint" aria-label={`Remove waypoint ${index + 1}`} onClick={() => removeWaypoint(index)}>×</button></div></article>)}</div>
      </section>
      </fieldset>

      {formError && <p className="form-error" role="alert">{formError}</p>}
      {notice && <p className="mission-edit-notice" role="status">{notice}</p>}
      <label>Altitude reference<select value={value.altitudeReference} onChange={event => setValue(current => current ? { ...current, altitudeReference: event.target.value as EditorValue["altitudeReference"] } : current)}><option value="">Not specified</option><option value="AGL">AGL</option><option value="MSL">MSL</option><option value="Relative">Relative</option></select></label>
      <footer className="builder-footer"><p><b>{value.waypoints.length}</b> WAYPOINTS · {dirty ? "CHANGES PENDING" : "UNCHANGED"}</p><div><Link className="module-secondary module-link" href={`/portal/missions/${id}`} onClick={leave}>CANCEL</Link><button type="submit" className="module-primary" disabled={!dirty || saving}>{saving ? "VALIDATING..." : "SAVE & SEND FOR REVIEW"}</button></div></footer>
    </form>
  </>;
}
