import type { AiAssessment, MissionTelemetry } from "./types";

export function assessmentBatteryState(telemetry: MissionTelemetry | null, missionId: string, uavId: string | null,
  assessment: AiAssessment | undefined, now: number) {
  const timestamps = [telemetry?.sourceTimestamp, telemetry?.receivedAt, telemetry?.batteryObservedAt ?? telemetry?.sourceTimestamp]
    .map(value => value ? Date.parse(value) : NaN);
  const age = Math.max(...timestamps.map(value => (now - value) / 1000));
  const fresh = Boolean(telemetry && uavId && telemetry.missionId === missionId && telemetry.uavId === uavId
    && telemetry.sourceId && telemetry.isValid && !telemetry.isStale && telemetry.snapshotKind === "Latest"
    && telemetry.connectivityStatus === "Online" && timestamps.every(value => Number.isFinite(value) && value <= now + 5000)
    && age <= Math.min(15, telemetry.staleAfterSeconds ?? 10));
  const percent = fresh && telemetry?.batteryPercent != null && Number.isFinite(telemetry.batteryPercent)
    && telemetry.batteryPercent >= 0 && telemetry.batteryPercent <= 100 ? telemetry.batteryPercent : null;
  const battery = assessment?.result?.battery;
  const captured = Date.parse(assessment?.completedAt ?? assessment?.createdAt ?? "");
  const estimateCurrent = Boolean(assessment && assessment.missionRef === missionId && !assessment.isOutdated
    && Number.isFinite(captured) && captured <= now && now - captured <= 600000);
  const required = estimateCurrent && battery?.requiredBatteryPercent != null && Number.isFinite(battery.requiredBatteryPercent)
    && battery.requiredBatteryPercent >= 0 ? battery.requiredBatteryPercent : null;
  const invalidPower = fresh && telemetry?.batteryVoltageVolts != null && telemetry.batteryVoltageVolts < 1;
  const margin = percent != null && required != null && !invalidPower ? percent - required : null;
  return { fresh, age: Number.isFinite(age) ? Math.max(0, age) : null, percent, required, margin, invalidPower, estimateCurrent };
}
