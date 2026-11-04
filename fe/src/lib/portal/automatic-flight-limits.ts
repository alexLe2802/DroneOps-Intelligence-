import type { AutomaticFlightLimits } from "./types";
export function formatAutomaticLimits(value: AutomaticFlightLimits | null | undefined): string {
  return value ? `Takeoff ${value.takeoffSpeedMetersPerSecond} m/s · Climb ${value.climbSpeedMetersPerSecond} m/s · Descent ${value.descentSpeedMetersPerSecond} m/s · Landing ${value.landingSpeedMetersPerSecond} m/s · Ceiling ${value.maxAltitudeMeters} m relative to home · Wind ${value.maxWindMetersPerSecond} m/s · Gust ${value.maxGustMetersPerSecond} m/s · ${value.policyVersion}` : "Not provided";
}
