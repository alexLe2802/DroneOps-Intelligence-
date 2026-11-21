"use client";

import { useMemo } from "react";
import type { MissionWaypoint } from "@/lib/portal/types";
import { haversineDistance, type TerrainSample } from "./terrain";

/**
 * Elevation profile chart — SVG cross-section view of the flight route
 * overlaid on terrain elevation.
 *
 * Shows:
 *  - Brown fill: terrain elevation (from DEM samples)
 *  - Cyan line: planned flight path (waypoint altitudes)
 *  - Waypoint labels along the bottom axis
 *
 * Altitude reference is shown as a label — the chart does not convert
 * between Relative/AGL/MSL, it plots the numbers as given.
 */
export default function ElevationProfile({
  waypoints,
  terrainSamples = [],
  altitudeReference = "",
}: {
  waypoints: MissionWaypoint[];
  terrainSamples?: TerrainSample[];
  altitudeReference?: string;
}) {
  const profile = useMemo(() => {
    if (waypoints.length < 2) return null;

    // Compute cumulative distances for each waypoint
    const distances: number[] = [0];
    for (let i = 1; i < waypoints.length; i++) {
      distances.push(distances[i - 1] + haversineDistance(waypoints[i - 1], waypoints[i]));
    }
    const totalDistance = distances[distances.length - 1];
    if (totalDistance <= 0) return null;

    // Flight path points
    const flightPoints = waypoints.map((wp, i) => ({
      distance: distances[i],
      altitude: typeof wp.altitude === "number" && Number.isFinite(wp.altitude) ? wp.altitude : null,
      label: `WP${i + 1}`,
      actionType: wp.actionType,
    }));

    // Terrain points (only if samples available)
    const terrainPoints = terrainSamples.filter(s => s.terrainElevation != null) as Array<TerrainSample & { terrainElevation: number }>;

    // Determine value ranges
    const allAltitudes = flightPoints.flatMap(p => p.altitude === null ? [] : [p.altitude]);
    const allTerrain = terrainPoints.map(p => p.terrainElevation);
    const minVal = Math.min(0, ...allAltitudes, ...allTerrain);
    const maxVal = Math.max(100, ...allAltitudes, ...allTerrain) * 1.15;

    // Mission altitudes can be relative to Home or AGL while DEM is MSL.
    // Without an explicit datum conversion these cannot establish clearance.
    return { flightPoints, terrainPoints, totalDistance, minVal, maxVal };
  }, [waypoints, terrainSamples]);

  if (!profile) return null;

  const { flightPoints, terrainPoints, totalDistance, minVal, maxVal } = profile;
  const W = 720;
  const H = 160;
  const PAD_L = 48;
  const PAD_R = 12;
  const PAD_T = 12;
  const PAD_B = 32;
  const plotW = W - PAD_L - PAD_R;
  const plotH = H - PAD_T - PAD_B;

  const x = (d: number) => PAD_L + (d / totalDistance) * plotW;
  const y = (v: number) => PAD_T + plotH - ((v - minVal) / (maxVal - minVal)) * plotH;

  // Build terrain area path
  let terrainPath = "";
  if (terrainPoints.length > 1) {
    terrainPath = `M${x(terrainPoints[0].distance)},${y(terrainPoints[0].terrainElevation)}`;
    for (let i = 1; i < terrainPoints.length; i++) {
      terrainPath += `L${x(terrainPoints[i].distance)},${y(terrainPoints[i].terrainElevation)}`;
    }
    terrainPath += `L${x(terrainPoints[terrainPoints.length - 1].distance)},${y(minVal)}L${x(terrainPoints[0].distance)},${y(minVal)}Z`;
  }

  // Build flight path
  const flightPath = flightPoints.map((p, index) => {
    if (p.altitude === null) return "";
    const command = index > 0 && flightPoints[index - 1].altitude !== null ? "L" : "M";
    return `${command}${x(p.distance)},${y(p.altitude)}`;
  }).join("");

  // Y axis grid lines
  const range = maxVal - minVal;
  const step = range > 200 ? 50 : range > 100 ? 25 : range > 40 ? 10 : 5;
  const gridLines: number[] = [];
  for (let v = Math.ceil(minVal / step) * step; v <= maxVal; v += step) gridLines.push(v);

  const altRef = altitudeReference || "Ref unspecified";

  return (
    <div className="elevation-profile">
      <div className="elevation-profile-header">
        <span>ELEVATION PROFILE</span>
        <span className="elevation-ref">{altRef}</span>
      </div>
      <svg viewBox={`0 0 ${W} ${H}`} className="elevation-svg" preserveAspectRatio="none" aria-label="Elevation cross-section">
        {/* Grid lines */}
        {gridLines.map(v => (
          <g key={v}>
            <line x1={PAD_L} x2={W - PAD_R} y1={y(v)} y2={y(v)} stroke="#2a3a4d" strokeWidth={0.5} />
            <text x={PAD_L - 4} y={y(v) + 3} textAnchor="end" fill="#6b8299" fontSize={8} fontFamily="Consolas, monospace">{v}</text>
          </g>
        ))}

        {/* Terrain fill */}
        {terrainPath && <path d={terrainPath} fill="#3a2e1e" opacity={0.7} />}
        {terrainPath && (
          <path
            d={terrainPoints.map((p, i) => `${i === 0 ? "M" : "L"}${x(p.distance)},${y(p.terrainElevation)}`).join("")}
            fill="none"
            stroke="#8b6914"
            strokeWidth={1.5}
          />
        )}

        {/* Flight path */}
        <path d={flightPath} fill="none" stroke="#25d6ff" strokeWidth={2.5} strokeLinejoin="round" />

        {/* Waypoint dots and labels */}
        {flightPoints.map((p, i) => (
          <g key={i}>
            {p.altitude !== null && <circle cx={x(p.distance)} cy={y(p.altitude)} r={4} fill="#25d6ff" stroke="#0a1929" strokeWidth={1.5} />}
            <text x={x(p.distance)} y={H - 6} textAnchor="middle" fill="#8bb8d0" fontSize={7} fontFamily="Consolas, monospace">
              {p.label}
            </text>
            <text x={x(p.distance)} y={p.altitude === null ? PAD_T : y(p.altitude) - 7} textAnchor="middle" fill="#a0d4ee" fontSize={7} fontFamily="Consolas, monospace">
              {p.altitude === null ? "Unknown" : `${p.altitude.toFixed(0)}m`}
            </text>
          </g>
        ))}

        {/* Axes */}
        <line x1={PAD_L} x2={PAD_L} y1={PAD_T} y2={H - PAD_B} stroke="#3c5068" strokeWidth={1} />
        <line x1={PAD_L} x2={W - PAD_R} y1={H - PAD_B} y2={H - PAD_B} stroke="#3c5068" strokeWidth={1} />

        {/* Distance label */}
        <text x={W - PAD_R} y={H - 6} textAnchor="end" fill="#6b8299" fontSize={7} fontFamily="Consolas, monospace">
          {totalDistance >= 1000 ? `${(totalDistance / 1000).toFixed(1)} km` : `${totalDistance.toFixed(0)} m`}
        </text>
      </svg>
      <p className="elevation-no-terrain">Terrain: MSL. Waypoints: {altRef}. Clearance is not calculated across different altitude references.</p>
      {terrainPoints.length === 0 && (
        <p className="elevation-no-terrain">Terrain elevation along this route is unavailable or still loading.</p>
      )}
    </div>
  );
}
