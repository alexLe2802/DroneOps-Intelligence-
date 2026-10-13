"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import type { GeofenceCoordinate } from "@/lib/portal/types";

export default function GeofenceMap({ coordinates, editable, onChange }: {
  coordinates: GeofenceCoordinate[]; editable: boolean;
  onChange: (coordinates: GeofenceCoordinate[]) => void;
}) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<Leaflet.Map | null>(null);
  const layer = useRef<Leaflet.LayerGroup | null>(null);
  const values = useRef({ coordinates, editable, onChange });
  const fitted = useRef(false);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => { values.current = { coordinates, editable, onChange }; }, [coordinates, editable, onChange]);
  useEffect(() => {
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !container.current) return;
      const instance = L.map(container.current, { center: [10.849, 106.7725], zoom: 12 });
      map.current = instance;
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", { attribution: "Tiles &copy; Esri", maxZoom: 19 })
        .on("tileerror", () => setError("Basemap unavailable. You can still enter coordinates below.")).addTo(instance);
      layer.current = L.layerGroup().addTo(instance);
      instance.on("click", event => {
        const current = values.current;
        if (current.editable) current.onChange([...current.coordinates, { latitude: Number(event.latlng.lat.toFixed(6)), longitude: Number(event.latlng.lng.toFixed(6)) }]);
      });
      setReady(true);
    }).catch(() => setError("Map unavailable. Use the coordinate fields below."));
    return () => { cancelled = true; map.current?.remove(); map.current = null; };
  }, []);
  useEffect(() => {
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !ready || !map.current || !layer.current) return;
      layer.current.clearLayers();
      const valid = coordinates.every(p => Number.isFinite(p.latitude) && Number.isFinite(p.longitude) && Math.abs(p.latitude) <= 90 && Math.abs(p.longitude) <= 180);
      if (!valid) return;
      const points = coordinates.map(p => [p.latitude, p.longitude] as Leaflet.LatLngTuple);
      if (points.length >= 3) L.polygon(points, { color: "#25d6ff", fillOpacity: .2 }).addTo(layer.current);
      else if (points.length > 1) L.polyline(points, { color: "#25d6ff" }).addTo(layer.current);
      points.forEach((point, index) => {
        const marker = L.marker(point, { draggable: editable, icon: L.divIcon({ className: "waypoint-marker-shell", html: `<span class="waypoint-marker"><b>${index + 1}</b></span>`, iconSize: [28, 28], iconAnchor: [14, 28] }) }).addTo(layer.current!);
        marker.on("dragend", () => {
          const position = marker.getLatLng();
          const current = values.current;
          current.onChange(current.coordinates.map((p, i) => i === index ? { latitude: Number(position.lat.toFixed(6)), longitude: Number(position.lng.toFixed(6)) } : p));
        });
      });
      if (!fitted.current && points.length) { map.current.fitBounds(L.latLngBounds(points), { padding: [30, 30], maxZoom: 15 }); fitted.current = true; }
    });
    return () => { cancelled = true; };
  }, [coordinates, editable, ready]);
  return <div className="mission-map-wrap"><div ref={container} className="mission-map" /><div className="map-hint">{editable ? "CLICK TO ADD · DRAG TO ADJUST" : "AIRSPACE BOUNDARY"} · {coordinates.length} VERTICES</div>{error && <p className="mission-map-error" role="alert">{error}</p>}</div>;
}
