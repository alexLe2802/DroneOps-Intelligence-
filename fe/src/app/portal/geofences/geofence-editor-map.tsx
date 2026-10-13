"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import { describeGeofence } from "@/lib/maps/geofences";
import type { Geofence, GeofenceCoordinate } from "@/lib/portal/types";

type Props = {
  coordinates: GeofenceCoordinate[];
  zones: Geofence[];
  editingId?: string;
  onChange: (coordinates: GeofenceCoordinate[]) => void;
};

export default function GeofenceEditorMap({ coordinates, zones, editingId, onChange }: Props) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | undefined>(undefined);
  const editLayerRef = useRef<Leaflet.LayerGroup | undefined>(undefined);
  const coordinatesRef = useRef(coordinates);
  const onChangeRef = useRef(onChange);
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
      const map = L.map(container, {
        center: [10.8231, 106.6297], zoom: 11, zoomControl: false, preferCanvas: true,
      });
      mapRef.current = map;
      L.control.zoom({ position: "bottomright" }).addTo(map);
      L.control.scale({ position: "bottomleft", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", {
        attribution: "Tiles &copy; Esri", maxZoom: 19, crossOrigin: true,
      }).on("tileerror", () => setError("Không thể tải lớp bản đồ nền.")).addTo(map);

      zones.forEach(zone => {
        if (zone.id === editingId || zone.coordinates.length < 3) return;
        const info = describeGeofence(zone);
        L.polygon(zone.coordinates.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple), {
          color: info.color, fillColor: info.color, weight: 1, opacity: .7, fillOpacity: .12, interactive: false,
        }).addTo(map);
      });

      editLayerRef.current = L.layerGroup().addTo(map);
      const initialCoordinates = coordinatesRef.current;
      if (initialCoordinates.length >= 2) {
        L.polyline(initialCoordinates.map(point => [point.latitude, point.longitude]), {
          color: "#22d3ee", weight: 2, dashArray: initialCoordinates.length < 3 ? "5 5" : undefined,
        }).addTo(editLayerRef.current);
      }
      if (initialCoordinates.length >= 3) {
        L.polygon(initialCoordinates.map(point => [point.latitude, point.longitude]), {
          color: "#22d3ee", fillColor: "#22d3ee", weight: 2, fillOpacity: .22,
        }).addTo(editLayerRef.current);
      }
      initialCoordinates.forEach((point, index) => {
        const marker = L.circleMarker([point.latitude, point.longitude], {
          radius: 8, color: "#07101a", weight: 2, fillColor: "#67e8f9", fillOpacity: 1,
        }).addTo(editLayerRef.current!);
        marker.bindTooltip(String(index + 1), { permanent: true, direction: "center", className: "geofence-point-label" });
      });
      map.on("click", event => {
        const next = [...coordinatesRef.current, {
          latitude: Number(event.latlng.lat.toFixed(6)), longitude: Number(event.latlng.lng.toFixed(6)),
        }];
        coordinatesRef.current = next;
        onChangeRef.current(next);
      });

      if (coordinatesRef.current.length >= 2) {
        map.fitBounds(L.latLngBounds(coordinatesRef.current.map(point => [point.latitude, point.longitude])), { padding: [32, 32], maxZoom: 15 });
      }
      resizeFrame = requestAnimationFrame(() => map.invalidateSize({ animate: false, pan: false }));
      setError("");
    }).catch(reason => setError(reason instanceof Error ? reason.message : "Không thể mở bản đồ."));

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
    if (!map || !group) return;
    void import("leaflet").then(({ default: L }) => {
      if (mapRef.current !== map || editLayerRef.current !== group) return;
      group.clearLayers();
      if (coordinates.length >= 2) {
        L.polyline(coordinates.map(point => [point.latitude, point.longitude]), {
          color: "#22d3ee", weight: 2, dashArray: coordinates.length < 3 ? "5 5" : undefined,
        }).addTo(group);
      }
      if (coordinates.length >= 3) {
        L.polygon(coordinates.map(point => [point.latitude, point.longitude]), {
          color: "#22d3ee", fillColor: "#22d3ee", weight: 2, fillOpacity: .22,
        }).addTo(group);
      }
      coordinates.forEach((point, index) => {
        const marker = L.circleMarker([point.latitude, point.longitude], {
          radius: 8, color: "#07101a", weight: 2, fillColor: "#67e8f9", fillOpacity: 1,
        }).addTo(group);
        marker.bindTooltip(String(index + 1), { permanent: true, direction: "center", className: "geofence-point-label" });
      });
    });
  }, [coordinates]);

  return <div className="geofence-editor-map-shell">
    <div ref={containerRef} className="geofence-editor-map" />
    <div className="geofence-map-instruction">Nhấp lên bản đồ để thêm điểm theo thứ tự · Tối thiểu 3 điểm</div>
    {error && <div className="dashboard-map-error" role="alert">{error}</div>}
  </div>;
}
