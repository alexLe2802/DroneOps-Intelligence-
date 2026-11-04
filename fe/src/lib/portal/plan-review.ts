import type { CreateMissionRequest, MissionWaypoint } from "./types";
export function planFieldChanges(current: CreateMissionRequest, proposed: CreateMissionRequest) {
  const fields = ["automaticFlightRequested", "automaticFlightLimits", "name", "description", "uavId", "averageSpeedMetersPerSecond", "maxSpeedMetersPerSecond", "startTime", "endTime", "altitudeReference"] as const;
  return fields.filter(key => {
    if (key === "automaticFlightLimits") return JSON.stringify(Object.entries(current[key] ?? {}).sort()) !== JSON.stringify(Object.entries(proposed[key] ?? {}).sort());
    if (key === "automaticFlightRequested") return Boolean(current[key]) !== Boolean(proposed[key]);
    if (key === "startTime" || key === "endTime") {
      const a = Date.parse(current[key]), b = Date.parse(proposed[key]);
      if (Number.isFinite(a) && Number.isFinite(b)) return a !== b;
    }
    return (current[key] ?? "") !== (proposed[key] ?? "");
  });
}
export function waypointReviewRows(current: MissionWaypoint[], proposed: MissionWaypoint[]) {
  const old = new Map(current.map(p => [p.sequenceOrder, p]));
  const next = new Map(proposed.map(p => [p.sequenceOrder, p]));
  return [...new Set([...old.keys(), ...next.keys()])].sort((a,b) => a-b).map(sequence => {
    const before = old.get(sequence), after = next.get(sequence);
    const navigationChanged = JSON.stringify(Object.entries(before?.navigation ?? {}).sort()) !== JSON.stringify(Object.entries(after?.navigation ?? {}).sort());
    const changed = navigationChanged || !before || !after || (["latitude", "longitude", "altitude", "actionType"] as const).some(key => before[key] !== after[key]);
    return { sequence, before, after, changed, status: !before ? "Added" : !after ? "Removed" : changed ? "Changed" : "Unchanged" };
  });
}
