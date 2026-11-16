import type { MissionDetail, MissionWaypoint } from "./types";

type AssessmentPlan = Pick<
  MissionDetail,
  | "uavId"
  | "averageSpeedMetersPerSecond"
  | "maxSpeedMetersPerSecond"
  | "startTime"
  | "endTime"
  | "waypoints"
  | "detailAvailable"
>;

export type AssessmentReadiness = {
  blockers: string[];
  ready: boolean;
};

function validCoordinate(point: MissionWaypoint) {
  return Number.isFinite(point.latitude)
    && point.latitude >= -90
    && point.latitude <= 90
    && Number.isFinite(point.longitude)
    && point.longitude >= -180
    && point.longitude <= 180;
}

export function assessmentReadiness(plan: AssessmentPlan | null): AssessmentReadiness {
  if (!plan || plan.detailAvailable === false) {
    return { ready: false, blockers: ["Mission details are not available yet."] };
  }

  const blockers: string[] = [];
  if (!plan.uavId) blockers.push("Assign a UAV to this mission.");

  if (plan.waypoints.length < 2) {
    blockers.push("Add at least two route waypoints.");
  } else {
    if (plan.waypoints.some(point => !validCoordinate(point))) {
      blockers.push("Correct invalid waypoint coordinates.");
    }
    if (plan.waypoints.some(point => point.altitude == null || !Number.isFinite(point.altitude))) {
      blockers.push("Provide an altitude for every waypoint.");
    }
  }

  const averageSpeed = plan.averageSpeedMetersPerSecond;
  const maximumSpeed = plan.maxSpeedMetersPerSecond;
  if (averageSpeed == null || !Number.isFinite(averageSpeed) || averageSpeed <= 0) {
    blockers.push("Provide a positive cruise speed.");
  }
  if (maximumSpeed == null || !Number.isFinite(maximumSpeed) || maximumSpeed <= 0) {
    blockers.push("Provide a positive maximum speed.");
  } else if (averageSpeed != null && Number.isFinite(averageSpeed) && averageSpeed > maximumSpeed) {
    blockers.push("Maximum speed must be at least the cruise speed.");
  }

  const start = plan.startTime ? Date.parse(plan.startTime) : Number.NaN;
  const end = plan.endTime ? Date.parse(plan.endTime) : Number.NaN;
  if (!Number.isFinite(start) || !Number.isFinite(end)) {
    blockers.push("Provide a valid mission start and end time.");
  } else if (end <= start) {
    blockers.push("Mission end time must be later than its start time.");
  }

  return { blockers, ready: blockers.length === 0 };
}
