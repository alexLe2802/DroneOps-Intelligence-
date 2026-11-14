"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { getLatestMissionTelemetry } from "@/lib/portal/client";
import { assessmentBatteryState } from "@/lib/portal/assessment-battery";
import type { AiAssessment, MissionTelemetry } from "@/lib/portal/types";
import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";

export default function LiveBattery({ missionId, uavId, assessment }: {
  missionId: string; uavId: string | null; assessment: AiAssessment | undefined;
}) {
  const [telemetry, setTelemetry] = useState<MissionTelemetry | null>(null);
  const [failed, setFailed] = useState(false);
  const [now, setNow] = useState(0);
  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    async function poll() {
      try {
        const sample = await getLatestMissionTelemetry(missionId);
        if (active) { setTelemetry(sample); setFailed(false); }
      } catch {
        if (active) { setTelemetry(null); setFailed(true); }
      } finally {
        if (active) timer = setTimeout(() => void poll(), 5000);
      }
    }
    void poll();
    const clock = setInterval(() => setNow(Date.now()), 1000);
    return () => { active = false; clearTimeout(timer); clearInterval(clock); };
  }, [missionId]);
  const state = assessmentBatteryState(telemetry, missionId, uavId, assessment, now);
  const battery = assessment?.result?.battery;
  const conclusion = state.invalidPower ? "Check battery power and monitor calibration"
    : state.required != null && state.required > 100 ? "Route requires more than a full battery"
    : state.margin == null ? "Battery sufficiency is not established"
    : state.margin < 0 ? "Battery is below the planning estimate" : "Reported charge covers the planning estimate";
  return <section className="ai-context ai-battery-live">
    <header><div><p className="module-kicker">CONNECTED UAV / AUTO-REFRESH EVERY 5 SECONDS</p><h2>Battery readiness</h2></div><span className={`ai-severity ${state.fresh ? "low" : "moderate"}`}>{state.fresh ? "Receiving telemetry" : failed ? "Telemetry request failed" : "No fresh battery evidence"}</span></header>
    <dl>
      <div><dt>REPORTED CHARGE NOW</dt><dd>{state.percent == null ? "Not available" : `${state.percent}%`}</dd></div>
      <div><dt>ESTIMATED DEPARTURE REQUIREMENT</dt><dd>{state.required == null ? "Assessment needed" : `${state.required.toFixed(1)}%`}</dd></div>
      <div><dt>MARGIN ABOVE REQUIREMENT</dt><dd>{state.margin == null ? "Not established" : `${state.margin >= 0 ? "+" : ""}${state.margin.toFixed(1)} percentage points`}</dd></div>
      <div><dt>PACK VOLTAGE / CONSUMED CHARGE</dt><dd>{state.fresh && telemetry?.batteryVoltageVolts != null ? `${telemetry.batteryVoltageVolts.toFixed(2)} V` : "Not reported"} / {state.fresh && telemetry?.batteryConsumedMah != null ? `${telemetry.batteryConsumedMah} mAh` : "Not reported"}</dd></div>
    </dl>
    <p className={`ai-battery-verdict ${state.invalidPower || (state.required ?? 0) > 100 || (state.margin != null && state.margin < 0) ? "high" : "review"}`} role="status"><strong>{conclusion}.</strong></p>
    {!state.fresh && <p>Connect the UAV assigned to this mission and select this mission when pairing. Old samples and samples from another UAV are not used as current battery evidence.</p>}
    {state.invalidPower && <p>The FC reports less than 1 V. USB power or an uncalibrated monitor cannot establish flight energy.</p>}
    {!state.estimateCurrent && <p>Generate a current assessment to calculate the route requirement. An outdated plan or an estimate older than 10 minutes is not compared with live charge.</p>}
    {state.fresh && telemetry && <p>UAV: {telemetry.uavCode ?? telemetry.uavName ?? "Assigned UAV"} · source: {telemetry.sourceId} · observed: {formatVietnamDateTime(telemetry.batteryObservedAt ?? telemetry.sourceTimestamp ?? "")} · age: {state.age?.toFixed(0)} s.</p>}
    {state.estimateCurrent && battery?.configuredCapacityMah != null && <p>Configured pack: {battery.configuredCapacityMah} mAh. Remaining charge estimate: {state.percent == null || state.invalidPower ? "not established" : `${(battery.configuredCapacityMah * state.percent / 100).toFixed(0)} mAh`}. Capacity is configured, not measured.</p>}
    <p>This comparison is advisory and does not replace preflight checks or manager approval.</p>
    <Link className="module-secondary" href="/portal/drone-connection">Connect UAV / inspect gateway</Link>
  </section>;
}
