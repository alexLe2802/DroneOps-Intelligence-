"use client";

/**
 * MissionMap — interactive map for mission planning and review.
 *
 * MapLibre preview loaded only when the user chooses 3D.
 * The default 2D view remains the original Leaflet map.
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
import { enableTerrain, TERRAIN_SOURCE_ID, sampleRouteElevations, type TerrainSample } from "@/lib/maps/terrain";
import {
  setGeofences, setRoute, setFindings, setMissionFences as setMissionFenceLayers,
  setDraftFence, add3DBuildings, removeAllLayers, GEOFENCE_FILL_LAYER,
} from "@/lib/maps/map-layers";
import type { MapView, MapViewRef } from "./mission-map";
import MapToolbar from "@/lib/maps/map-toolbar";
import ElevationProfile from "@/lib/maps/elevation-profile";

// MapLibre types — imported for type annotations only. The runtime library
// is loaded dynamically to avoid SSR issues and to code-split WebGL.
import type {
  Map as MaplibreMap,
  Marker as MaplibreMarker,
  Popup as MaplibrePopup,
  MapMouseEvent,
} from "maplibre-gl";

const EMPTY_FENCES: MissionFence[] = [];
const EMPTY_COORDINATES: GeofenceCoordinate[] = [];

export default function MissionMap3D({
  waypoints,
  geofences,
  onAdd,
  onUpdate,
  readOnly = false,
  findings,
  plannedHome = waypoints[0]?.navigation?.plannedHome ?? null,
  pickingHome = false,
  missionFences = waypoints[0]?.navigation?.missionFences ?? EMPTY_FENCES,
  draftFence = EMPTY_COORDINATES,
  drawingFence = null,
  onFenceUpdate,
  viewRef,
}: {
  viewRef?: MapViewRef;
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

  const mode = "3d" as const;
  const initialView = useRef<MapView | null>(null);
  const firstRouteRender = useRef(true);
  const [terrainStatus, setTerrainStatus] = useState("Loading terrain…");
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
    initialView.current = viewRef?.current ?? null;
    firstRouteRender.current = true;
    let cancelled = false;

    void import("maplibre-gl").then(({ Map, NavigationControl, ScaleControl, setWorkerUrl }) => {
      if (cancelled || !containerRef.current) return;

      // Serve the bundled worker directly; Webpack cannot resolve MapLibre's
      // package-relative worker URL reliably from its runtime module URL.
      setWorkerUrl("/maplibre-gl-worker.mjs");
      const map = new Map({
        container: containerRef.current,
        style: droneOpsMapStyle(),
        center: initialView.current ? [initialView.current.longitude, initialView.current.latitude] : MAP_CENTER,
        zoom: initialView.current?.zoom ?? MAP_ZOOM,
        pitch: 60,
        bearing: 0,
        attributionControl: { compact: true },
      });

      mapRef.current = map;
      map.on("moveend", () => {
        const center = map.getCenter();
        if (viewRef) viewRef.current = { longitude: center.lng, latitude: center.lat, zoom: map.getZoom() };
      });
      map.on("error", event => {
        if (cancelled) return;
        if (("sourceId" in event && event.sourceId === TERRAIN_SOURCE_ID) || String(event.error?.message).includes("elevation-tiles-prod")) {
          setTerrainStatus("Terrain could not be loaded. Check your connection and try 3D again.");
        } else {
          setError(event.error?.message ?? "3D map could not be loaded.");
        }
      });

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
          if (cancelled || mapRef.current !== map) return;
          const content = document.createElement("div");
          content.className = "airspace-popup";
          for (const [tag, text] of [["strong", props.name], ["span", props.categoryLabel], ["span", props.limitLabel]]) {
            const item = document.createElement(tag);
            item.textContent = String(text ?? "");
            content.append(item);
          }
          const popup = new Popup({ closeButton: true, maxWidth: "240px" })
            .setLngLat(event.lngLat)
            .setDOMContent(content)
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
  }, [viewRef]);

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
      if (waypoints.length > 0 && !(firstRouteRender.current && initialView.current)) {
        const lngs = waypoints.map(p => p.longitude);
        const lats = waypoints.map(p => p.latitude);
        map.fitBounds(
          [[Math.min(...lngs), Math.min(...lats)], [Math.max(...lngs), Math.max(...lats)]],
          { padding: 44, maxZoom: 16, animate: false },
        );
      }
      firstRouteRender.current = false;
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

  // Set up terrain only after the style is loaded. DEM readiness is driven
  // by tile events, so a slow connection does not leave a permanently empty profile.
  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady) return;
    enableTerrain(map);
    add3DBuildings(map as unknown as Parameters<typeof add3DBuildings>[0]);
  }, [mapReady]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !mapReady) return;
    setTerrainSamples([]);
    const sample = () => {
      if (mapRef.current !== map || !map.getSource(TERRAIN_SOURCE_ID) || !map.isSourceLoaded(TERRAIN_SOURCE_ID)) return;
      const centerElevation = map.queryTerrainElevation(map.getCenter());
      if (centerElevation == null) return;
      setTerrainStatus("Terrain loaded · actual scale 1×");
      setTerrainSamples(waypoints.length >= 2 ? sampleRouteElevations(map, waypoints) : []);
    };
    const onSourceData = (event: { sourceId?: string }) => {
      if (event.sourceId === TERRAIN_SOURCE_ID) sample();
    };
    const onSourceLoading = (event: { sourceId?: string }) => {
      if (event.sourceId === TERRAIN_SOURCE_ID) setTerrainStatus("Loading terrain…");
    };
    map.on("sourcedata", onSourceData);
    map.on("sourcedataloading", onSourceLoading);
    map.on("idle", sample);
    sample();
    return () => {
      map.off("sourcedata", onSourceData);
      map.off("sourcedataloading", onSourceLoading);
      map.off("idle", sample);
    };
  }, [mapReady, waypoints]);

  // ---- Toolbar handlers ----
  const handleResetNorth = useCallback(() => {
    mapRef.current?.easeTo({ bearing: 0, pitch: 60, duration: 400 });
  }, []);

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
    const el = wrapRef.current?.parentElement;
    if (!el) return;
    if (document.fullscreenElement === el) {
      void document.exitFullscreen();
    } else {
      void el.requestFullscreen();
    }
  }, []);

  return (
    <div className="mission-map-wrap" ref={wrapRef}>
      <div className="mission-map-canvas-wrap">
      <div ref={containerRef} className="mission-map" />

      <MapToolbar
        mode={mode}
        showModeToggle={false}
        onModeChange={() => {}}
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

      <div className="terrain-status" role="status">{terrainStatus}</div>
      {mode === "3d" && (
        <div className="terrain-disclaimer">
          ⚠ Building heights are estimates. Terrain does not detect obstacles.
          Verify against site survey.
        </div>
      )}

      {error && <div className="mission-map-error" role="alert">{error}</div>}
      </div>

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
