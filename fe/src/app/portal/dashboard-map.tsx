"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import { describeGeofence } from "@/lib/maps/geofences";
import type { Geofence } from "@/lib/portal/types";

const valid = (latitude: number, longitude: number) => longitude >= 100 && longitude <= 110 && latitude >= 7 && latitude <= 24;

type CursorCoordinate = { latitude: number; longitude: number } | null;
type UserPosition = { latitude: number; longitude: number; accuracy: number };

export default function DashboardMap({ geofences, onCursorCoordinate }: { geofences: Geofence[]; onCursorCoordinate?: (coordinate: CursorCoordinate) => void }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const leafletRef = useRef<typeof Leaflet | null>(null);
  const locationLayerRef = useRef<Leaflet.FeatureGroup | null>(null);
  const positionRef = useRef<UserPosition | null>(null);
  const [error, setError] = useState("");
  const [locationError, setLocationError] = useState("");
  const [locating, setLocating] = useState(false);
  const [mapReady, setMapReady] = useState(false);

  const displayPosition = useCallback((L: typeof Leaflet, map: Leaflet.Map, position: UserPosition, focus = false) => {
    locationLayerRef.current?.remove();
    const coordinates: Leaflet.LatLngTuple = [position.latitude, position.longitude];
    const theme = getComputedStyle(containerRef.current!);
    const accent = theme.getPropertyValue("--color-accent").trim();
    const ink = theme.getPropertyValue("--color-ink").trim();
    const accuracy = L.circle(coordinates, { radius: position.accuracy, color: accent, weight: 1, fillOpacity: .12, interactive: false });
    const marker = L.circleMarker(coordinates, { radius: 8, color: ink, weight: 2, fillColor: accent, fillOpacity: 1 });
    const label = document.createElement("span");
    label.textContent = `Your current location (accuracy ±${Math.round(position.accuracy)} m)`;
    marker.bindPopup(label);
    locationLayerRef.current = L.featureGroup([accuracy, marker]).addTo(map);
    if (focus) {
      map.fitBounds(accuracy.getBounds(), { maxZoom: 16, padding: [40, 40], animate: false });
      marker.openPopup();
    } else {
      map.panTo(coordinates, { animate: false });
    }
  }, []);

  const locateUser = useCallback((focus = true) => {
    const requestedMap = mapRef.current;
    if (!requestedMap) return;
    setLocationError("");
    if (!navigator.geolocation) {
      setLocationError("This browser does not support location access.");
      return;
    }
    setLocating(true);
    navigator.geolocation.getCurrentPosition(position => {
      const map = mapRef.current;
      const L = leafletRef.current;
      if (!map || !L || map !== requestedMap) return;
      const current = { latitude: position.coords.latitude, longitude: position.coords.longitude, accuracy: position.coords.accuracy };
      positionRef.current = current;
      displayPosition(L, map, current, focus);
      setLocating(false);
    }, reason => {
      if (mapRef.current !== requestedMap) return;
      setLocating(false);
      setLocationError(reason.code === 1 ? "Location access unavailable."
        : reason.code === 3 ? "Location request timed out."
          : "Unable to determine your location.");
    }, { enableHighAccuracy: true, timeout: 15000, maximumAge: 30000 });
  }, [displayPosition]);

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    let map: Leaflet.Map | undefined;
    let cancelled = false;
    let resizeFrame: number | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled) return;
      // SVG avoids queued Canvas redraws accessing a destroyed context on teardown.
      map = L.map(container, { center: [10.8231, 106.6297], zoom: 11, zoomControl: false, preferCanvas: false });
      mapRef.current = map;
      leafletRef.current = L;
      setMapReady(true);
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
      if (positionRef.current) displayPosition(L, map, positionRef.current);
      else locateUser(false);
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
      mapRef.current = null;
      locationLayerRef.current = null;
      map = undefined;
    };
  }, [geofences, onCursorCoordinate, displayPosition, locateUser]);

  return <div className="dashboard-map-shell">
    <div ref={containerRef} className="dashboard-live-map" />
    <div className="airspace-map-legend"><span className="prohibited">Vùng cấm bay</span><span className="restricted">Vùng giới hạn bay</span></div>
    {error && <div className="dashboard-map-error" role="alert">{error}</div>}
    <div className="dashboard-map-toolbar">
      {locationError && <p className="dashboard-location-error" role="alert">{locationError}</p>}
      <button className="dashboard-locate-button" type="button" onClick={() => locateUser()} disabled={!mapReady || locating} aria-label="Show my current location on the map">{locating ? "Locating…" : "My location"}</button>
    </div>
  </div>;
}
