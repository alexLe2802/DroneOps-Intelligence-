"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import type { Account } from "@/lib/auth/types";
import { ApiError } from "@/lib/auth/client";
import { createGeofence, deleteGeofence, getGeofence, getGeofences, updateGeofence } from "@/lib/portal/client";
import type { Geofence, GeofenceCoordinate, SaveGeofenceRequest } from "@/lib/portal/types";
import { describeGeofence } from "@/lib/maps/geofences";
import { ModuleEmpty, ModuleError, ModuleLoading } from "../module-state";
import GeofenceEditorMap from "./geofence-editor-map";

type Editor = { mode: "create" | "edit"; id?: string; name: string; description: string; coordinates: GeofenceCoordinate[] };
const errorText = (value: unknown) => value instanceof ApiError || value instanceof Error ? value.message : "Unable to complete this action.";

export default function GeofencesClient({ account }: { account: Account }) {
  const manager = account.role === "operations_manager";
  const [zones, setZones] = useState<Geofence[] | null>(null);
  const [selected, setSelected] = useState<Geofence | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [actionError, setActionError] = useState("");
  const [query, setQuery] = useState("");
  const [editor, setEditor] = useState<Editor | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setError(null);
    try {
      const data = await getGeofences(); setZones(data);
      setSelected(current => current ? data.find(zone => zone.id === current.id) ?? data[0] ?? null : data[0] ?? null);
    } catch (reason) { setError(reason); }
  }, []);
  useEffect(() => {
    let active = true;
    void getGeofences().then(data => {
      if (!active) return;
      setZones(data); setSelected(data[0] ?? null);
    }).catch(reason => { if (active) setError(reason); });
    return () => { active = false; };
  }, []);
  const filtered = useMemo(() => zones?.filter(zone => `${zone.name} ${zone.description ?? ""}`.toLowerCase().includes(query.trim().toLowerCase())) ?? [], [zones, query]);

  async function choose(zone: Geofence) {
    setSelected(zone); setActionError("");
    try { setSelected(await getGeofence(zone.id)); } catch (reason) { setActionError(errorText(reason)); }
  }
  function openEdit(zone: Geofence) {
    setEditor({ mode: "edit", id: zone.id, name: zone.name, description: zone.description ?? "", coordinates: zone.coordinates.map(point => ({ ...point })) }); setActionError("");
  }
  async function save() {
    if (!editor) return;
    const distinct = new Set(editor.coordinates.map(point => `${point.latitude},${point.longitude}`));
    if (!editor.name.trim()) return setActionError("An airspace name is required.");
    if (editor.coordinates.length < 3 || distinct.size < 3) return setActionError("An airspace boundary requires at least three distinct vertices.");
    const body: SaveGeofenceRequest = { name: editor.name.trim(), description: editor.description.trim() || null, coordinates: editor.coordinates };
    setBusy(true); setActionError("");
    try {
      const saved = editor.mode === "edit" && editor.id ? await updateGeofence(editor.id, body) : await createGeofence(body);
      setEditor(null); await load(); setSelected(saved);
    } catch (reason) { setActionError(errorText(reason)); } finally { setBusy(false); }
  }
  async function remove(zone: Geofence) {
    if (!window.confirm(`Delete airspace “${zone.name}”? This action cannot be undone.`)) return;
    setBusy(true); setActionError("");
    try { await deleteGeofence(zone.id); setSelected(null); await load(); }
    catch (reason) { setActionError(errorText(reason)); } finally { setBusy(false); }
  }

  if (error) return <ModuleError error={error} retry={load} />;
  if (!zones) return <ModuleLoading label="Loading controlled airspace" />;
  return <>
    <header className="module-heading geofence-heading"><div><p className="module-kicker">AIRSPACE / GIS REGISTRY</p><h1>Airspace Management</h1><p>View and manage prohibited and restricted airspace.</p></div>{manager && <button className="module-primary" onClick={() => { setEditor({ mode: "create", name: "", description: "", coordinates: [] }); setActionError(""); }}>+ ADD AIRSPACE</button>}</header>
    <section className="module-toolbar geofence-toolbar"><label>SEARCH AIRSPACE<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Airspace name or description" /></label><span>{filtered.length} / {zones.length} AIRSPACE ZONES</span></section>
    {actionError && !editor && <div className="geofence-action-error" role="alert">{actionError}</div>}
    <div className="geofence-management-layout">
      <section className="geofence-registry-panel">{filtered.length === 0 ? <ModuleEmpty title="No airspace found" message={zones.length ? "Try a different search term." : "No airspace has been configured."} /> : <div className="geofence-registry-list">{filtered.map(zone => { const info = describeGeofence(zone); return <button type="button" className={`geofence-registry-item ${selected?.id === zone.id ? "selected" : ""}`} key={zone.id} onClick={() => void choose(zone)}><i style={{ backgroundColor: info.color }} /><span><strong>{zone.name}</strong><small>{info.label} · {zone.coordinates.length} vertices</small></span><b>›</b></button>; })}</div>}</section>
      <aside className="geofence-detail-panel">{!selected ? <ModuleEmpty title="Select an airspace zone" message="Select a zone to view its details." /> : (() => { const info = describeGeofence(selected); return <><div className="geofence-detail-title"><div><p className="module-kicker">AIRSPACE RECORD</p><h2>{selected.name}</h2></div><span style={{ color: info.color }}>{info.label}</span></div><dl className="geofence-detail-facts"><div><dt>LIMITS</dt><dd>{info.limitLabel}</dd></div><div><dt>VERTICES</dt><dd>{selected.coordinates.length}</dd></div><div><dt>CREATED</dt><dd>{new Date(selected.createdAt).toLocaleDateString("en-GB")}</dd></div></dl><p className="geofence-description">{selected.description?.replaceAll("Vùng hạn chế bay.", "Restricted airspace.").replaceAll("Vùng giới hạn bay.", "Restricted airspace.").replaceAll("Vùng cấm bay.", "Prohibited airspace.") || "No operational description provided."}</p><div className="coordinate-list">{selected.coordinates.map((point, index) => <code key={`${index}-${point.latitude}-${point.longitude}`}>{String(index + 1).padStart(2, "0")} · {point.latitude.toFixed(6)}, {point.longitude.toFixed(6)}</code>)}</div>{manager && <div className="geofence-detail-actions"><button onClick={() => openEdit(selected)}>EDIT</button><button className="danger" disabled={busy} onClick={() => void remove(selected)}>DELETE AIRSPACE</button></div>}</>; })()}</aside>
    </div>
    {editor && <div className="geofence-modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget && !busy) setEditor(null); }}><section className="geofence-editor-modal" role="dialog" aria-modal="true"><header><div><p className="module-kicker">AIRSPACE EDITOR</p><h2>{editor.mode === "create" ? "Add airspace" : "Edit airspace"}</h2></div><button aria-label="Close" onClick={() => setEditor(null)}>×</button></header><div className="geofence-editor-grid"><div className="geofence-editor-fields"><label>AIRSPACE NAME<input maxLength={100} value={editor.name} onChange={event => setEditor({ ...editor, name: event.target.value })} /></label><label>DESCRIPTION<textarea value={editor.description} onChange={event => setEditor({ ...editor, description: event.target.value })} /></label><div className="geofence-coordinate-summary"><span>POLYGON COORDINATES</span><strong>{editor.coordinates.length} VERTICES</strong></div><div className="geofence-coordinate-scroll">{editor.coordinates.length === 0 ? <p>Click the map to add vertices.</p> : editor.coordinates.map((point, index) => <div key={`${index}-${point.latitude}-${point.longitude}`}><code>{index + 1}. {point.latitude.toFixed(6)}, {point.longitude.toFixed(6)}</code><button onClick={() => setEditor({ ...editor, coordinates: editor.coordinates.filter((_, i) => i !== index) })}>×</button></div>)}</div><div className="geofence-point-actions"><button disabled={!editor.coordinates.length} onClick={() => setEditor({ ...editor, coordinates: editor.coordinates.slice(0, -1) })}>UNDO LAST VERTEX</button><button disabled={!editor.coordinates.length} onClick={() => setEditor({ ...editor, coordinates: [] })}>CLEAR ALL</button></div></div><GeofenceEditorMap coordinates={editor.coordinates} zones={zones} editingId={editor.id} onChange={coordinates => setEditor(current => current ? { ...current, coordinates } : current)} /></div>{actionError && <div className="geofence-action-error" role="alert">{actionError}</div>}<footer><span>{editor.coordinates.length >= 3 ? "Minimum vertex count reached." : `Add ${3 - editor.coordinates.length} more vertices.`}</span><div><button onClick={() => setEditor(null)}>CANCEL</button><button className="module-primary" disabled={busy} onClick={() => void save()}>{busy ? "SAVING…" : "SAVE AIRSPACE"}</button></div></footer></section></div>}
  </>;
}
