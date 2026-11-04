import type { MissionTelemetry } from "./types";

export function mapLinkState(telemetry: MissionTelemetry | null, streamLive: boolean) {
  const status = telemetry?.connectivityStatus?.toLowerCase() ?? "offline";
  if (!telemetry || status === "offline" || status === "missing") return "offline";
  if (!streamLive || telemetry.isStale || status !== "online") return "stale";
  return "online";
}
