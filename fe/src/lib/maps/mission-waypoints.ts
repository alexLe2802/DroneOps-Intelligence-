import type { MissionWaypoint } from "@/lib/portal/types";

type WaypointPosition = Omit<MissionWaypoint, "sequenceOrder" | "actionType">;

export function appendLandingWaypoint(current: MissionWaypoint[], position: WaypointPosition): MissionWaypoint[] {
  const previous = current.map((point, index) => current.length > 1 && index === current.length - 1
    ? { ...point, actionType: "FlyThrough" as const }
    : point);

  return [...previous, {
    ...position,
    sequenceOrder: current.length + 1,
    actionType: current.length === 0 ? "TakeOff" : "Land",
  }];
}
