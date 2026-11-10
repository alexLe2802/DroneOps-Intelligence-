"use client";

import styles from "./planned-home-fields.module.css";

import type { MissionWaypoint, PlannedHome } from "@/lib/portal/types";

export default function PlannedHomeFields({ home, onChange, picking, onPick, first, disabled = false }: {
  home: PlannedHome | null; onChange: (home: PlannedHome | null) => void;
  picking: boolean; onPick: () => void; first?: MissionWaypoint; disabled?: boolean;
}) {
  const set = (key: keyof PlannedHome, text: string) => onChange({
    latitude: home?.latitude ?? NaN, longitude: home?.longitude ?? NaN, ...home,
    [key]: text === "" ? (key === "altitudeMslMeters" ? null : NaN) : Number(text),
  });
  return <section className={styles.panel} aria-label="Planned Home">
    <div className="section-heading"><div><h2>Planned Home</h2><p>Select the expected launch and return location. FC Home is checked before mission upload.</p></div></div>
    <div className={styles.actions}>
      <button type="button" className="module-secondary" disabled={disabled} aria-pressed={picking} onClick={onPick}>{picking ? "CLICK MAP TO SET HOME · CANCEL" : "SET HOME ON MAP"}</button>
      <button type="button" className="module-secondary" disabled={disabled || !first || !Number.isFinite(first.latitude) || !Number.isFinite(first.longitude)} onClick={() => first && onChange({ latitude: first.latitude, longitude: first.longitude })}>USE TAKEOFF LOCATION</button>
      {home && <button type="button" className="text-action" disabled={disabled} onClick={() => onChange(null)}>CLEAR HOME</button>}
    </div>
    <div className={styles.grid}>
      <label>HOME LATITUDE<input type="number" min="-90" max="90" step="any" disabled={disabled} value={Number.isFinite(home?.latitude) ? home!.latitude : ""} onChange={e => set("latitude", e.target.value)} placeholder="Select on map or enter latitude" /></label>
      <label>HOME LONGITUDE<input type="number" min="-180" max="180" step="any" disabled={disabled} value={Number.isFinite(home?.longitude) ? home!.longitude : ""} onChange={e => set("longitude", e.target.value)} placeholder="Select on map or enter longitude" /></label>
      <label>HOME ELEVATION (M MSL) · OPTIONAL<input type="number" min="-500" max="10000" step="any" disabled={disabled} value={home?.altitudeMslMeters ?? ""} onChange={e => set("altitudeMslMeters", e.target.value)} placeholder="Use actual FC Home elevation when blank" /></label>
    </div>
  </section>;
}
