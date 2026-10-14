"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import { describeGeofence } from "@/lib/maps/geofences";
import type { Geofence } from "@/lib/portal/types";

const valid = (latitude: number, longitude: number) => longitude >= 100 && longitude <= 110 && latitude >= 7 && latitude <= 24;

type CursorCoordinate = { latitude: number; longitude: number } | null;

export default function DashboardMap({ geofences, onCursorCoordinate }: { geofences: Geofence[]; onCursorCoordinate?: (coordinate: CursorCoordinate) => void }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    let map: Leaflet.Map | undefined;
    let cancelled = false;
    let resizeFrame: number | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled) return;
      map = L.map(container, { center: [10.8231, 106.6297], zoom: 11, zoomControl: false, preferCanvas: true });
      map.on("mousemove", event => onCursorCoordinate?.({ latitude: event.latlng.lat, longitude: event.latlng.lng }));
      map.on("mouseout", () => onCursorCoordinate?.(null));
      L.control.zoom({ position: "bottomleft" }).addTo(map);
      L.control.scale({ position: "bottomright", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", {
        attribution: "Tiles &copy; Esri", maxZoom: 19, crossOrigin: true,
      }).on("tileerror", () => setError("Basemap tile could not be loaded.")).addTo(map);
      geofences.forEach(zone => {
        const coordinates = zone.coordinates.filter(point => valid(point.latitude, point.longitude));
        if (coordinates.length < 3) return;
        const info = describeGeofence(zone);
        const polygon = L.polygon(coordinates.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple), {
          color: info.color, fillColor: info.color, weight: 1.5, opacity: .95,
          fillOpacity: info.category === "prohibited" ? .34 : .25, bubblingMouseEvents: false,
        });
        const popup = document.createElement("div"); popup.className = "airspace-popup";
        const title = document.createElement("strong"); title.textContent = zone.name;
        const category = document.createElement("span"); category.textContent = info.label;
        const limits = document.createElement("span"); limits.textContent = info.limitLabel;
        popup.append(title, category, limits);
        polygon.bindPopup(popup).addTo(map!);
      });
      resizeFrame = requestAnimationFrame(() => {
        if (!cancelled && map) map.invalidateSize({ animate: false, pan: false });
      });
      setError("");
    }).catch(reason => setError(reason instanceof Error ? reason.message : "Map could not be loaded."));
    return () => {
      cancelled = true;
      if (resizeFrame !== undefined) cancelAnimationFrame(resizeFrame);
      map?.stop();
      map?.remove();
      map = undefined;
    };
  }, [geofences, onCursorCoordinate]);

  return <div className="dashboard-map-shell">
    <div ref={containerRef} className="dashboard-live-map" />
    <div className="dashboard-airspace-source">LEAFLET · ESRI STREET MAP · {geofences.length} BACKEND GEOFENCES</div>
    <div className="airspace-map-legend"><span className="prohibited">Vùng cấm bay</span><span className="restricted">Vùng giới hạn bay</span></div>
    {error && <div className="dashboard-map-error" role="alert">{error}</div>}
  </div>;
}
