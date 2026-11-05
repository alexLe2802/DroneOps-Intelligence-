import type { AutomaticFlightLimits, MissionWaypoint } from "./types";
export function formatAutomaticLimits(value: AutomaticFlightLimits | null | undefined): string {
  return value ? `Takeoff ${value.takeoffSpeedMetersPerSecond} m/s · Climb ${value.climbSpeedMetersPerSecond} m/s · Descent ${value.descentSpeedMetersPerSecond} m/s · Landing ${value.landingSpeedMetersPerSecond} m/s · Ceiling ${value.maxAltitudeMeters} m relative to home · Wind ${value.maxWindMetersPerSecond} m/s · Gust ${value.maxGustMetersPerSecond} m/s · ${value.policyVersion}${value.recoveryEnergyProfile ? ` · Recovery measurements: ${value.recoveryEnergyProfile.measurementReference}; ${value.recoveryEnergyProfile.usableCapacityWh} Wh; ${value.recoveryEnergyProfile.payloadKg} kg payload; ${value.recoveryEnergyProfile.remainingEnergyCurve.length} SOC points — review full measurements below` : ""}` : "Not provided";
}

// Immediate authoring feedback; the API independently validates the same policy.
export function automaticFlightError(frame: string, cruise: number, maximum: number, limits: AutomaticFlightLimits, waypoints: MissionWaypoint[]): string | null {
  if (frame !== "Relative") return "Automatic missions require altitude relative to FC Home.";
  if (!Number.isFinite(cruise) || !Number.isFinite(maximum) || cruise < 0.5 || maximum > 5 || cruise > maximum) return "Automatic cruise must be at least 0.5 m/s and no higher than the speed ceiling (maximum 5 m/s).";
  if (limits.policyVersion !== "multicopter-envelope-v1") return "This automatic-flight profile is unsupported. Reload the mission form.";
  if (Object.values(limits).some(value => typeof value === "number" && !Number.isFinite(value))) return "Complete the automatic flight limits or restore the system defaults.";
  if (!(limits.takeoffSpeedMetersPerSecond >= 1 && limits.takeoffSpeedMetersPerSecond <= 1.5 && limits.climbSpeedMetersPerSecond >= limits.takeoffSpeedMetersPerSecond && limits.climbSpeedMetersPerSecond <= 1.5)) return "Takeoff speed must be 1–1.5 m/s; climb must be at least takeoff speed and no higher than 1.5 m/s.";
  if (!(limits.descentSpeedMetersPerSecond >= 0.6 && limits.descentSpeedMetersPerSecond <= 1 && limits.landingSpeedMetersPerSecond >= 0.6 && limits.landingSpeedMetersPerSecond <= 0.7 && limits.landingSpeedMetersPerSecond <= limits.descentSpeedMetersPerSecond)) return "Descent must be 0.6–1 m/s; landing must be 0.6–0.7 m/s and no higher than descent speed.";
  if (!(limits.maxAltitudeMeters > 10 && limits.maxAltitudeMeters <= 30)) return "Automatic altitude ceiling must be greater than 10 m and no higher than 30 m above Home.";
  if (!(limits.maxWindMetersPerSecond > 0 && limits.maxWindMetersPerSecond <= 3 && limits.maxGustMetersPerSecond >= limits.maxWindMetersPerSecond && limits.maxGustMetersPerSecond <= 5)) return "Forecast wind limit must be above 0 and at most 3 m/s; gust limit must be at least wind and at most 5 m/s.";
  for (const point of waypoints) {
    const prefix = `Waypoint ${point.sequenceOrder}: `;
    if (point.altitude == null || !Number.isFinite(point.altitude) || point.altitude > limits.maxAltitudeMeters) return prefix + `altitude must fit the ${limits.maxAltitudeMeters} m automatic ceiling. Existing route heights are not changed automatically.`;
    const nav = point.navigation;
    if (point.actionType === "Hover" && !(nav?.holdTimeSeconds != null && nav.holdTimeSeconds > 0)) return prefix + "Hover requires a positive hold time. Open its navigation settings.";
    if (!nav) continue;
    if (Object.values(nav).some(value => typeof value === "number" && !Number.isFinite(value))) return prefix + "navigation values must be finite numbers.";
    if (nav.speedMetersPerSecond != null && (nav.speedMetersPerSecond < 0.5 || nav.speedMetersPerSecond > maximum)) return prefix + "speed override must be between 0.5 m/s and the mission speed ceiling.";
    if (point.actionType === "TakeOff" && nav.speedMetersPerSecond != null) return prefix + "TakeOff uses vertical takeoff speed; clear the horizontal speed override.";
    if (nav.holdTimeSeconds != null && (nav.holdTimeSeconds < 0 || nav.holdTimeSeconds > 600)) return prefix + "hold time must be between 0 and 600 seconds.";
    if (nav.acceptanceRadiusMeters != null && (nav.acceptanceRadiusMeters <= 0 || nav.acceptanceRadiusMeters > 10)) return prefix + "acceptance radius must be above 0 and at most 10 m.";
    if (!["FlyThrough", "Hover"].includes(point.actionType) && ((nav.holdTimeSeconds ?? 0) > 0 || nav.acceptanceRadiusMeters != null)) return prefix + "hold and acceptance radius apply only to FlyThrough/Hover.";
    if (nav.passRadiusMeters != null && nav.passRadiusMeters !== 0) return prefix + "nonzero pass radius is unsupported for this multicopter profile.";
    if (nav.yawDegrees != null && (nav.yawDegrees < 0 || nav.yawDegrees >= 360)) return prefix + "yaw must be between 0 (inclusive) and 360 (exclusive).";
    if (nav.emergencyLandingCandidate && (nav.landingGroundAltitudeMeters == null || nav.landingGroundAltitudeMeters < 0 || (nav.landingSiteNotes?.trim().length ?? 0) < 10)) return prefix + "an emergency landing candidate requires nonnegative ground altitude and clearance notes of at least 10 characters.";
    if ((nav.landingSiteNotes?.length ?? 0) > 500) return prefix + "landing-site notes must not exceed 500 characters.";
  }
  return null;
}
