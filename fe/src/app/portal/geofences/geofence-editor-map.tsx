"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import { validCoordinate } from "@/lib/maps/planning-validation";
import { describeGeofence } from "@/lib/maps/geofences";
import type { Geofence, GeofenceCoordinate } from "@/lib/portal/types";

type Props = {
  disabled?: boolean;
  coordinates: GeofenceCoordinate[];
  zones: Geofence[];
  editingId?: string;
  onChange: (coordinates: GeofenceCoordinate[]) => void;
};

export default function GeofenceEditorMap({ coordinates, zones, editingId, onChange, disabled = false }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | undefined>(undefined);
  const editLayerRef = useRef<Leaflet.LayerGroup | undefined>(undefined);
  const coordinatesRef = useRef(coordinates);
  const onChangeRef = useRef(onChange);
  const disabledRef = useRef(disabled);
  const [ready, setReady] = useState<Leaflet.Map | null>(null);
  useEffect(() => { disabledRef.current = disabled; }, [disabled]);
  const [error, setError] = useState("");

  useEffect(() => { coordinatesRef.current = coordinates; }, [coordinates]);
  useEffect(() => { onChangeRef.current = onChange; }, [onChange]);

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    let cancelled = false;
    let resizeFrame: number | undefined;

    void import("leaflet").then(({ default: L }) => {
      if (cancelled) return;
      // SVG avoids queued Canvas redraws accessing a destroyed context on teardown.
      const map = L.map(container, {
        center: [10.8231, 106.6297], zoom: 11, zoomControl: false, preferCanvas: false,
      });
      mapRef.current = map;
      L.control.zoom({ position: "bottomright" }).addTo(map);
      L.control.scale({ position: "bottomleft", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", {
        attribution: "Tiles &copy; Esri", maxZoom: 19, crossOrigin: true,
      }).on("tileerror", () => setError("Unable to load the basemap.")).addTo(map);

      zones.forEach(zone => {
        if (zone.id === editingId || zone.coordinates.length < 3) return;
        const info = describeGeofence(zone);
        L.polygon(zone.coordinates.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple), {
          color: info.color, fillColor: info.color, weight: 1, opacity: .7, fillOpacity: .12, interactive: false,
        }).addTo(map);
      });

      editLayerRef.current = L.layerGroup().addTo(map);
      map.on("click", event => {
        if (disabledRef.current) return;
        const next = [...coordinatesRef.current, {
          latitude: Number(event.latlng.lat.toFixed(6)), longitude: Number(event.latlng.lng.toFixed(6)),
        }];
        coordinatesRef.current = next;
        onChangeRef.current(next);
      });

      if (coordinatesRef.current.length >= 2 && coordinatesRef.current.every(validCoordinate)) {
        map.fitBounds(L.latLngBounds(coordinatesRef.current.map(point => [point.latitude, point.longitude])), { padding: [32, 32], maxZoom: 15 });
      }
      resizeFrame = requestAnimationFrame(() => map.invalidateSize({ animate: false, pan: false }));
      setReady(map);
      setError("");
    }).catch(reason => setError(reason instanceof Error ? reason.message : "Unable to open the map."));

    return () => {
      cancelled = true;
      if (resizeFrame !== undefined) cancelAnimationFrame(resizeFrame);
      mapRef.current?.stop();
      mapRef.current?.remove();
      mapRef.current = undefined;
      editLayerRef.current = undefined;
    };
  }, [editingId, zones]);

  useEffect(() => {
    const map = mapRef.current;
    const group = editLayerRef.current;
    if (!map || !group || ready !== map) return;
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map || editLayerRef.current !== group) return;
      group.clearLayers();
      if (coordinates.length >= 2 && coordinates.every(validCoordinate)) {
        L.polyline(coordinates.map(point => [point.latitude, point.longitude]), {
          color: "#22d3ee", weight: 2, dashArray: coordinates.length < 3 ? "5 5" : undefined,
        }).addTo(group);
      }
      if (coordinates.length >= 3 && coordinates.every(validCoordinate)) {
        L.polygon(coordinates.map(point => [point.latitude, point.longitude]), {
          color: "#22d3ee", fillColor: "#22d3ee", weight: 2, fillOpacity: .22,
        }).addTo(group);
      }
      coordinates.forEach((point, index) => {
        if (!validCoordinate(point)) return;
        const marker = L.marker([point.latitude, point.longitude], {
          draggable: !disabled,
          icon: L.divIcon({ className: "waypoint-marker-shell", html: `<span class="waypoint-marker"><b>${index + 1}</b></span>`, iconSize: [28, 28], iconAnchor: [14, 28] }),
        }).addTo(group);
        marker.on("dragend", () => {
          if (disabledRef.current) return;
          const location = marker.getLatLng();
          const next = coordinatesRef.current.map((current, i) => i === index ? {
            latitude: Number(location.lat.toFixed(6)), longitude: Number(location.lng.toFixed(6)),
          } : current);
          coordinatesRef.current = next;
          onChangeRef.current(next);
        });
      });
    });
    return () => { cancelled = true; };
  }, [coordinates, ready, disabled]);

  return <div className="geofence-editor-map-shell">
    <div ref={containerRef} className="geofence-editor-map" />
    <div className="geofence-map-instruction">Click to add · Drag to adjust · At least 3 vertices</div>
    {error && <div className="dashboard-map-error" role="alert">{error}</div>}
  </div>;
}
