"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { getMissionHistory, getMissionWeather } from "@/lib/portal/client";
import type { MissionUpdateSummary, MissionWeather } from "@/lib/portal/types";

import { formatWeatherTime, groupWeatherWaypoints, weatherWarnings } from "@/lib/portal/weather-display";
import MissionPlanComparison from "./mission-plan-comparison";
import styles from "./mission-evidence.module.css";

export default function MissionEvidencePanel({ id, requestId }: { id: string; requestId?: string }) {
  const requests = useRef(0);
  const [inspecting, setInspecting] = useState<string | null>(null);
  const [history, setHistory] = useState<MissionUpdateSummary[]>([]);
  const [weather, setWeather] = useState<MissionWeather | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(true);
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
  const weatherGroups = weather ? groupWeatherWaypoints(weather.waypoints.map(point => ({
    ...point,
    arrivalTimeReason: point.estimatedArrivalTime
      ? point.arrivalTimeReason
      : "Forecast based on the scheduled mission flight window.",
  }))) : [];
  return <section className={styles.panel} aria-busy={busy}>
    <header className={styles.heading}><div><span className={styles.kicker}>SUPPORTING EVIDENCE</span><h2>Weather &amp; review history</h2><p>Check forecast conditions and previous review decisions.</p></div><button type="button" disabled={busy} onClick={() => void load(true)}>{busy ? "Refreshing…" : "Refresh forecast and history"}</button></header><div className={styles.body}>
    <label className={styles.context}><span>FORECAST FOR</span><select value={context} onChange={event => { setContext(event.target.value); setWeather(null); setError(""); setBusy(true); }}><option value="">Current mission</option>{requestId && !history.some(request => request.id === requestId) && <option value={requestId}>Selected proposal</option>}{history.map(request => <option key={request.id} value={request.id}>{request.status} request · {formatWeatherTime(request.createdAt)}</option>)}</select></label>
    {busy && <p role="status">Loading forecast and review records…</p>}
    {error && <div className={styles.notice} role="alert"><strong>Some evidence could not be loaded</strong><p>{error}</p><span>Use Refresh to try again.</span></div>}
    <p className={styles.advisory}>Forecasts are advisory and do not block approval. They are not measurements from the UAV. <a href="https://open-meteo.com/" target="_blank" rel="noreferrer">Open-Meteo</a></p>
    {weather && <><p>{weather.status} · Source: {weather.provider} · Fetched: {formatWeatherTime(weather.fetchedAt)} · Expires: {formatWeatherTime(weather.expiresAt)}</p><p>Flight window: {formatWeatherTime(weather.flightStartTime)} — {formatWeatherTime(weather.flightEndTime)}</p>
      <div className="mission-waypoint-table-wrap"><table className="mission-waypoint-table"><thead><tr><th>WP / forecast time</th><th>Temperature / humidity</th><th>Wind / gust / direction</th><th>Rain / probability</th><th>Visibility / cloud</th><th>Condition</th></tr></thead><tbody>{weatherGroups.map(({ point, sequences }) => <tr key={point.sequenceOrder}><td><b>WP{point.sequenceOrder}</b>{sequences.length > 1 && <small>Đại diện: {sequences.map(sequence => `WP${sequence}`).join(", ")}</small>}<small>Forecast: {formatWeatherTime(point.forecastTime)}</small><small>Arrival: {formatWeatherTime(point.estimatedArrivalTime)}</small><small>{point.arrivalTimeReason}</small></td><td>{point.temperatureCelsius ?? "—"} °C / {point.relativeHumidityPercent ?? "—"}%</td><td>{point.windSpeedMetersPerSecond ?? "—"} / {point.windGustMetersPerSecond ?? "—"} m/s · {point.windDirectionDegrees ?? "—"}°<small>Wind reference: {point.windReferenceHeightMeters} m</small></td><td>{point.precipitationMillimeters ?? "—"} mm/h / {point.precipitationProbabilityPercent ?? "—"}%</td><td>{point.visibilityMeters ?? "—"} m / {point.cloudCoverPercent ?? "—"}%</td><td>{point.status} · {point.riskLevel}<small>Weather code: {point.weatherCode ?? "—"}</small></td></tr>)}</tbody></table></div>{weatherWarnings(weather).map((warning, i) => <p role="status" key={i}>{warning}</p>)}</>}
    <div className={styles.historyHeading}><span className={styles.kicker}>REVIEW TRAIL</span><h3>Requests &amp; decisions <small>{history.length}</small></h3><p>Open a request to inspect its proposal and review outcome.</p></div>
    {!busy && history.length === 0 && <div className={styles.historyEmpty}><strong>No review records to display</strong><p>Requests and decisions will appear here when they are available.</p></div>}
    {history.map(request => <details className={styles.historyItem} key={request.id} onToggle={event => { if (event.currentTarget.open) setInspecting(request.id); }}><summary>{formatWeatherTime(request.createdAt)} · {request.isInitial ? "Initial submission" : "Update request"} · {request.status}</summary><p>Submitted by: {request.actorId}</p><p>Reviewed by: {request.reviewedBy ?? "Not reviewed"} · {formatWeatherTime(request.reviewedAt)}</p><p>{request.reason}</p><p>Proposed: {request.proposedPlan.name} · {formatWeatherTime(request.proposedPlan.startTime)} — {formatWeatherTime(request.proposedPlan.endTime)} · {request.proposedPlan.waypoints.length} waypoints</p>{inspecting === request.id && (request.isInitial || request.basePlan) && <MissionPlanComparison isInitial={request.isInitial} current={request.basePlan} proposed={request.proposedPlan} validation={request.validation} />}<p>Validation: {request.validation?.status ?? "Unavailable"} · {formatWeatherTime(request.validation?.checkedAt)}</p>{request.validation?.issues?.map((issue, index) => <p key={index}>{issue}</p>)}</details>)}
  </div></section>;
}
