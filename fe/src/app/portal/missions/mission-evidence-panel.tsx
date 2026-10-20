"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { getMissionHistory, getMissionWeather } from "@/lib/portal/client";
import type { MissionUpdateSummary, MissionWeather } from "@/lib/portal/types";

import MissionPlanComparison from "./mission-plan-comparison";

export default function MissionEvidencePanel({ id, requestId }: { id: string; requestId?: string }) {
  const requests = useRef(0);
  const [inspecting, setInspecting] = useState<string | null>(null);
  const [history, setHistory] = useState<MissionUpdateSummary[]>([]);
  const [weather, setWeather] = useState<MissionWeather | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [context, setContext] = useState(requestId ?? "");
  const load = useCallback(async (refresh = false) => {
    const sequence = ++requests.current;
    setBusy(true); setError("");
    const [events, forecast] = await Promise.allSettled([getMissionHistory(id), getMissionWeather(id, context || undefined, refresh)]);
    if (sequence !== requests.current) return;
    if (events.status === "fulfilled") setHistory(events.value);
    if (forecast.status === "fulfilled") setWeather(forecast.value);
    else { setWeather(null); setError(forecast.reason instanceof Error ? forecast.reason.message : "Forecast unavailable."); }
    if (events.status === "rejected") setError(events.reason instanceof Error ? events.reason.message : "History unavailable.");
    setBusy(false);
  }, [id, context]);
  useEffect(() => { const sequence = ++requests.current; let active = true; void Promise.allSettled([getMissionHistory(id), getMissionWeather(id, context || undefined)]).then(([events, forecast]) => { if (!active || sequence !== requests.current) return; setBusy(false); if (events.status === "fulfilled") setHistory(events.value); else setError("History unavailable. Retry to load history."); if (forecast.status === "fulfilled") setWeather(forecast.value); else { setWeather(null); setError("Forecast unavailable. You may still review the mission manually."); } }); return () => { active = false; }; }, [id, context]);
  return <section className="mission-waypoint-panel">
    <header><h2>Weather forecast &amp; review history</h2><button type="button" disabled={busy} onClick={() => void load(true)}>{busy ? "Refreshing…" : "Refresh forecast and history"}</button></header>
    <label>Forecast context<select value={context} onChange={event => { setContext(event.target.value); setWeather(null); }}><option value="">Current mission</option>{history.map(request => <option key={request.id} value={request.id}>{request.status} request · {new Date(request.createdAt).toLocaleString()}</option>)}</select></label>
    {error && <p role="alert">{error}</p>}
    <p>Forecasts are advisory. They do not represent sensors measured on the UAV. <a href="https://open-meteo.com/" target="_blank" rel="noreferrer">Open-Meteo</a></p>
    {weather && <><p>{weather.status} · Source: {weather.provider} · Fetched: {new Date(weather.fetchedAt).toLocaleString()} · Expires: {new Date(weather.expiresAt).toLocaleString()}</p><p>Flight window: {weather.flightStartTime} — {weather.flightEndTime}</p>
      <div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>WP / forecast time</th><th>Temperature / humidity</th><th>Wind / gust / direction</th><th>Rain / probability</th><th>Visibility / cloud</th><th>Condition</th></tr></thead><tbody>{weather.waypoints.map(point => <tr key={point.sequenceOrder}><td>WP{point.sequenceOrder}<small>{point.forecastTime ?? "Unavailable"}</small><small>Arrival: {point.estimatedArrivalTime ?? "Unknown"}</small><small>{point.arrivalTimeReason}</small></td><td>{point.temperatureCelsius ?? "—"} °C / {point.relativeHumidityPercent ?? "—"}%</td><td>{point.windSpeedMetersPerSecond ?? "—"} / {point.windGustMetersPerSecond ?? "—"} m/s · {point.windDirectionDegrees ?? "—"}°<small>Wind reference: {point.windReferenceHeightMeters} m</small></td><td>{point.precipitationMillimeters ?? "—"} mm/h / {point.precipitationProbabilityPercent ?? "—"}%</td><td>{point.visibilityMeters ?? "—"} m / {point.cloudCoverPercent ?? "—"}%</td><td>{point.status} · {point.riskLevel}<small>Weather code: {point.weatherCode ?? "—"}</small></td></tr>)}</tbody></table></div>{weather.warnings.map((warning, i) => <p role="status" key={i}>{warning}</p>)}</>}
    <h3>Update requests and decisions</h3><p>Each row preserves the proposal and its review outcome.</p>
    {history.length === 0 && <p>No review history is available.</p>}
    {history.map(request => <details key={request.id} onToggle={event => { if (event.currentTarget.open) setInspecting(request.id); }}><summary>{new Date(request.createdAt).toLocaleString()} · {request.isInitial ? "Initial submission" : "Update request"} · {request.status}</summary><p>Submitted by: {request.actorId}</p><p>Reviewed by: {request.reviewedBy ?? "Not reviewed"} · {request.reviewedAt ?? ""}</p><p>{request.reason}</p><p>Proposed: {request.proposedPlan.name} · {request.proposedPlan.startTime} — {request.proposedPlan.endTime} · {request.proposedPlan.waypoints.length} waypoints</p>{inspecting === request.id && request.basePlan && <MissionPlanComparison current={request.basePlan} proposed={request.proposedPlan} validation={request.validation} />}<p>Validation: {request.validation?.status ?? "Unavailable"} · {request.validation?.checkedAt ?? ""}</p>{request.validation?.issues?.map((issue, index) => <p key={index}>{issue}</p>)}</details>)}
  </section>;
}
