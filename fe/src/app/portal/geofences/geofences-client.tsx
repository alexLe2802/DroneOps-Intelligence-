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
const errorText = (value: unknown) => value instanceof ApiError || value instanceof Error ? value.message : "Không thể hoàn tất thao tác.";

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
    if (!editor.name.trim()) return setActionError("Tên khu vực bay là bắt buộc.");
    if (editor.coordinates.length < 3 || distinct.size < 3) return setActionError("Khu vực bay cần tối thiểu 3 điểm tọa độ khác nhau.");
    const body: SaveGeofenceRequest = { name: editor.name.trim(), description: editor.description.trim() || null, coordinates: editor.coordinates };
    setBusy(true); setActionError("");
    try {
      const saved = editor.mode === "edit" && editor.id ? await updateGeofence(editor.id, body) : await createGeofence(body);
      setEditor(null); await load(); setSelected(saved);
    } catch (reason) { setActionError(errorText(reason)); } finally { setBusy(false); }
  }
  async function remove(zone: Geofence) {
    if (!window.confirm(`Xóa khu vực “${zone.name}”? Thao tác này không thể hoàn tác.`)) return;
    setBusy(true); setActionError("");
    try { await deleteGeofence(zone.id); setSelected(null); await load(); }
    catch (reason) { setActionError(errorText(reason)); } finally { setBusy(false); }
  }

  if (error) return <ModuleError error={error} retry={load} />;
  if (!zones) return <ModuleLoading label="Loading controlled airspace" />;
  return <>
    <header className="module-heading geofence-heading"><div><p className="module-kicker">AIRSPACE / GIS REGISTRY</p><h1>Quản lý khu vực bay</h1><p>Dữ liệu vùng cấm bay và vùng giới hạn bay từ DroneOps Geofences API.</p></div>{manager && <button className="module-primary" onClick={() => { setEditor({ mode: "create", name: "", description: "", coordinates: [] }); setActionError(""); }}>+ THÊM KHU VỰC</button>}</header>
    <section className="module-toolbar geofence-toolbar"><label>TÌM KHU VỰC<input value={query} onChange={event => setQuery(event.target.value)} placeholder="Tên hoặc mô tả khu vực" /></label><span>{filtered.length} / {zones.length} KHU VỰC</span></section>
    {actionError && !editor && <div className="geofence-action-error" role="alert">{actionError}</div>}
    <div className="geofence-management-layout">
      <section className="geofence-registry-panel">{filtered.length === 0 ? <ModuleEmpty title="Không tìm thấy khu vực" message={zones.length ? "Hãy thử từ khóa khác." : "Chưa có khu vực bay nào."} /> : <div className="geofence-registry-list">{filtered.map(zone => { const info = describeGeofence(zone); return <button type="button" className={`geofence-registry-item ${selected?.id === zone.id ? "selected" : ""}`} key={zone.id} onClick={() => void choose(zone)}><i style={{ backgroundColor: info.color }} /><span><strong>{zone.name}</strong><small>{info.label} · {zone.coordinates.length} điểm</small></span><b>›</b></button>; })}</div>}</section>
      <aside className="geofence-detail-panel">{!selected ? <ModuleEmpty title="Chọn một khu vực" message="Chi tiết khu vực sẽ hiển thị tại đây." /> : (() => { const info = describeGeofence(selected); return <><div className="geofence-detail-title"><div><p className="module-kicker">AIRSPACE RECORD</p><h2>{selected.name}</h2></div><span style={{ color: info.color }}>{info.label}</span></div><dl className="geofence-detail-facts"><div><dt>GIỚI HẠN</dt><dd>{info.limitLabel}</dd></div><div><dt>SỐ ĐIỂM</dt><dd>{selected.coordinates.length}</dd></div><div><dt>NGÀY TẠO</dt><dd>{new Date(selected.createdAt).toLocaleDateString("vi-VN")}</dd></div></dl><p className="geofence-description">{selected.description || "Chưa có mô tả vận hành."}</p><div className="coordinate-list">{selected.coordinates.map((point, index) => <code key={`${index}-${point.latitude}-${point.longitude}`}>{String(index + 1).padStart(2, "0")} · {point.latitude.toFixed(6)}, {point.longitude.toFixed(6)}</code>)}</div>{manager && <div className="geofence-detail-actions"><button onClick={() => openEdit(selected)}>CHỈNH SỬA</button><button className="danger" disabled={busy} onClick={() => void remove(selected)}>XÓA KHU VỰC</button></div>}</>; })()}</aside>
    </div>
    {editor && <div className="geofence-modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget && !busy) setEditor(null); }}><section className="geofence-editor-modal" role="dialog" aria-modal="true"><header><div><p className="module-kicker">AIRSPACE EDITOR</p><h2>{editor.mode === "create" ? "Thêm khu vực bay" : "Chỉnh sửa khu vực bay"}</h2></div><button aria-label="Đóng" onClick={() => setEditor(null)}>×</button></header><div className="geofence-editor-grid"><div className="geofence-editor-fields"><label>TÊN KHU VỰC<input maxLength={100} value={editor.name} onChange={event => setEditor({ ...editor, name: event.target.value })} /></label><label>MÔ TẢ<textarea value={editor.description} onChange={event => setEditor({ ...editor, description: event.target.value })} /></label><div className="geofence-coordinate-summary"><span>TỌA ĐỘ ĐA GIÁC</span><strong>{editor.coordinates.length} ĐIỂM</strong></div><div className="geofence-coordinate-scroll">{editor.coordinates.length === 0 ? <p>Nhấp lên bản đồ để chọn điểm.</p> : editor.coordinates.map((point, index) => <div key={`${index}-${point.latitude}-${point.longitude}`}><code>{index + 1}. {point.latitude.toFixed(6)}, {point.longitude.toFixed(6)}</code><button onClick={() => setEditor({ ...editor, coordinates: editor.coordinates.filter((_, i) => i !== index) })}>×</button></div>)}</div><div className="geofence-point-actions"><button disabled={!editor.coordinates.length} onClick={() => setEditor({ ...editor, coordinates: editor.coordinates.slice(0, -1) })}>BỎ ĐIỂM CUỐI</button><button disabled={!editor.coordinates.length} onClick={() => setEditor({ ...editor, coordinates: [] })}>XÓA TẤT CẢ</button></div></div><GeofenceEditorMap coordinates={editor.coordinates} zones={zones} editingId={editor.id} onChange={coordinates => setEditor(current => current ? { ...current, coordinates } : current)} /></div>{actionError && <div className="geofence-action-error" role="alert">{actionError}</div>}<footer><span>{editor.coordinates.length >= 3 ? "Đã đủ số điểm tối thiểu." : `Cần thêm ${3 - editor.coordinates.length} điểm.`}</span><div><button onClick={() => setEditor(null)}>HỦY</button><button className="module-primary" disabled={busy} onClick={() => void save()}>{busy ? "ĐANG LƯU…" : "LƯU KHU VỰC"}</button></div></footer></section></div>}
  </>;
}
