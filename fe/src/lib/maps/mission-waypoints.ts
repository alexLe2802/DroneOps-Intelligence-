import type { MissionWaypoint, PlannedHome } from "@/lib/portal/types";

type WaypointPosition = Omit<MissionWaypoint, "sequenceOrder" | "actionType">;

export function appendMissionPoint(current: MissionWaypoint[], home: PlannedHome | null, position: WaypointPosition, automatic: boolean): { waypoints: MissionWaypoint[]; plannedHome: PlannedHome | null } {
  if (automatic && home === null)
    return { waypoints: current, plannedHome: { latitude: position.latitude, longitude: position.longitude } };
  return { waypoints: appendLandingWaypoint(current, position), plannedHome: home };
}

export function normalizeWaypointActions(current: MissionWaypoint[]): MissionWaypoint[] {
  return current.map((point, index) => {
    const isFirst = index === 0;
    const isLast = current.length > 1 && index === current.length - 1;
    const actionType = isFirst
      ? "TakeOff"
      : isLast
        ? "Land"
        : point.actionType === "TakeOff" || point.actionType === "Land"
          ? "FlyThrough"
          : point.actionType;
    return { ...point, sequenceOrder: index + 1, actionType };
  });
}

export function appendLandingWaypoint(current: MissionWaypoint[], position: WaypointPosition): MissionWaypoint[] {
  const previous = current.map((point, index) => current.length > 1 && index === current.length - 1
    ? { ...point, actionType: "FlyThrough" as const }
    : point);

  return normalizeWaypointActions([...previous, {
    ...position,
    sequenceOrder: current.length + 1,
    actionType: "FlyThrough",
  }]);
}
