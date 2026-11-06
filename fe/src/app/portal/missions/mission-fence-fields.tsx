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
    <h2>GeoFence · permitted flight areas</h2>
    <p>Stay inside at least one area. Leaving all areas raises a geofence incident during the mission.</p>
    <div className={styles.actions}>
      <button type="button" className="module-secondary" disabled={disabled||fences.length>=20} onClick={()=>onDraw("Polygon")}>POLYGON FENCE</button>
      <button type="button" className="module-secondary" disabled={disabled||fences.length>=20} onClick={()=>onDraw("Circle")}>CIRCULAR FENCE</button>
      {drawing==="Polygon" && <button type="button" className="module-secondary" disabled={disabled||vertexCount<3} onClick={onFinish}>FINISH POLYGON ({vertexCount})</button>}
      {drawing && <button type="button" className="text-action" disabled={disabled} onClick={()=>onDraw(null)}>CANCEL DRAWING</button>}
    </div>
    {drawing && <p role="status">{drawing==="Polygon"?"Click map to add vertices, then Finish polygon.":"Click map for the center, then click the edge to set radius."}</p>}
    {!fences.length && <p>No mission fences.</p>}
    {fences.map((f,i)=><div key={f.id} className={styles.actions}>
      <span>{i+1}. {f.shape==="Circle"?"Circular fence":"Polygon fence"}{f.shape==="Polygon"?` · ${f.vertices?.length ?? 0} vertices`:""}</span>
      {f.shape==="Circle" && <label>Radius (m) <input aria-label={`Fence ${i+1} radius (m)`} type="number" min="1" max="100000" step="1" value={Number.isFinite(f.radiusMeters) ? f.radiusMeters! : ""} disabled={disabled} onChange={e=>onChange(fences.map(x=>x.id===f.id?{...x,radiusMeters:e.target.value===""?NaN:Number(e.target.value)}:x))}/></label>}
      <button type="button" className="text-action" disabled={disabled} onClick={()=>onChange(fences.filter(x=>x.id!==f.id))}>DELETE</button>
    </div>)}
    <p>Drag polygon vertices or a circle center to edit. This fence monitors incidents; onboard FC fence enforcement is separate.</p>
  </section>;
}
