import type { GeofenceCoordinate, MissionFence, MissionWaypoint } from "../portal/types";

export const fenceDistance = (a: GeofenceCoordinate, b: GeofenceCoordinate) => {
  const r = Math.PI / 180;
  const h = Math.sin((b.latitude-a.latitude)*r/2)**2 + Math.cos(a.latitude*r)*Math.cos(b.latitude*r)*Math.sin((b.longitude-a.longitude)*r/2)**2;
  return 6371000 * 2 * Math.asin(Math.sqrt(Math.min(1, Math.max(0,h))));
};
export function missionFencesError(fences: MissionFence[]): string | null {
  const coord = (p?: GeofenceCoordinate | null) => p && Number.isFinite(p.latitude) && Math.abs(p.latitude)<=90 && Number.isFinite(p.longitude) && Math.abs(p.longitude)<=180;
  if (fences.length>20 || new Set(fences.map(f=>f.id)).size!==fences.length) return "Use up to 20 uniquely identified mission fences.";
  for (const f of fences) {
    if (!f.id || f.id.length>64) return "Invalid fence identity.";
    if (f.shape==="Circle") {
      if (!coord(f.center) || !Number.isFinite(f.radiusMeters) || f.radiusMeters!<1 || f.radiusMeters!>100000) return "Circle needs a valid center and radius of 1–100000 m.";
    } else if (f.shape!=="Polygon" || !f.vertices || f.vertices.length<3 || f.vertices.length>100 || f.vertices.some(p=>!coord(p))) return "Polygon needs 3–100 valid vertices.";
  }
  return null;
}
export function withMissionFences(points: MissionWaypoint[], fences: MissionFence[]): MissionWaypoint[] {
  return points.map((point,index)=>{
    const navigation={...point.navigation}; delete navigation.missionFences;
    if(index===0 && fences.length) navigation.missionFences=fences;
    return {...point,navigation:Object.keys(navigation).length?navigation:null};
  });
}
