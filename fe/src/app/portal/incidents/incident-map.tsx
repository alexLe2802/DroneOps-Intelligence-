"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import styles from "./incident-management.module.css";

const DEFAULT_CENTER: Leaflet.LatLngTuple = [10.7769, 106.7009];

function hasCoordinates(latitude: number | null, longitude: number | null): latitude is number {
  return latitude !== null && longitude !== null && Number.isFinite(latitude) && Number.isFinite(longitude) && Math.abs(latitude) <= 90 && Math.abs(longitude) <= 180;
}

export default function IncidentMap({ latitude, longitude, uavCode }: { latitude: number | null; longitude: number | null; uavCode: string | null }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const markerRef = useRef<Leaflet.Marker | null>(null);
  const radiusRef = useRef<Leaflet.Circle | null>(null);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;
    let observer: ResizeObserver | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !containerRef.current) return;
      const map = L.map(containerRef.current, {
        center: DEFAULT_CENTER,
        zoom: 12,
        zoomControl: false,
        preferCanvas: false,
        zoomAnimation: false,
        fadeAnimation: false,
      });
      mapRef.current = map;
      L.control.zoom({ position: "bottomright" }).addTo(map);
      L.control.scale({ position: "bottomleft", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}", {
        attribution: "Tiles &copy; Esri",
        maxZoom: 19,
        crossOrigin: true,
      }).on("tileerror", () => setError("Basemap tiles are unavailable. The recorded coordinates remain visible.")).addTo(map);
      observer = new ResizeObserver(() => map.invalidateSize({ animate: false, pan: false }));
      observer.observe(containerRef.current);
      setReady(true);
    }).catch(reason => {
      if (!cancelled) setError(reason instanceof Error ? reason.message : "Incident map could not be loaded.");
    });
    return () => {
      cancelled = true;
      observer?.disconnect();
      markerRef.current = null;
      radiusRef.current = null;
      mapRef.current?.stop();
      mapRef.current?.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || !ready) return;
    markerRef.current?.remove();
    markerRef.current = null;
    radiusRef.current?.remove();
    radiusRef.current = null;
    if (!hasCoordinates(latitude, longitude)) {
      map.setView(DEFAULT_CENTER, 12, { animate: false });
      return;
    }
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map) return;
      const position: Leaflet.LatLngExpression = [latitude, longitude!];
      markerRef.current = L.marker(position, {
        zIndexOffset: 1000,
        icon: L.divIcon({
          className: styles.incidentMarkerShell,
          html: `<span class="${styles.incidentMarkerPulse}"></span><span class="${styles.incidentMarker}">${uavCode ? "UAV" : "!"}</span>`,
          iconSize: [54, 54],
          iconAnchor: [27, 27],
        }),
      }).addTo(map).bindTooltip(`${uavCode ?? "UAV"} / INCIDENT FIX`, { direction: "top", permanent: false });
      radiusRef.current = L.circle(position, { radius: 180, color: "#ff8c91", weight: 1, dashArray: "7 6", fillColor: "#ff7178", fillOpacity: .08 }).addTo(map);
      map.setView(position, 15, { animate: false });
    });
    return () => { cancelled = true; };
  }, [latitude, longitude, ready, uavCode]);

  const positioned = hasCoordinates(latitude, longitude);
  return <div className={styles.mapStage}>
    <div ref={containerRef} className={styles.mapCanvas} />
    <div className={styles.mapBanner}>{positioned ? `${uavCode ?? "UAV"} / RECORDED INCIDENT FIX` : "POSITION DATA UNAVAILABLE"}</div>
    <div className={styles.mapCoordinates}>{positioned ? `${latitude.toFixed(5)} N / ${longitude!.toFixed(5)} E` : "NO LATITUDE / LONGITUDE RETURNED"}</div>
    {!positioned && <div className={styles.mapEmpty}><b>NO SPATIAL FIX</b><span>This incident has no recorded coordinates.</span></div>}
    {error && <div className={styles.mapError} role="alert">{error}</div>}
  </div>;
}
