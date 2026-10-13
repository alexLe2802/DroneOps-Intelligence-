"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { createGeofence, deleteGeofence, getGeofence, getGeofences, updateGeofence } from "@/lib/portal/client";
import type { Geofence, GeofenceCoordinate } from "@/lib/portal/types";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import { describeGeofence } from "@/lib/maps/geofences";
import GeofenceMap from "./geofence-map";

type Editor = { id: string | null; name: string; description: string; coordinates: GeofenceCoordinate[]; mode: "view" | "edit" | "delete" };
const message = (error: unknown) => error instanceof Error ? error.message : "Unable to complete the request.";

export default function GeofencesClient({ canManage }: { canManage: boolean }) {
  const [zones, setZones] = useState<Geofence[] | null>(null);
  const [loadError, setLoadError] = useState<unknown>(null);
  const [query, setQuery] = useState("");
  const [editor, setEditor] = useState<Editor | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const load = useCallback(async () => {
    setLoadError(null);
    try { setZones(await getGeofences()); } catch (reason) { setLoadError(reason); }
  }, []);
  useEffect(() => { let active = true; void getGeofences().then(value => { if (active) setZones(value); }).catch(reason => { if (active) setLoadError(reason); }); return () => { active = false; }; }, []);
  function close() {
    if (busy) return;
    if (editor?.mode === "edit" && !window.confirm("Discard unsaved airspace changes?")) return;
    setEditor(null); setError("");
  }
  async function open(id: string, mode: Editor["mode"]) {
    if (busy) return;
    if (editor?.mode === "edit" && !window.confirm("Discard unsaved airspace changes?")) return;
    setBusy(true); setError(""); setNotice("");
    try {
      const zone = await getGeofence(id);
      setEditor({ id: zone.id, name: zone.name, description: zone.description ?? "", coordinates: zone.coordinates, mode });
    } catch (reason) { setError(message(reason)); }
    finally { setBusy(false); }
  }
  function patch(values: Partial<Editor>) { setEditor(current => current ? { ...current, ...values } : current); }
  async function save(event: FormEvent) {
    event.preventDefault();
    if (!editor || busy || !canManage || editor.mode !== "edit") return;
    const points = editor.coordinates;
    if (!editor.name.trim()) { setError("Enter an airspace name."); return; }
    if (points.length < 3 || new Set(points.map(p => `${p.latitude},${p.longitude}`)).size < 3) { setError("Choose at least three distinct vertices."); return; }
    if (points.some(p => !Number.isFinite(p.latitude) || Math.abs(p.latitude) > 90 || !Number.isFinite(p.longitude) || Math.abs(p.longitude) > 180)) { setError("Latitude must be between -90 and 90; longitude between -180 and 180."); return; }
    setBusy(true); setError("");
    try {
      const request = { name: editor.name.trim(), description: editor.description.trim() || null, coordinates: points };
      const saved = editor.id ? await updateGeofence(editor.id, request) : await createGeofence(request);
      setZones(current => [saved, ...(current ?? []).filter(zone => zone.id !== saved.id)]);
      setEditor(null); setNotice("Airspace saved to the backend.");
    } catch (reason) { setError(message(reason)); }
    finally { setBusy(false); }
  }
  async function remove() {
    if (!editor?.id || editor.mode !== "delete" || busy || !canManage) return;
    setBusy(true); setError("");
    try { await deleteGeofence(editor.id); setZones(current => current?.filter(zone => zone.id !== editor.id) ?? []); setEditor(null); setNotice("Airspace deleted from the backend."); }
    catch (reason) { setError(message(reason)); }
    finally { setBusy(false); }
  }
  if (loadError) return <ModuleError error={loadError} retry={() => void load()} />;
  if (!zones) return <ModuleLoading label="Loading controlled airspace" />;
  const filtered = zones.filter(zone => `${zone.name} ${zone.description ?? ""}`.toLowerCase().includes(query.toLowerCase()));
  return <>
    <header className="module-heading"><div><p className="module-kicker">AIRSPACE / GIS REGISTRY</p><h1>Operational Airspace & Geofences</h1><p>Manage the airspace boundaries saved by the backend.</p></div>{canManage && <button className="module-primary" disabled={busy} onClick={() => { if (editor?.mode === "edit" && !window.confirm("Discard unsaved airspace changes?")) return; setEditor({ id: null, name: "", description: "", coordinates: [], mode: "edit" }); setError(""); setNotice(""); }}>ADD AIRSPACE</button>}</header>
    {error && <p className="form-error" role="alert">{error}</p>}{notice && <p className="module-success" role="status">{notice}</p>}
    {editor && <section className="mission-builder geofence-editor" aria-label="Airspace details">
      <header className="module-heading"><h2>{editor.mode === "delete" ? "Delete airspace" : editor.mode === "view" ? "Airspace details" : editor.id ? "Edit airspace" : "New airspace"}</h2><button className="module-secondary" disabled={busy} onClick={close}>CLOSE</button></header>
      {editor.mode === "delete" ? <><p>Delete <strong>{editor.name}</strong>? This removes the saved boundary. This action cannot be undone.</p><div className="geofence-actions"><button className="module-secondary" disabled={busy} onClick={close}>CANCEL</button><button className="module-danger" disabled={busy} onClick={() => void remove()}>{busy ? "DELETING…" : "CONFIRM DELETE"}</button></div></> : <form onSubmit={save}>
        <fieldset disabled={busy || editor.mode === "view"} className="geofence-fields"><div className="mission-field-grid"><label>NAME<input value={editor.name} required onChange={event => patch({ name: event.target.value })} /></label><label>DESCRIPTION<textarea value={editor.description} onChange={event => patch({ description: event.target.value })} /></label></div></fieldset>
        <GeofenceMap key={editor.id ?? "new"} coordinates={editor.coordinates} editable={editor.mode === "edit" && !busy} onChange={coordinates => patch({ coordinates })} />
        <fieldset disabled={busy || editor.mode === "view"} className="geofence-fields"><div className="geofence-vertices">{editor.coordinates.map((point, index) => <div key={index}><b>{index + 1}</b><label>LATITUDE<input type="number" step="any" min="-90" max="90" required value={Number.isNaN(point.latitude) ? "" : point.latitude} onChange={event => patch({ coordinates: editor.coordinates.map((p, i) => i === index ? { ...p, latitude: event.target.valueAsNumber } : p) })} /></label><label>LONGITUDE<input type="number" step="any" min="-180" max="180" required value={Number.isNaN(point.longitude) ? "" : point.longitude} onChange={event => patch({ coordinates: editor.coordinates.map((p, i) => i === index ? { ...p, longitude: event.target.valueAsNumber } : p) })} /></label>{editor.mode === "edit" && <button type="button" className="module-danger" aria-label={`Remove vertex ${index + 1}`} onClick={() => patch({ coordinates: editor.coordinates.filter((_, i) => i !== index) })}>REMOVE</button>}</div>)}</div>{editor.mode === "edit" && <div className="geofence-actions"><button type="button" className="module-secondary" onClick={() => patch({ coordinates: [...editor.coordinates, { latitude: 10.849, longitude: 106.7725 }] })}>ADD VERTEX</button><button type="button" className="module-secondary" onClick={() => patch({ coordinates: [] })}>CLEAR BOUNDARY</button><button className="module-primary" type="submit">{busy ? "SAVING…" : "SAVE AIRSPACE"}</button></div>}</fieldset>
      </form>}
    </section>}
    <section className="module-toolbar"><label>SEARCH AIRSPACE<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Zone name or description" /></label><span>{filtered.length} / {zones.length} ZONES</span><button className="module-secondary" disabled={busy} onClick={() => void load()}>REFRESH</button></section>
    {filtered.length === 0 ? <ModuleEmpty title="No geofences found" message={zones.length ? "Try a different search term." : "No controlled airspace has been configured."} /> : <section className="geofence-grid">{filtered.map(zone => { const info = describeGeofence(zone); return <article className={`geofence-card geofence-${info.category}`} key={zone.id}><header><span>{info.label}</span><small>{zone.coordinates.length} VERTICES</small></header><h2>{zone.name}</h2><strong className="geofence-limit">{info.limitLabel}</strong><p>{zone.description ?? "No operational notes provided."}</p><footer>CREATED {new Date(zone.createdAt).toLocaleDateString()}</footer><div className="geofence-actions"><button className="module-secondary" disabled={busy} onClick={() => void open(zone.id, "view")}>DETAILS</button>{canManage && <><button className="module-secondary" disabled={busy} onClick={() => void open(zone.id, "edit")}>EDIT</button><button className="module-danger" disabled={busy} onClick={() => void open(zone.id, "delete")}>DELETE</button></>}</div></article>; })}</section>}
  </>;
}
