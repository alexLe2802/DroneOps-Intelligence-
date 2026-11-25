"use client";

import { formatVietnamDateTime } from "@/lib/portal/vietnam-time";

import { useWeatherRefreshWait } from "./use-weather-refresh";
import Link from "next/link";
import { useEffect, useState } from "react";
import { getMissionWeather } from "@/lib/portal/client";
import type { FlightHistoryItem, MissionWeather } from "@/lib/portal/types";

type ForecastState = { weather?: MissionWeather; error?: string; loading?: boolean; retryAt?: number };

function timeLabel(value: string | null | undefined) {
  if (!value) return "Time unavailable";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Time unavailable" : formatVietnamDateTime(date);
}

export default function DashboardRouteWeather({ missions }: { missions: FlightHistoryItem[] }) {
  const [forecasts, setForecasts] = useState<Record<string, ForecastState>>(() =>
    Object.fromEntries(missions.map(mission => [mission.missionId, { loading: true }])));

  const deadlineFor = (state?: ForecastState) =>
    state?.weather?.nextRefreshAt ? Date.parse(state.weather.nextRefreshAt) : state?.retryAt ?? 0;
  const nextRefresh = missions.length
    ? Math.min(...missions.map(mission => deadlineFor(forecasts[mission.missionId]))) : 0;
  const wait = useWeatherRefreshWait(nextRefresh);

  async function refresh() {
    const due = missions.filter(mission => deadlineFor(forecasts[mission.missionId]) <= Date.now());
    if (!due.length) return;
    setForecasts(current => ({ ...current, ...Object.fromEntries(due.map(mission =>
      [mission.missionId, { ...current[mission.missionId], loading: true }])) }));
    const results = await Promise.allSettled(due.map(mission =>
      getMissionWeather(mission.missionId, undefined, true)));
    setForecasts(current => ({ ...current, ...Object.fromEntries(results.map((result, index) =>
      [due[index].missionId, result.status === "fulfilled" ? { weather: result.value }
        : { retryAt: Date.now() + 60000, error: result.reason instanceof Error ? result.reason.message : "Forecast unavailable." }])) }));
  }

  useEffect(() => {
    let active = true;
    void Promise.allSettled(missions.map(mission => getMissionWeather(mission.missionId))).then(results => {
      if (!active) return;
      setForecasts(Object.fromEntries(results.map((result, index) => [missions[index].missionId,
        result.status === "fulfilled"
          ? { weather: result.value }
          : { retryAt: Date.now() + 60000, error: result.reason instanceof Error ? result.reason.message : "Forecast unavailable." },
      ])));
    });
    return () => { active = false; };
  }, [missions]);

  const busy = Object.values(forecasts).some(item => item.loading);
  const visibleMissions = missions.filter(mission => {
    const state = forecasts[mission.missionId];
    return !state || state.loading || (!state.error && state.weather && state.weather.status !== "Unavailable");
  });

  return <article className="dashboard-route-weather">
    <header><b>MISSION ROUTE WEATHER</b><span>{busy ? "LOADING" : `${visibleMissions.length} MISSIONS`}</span></header>
    {!missions.length ? <div className="dashboard-panel-empty"><span>◌</span><strong>NO MISSION WEATHER</strong><p>Create or schedule a mission to see forecast conditions for its route.</p></div> : <ul className="dashboard-mission-weather-list">
      {visibleMissions.map(mission => {
        const state = forecasts[mission.missionId];
        const weather = state?.weather;
        const firstWaypoint = weather?.waypoints.length
          ? [...weather.waypoints].sort((a, b) => a.sequenceOrder - b.sequenceOrder)[0]
          : null;
        return <li key={mission.missionId}>
          <div className="dashboard-mission-weather-title">
            <Link href={`/portal/missions/${encodeURIComponent(mission.missionId)}`}>{mission.missionName}</Link>
            <span>{mission.status} · {timeLabel(mission.startTime)}</span>
            <b>{state?.loading ? "LOADING" : weather?.status?.toUpperCase() ?? (state?.error ? "UNAVAILABLE" : "PENDING")}</b>
          </div>
          {state?.loading && !weather ? <p className="dashboard-mission-weather-empty">Loading forecast…</p>
            : state?.error ? <p className="dashboard-mission-weather-empty" role="alert">{state.error}</p>
              : firstWaypoint ? <div className="dashboard-mission-weather-values">
                <span>First point · WP{firstWaypoint.sequenceOrder}<small>{timeLabel(firstWaypoint.estimatedArrivalTime ?? firstWaypoint.forecastTime)}</small></span>
                <strong>{firstWaypoint.temperatureCelsius == null ? "—" : `${firstWaypoint.temperatureCelsius}°C`}</strong>
                <span>{firstWaypoint.status} · {firstWaypoint.riskLevel}</span>
                <span>Wind <b>{firstWaypoint.windSpeedMetersPerSecond == null ? "—" : `${firstWaypoint.windSpeedMetersPerSecond} m/s`}</b></span>
                <span>Gust <b>{firstWaypoint.windGustMetersPerSecond == null ? "—" : `${firstWaypoint.windGustMetersPerSecond} m/s`}</b></span>
              </div> : <p className="dashboard-mission-weather-empty">{weather?.warnings[0] ?? "No forecast for the first waypoint."}</p>}
        </li>;
      })}
    </ul>}
    {!busy && missions.length > 0 && visibleMissions.length === 0 && <div className="dashboard-panel-empty"><strong>NO AVAILABLE FORECASTS</strong><p>No mission currently has an available weather forecast.</p></div>}
    <footer><span>First waypoint forecast for each mission</span><button type="button" disabled={!missions.length || busy || wait > 0} onClick={() => void refresh()}>{wait ? `Refresh in ${wait}s` : "Refresh forecasts"}</button></footer>
  </article>;
}
