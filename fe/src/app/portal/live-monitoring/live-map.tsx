"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import type { MissionTelemetry, MissionWaypoint } from "@/lib/portal/types";
import styles from "./live-monitoring.module.css";

const hasPosition = (value: { latitude: number | null; longitude: number | null } | null): value is { latitude: number; longitude: number } =>
  Boolean(value && Number.isFinite(value.latitude) && Number.isFinite(value.longitude) && Math.abs(value.latitude!) <= 90 && Math.abs(value.longitude!) <= 180);

export default function LiveMap({ missionId, telemetry, waypoints, sequence, recenterToken }: {
  missionId: string;
  telemetry: MissionTelemetry | null;
  waypoints: MissionWaypoint[];
  sequence: number;
  recenterToken: number;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const aircraftRef = useRef<Leaflet.Marker | null>(null);
  const routeLayersRef = useRef<Leaflet.Polyline[]>([]);
  const waypointLayersRef = useRef<Leaflet.CircleMarker[]>([]);
  const centeredViewRef = useRef("");
  const [ready, setReady] = useState(false);
  const [mapError, setMapError] = useState("");

  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;
    let resizeObserver: ResizeObserver | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !containerRef.current) return;
      const map = L.map(containerRef.current, {
        center: [10.7769, 106.7009],
        zoom: 12,
        zoomControl: false,
        preferCanvas: false,
        zoomAnimation: false,
        fadeAnimation: false,
      });
      mapRef.current = map;
      L.control.zoom({ position: "bottomright" }).addTo(map);
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", {
        attribution: "Tiles &copy; Esri",
        maxZoom: 19,
        crossOrigin: true,
      }).on("tileerror", () => setMapError("Basemap tiles are temporarily unavailable. Telemetry values remain visible below.")).addTo(map);
      resizeObserver = new ResizeObserver(() => map.invalidateSize({ animate: false, pan: false }));
      resizeObserver.observe(containerRef.current);
      setReady(true);
    }).catch(error => {
      if (!cancelled) setMapError(error instanceof Error ? error.message : "Live map could not be loaded.");
    });
    return () => {
      cancelled = true;
      resizeObserver?.disconnect();
      aircraftRef.current = null;
      routeLayersRef.current = [];
      waypointLayersRef.current = [];
      mapRef.current?.stop();
      mapRef.current?.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready) return;
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map) return;
      routeLayersRef.current.forEach(layer => layer.remove());
      waypointLayersRef.current.forEach(layer => layer.remove());
      routeLayersRef.current = [];
      waypointLayersRef.current = [];

      const validWaypoints = waypoints.filter(point => Number.isFinite(point.latitude) && Number.isFinite(point.longitude) && Math.abs(point.latitude) <= 90 && Math.abs(point.longitude) <= 180);
      if (validWaypoints.length > 1) {
        const positions = validWaypoints.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple);
        routeLayersRef.current.push(L.polyline(positions, { color: "#71cdf2", weight: 3, opacity: .76, dashArray: "8 7" }).addTo(map));
        const completed = positions.slice(0, Math.min(Math.max(sequence, 1), positions.length));
        if (completed.length > 1) routeLayersRef.current.push(L.polyline(completed, { color: "#54e2b5", weight: 4, opacity: .95 }).addTo(map));
      }
      waypointLayersRef.current = validWaypoints.map((point, index) => {
        const completed = index < sequence;
        const active = index === Math.max(0, sequence - 1);
        return L.circleMarker([point.latitude, point.longitude], {
          radius: active ? 7 : 5,
          color: active ? "#e9f7ff" : completed ? "#57e2b6" : "#70cdf2",
          fillColor: active ? "#20bbed" : completed ? "#57e2b6" : "#172638",
          fillOpacity: 1,
          weight: 2,
        }).bindTooltip(`WP-${String(index + 1).padStart(2, "0")} / ${point.actionType}`, { direction: "top" }).addTo(map);
      });
    });
    return () => { cancelled = true; };
  }, [ready, sequence, waypoints]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready || !hasPosition(telemetry)) {
      aircraftRef.current?.remove();
      aircraftRef.current = null;
      return;
    }
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map || !hasPosition(telemetry)) return;
      const position: Leaflet.LatLngExpression = [telemetry.latitude, telemetry.longitude];
      if (!aircraftRef.current) {
        aircraftRef.current = L.marker(position, {
          zIndexOffset: 1000,
          icon: L.divIcon({
            className: styles.aircraftIconShell,
            html: `<span class="${styles.aircraftPulse}"></span><span class="${styles.aircraftIcon}">UAV</span>`,
            iconSize: [52, 52],
            iconAnchor: [26, 26],
          }),
        }).addTo(map);
      } else {
        aircraftRef.current.setLatLng(position);
      }
    });
    return () => { cancelled = true; };
  }, [ready, telemetry]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready) return;
    const centerKey = `${missionId}:${recenterToken}`;
    if (centeredViewRef.current === centerKey) return;
    const points: Leaflet.LatLngTuple[] = waypoints
      .filter(point => Number.isFinite(point.latitude) && Number.isFinite(point.longitude))
      .map(point => [point.latitude, point.longitude]);
    if (hasPosition(telemetry)) points.push([telemetry.latitude, telemetry.longitude]);
    if (points.length > 1) {
      map.fitBounds(points, { padding: [54, 54], maxZoom: 16, animate: false });
      centeredViewRef.current = centerKey;
    } else if (points.length === 1) {
      map.setView(points[0], 15, { animate: false });
      centeredViewRef.current = centerKey;
    }
  }, [missionId, ready, recenterToken, telemetry, waypoints]);

  const noSpatialData = waypoints.length === 0 && !hasPosition(telemetry);
  return <div className={styles.mapStage}>
    <div ref={containerRef} className={styles.mapCanvas} />
    <div className={styles.mapTopLeft}>SECTOR GRID / LIVE POSITION</div>
    <div className={styles.mapTopRight}><i /> ROUTE LINK {telemetry?.connectivityStatus?.toUpperCase() || "OFFLINE"}</div>
    {noSpatialData && <div className={styles.mapEmpty}><b>NO SPATIAL TELEMETRY</b><span>The mission has no route or current UAV position.</span></div>}
    {mapError && <div className={styles.mapError} role="alert">{mapError}</div>}
  </div>;
}
