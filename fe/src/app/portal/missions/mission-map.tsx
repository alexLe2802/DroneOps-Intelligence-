"use client";

/**
 * MissionMap — interactive map for mission planning and review.
 *
 * Uses MapLibre GL JS instead of Leaflet. Supports both 2D (default, top-down)
 * and 3D (terrain + extruded buildings) modes via a toolbar toggle.
 *
 * The map instance is created once on mount and kept stable — prop changes
 * update layers and markers without re-creating the map or its WebGL context.
 *
 * IMPORTANT: 3D terrain and building extrusions are visual aids only.
 * Building heights are estimates from OpenStreetMap data. Terrain does not
 * include obstacles like power lines, furniture, or recent constructions.
 * Always verify against on-site surveys before flight.
 */

import { useEffect, useRef, useState, useCallback } from "react";
import type { Geofence, MissionWaypoint, MissionValidationResult, PlannedHome, MissionFence, GeofenceCoordinate } from "@/lib/portal/types";
import { droneOpsMapStyle, MAP_CENTER, MAP_ZOOM } from "@/lib/maps/style";
import { enableTerrain, disableTerrain, sampleRouteElevations, type TerrainSample } from "@/lib/maps/terrain";
import {
  setGeofences, setRoute, setFindings, setMissionFences as setMissionFenceLayers,
  setDraftFence, add3DBuildings, remove3DBuildings, removeAllLayers, GEOFENCE_FILL_LAYER,
} from "@/lib/maps/map-layers";
import MapToolbar, { type MapMode } from "@/lib/maps/map-toolbar";
import ElevationProfile from "@/lib/maps/elevation-profile";

// MapLibre types — imported for type annotations only. The runtime library
// is loaded dynamically to avoid SSR issues and to code-split WebGL.
import type {
  Map as MaplibreMap,
  Marker as MaplibreMarker,
  Popup as MaplibrePopup,
  MapMouseEvent,
} from "maplibre-gl";

