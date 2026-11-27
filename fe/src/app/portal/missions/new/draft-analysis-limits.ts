import type { AutomaticFlightLimits } from "@/lib/portal/types";

export type AutomaticLimitFieldKey =
  | "takeoffSpeedMetersPerSecond" | "climbSpeedMetersPerSecond" | "descentSpeedMetersPerSecond" | "landingSpeedMetersPerSecond"
  | "maxAltitudeMeters" | "maxWindMetersPerSecond" | "maxGustMetersPerSecond";

export const automaticLimitGroups: { title: string; fields: { key: AutomaticLimitFieldKey; label: string; unit: string }[] }[] = [
  { title: "Vertical speeds", fields: [
    { key: "takeoffSpeedMetersPerSecond", label: "Takeoff", unit: "m/s" },
    { key: "climbSpeedMetersPerSecond", label: "Climb", unit: "m/s" },
    { key: "descentSpeedMetersPerSecond", label: "Descent", unit: "m/s" },
    { key: "landingSpeedMetersPerSecond", label: "Landing", unit: "m/s" },
  ] },
  { title: "Altitude & weather", fields: [
    { key: "maxAltitudeMeters", label: "Altitude ceiling", unit: "m" },
    { key: "maxWindMetersPerSecond", label: "Wind limit", unit: "m/s" },
    { key: "maxGustMetersPerSecond", label: "Gust limit", unit: "m/s" },
  ] },
];

export const automaticLimitKeys = automaticLimitGroups.flatMap(group => group.fields.map(field => field.key));

export function matchesNumericValue(current: number | null | undefined, target: number | null | undefined): boolean {
  return current != null && target != null && Number.isFinite(current) && Number.isFinite(target) && Math.abs(current - target) < 1e-6;
}

// An Apply button is offered while the form value differs from the AI value; it disappears once applied and returns after an edit.
export function differingLimitKeys(current: AutomaticFlightLimits | null | undefined, recommended: AutomaticFlightLimits): AutomaticLimitFieldKey[] {
  return automaticLimitKeys.filter(key => !matchesNumericValue(current?.[key], recommended[key]));
}

