type ArmingTelemetry = {
  missionId: string;
  armed: boolean | null | undefined;
  connectivityStatus: string;
  isStale: boolean;
};

// An absent/stale heartbeat is not evidence that the motors are disarmed.
export function armingDisplay(telemetry: ArmingTelemetry | null, missionId: string,
  streamLive: boolean, packetAgeSeconds: number | null) {
  const sameAircraft = Boolean(missionId && telemetry?.missionId === missionId);
  const known = sameAircraft && typeof telemetry?.armed === "boolean";
  const fresh = known && streamLive && telemetry?.connectivityStatus.toLowerCase() === "online"
    && !telemetry.isStale && packetAgeSeconds !== null && Number.isFinite(packetAgeSeconds)
    && packetAgeSeconds >= 0 && packetAgeSeconds <= 15;
  return {
    label: !known ? "ARM STATE UNKNOWN" : fresh ? telemetry!.armed ? "ARMED" : "DISARMED"
      : telemetry!.armed ? "LAST KNOWN: ARMED" : "LAST KNOWN: DISARMED",
    remindToArm: fresh && telemetry?.armed === false,
    state: fresh ? telemetry!.armed ? "armed" : "disarmed" : "unknown",
  };
}
