import type { MissionWaypoint, PlannedHome } from "../portal/types";

export function plannedHomeError(home: PlannedHome | null): string | null {
  if (!home) return null;
  if (!Number.isFinite(home.latitude) || home.latitude < -90 || home.latitude > 90
    || !Number.isFinite(home.longitude) || home.longitude < -180 || home.longitude > 180)
    return "Enter valid Home latitude and longitude, or select Home on the map.";
  if (home.altitudeMslMeters != null && (!Number.isFinite(home.altitudeMslMeters) || home.altitudeMslMeters < -500 || home.altitudeMslMeters > 10000))
    return "Home elevation must be between -500 and 10000 m MSL, or left blank.";
  return null;
}

export function withPlannedHome(points: MissionWaypoint[], home: PlannedHome | null): MissionWaypoint[] {
  return points.map((point, index) => {
    const navigation = { ...point.navigation };
    delete navigation.plannedHome;
    if (index === 0 && home) navigation.plannedHome = home;
    return { ...point, navigation: Object.keys(navigation).length ? navigation : null };
  });
}
