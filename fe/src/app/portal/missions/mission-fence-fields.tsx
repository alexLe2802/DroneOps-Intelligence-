"use client";
import type { MissionFence, GeofenceCoordinate } from "@/lib/portal/types";
import { useState } from "react";
import { fenceDistance } from "@/lib/maps/mission-fences";
import styles from "./planned-home-fields.module.css";

export function useFenceDrawing(fences:MissionFence[], onChange:(fences:MissionFence[])=>void) {
  const [drawing,setDrawing]=useState<"Polygon"|"Circle"|null>(null);
  const [draft,setDraft]=useState<GeofenceCoordinate[]>([]);
  function begin(shape:"Polygon"|"Circle"|null) { setDrawing(shape); setDraft([]); }
  function point(longitude:number,latitude:number) {
    if(!drawing) return false;
    const p={latitude,longitude};
    if(drawing==="Polygon") { setDraft(current=>current.length<100?[...current,p]:current); return true; }
    if(!draft.length) setDraft([p]);
    else { const radius=Math.round(fenceDistance(draft[0],p)); if(radius>=1 && radius<=100000) {onChange([...fences,{id:crypto.randomUUID(),shape:"Circle",center:draft[0],radiusMeters:radius}]);begin(null);} }
    return true;
  }
  function finish() { if(drawing==="Polygon" && draft.length>=3) {onChange([...fences,{id:crypto.randomUUID(),shape:"Polygon",vertices:draft}]);begin(null);} }
  return {drawing,draft,begin,point,finish};
}

export default function MissionFenceFields({ fences, onChange, drawing, onDraw, vertexCount, onFinish, disabled=false }: {
  fences: MissionFence[]; onChange:(fences:MissionFence[])=>void; drawing:"Polygon"|"Circle"|null;
  onDraw:(shape:"Polygon"|"Circle"|null)=>void; vertexCount:number; onFinish:()=>void; disabled?:boolean;
}) {
  return <section className={styles.panel} aria-label="Mission geofence">
    <div className={styles.header}>
      <div>
        <h2>GeoFence · permitted flight areas</h2>
        <p>Stay inside at least one area. Leaving all areas raises a geofence incident during the mission.</p>
      </div>
      <span className={styles.badge}>{fences.length} / 20 FENCES</span>
    </div>
    <div className={styles.actions}>
      <button type="button" className="module-secondary" aria-pressed={drawing==="Polygon"} disabled={disabled||fences.length>=20} onClick={()=>onDraw(drawing==="Polygon"?null:"Polygon")}>POLYGON FENCE</button>
      <button type="button" className="module-secondary" aria-pressed={drawing==="Circle"} disabled={disabled||fences.length>=20} onClick={()=>onDraw(drawing==="Circle"?null:"Circle")}>CIRCULAR FENCE</button>
      {drawing==="Polygon" && <button type="button" className="module-primary" disabled={disabled||vertexCount<3} onClick={onFinish}>FINISH POLYGON ({vertexCount})</button>}
      {drawing && <button type="button" className={styles.cancelButton} disabled={disabled} onClick={()=>onDraw(null)}>CANCEL DRAWING</button>}
    </div>
    {drawing && <p className={styles.drawingHint} role="status">{drawing==="Polygon"?"Click the map below to add vertices (at least 3), then click Finish polygon.":"Click the map below for the center, then click the edge to set the radius."}</p>}
    {!fences.length ? <p className={styles.emptyState}>No mission fences configured yet. Choose Polygon fence or Circular fence above, then click on the map below.</p> : <div className={styles.fenceList}>
      {fences.map((f,i)=>{
        const invalidRadius = f.shape === "Circle" && (!Number.isFinite(f.radiusMeters) || f.radiusMeters! < 1 || f.radiusMeters! > 100000);
        return <div key={f.id} className={styles.fenceItem}>
          <div className={styles.fenceMeta}>
            <span className={styles.fenceIndex}>{String(i+1).padStart(2, "0")}</span>
            <strong>{f.shape==="Circle"?"Circular fence":"Polygon fence"}</strong>
            {f.shape==="Polygon" && <span className={styles.fenceVertices}>{f.vertices?.length ?? 0} vertices</span>}
          </div>
          <div className={styles.fenceControls}>
            {f.shape==="Circle" && <div className={styles.radiusGroup}>
              <label htmlFor={`mission-fence-radius-${f.id}`}>Radius (m)</label>
              <span className={`${styles.radiusInputWrap}${invalidRadius ? ` ${styles.radiusInvalid}` : ""}`}>
                <input id={`mission-fence-radius-${f.id}`} name={`fenceRadius_${i+1}`} data-field-label={`Fence ${i+1} radius`} type="number" min="1" max="100000" step="1" required aria-invalid={invalidRadius} aria-describedby={invalidRadius ? `mission-fence-error-${f.id}` : undefined} value={Number.isFinite(f.radiusMeters) ? f.radiusMeters! : ""} disabled={disabled} onChange={e=>onChange(fences.map(x=>x.id===f.id?{...x,radiusMeters:e.target.value===""?NaN:Number(e.target.value)}:x))}/>
                <span className={styles.radiusUnit} aria-hidden="true">m</span>
              </span>
              {invalidRadius && <span id={`mission-fence-error-${f.id}`} className={styles.fenceError}><span role="img" aria-label="Error">!</span> 1–100,000 m</span>}
            </div>}
            <button type="button" className={styles.deleteButton} aria-label={`Delete fence ${i+1}`} disabled={disabled} onClick={()=>onChange(fences.filter(x=>x.id!==f.id))}>DELETE</button>
          </div>
        </div>;
      })}
    </div>}
    <p className={styles.helperNote}>Drag polygon vertices or a circle center to move it. Drag the circle edge handle to resize; the radius updates automatically. This fence monitors incidents; onboard FC fence enforcement is separate.</p>
  </section>;
}
