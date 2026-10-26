"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { getMissionWeather } from "@/lib/portal/client";
import type { FlightHistoryItem, MissionWeather } from "@/lib/portal/types";

type ForecastState = { weather?: MissionWeather; error?: string; loading?: boolean };

function timeLabel(value: string | null | undefined) {
  if (!value) return "Time unavailable";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Time unavailable" : date.toLocaleString();
}

export default function DashboardRouteWeather({ missions }: { missions: FlightHistoryItem[] }) {
  const [forecasts, setForecasts] = useState<Record<string, ForecastState>>({});

  const load = useCallback(async (refresh = false) => {
    setForecasts(current => Object.fromEntries(missions.map(mission => [mission.missionId, {
      ...current[mission.missionId], loading: true, error: "",
    }])));
    const results = await Promise.allSettled(missions.map(mission =>
      getMissionWeather(mission.missionId, undefined, refresh)));
    setForecasts(Object.fromEntries(results.map((result, index) => {
      const id = missions[index].missionId;
      return [id, result.status === "fulfilled"
        ? { weather: result.value }
        : { error: result.reason instanceof Error ? result.reason.message : "Forecast unavailable." }];
    })));
  }, [missions]);

  useEffect(() => {
    let active = true;
    setForecasts(Object.fromEntries(missions.map(mission => [mission.missionId, { loading: true }])));
    void Promise.allSettled(missions.map(mission => getMissionWeather(mission.missionId))).then(results => {
      if (!active) return;
      setForecasts(Object.fromEntries(results.map((result, index) => [missions[index].missionId,
        result.status === "fulfilled"
          ? { weather: result.value }
          : { error: result.reason instanceof Error ? result.reason.message : "Forecast unavailable." },
      ])));
    });
    return () => { active = false; };
  }, [missions]);

  return <article className="dashboard-route-weather">
    <header><b>MISSION ROUTE WEATHER</b><span>{missions.length ? `${missions.length} MISSIONS` : "NO MISSIONS"}</span></header>
    {!missions.length ? <div className="dashboard-panel-empty"><span>◌</span><strong>NO MISSION WEATHER</strong><p>Create or schedule a mission to see forecast conditions for its route.</p></div> : <ul className="dashboard-mission-weather-list">
      {missions.map(mission => {
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
    <footer><span>First waypoint forecast for each mission · advisory only</span><button type="button" disabled={!missions.length || Object.values(forecasts).some(item => item.loading)} onClick={() => void load(true)}>Refresh forecasts</button></footer>
  </article>;
}
