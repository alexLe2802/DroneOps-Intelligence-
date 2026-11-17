/**
 * MapLibre layer helpers for DroneOps mission maps.
 *
 * Each function sets or updates a named GeoJSON source + style layer.
 * They are safe to call repeatedly — existing sources are updated in place.
 */

import type { GeofenceCoordinate, MissionFence, MissionValidationResult, MissionWaypoint } from "@/lib/portal/types";
import { geofenceGeoJson } from "./geofences";
import type { Geofence } from "@/lib/portal/types";

// ---- Source / Layer IDs ----
export const ROUTE_SOURCE = "droneops-route";
export const ROUTE_LAYER = "droneops-route-line";
export const GEOFENCE_SOURCE = "droneops-geofences";
export const GEOFENCE_FILL_LAYER = "droneops-geofence-fill";
export const GEOFENCE_LINE_LAYER = "droneops-geofence-line";
export const FINDINGS_SOURCE = "droneops-findings";
export const FINDINGS_LAYER = "droneops-findings-line";
export const FENCE_SOURCE = "droneops-mission-fences";
export const FENCE_FILL_LAYER = "droneops-fence-fill";
export const FENCE_LINE_LAYER = "droneops-fence-line";
export const DRAFT_SOURCE = "droneops-draft-fence";
export const DRAFT_LINE_LAYER = "droneops-draft-fence-line";
export const DRAFT_POINTS_LAYER = "droneops-draft-fence-points";
export const BUILDINGS_LAYER = "droneops-3d-buildings";

// ---- Internal helpers ----

type AnyMap = {
  getSource(id: string): unknown;
  addSource(id: string, source: Record<string, unknown>): void;
  getLayer(id: string): unknown;
  addLayer(layer: Record<string, unknown>): void;
  removeLayer(id: string): void;
  removeSource(id: string): void;
  getStyle(): { sources: Record<string, { type: string }> };
};

function setGeoJsonSource(map: AnyMap, id: string, data: GeoJSON.FeatureCollection | GeoJSON.Feature | GeoJSON.Geometry): void {
  const existing = map.getSource(id) as { setData?: (data: unknown) => void } | undefined;
  if (existing && typeof existing.setData === "function") {
    existing.setData(data);
  } else {
    if (existing) { /* source type mismatch — remove and recreate */ try { map.removeSource(id); } catch { /* ok */ } }
    map.addSource(id, { type: "geojson", data });
  }
}

function ensureLayer(map: AnyMap, layer: Record<string, unknown>): void {
  if (!map.getLayer(layer.id as string)) map.addLayer(layer);
}

function circleToPolygon(center: GeofenceCoordinate, radiusMeters: number, segments = 64): number[][] {
  const coords: number[][] = [];
  const latRad = center.latitude * Math.PI / 180;
  for (let i = 0; i <= segments; i++) {
    const angle = (i / segments) * 2 * Math.PI;
    const dLat = (radiusMeters * Math.sin(angle)) / 111320;
    const dLng = (radiusMeters * Math.cos(angle)) / (111320 * Math.cos(latRad));
    coords.push([center.longitude + dLng, center.latitude + dLat]);
  }
  return coords;
}

// ---- Public API ----

/** Render geofence polygons (prohibited/restricted). */
export function setGeofences(map: AnyMap, geofences: Geofence[]): void {
  const data = geofenceGeoJson(geofences);
  setGeoJsonSource(map, GEOFENCE_SOURCE, data);

  ensureLayer(map, {
    id: GEOFENCE_FILL_LAYER,
    type: "fill",
    source: GEOFENCE_SOURCE,
    paint: {
      "fill-color": ["case", ["==", ["get", "category"], "prohibited"], "#ef3340", "#facc15"],
      "fill-opacity": ["case", ["==", ["get", "category"], "prohibited"], 0.34, 0.25],
    },
  });
  ensureLayer(map, {
    id: GEOFENCE_LINE_LAYER,
    type: "line",
    source: GEOFENCE_SOURCE,
    paint: {
      "line-color": ["case", ["==", ["get", "category"], "prohibited"], "#ef3340", "#facc15"],
      "line-width": 1.2,
    },
  });
}

/** Render the planned flight route as a cyan polyline. */
export function setRoute(map: AnyMap, waypoints: MissionWaypoint[]): void {
  const coordinates = waypoints
    .filter(p => Number.isFinite(p.latitude) && Number.isFinite(p.longitude))
    .map(p => [p.longitude, p.latitude]);
  if (waypoints.length > 0 && waypoints.at(-1)?.actionType === "Return") {
    coordinates.push([waypoints[0].longitude, waypoints[0].latitude]);
  }
  const data: GeoJSON.Feature = {
    type: "Feature",
    properties: {},
    geometry: { type: "LineString", coordinates },
  };
  setGeoJsonSource(map, ROUTE_SOURCE, data);
  ensureLayer(map, {
    id: ROUTE_LAYER,
    type: "line",
    source: ROUTE_SOURCE,
    paint: { "line-color": "#25d6ff", "line-width": 4, "line-opacity": 0.95 },
    layout: { "line-cap": "round", "line-join": "round" },
  });
}

