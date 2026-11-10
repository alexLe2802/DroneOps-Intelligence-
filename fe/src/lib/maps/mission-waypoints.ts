import type { MissionWaypoint } from "@/lib/portal/types";

type WaypointPosition = Omit<MissionWaypoint, "sequenceOrder" | "actionType">;

export function normalizeWaypointActions(current: MissionWaypoint[]): MissionWaypoint[] {
  return current.map((point, index) => {
    const isFirst = index === 0;
    const isLast = current.length > 1 && index === current.length - 1;
    const actionType = isFirst
      ? "TakeOff"
      : isLast
        ? point.actionType === "Return" ? "Return" : "Land"
        : point.actionType === "TakeOff" || point.actionType === "Land" || point.actionType === "Return"
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
    actionType: current.at(-1)?.actionType === "Return" ? "Return" : "Land",
  }]);
}

export const waypointActionLabel = (action: string) => action === "TakeOff" ? "Takeoff/Home" : action === "Return" ? "Return/RTL" : action;