export default function MissionMap({
  waypoints,
  geofences,
  onAdd,
  onUpdate,
  readOnly = false,
  findings,
  plannedHome = waypoints[0]?.navigation?.plannedHome ?? null,
  pickingHome = false,
  missionFences = waypoints[0]?.navigation?.missionFences ?? [],
  draftFence = [],
  drawingFence = null,
  onFenceUpdate,
}: {
  missionFences?: MissionFence[];
  draftFence?: GeofenceCoordinate[];
  drawingFence?: "Polygon" | "Circle" | null;
  onFenceUpdate?: (fence: MissionFence) => void;
  plannedHome?: PlannedHome | null;
  pickingHome?: boolean;
  waypoints: MissionWaypoint[];
  geofences: Geofence[];
  onAdd?: (longitude: number, latitude: number) => void;
  onUpdate?: (index: number, patch: Partial<MissionWaypoint>) => void;
  readOnly?: boolean;
  findings?: MissionValidationResult["findings"];
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const wrapRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<MaplibreMap | null>(null);
  const markersRef = useRef<MaplibreMarker[]>([]);
  const fenceHandlesRef = useRef<MaplibreMarker[]>([]);
  const popupRef = useRef<MaplibrePopup | null>(null);

  const [mode, setMode] = useState<MapMode>("2d");
  const [mapReady, setMapReady] = useState(false);
  const [error, setError] = useState("");
  const [terrainSamples, setTerrainSamples] = useState<TerrainSample[]>([]);

  // Stable refs for callbacks to avoid re-creating the map
  const readOnlyRef = useRef(readOnly);
  const onAddRef = useRef(onAdd);
  const onUpdateRef = useRef(onUpdate);
  const fenceUpdateRef = useRef(onFenceUpdate);

  useEffect(() => { readOnlyRef.current = readOnly; }, [readOnly]);
  useEffect(() => { onAddRef.current = onAdd; }, [onAdd]);
  useEffect(() => { onUpdateRef.current = onUpdate; }, [onUpdate]);
  useEffect(() => { fenceUpdateRef.current = onFenceUpdate; }, [onFenceUpdate]);

  // ---- Mount / teardown ----
  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;

    void import("maplibre-gl").then(({ Map, NavigationControl, ScaleControl }) => {
      if (cancelled || !containerRef.current) return;

      const map = new Map({
        container: containerRef.current,
        style: droneOpsMapStyle(),
        center: MAP_CENTER,
        zoom: MAP_ZOOM,
        pitch: 0,
        bearing: 0,
        attributionControl: { compact: true },
      });

      mapRef.current = map;

      map.addControl(new NavigationControl({ showCompass: true, showZoom: true, visualizePitch: true }), "bottom-left");
      map.addControl(new ScaleControl({ maxWidth: 150, unit: "metric" }), "bottom-right");

      map.on("load", () => {
        if (cancelled) return;
        setMapReady(true);
        setError("");
      });

      map.on("click", (event: MapMouseEvent) => {
        if (readOnlyRef.current) return;
        // Avoid firing when clicking a marker or fence handle
        const target = event.originalEvent.target as HTMLElement;
        if (target.closest(".waypoint-marker-shell") || target.closest(".fence-handle")) return;
        onAddRef.current?.(event.lngLat.lng, event.lngLat.lat);
      });

      // Geofence popup on click
      map.on("click", GEOFENCE_FILL_LAYER, (event) => {
        const props = event.features?.[0]?.properties;
        if (!props) return;
        popupRef.current?.remove();
        void import("maplibre-gl").then(({ Popup }) => {
          const popup = new Popup({ closeButton: true, maxWidth: "240px" })
            .setLngLat(event.lngLat)
            .setHTML(`<div class="airspace-popup"><strong>${String(props.name ?? "")}</strong><span>${String(props.categoryLabel ?? "")}</span><span>${String(props.limitLabel ?? "")}</span></div>`)
            .addTo(map);
          popupRef.current = popup;
        });
      });

      map.on("mouseenter", GEOFENCE_FILL_LAYER, () => { map.getCanvas().style.cursor = "pointer"; });
      map.on("mouseleave", GEOFENCE_FILL_LAYER, () => { map.getCanvas().style.cursor = ""; });

    }).catch((reason: unknown) => {
      if (!cancelled) setError(reason instanceof Error ? reason.message : "Map could not be loaded.");
    });

    return () => {
      cancelled = true;
      markersRef.current.forEach(m => m.remove());
      markersRef.current = [];
      fenceHandlesRef.current.forEach(m => m.remove());
      fenceHandlesRef.current = [];
      popupRef.current?.remove();
      popupRef.current = null;
      if (mapRef.current) {
        try { removeAllLayers(mapRef.current as unknown as Parameters<typeof removeAllLayers>[0]); } catch { /* teardown */ }
        mapRef.current.remove();
      }
      mapRef.current = null;
      setMapReady(false);
    };
  }, []);

  // ---- Update geofences ----
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady) return;
    setGeofences(map as unknown as Parameters<typeof setGeofences>[0], geofences);
  }, [geofences, mapReady]);

  // ---- Update route, waypoint markers, findings ----
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady) return;
    let cancelled = false;

    void import("maplibre-gl").then(({ Marker, Popup }) => {
      if (cancelled || mapRef.current !== map) return;

      // Clear old markers
      markersRef.current.forEach(m => m.remove());
      markersRef.current = [];

      // Validate coordinates
      if (waypoints.some(p => !Number.isFinite(p.latitude) || Math.abs(p.latitude) > 90 || !Number.isFinite(p.longitude) || Math.abs(p.longitude) > 180)) return;

      // Waypoint markers
      waypoints.forEach((point, index) => {
        const hasIssue = findings?.some(f => f.waypointSequence === point.sequenceOrder);
        const el = document.createElement("div");
        el.className = "waypoint-marker-shell";
        el.innerHTML = `<span class="waypoint-marker"${hasIssue ? ' style="background:#ef3340;border-color:#ef3340"' : ""}><b>${index + 1}</b></span>`;

        const marker = new Marker({
          element: el,
          draggable: !readOnly,
          anchor: "bottom",
        })
          .setLngLat([point.longitude, point.latitude])
          .addTo(map);

        const tooltipText = point.actionType === "TakeOff"
          ? "Takeoff/Home"
          : point.actionType === "Return"
            ? "Return/RTL · visit this point, then return to FC Home"
            : point.actionType;

        const popup = new Popup({ offset: 24, closeButton: false }).setText(tooltipText);
        marker.setPopup(popup);

        if (!readOnly) {
          marker.on("dragend", () => {
            if (readOnlyRef.current) return;
            const lngLat = marker.getLngLat();
            onUpdateRef.current?.(index, {
              latitude: Number(lngLat.lat.toFixed(6)),
              longitude: Number(lngLat.lng.toFixed(6)),
            });
          });
        }

        markersRef.current.push(marker);
      });

      // Planned home marker
      if (plannedHome && Number.isFinite(plannedHome.latitude) && Number.isFinite(plannedHome.longitude)) {
        const homeEl = document.createElement("div");
        homeEl.className = "waypoint-marker-shell";
        homeEl.innerHTML = '<span class="waypoint-marker" style="background:#ffc857;color:#101a27;border-color:#fff"><b>H</b></span>';

        const homeMarker = new Marker({ element: homeEl, anchor: "bottom" })
          .setLngLat([plannedHome.longitude, plannedHome.latitude])
          .addTo(map);

        const homePopup = new Popup({ offset: 24, closeButton: false }).setText("Planned Home");
        homeMarker.setPopup(homePopup);
        markersRef.current.push(homeMarker);
      }

      // Route line
      setRoute(map as unknown as Parameters<typeof setRoute>[0], waypoints);

      // Findings (red segments)
      setFindings(map as unknown as Parameters<typeof setFindings>[0], waypoints, findings);

      // Fit bounds
      if (waypoints.length > 0) {
        const lngs = waypoints.map(p => p.longitude);
        const lats = waypoints.map(p => p.latitude);
        map.fitBounds(
          [[Math.min(...lngs), Math.min(...lats)], [Math.max(...lngs), Math.max(...lats)]],
          { padding: 44, maxZoom: 16, animate: false },
        );
      }
    });

    return () => { cancelled = true; };
  }, [waypoints, mapReady, readOnly, findings, plannedHome]);

  // ---- Update mission fences and draft fence ----
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady) return;
    let cancelled = false;

    void import("maplibre-gl").then(({ Marker }) => {
      if (cancelled || mapRef.current !== map) return;

      // Clear fence handles
      fenceHandlesRef.current.forEach(m => m.remove());
      fenceHandlesRef.current = [];

      const castMap = map as unknown as Parameters<typeof setMissionFenceLayers>[0];

      // Set mission fence layers
      setMissionFenceLayers(castMap, missionFences);

      // Add draggable handles for fence editing (non read-only)
      if (!readOnly) {
        for (const fence of missionFences) {
          if (fence.shape === "Circle" && fence.center && Number.isFinite(fence.center.latitude) && Number.isFinite(fence.center.longitude)) {
            const handleEl = document.createElement("span");
            handleEl.className = "fence-handle";
            handleEl.title = "Drag fence center";
            const handle = new Marker({ element: handleEl, draggable: true })
              .setLngLat([fence.center.longitude, fence.center.latitude])
              .addTo(map);
            handle.on("dragend", () => {
              if (readOnlyRef.current) return;
              const p = handle.getLngLat();
              fenceUpdateRef.current?.({ ...fence, center: { latitude: p.lat, longitude: p.lng } });
            });
            fenceHandlesRef.current.push(handle);
          } else if (fence.shape === "Polygon" && fence.vertices?.length) {
            fence.vertices.forEach((v, vi) => {
              const handleEl = document.createElement("span");
              handleEl.className = "fence-handle";
              handleEl.title = `Fence vertex ${vi + 1}`;
              const handle = new Marker({ element: handleEl, draggable: true })
                .setLngLat([v.longitude, v.latitude])
                .addTo(map);
              handle.on("dragend", () => {
                if (readOnlyRef.current) return;
                const p = handle.getLngLat();
                fenceUpdateRef.current?.({
                  ...fence,
                  vertices: fence.vertices!.map((vert, j) =>
                    j === vi ? { latitude: p.lat, longitude: p.lng } : vert
                  ),
                });
              });
              fenceHandlesRef.current.push(handle);
            });
          }
        }
      }

      // Draft fence
      setDraftFence(castMap, draftFence);
    });

    return () => { cancelled = true; };
  }, [mapReady, missionFences, draftFence, readOnly]);

  // ---- 2D / 3D mode switch ----
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady) return;

    if (mode === "3d") {
      enableTerrain(map as unknown as Parameters<typeof enableTerrain>[0]);
      add3DBuildings(map as unknown as Parameters<typeof add3DBuildings>[0]);
      map.easeTo({ pitch: 60, duration: 500 });
    } else {
      disableTerrain(map as unknown as Parameters<typeof disableTerrain>[0]);
      remove3DBuildings(map as unknown as Parameters<typeof remove3DBuildings>[0]);
      map.easeTo({ pitch: 0, bearing: 0, duration: 500 });
    }
  }, [mode, mapReady]);

  // ---- Sample terrain for elevation profile after mode switch ----
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady || mode !== "3d" || waypoints.length < 2) {
      setTerrainSamples([]);
      return;
    }
    // Wait for terrain tiles to render, then sample
    const timer = setTimeout(() => {
      if (mapRef.current !== map) return;
      const samples = sampleRouteElevations(map, waypoints);
      setTerrainSamples(samples);
    }, 1500);
    return () => clearTimeout(timer);
  }, [mode, mapReady, waypoints]);

  // ---- Toolbar handlers ----
  const handleResetNorth = useCallback(() => {
    mapRef.current?.easeTo({ bearing: 0, pitch: mode === "3d" ? 60 : 0, duration: 400 });
  }, [mode]);

  const handleFitRoute = useCallback(() => {
    if (!mapRef.current || waypoints.length === 0) return;
    const lngs = waypoints.map(p => p.longitude);
    const lats = waypoints.map(p => p.latitude);
    mapRef.current.fitBounds(
      [[Math.min(...lngs), Math.min(...lats)], [Math.max(...lngs), Math.max(...lats)]],
      { padding: 44, maxZoom: 16, animate: true },
    );
  }, [waypoints]);

  const handleFullscreen = useCallback(() => {
    const el = wrapRef.current;
    if (!el) return;
    if (document.fullscreenElement === el) {
      void document.exitFullscreen();
    } else {
      void el.requestFullscreen();
    }
  }, []);

  return (
    <div className="mission-map-wrap mission-map-3d-wrap" ref={wrapRef}>
      <div ref={containerRef} className="mission-map" />

      <MapToolbar
        mode={mode}
        onModeChange={setMode}
        onResetNorth={handleResetNorth}
        onFitRoute={handleFitRoute}
        onFullscreen={handleFullscreen}
        disabled={!mapReady}
      />

      <div className="map-hint">
        {readOnly
          ? "ROUTE PREVIEW"
          : drawingFence
            ? `DRAW ${drawingFence.toUpperCase()} FENCE`
            : pickingHome
              ? "CLICK TO SET PLANNED HOME"
              : "CLICK TO ADD · DRAG TO ADJUST"}{" "}
        · {waypoints.length} WAYPOINTS
        {mode === "3d" && " · 3D TERRAIN"}
      </div>

      <div className="airspace-map-legend">
        <span className="prohibited">Vùng cấm bay</span>
        <span className="restricted">Vùng giới hạn bay</span>
      </div>

      {mode === "3d" && (
        <div className="terrain-disclaimer">
          ⚠ Building heights are estimates. Terrain does not detect obstacles.
          Verify against site survey.
        </div>
      )}

      {error && <div className="mission-map-error" role="alert">{error}</div>}

      {waypoints.length >= 2 && (
        <ElevationProfile
          waypoints={waypoints}
          terrainSamples={terrainSamples}
          altitudeReference={waypoints[0]?.navigation?.plannedHome ? "Planned Home" : ""}
        />
      )}
    </div>
  );
}
