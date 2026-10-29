"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import type { FlightTelemetryPoint } from "@/lib/portal/types";
import styles from "../flight-history.module.css";

const DEFAULT_CENTER: Leaflet.LatLngTuple = [10.7769, 106.7009];
const positioned = (point: FlightTelemetryPoint) => point.latitude !== null && point.longitude !== null
  && Number.isFinite(point.latitude) && Number.isFinite(point.longitude)
  && Math.abs(point.latitude) <= 90 && Math.abs(point.longitude) <= 180;

export default function FlightPathMap({ telemetry, uavCode }: { telemetry: FlightTelemetryPoint[]; uavCode: string }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const layersRef = useRef<Leaflet.Layer[]>([]);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  const points = telemetry.filter(positioned);
  const missingRoute = telemetry.length === 0
    ? {
        title: "No flight telemetry received",
        message: "This flight has no telemetry samples, so its route cannot be drawn.",
        action: "Check the drone connection and telemetry ingestion for this flight.",
      }
    : {
        title: "Position data is missing or invalid",
        message: `None of the ${telemetry.length} telemetry samples contains a valid latitude and longitude pair.`,
        action: "Check the GPS fields in the recorded telemetry, then regenerate the report after correcting the source data.",
      };

  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;
    let observer: ResizeObserver | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !containerRef.current) return;
      const map = L.map(containerRef.current, { center: DEFAULT_CENTER, zoom: 12, zoomControl: false, preferCanvas: false, zoomAnimation: false, fadeAnimation: false });
      mapRef.current = map;
      L.control.zoom({ position: "bottomright" }).addTo(map);
      L.control.scale({ position: "bottomleft", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", { attribution: "Tiles &copy; Esri", maxZoom: 19, crossOrigin: true })
        .on("tileerror", () => setError("Basemap tiles are unavailable. Recorded telemetry is still listed below."))
        .addTo(map);
      observer = new ResizeObserver(() => map.invalidateSize({ animate: false, pan: false }));
      observer.observe(containerRef.current);
      setReady(true);
    }).catch(reason => { if (!cancelled) setError(reason instanceof Error ? reason.message : "Flight map could not be loaded."); });
    return () => { cancelled = true; observer?.disconnect(); layersRef.current = []; mapRef.current?.remove(); mapRef.current = null; };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready) return;
    layersRef.current.forEach(layer => layer.remove());
    layersRef.current = [];
    if (!points.length) { map.setView(DEFAULT_CENTER, 12, { animate: false }); return; }
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map) return;
      const stride = Math.max(1, Math.ceil(points.length / 1500));
      const rendered = points.filter((_, index) => index % stride === 0 || index === points.length - 1);
      const coordinates = rendered.map(point => [point.latitude!, point.longitude!] as Leaflet.LatLngTuple);
      const route = L.polyline(coordinates, { color: "#58d4fb", weight: 4, opacity: .9 }).addTo(map);
      const start = L.circleMarker(coordinates[0], { radius: 7, color: "#08141d", weight: 3, fillColor: "#5ce0b5", fillOpacity: 1 }).bindTooltip("FLIGHT START").addTo(map);
      const end = L.circleMarker(coordinates.at(-1)!, { radius: 7, color: "#08141d", weight: 3, fillColor: "#ff9d99", fillOpacity: 1 }).bindTooltip("FLIGHT END").addTo(map);
      layersRef.current = [route, start, end];
      map.fitBounds(L.latLngBounds(coordinates), { padding: [44, 44], maxZoom: 16, animate: false });
    });
    return () => { cancelled = true; };
  }, [points, ready]);

  return <div className={styles.flightMap}>
    <div ref={containerRef} className={styles.flightMapCanvas} />
    <div className={styles.flightMapBanner}>{points.length ? `${uavCode || "UAV"} // ${points.length} POSITION FIXES` : `0 OF ${telemetry.length} VALID POSITION FIXES`}</div>
    {!points.length && <div className={styles.flightMapEmpty} role="status" aria-live="polite">
      <div>
        <span className={styles.flightMapEmptyIcon} aria-hidden="true">!</span>
        <b>{missingRoute.title}</b>
        <p>{missingRoute.message}</p>
        <small>{missingRoute.action}</small>
      </div>
    </div>}
    {error && <div className={styles.flightMapError} role="alert">{error}</div>}
  </div>;
}