/** Render validation findings as red segments. */
export function setFindings(map: AnyMap, waypoints: MissionWaypoint[], findings: MissionValidationResult["findings"] | undefined): void {
  const features: GeoJSON.Feature[] = [];
  for (const f of findings ?? []) {
    if (f.segmentFrom == null || f.segmentTo == null) continue;
    const a = waypoints.find(p => p.sequenceOrder === f.segmentFrom);
    const b = waypoints.find(p => p.sequenceOrder === f.segmentTo);
    if (a && b) {
      features.push({
        type: "Feature",
        properties: { reason: f.reason },
        geometry: { type: "LineString", coordinates: [[a.longitude, a.latitude], [b.longitude, b.latitude]] },
      });
    }
  }
  setGeoJsonSource(map, FINDINGS_SOURCE, { type: "FeatureCollection", features });
  ensureLayer(map, {
    id: FINDINGS_LAYER,
    type: "line",
    source: FINDINGS_SOURCE,
    paint: { "line-color": "#ef3340", "line-width": 6 },
  });
}

/** Render permitted mission fences (polygons and circles). */
export function setMissionFences(map: AnyMap, fences: MissionFence[]): void {
  const features: GeoJSON.Feature[] = [];
  for (const fence of fences) {
    if (fence.shape === "Circle" && fence.center && Number.isFinite(fence.center.latitude) && Number.isFinite(fence.center.longitude) && Number.isFinite(fence.radiusMeters) && fence.radiusMeters! > 0) {
      features.push({
        type: "Feature",
        properties: { id: fence.id, label: "Permitted circle" },
        geometry: { type: "Polygon", coordinates: [circleToPolygon(fence.center, fence.radiusMeters!)] },
      });
    } else if (fence.shape === "Polygon" && fence.vertices?.length && fence.vertices.length >= 3 && fence.vertices.every(p => Number.isFinite(p.latitude) && Number.isFinite(p.longitude))) {
      const ring = fence.vertices.map(p => [p.longitude, p.latitude]);
      ring.push([...ring[0]]);
      features.push({
        type: "Feature",
        properties: { id: fence.id, label: "Permitted polygon" },
        geometry: { type: "Polygon", coordinates: [ring] },
      });
    }
  }
  setGeoJsonSource(map, FENCE_SOURCE, { type: "FeatureCollection", features });
  ensureLayer(map, { id: FENCE_FILL_LAYER, type: "fill", source: FENCE_SOURCE, paint: { "fill-color": "#58e6b0", "fill-opacity": 0.1 } });
  ensureLayer(map, { id: FENCE_LINE_LAYER, type: "line", source: FENCE_SOURCE, paint: { "line-color": "#58e6b0", "line-width": 2 } });
}

/** Render the draft fence being drawn. */
export function setDraftFence(map: AnyMap, draftFence: GeofenceCoordinate[]): void {
  const coordinates = draftFence.map(p => [p.longitude, p.latitude]);
  const lineData: GeoJSON.Feature = {
    type: "Feature",
    properties: {},
    geometry: { type: "LineString", coordinates },
  };
  const pointsData: GeoJSON.FeatureCollection = {
    type: "FeatureCollection",
    features: draftFence.map(p => ({
      type: "Feature" as const,
      properties: {},
      geometry: { type: "Point" as const, coordinates: [p.longitude, p.latitude] },
    })),
  };
  setGeoJsonSource(map, DRAFT_SOURCE, draftFence.length >= 2 ? lineData : { type: "FeatureCollection", features: [] });
  setGeoJsonSource(map, DRAFT_SOURCE + "-pts", pointsData);
  ensureLayer(map, {
    id: DRAFT_LINE_LAYER, type: "line", source: DRAFT_SOURCE,
    paint: { "line-color": "#ffc857", "line-width": 2, "line-dasharray": [6, 4] },
  });
  ensureLayer(map, {
    id: DRAFT_POINTS_LAYER, type: "circle", source: DRAFT_SOURCE + "-pts",
    paint: { "circle-radius": 5, "circle-color": "#ffc857", "circle-stroke-width": 1, "circle-stroke-color": "#fff" },
  });
}

/** Add 3D building extrusions from the vector tile building layer. */
export function add3DBuildings(map: AnyMap): void {
  if (map.getLayer(BUILDINGS_LAYER)) return;
  // Discover the vector tile source name from the loaded style.
  const vectorSource = Object.entries(map.getStyle().sources).find(([, s]) => s.type === "vector")?.[0];
  if (!vectorSource) return;
  map.addLayer({
    id: BUILDINGS_LAYER,
    source: vectorSource,
    "source-layer": "building",
    type: "fill-extrusion",
    minzoom: 14,
    paint: {
      "fill-extrusion-color": "#8ca0b4",
      "fill-extrusion-height": ["coalesce", ["get", "render_height"], 10],
      "fill-extrusion-base": ["coalesce", ["get", "render_min_height"], 0],
      "fill-extrusion-opacity": 0.55,
    },
  });
}

/** Remove 3D building extrusion layer. */
export function remove3DBuildings(map: AnyMap): void {
  if (map.getLayer(BUILDINGS_LAYER)) {
    try { map.removeLayer(BUILDINGS_LAYER); } catch { /* ok */ }
  }
}

/** Remove all DroneOps layers and sources from the map. */
export function removeAllLayers(map: AnyMap): void {
  const layerIds = [ROUTE_LAYER, GEOFENCE_FILL_LAYER, GEOFENCE_LINE_LAYER, FINDINGS_LAYER, FENCE_FILL_LAYER, FENCE_LINE_LAYER, DRAFT_LINE_LAYER, DRAFT_POINTS_LAYER, BUILDINGS_LAYER];
  const sourceIds = [ROUTE_SOURCE, GEOFENCE_SOURCE, FINDINGS_SOURCE, FENCE_SOURCE, DRAFT_SOURCE, DRAFT_SOURCE + "-pts"];
  for (const id of layerIds) { try { if (map.getLayer(id)) map.removeLayer(id); } catch { /* ok */ } }
  for (const id of sourceIds) { try { if (map.getSource(id)) map.removeSource(id); } catch { /* ok */ } }
}
