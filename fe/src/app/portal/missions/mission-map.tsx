"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import { describeGeofence } from "@/lib/maps/geofences";
import type { Geofence, MissionWaypoint } from "@/lib/portal/types";

const valid = (latitude: number, longitude: number) => longitude >= 100 && longitude <= 110 && latitude >= 7 && latitude <= 24;

export default function MissionMap({ waypoints, geofences, onAdd, onUpdate, readOnly = false }: {
  waypoints: MissionWaypoint[]; geofences: Geofence[];
  onAdd?: (longitude: number, latitude: number) => void;
  onUpdate?: (index: number, patch: Partial<MissionWaypoint>) => void;
  readOnly?: boolean;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const routeRef = useRef<Leaflet.Polyline | null>(null);
  const markersRef = useRef<Leaflet.Marker[]>([]);
  const onAddRef = useRef(onAdd);
  const onUpdateRef = useRef(onUpdate);
  const [error, setError] = useState("");
  const [mapReady, setMapReady] = useState(false);
  useEffect(() => { onAddRef.current = onAdd; }, [onAdd]);
  useEffect(() => { onUpdateRef.current = onUpdate; }, [onUpdate]);

  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;
    let resizeFrame: number | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !containerRef.current) return;
      // SVG avoids queued Canvas redraws accessing a destroyed context on teardown.
      const map = L.map(containerRef.current, { center: [10.849, 106.7725], zoom: 12, preferCanvas: false });
      mapRef.current = map;
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", {
        attribution: "Tiles &copy; Esri", maxZoom: 19, crossOrigin: true,
      }).on("tileerror", () => setError("Basemap tile could not be loaded.")).addTo(map);
      geofences.forEach(zone => {
        const coordinates = zone.coordinates.filter(point => valid(point.latitude, point.longitude));
        if (coordinates.length < 3) return;
        const info = describeGeofence(zone);
        const polygon = L.polygon(coordinates.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple), {
          color: info.color, fillColor: info.color, weight: 1.2,
          fillOpacity: info.category === "prohibited" ? .34 : .25, bubblingMouseEvents: false,
        });
        const popup = document.createElement("div"); popup.className = "airspace-popup";
        const title = document.createElement("strong"); title.textContent = zone.name;
        const category = document.createElement("span"); category.textContent = info.label;
        const limits = document.createElement("span"); limits.textContent = info.limitLabel;
        popup.append(title, category, limits);
        polygon.bindPopup(popup).addTo(map);
      });
      if (!readOnly) map.on("click", event => onAddRef.current?.(event.latlng.lng, event.latlng.lat));
      resizeFrame = requestAnimationFrame(() => {
        if (!cancelled) map.invalidateSize({ animate: false, pan: false });
      });
      setMapReady(true);
      setError("");
    }).catch(reason => setError(reason instanceof Error ? reason.message : "Map could not be loaded."));
    return () => {
      cancelled = true;
      if (resizeFrame !== undefined) cancelAnimationFrame(resizeFrame);
      markersRef.current.forEach(marker => marker.remove());
      mapRef.current?.stop();
      mapRef.current?.remove();
      mapRef.current = null;
    };
  }, [geofences, readOnly]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map) return;
      markersRef.current.forEach(marker => marker.remove());
      routeRef.current?.remove();
      markersRef.current = waypoints.map((point, index) => {
        const marker = L.marker([point.latitude, point.longitude], {
          draggable: !readOnly,
          icon: L.divIcon({ className: "waypoint-marker-shell", html: `<span class="waypoint-marker"><b>${index + 1}</b></span>`, iconSize: [28, 28], iconAnchor: [14, 28] }),
        }).addTo(map);
        if (!readOnly) marker.on("dragend", () => { const value = marker.getLatLng(); onUpdateRef.current?.(index, { latitude: Number(value.lat.toFixed(6)), longitude: Number(value.lng.toFixed(6)) }); });
        return marker;
      });
      if (waypoints.length > 1) routeRef.current = L.polyline(waypoints.map(point => [point.latitude, point.longitude]), { color: "#25d6ff", weight: 4, opacity: .95 }).addTo(map);
      if (waypoints.length > 0) {
        const bounds = L.latLngBounds(waypoints.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple));
        map.fitBounds(bounds, { padding: [44, 44], maxZoom: 16 });
      }
    });
    return () => { cancelled = true; };
  }, [waypoints, mapReady, readOnly]);

  return <div className="mission-map-wrap">
    <div ref={containerRef} className="mission-map" />
    <div className="map-hint">{readOnly ? "ROUTE PREVIEW" : "CLICK TO ADD · DRAG TO ADJUST"} · {waypoints.length} WAYPOINTS</div>
    <div className="airspace-map-legend"><span className="prohibited">Vùng cấm bay</span><span className="restricted">Vùng giới hạn bay</span></div>
    {error && <div className="mission-map-error" role="alert">{error}</div>}
  </div>;
}
