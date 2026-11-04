import type { CreateUavRequest } from "@/lib/portal/types";

export function optionalNumber(value: string): number | null {
  return value.trim() === "" ? null : Number(value);
}

export function uavSpecificationError(request: CreateUavRequest): string | null {
  const positive: Array<[number | null, string]> = [
    [request.emptyWeightKg, "Empty weight"],
    [request.maxTakeoffWeightKg, "Maximum takeoff weight"],
    [request.batteryCapacityMah, "Battery capacity"],
    [request.batteryVoltage, "Nominal battery voltage"],
    [request.batteryMaxVoltage ?? null, "Maximum pack voltage"],
    [request.batteryCellCount, "Battery cell count"],
    [request.maxFlightTimeMinutes, "Maximum flight time"],
    [request.maxRangeKm, "Maximum range"],
    [request.maxSpeedKmh, "Maximum speed"],
    [request.maxWindSpeedMps, "Maximum wind speed"],
    [request.maxOperatingAltitudeMeters, "Maximum operating altitude"],
  ];
  const invalidPositive = positive.find(([value]) => value != null && (!Number.isFinite(value) || value <= 0));
  if (invalidPositive) return `${invalidPositive[1]} must be greater than 0.`;
  if (request.maxPayloadKg != null && (!Number.isFinite(request.maxPayloadKg) || request.maxPayloadKg < 0)) return "Maximum payload cannot be negative.";
  if (request.batteryCellCount != null && (!Number.isInteger(request.batteryCellCount) || request.batteryCellCount > 100)) return "Battery cell count must be a whole number from 1 to 100.";
  for (const [value, label] of [[request.batteryCapacityMah, "Battery capacity"], [request.maxFlightTimeMinutes, "Maximum flight time"]] as const) {
    if (value != null && !Number.isInteger(value)) return `${label} must be a whole number.`;
  }
  if (request.emptyWeightKg != null && request.maxTakeoffWeightKg != null && request.maxTakeoffWeightKg < request.emptyWeightKg) return "Maximum takeoff weight cannot be less than empty weight.";
  if (request.emptyWeightKg != null && request.maxPayloadKg != null && request.maxTakeoffWeightKg != null && request.emptyWeightKg + request.maxPayloadKg > request.maxTakeoffWeightKg) return "Empty weight plus maximum payload cannot exceed maximum takeoff weight.";
  if (request.batteryMaxVoltage != null && request.batteryVoltage != null && request.batteryMaxVoltage < request.batteryVoltage) return "Maximum pack voltage cannot be below the stored nominal voltage.";
  return null;
}
