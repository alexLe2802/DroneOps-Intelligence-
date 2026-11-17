/**
 * Terrain utilities for MapLibre GL JS.
 *
 * Uses AWS Terrain Tiles (Terrarium encoding) — free, no API key required.
 * Terrain exaggeration is fixed at 1× for mission planning accuracy.
 *
 * IMPORTANT: 3D terrain visualisation does NOT detect obstacles like power
 * lines, furniture, or newly erected structures. Building heights from vector
 * tiles are estimates. Always cross-check against site surveys.
 */

export const TERRAIN_SOURCE_ID = "droneops-terrain-dem";

export const TERRAIN_SOURCE_CONFIG = {
  type: "raster-dem" as const,
  tiles: [
    "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png",
  ],
  encoding: "terrarium" as const,
  tileSize: 256,
  maxzoom: 15,
};

/** Terrain exaggeration 1× — no distortion in planning view. */
export const TERRAIN_EXAGGERATION = 1.0;

/**
 * Add the DEM raster source to the map if it does not already exist.
 * Safe to call multiple times.
 */
export function addTerrainSource(map: { getSource: (id: string) => unknown; addSource: (id: string, source: Record<string, unknown>) => void }): void {
  if (!map.getSource(TERRAIN_SOURCE_ID)) {
    map.addSource(TERRAIN_SOURCE_ID, TERRAIN_SOURCE_CONFIG);
  }
}

/**
 * Enable 3D terrain rendering on the map.
 * Adds the DEM source if needed and sets terrain with 1× exaggeration.
 */
export function enableTerrain(map: {
  getSource: (id: string) => unknown;
  addSource: (id: string, source: Record<string, unknown>) => void;
  setTerrain: (spec: { source: string; exaggeration: number } | null) => void;
}): void {
  addTerrainSource(map);
  map.setTerrain({ source: TERRAIN_SOURCE_ID, exaggeration: TERRAIN_EXAGGERATION });
}

/** Disable terrain rendering — returns to flat 2D view. */
export function disableTerrain(map: { setTerrain: (spec: null) => void }): void {
  map.setTerrain(null);
}

/**
 * Query terrain elevation at a geographic point.
 * Returns null if terrain is not loaded or the point is out of coverage.
 */
export function queryElevation(
  map: { queryTerrainElevation?: (lngLat: { lng: number; lat: number }) => number | null },
  lng: number,
  lat: number,
): number | null {
  try {
    if (typeof map.queryTerrainElevation !== "function") return null;
    const elevation = map.queryTerrainElevation({ lng, lat });
    return typeof elevation === "number" && Number.isFinite(elevation) ? elevation : null;
  } catch {
    return null;
  }
}

/** Haversine distance in metres between two geographic points. */
export function haversineDistance(
  a: { longitude: number; latitude: number },
  b: { longitude: number; latitude: number },
): number {
  const r = Math.PI / 180;
  const dLat = (b.latitude - a.latitude) * r;
  const dLng = (b.longitude - a.longitude) * r;
  const h =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(a.latitude * r) * Math.cos(b.latitude * r) * Math.sin(dLng / 2) ** 2;
  return 6371000 * 2 * Math.asin(Math.sqrt(Math.min(1, Math.max(0, h))));
}

export interface TerrainSample {
  distance: number;
  terrainElevation: number | null;
  longitude: number;
  latitude: number;
}

/**
 * Sample terrain elevations along a polyline route.
 * Requires the terrain source to be loaded on the map.
 * Returns an array of {distance, terrainElevation, lng, lat} samples.
 */
export function sampleRouteElevations(
  map: { queryTerrainElevation?: (lngLat: { lng: number; lat: number }) => number | null },
  points: ReadonlyArray<{ longitude: number; latitude: number }>,
  samplesPerSegment = 8,
): TerrainSample[] {
  if (points.length < 2) return [];
  const result: TerrainSample[] = [];
  let cumDist = 0;

  for (let i = 0; i < points.length; i++) {
    const p = points[i];
    if (i > 0) cumDist += haversineDistance(points[i - 1], p);

    result.push({
      distance: cumDist,
      terrainElevation: queryElevation(map, p.longitude, p.latitude),
      longitude: p.longitude,
      latitude: p.latitude,
    });

    if (i < points.length - 1) {
      const next = points[i + 1];
      const segDist = haversineDistance(p, next);
      for (let s = 1; s < samplesPerSegment; s++) {
        const t = s / samplesPerSegment;
        const lng = p.longitude + (next.longitude - p.longitude) * t;
        const lat = p.latitude + (next.latitude - p.latitude) * t;
        result.push({
          distance: cumDist + segDist * t,
          terrainElevation: queryElevation(map, lng, lat),
          longitude: lng,
          latitude: lat,
        });
      }
    }
  }
  return result;
}
