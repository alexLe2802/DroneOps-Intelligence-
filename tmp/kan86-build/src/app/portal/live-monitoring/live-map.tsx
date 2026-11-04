"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import type { MissionTelemetry, MissionWaypoint } from "@/lib/portal/types";
import { hasTelemetryPosition } from "@/lib/portal/telemetry-position";
import { mapLinkState } from "@/lib/portal/map-link-state";
import { formatWeatherTime } from "@/lib/portal/weather-display";
import { getGeofences } from "@/lib/portal/client";
import { describeGeofence } from "@/lib/maps/geofences";
import styles from "./live-monitoring.module.css";

const hasPosition = hasTelemetryPosition;

export default function LiveMap({ missionId, telemetry, waypoints, sequence, recenterToken, streamLive }: {
  missionId: string;
  telemetry: MissionTelemetry | null;
  waypoints: MissionWaypoint[];
  sequence: number;
  recenterToken: number;
  streamLive: boolean;
}) {
  const linkState = mapLinkState(telemetry, streamLive);
  const livePosition = linkState === "online";
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const aircraftRef = useRef<Leaflet.Marker | null>(null);
  const routeLayersRef = useRef<Leaflet.Polyline[]>([]);
  const waypointLayersRef = useRef<Leaflet.CircleMarker[]>([]);
  const centeredViewRef = useRef("");
  const [ready, setReady] = useState(false);
  const [airspaceState, setAirspaceState] = useState<"loading" | "loaded" | "empty" | "error">("loading");
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
        // Slow wheel/trackpad zoom and allow small, controlled zoom steps.
        wheelPxPerZoomLevel: 240,
        wheelDebounceTime: 80,
        zoomSnap: 0.25,
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
    let layer: Leaflet.LayerGroup | undefined;
    void Promise.all([import("leaflet"), getGeofences()]).then(([{ default: L }, zones]) => {
      if (cancelled || mapRef.current !== map) return;
      const pane = map.getPane("airspace") ?? map.createPane("airspace");
      pane.style.zIndex = "350";
      layer = L.layerGroup().addTo(map);
      let drawn = 0;
      for (const zone of zones) {
        const points = zone.coordinates;
        if (points.length < 3 || points.some(point => !Number.isFinite(point.latitude) || !Number.isFinite(point.longitude) || Math.abs(point.latitude) > 90 || Math.abs(point.longitude) > 180)) continue;
        const info = describeGeofence(zone);
        const popup = document.createElement("div");
        const title = document.createElement("strong"); title.textContent = zone.name;
        const detail = document.createElement("p"); detail.textContent = `${info.label} · ${info.limitLabel}`;
        popup.append(title, detail);
        L.polygon(points.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple), {
          pane: "airspace", color: info.color, fillColor: info.color,
          weight: 1.2, fillOpacity: info.category === "prohibited" ? .34 : .25,
          fillRule: "evenodd", bubblingMouseEvents: false,
        }).bindPopup(popup).addTo(layer);
        drawn++;
      }
      setAirspaceState(drawn ? "loaded" : zones.length ? "error" : "empty");
    }).catch(() => { if (!cancelled) setAirspaceState("error"); });
    return () => { cancelled = true; layer?.remove(); };
  }, [ready]);

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
      const icon = L.divIcon({
        className: styles.aircraftIconShell,
        html: `${livePosition ? `<span class="${styles.aircraftPulse}"></span>` : ""}<span class="${styles.aircraftIcon} ${livePosition ? "" : linkState === "offline" ? styles.aircraftOffline : styles.aircraftStale}">${livePosition ? "UAV" : linkState === "offline" ? "OFF" : "STALE"}</span>`,
        iconSize: [52, 52],
        iconAnchor: [26, 26],
      });
      if (!aircraftRef.current) {
        aircraftRef.current = L.marker(position, { zIndexOffset: 1000, icon }).addTo(map);
      } else {
        aircraftRef.current.setLatLng(position).setIcon(icon);
      }
      aircraftRef.current.unbindTooltip();
      if (!livePosition) {
        const label = document.createElement("span");
        label.textContent = `Vị trí cuối cùng · ${formatWeatherTime(telemetry.sourceTimestamp)}`;
        aircraftRef.current.bindTooltip(label, { permanent: true, direction: "bottom", offset: [0, 20] });
      }
    });
    return () => { cancelled = true; };
  }, [ready, telemetry, livePosition, linkState]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready) return;
    // A position snapshot can arrive before the route. Fit again once the
    // selected mission's route arrives, without resetting manual zoom on packets.
    const centerKey = `${missionId}:${recenterToken}:${waypoints.length ? "route" : "position"}`;
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
    <div className={styles.mapTopLeft}>SECTOR GRID / {hasPosition(telemetry) ? livePosition ? "LIVE POSITION" : "LAST KNOWN POSITION" : "POSITION UNAVAILABLE"}</div>
    <div className={styles.mapTopRight} data-state={linkState}><i /> DRONE {linkState.toUpperCase()}</div>
    {hasPosition(telemetry) && !livePosition && <div className={styles.mapLastKnown} role="status">{linkState === "offline" ? "Drone đã mất kết nối." : "Dữ liệu vị trí chưa được cập nhật."} Marker hiển thị vị trí cuối cùng nhận lúc {formatWeatherTime(telemetry.sourceTimestamp)}.</div>}
    <div className={styles.airspaceLegend} aria-label="Airspace restrictions"><span><i style={{ background: "#ff3344" }} /> Vùng cấm bay</span><span><i style={{ background: "#facc15" }} /> Vùng hạn chế bay</span>{airspaceState !== "loaded" && <small role="status">{airspaceState === "loading" ? "Loading airspace…" : airspaceState === "empty" ? "No restriction data available" : "Airspace could not be loaded. Reload the page to retry."}</small>}</div>
    {!hasPosition(telemetry) && !noSpatialData && <div className={styles.mapError} role="status">UAV position unavailable / Chưa có vị trí UAV hợp lệ</div>}
    {noSpatialData && <div className={styles.mapEmpty}><b>NO SPATIAL TELEMETRY</b><span>Waiting for a valid UAV position / Đang chờ vị trí UAV hợp lệ.</span></div>}
    {mapError && <div className={styles.mapError} role="alert">{mapError}</div>}
  </div>;
}
