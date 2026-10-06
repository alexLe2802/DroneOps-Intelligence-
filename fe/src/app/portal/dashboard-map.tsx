"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import type { Feature, GeoJsonObject } from "geojson";

type Aircraft = { code: string; model: string; longitude: number; latitude: number; altitude: number; speed: number; battery: number; link: number };

const aircraft: Aircraft[] = [
  { code: "UAV-01", model: "M350 RTK", longitude: 106.7761, latitude: 10.8477, altitude: 120, speed: 14.2, battery: 82, link: 98 },
  { code: "UAV-02", model: "Inspire 3", longitude: 106.7708, latitude: 10.8522, altitude: 85, speed: 8.5, battery: 74, link: 94 },
];

const routeCoordinates: Leaflet.LatLngExpression[] = [
  [10.8462, 106.7658], [10.8484, 106.7681], [10.8505, 106.7712],
  [10.8496, 106.7737], [10.8477, 106.7761], [10.8501, 106.7790],
];

function aircraftIcon(item: Aircraft, L: typeof Leaflet) {
  return L.divIcon({
    className: "leaflet-uav-icon",
    iconAnchor: [14, 14],
    html: `<span class="leaflet-uav-arrow">➤</span><span class="leaflet-uav-info"><b>${item.code} (${item.model})</b><small>ALT: ${item.altitude}m | ${item.speed} m/s<br>BAT: ${item.battery}% | LINK: ${item.link}%</small></span>`,
  });
}

export default function DashboardMap({ manager }: { manager: boolean }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [airspaceStatus, setAirspaceStatus] = useState("AIP ENR 5.1 · LOADING");

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    let map: Leaflet.Map | undefined;
    let cancelled = false;
    void Promise.all([
      import("leaflet"),
      fetch("/data/airspace-zones.geojson?v=20261006-3", { cache: "no-store" }).then(response => {
        if (!response.ok) throw new Error(`Airspace GeoJSON ${response.status}`);
        return response.json() as Promise<GeoJsonObject & { metadata?: { aipEffectiveDate?: string } }>;
      }),
    ]).then(([{ default: L }, airspace]) => {
      if (cancelled) return;
      map = L.map(container, { center: [10.849, 106.7725], zoom: 14, zoomControl: false, attributionControl: true });
      L.control.zoom({ position: "topright" }).addTo(map);
      L.control.scale({ position: "bottomleft", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { attribution: "&copy; OpenStreetMap contributors", maxZoom: 19, crossOrigin: true }).addTo(map);
      map.createPane("airspace");
      const airspacePane = map.getPane("airspace");
      if (airspacePane) airspacePane.style.zIndex = "310";
      L.geoJSON(airspace, {
        pane: "airspace",
        style: feature => {
          const properties = feature?.properties ?? {};
          const prohibited = properties.category === "prohibited";
          const supplementary = properties.reconciliation === "mod_only_supplement";
          return {
            color: prohibited ? "#ff0000" : "#facc15",
            fillColor: prohibited ? "#ff0000" : "#facc15",
            weight: supplementary ? 1 : 2,
            opacity: supplementary ? 0.65 : 0.95,
            fillOpacity: supplementary ? 0.2 : prohibited ? 0.58 : 0.38,
            dashArray: supplementary ? "5 5" : undefined,
          };
        },
        onEachFeature: (feature: Feature, layer) => {
          const properties = feature.properties ?? {};
          const popup = document.createElement("div");
          popup.className = "airspace-popup";
          const title = document.createElement("strong");
          title.textContent = `${properties.code ?? "AIRSPACE"} · ${properties.name ?? properties.categoryLabel ?? "Vùng trời"}`;
          const details = document.createElement("span");
          details.textContent = [
            properties.categoryLabel,
            properties.lowerLimit && `LOWER ${properties.lowerLimit}`,
            properties.upperLimit && `UPPER ${properties.upperLimit}`,
            properties.reconciliation === "mod_only_supplement" ? "Bổ sung từ cambay.mod.gov.vn" : `AIP hiệu lực ${properties.effectiveDate ?? "01/10/2026"}`,
          ].filter(Boolean).join(" · ");
          popup.append(title, details);
          layer.bindPopup(popup);
        },
      }).addTo(map);
      L.polyline(routeCoordinates, { color: "#29d7ff", weight: 4, dashArray: "10 7", opacity: 0.95 }).addTo(map);
      L.circle([10.8492, 106.7720], { radius: 780, color: "#57eac7", weight: 2, dashArray: "8 6", fillColor: "#38e1bc", fillOpacity: 0.08 })
        .bindTooltip("ZONE BRAVO GEOFENCE", { permanent: true, direction: "top", className: "geofence-tooltip" }).addTo(map);
      (manager ? aircraft : aircraft.slice(0, 1)).forEach(item => {
        L.marker([item.latitude, item.longitude], { icon: aircraftIcon(item, L) })
          .bindPopup(`${item.code} · ${item.model} · ALT ${item.altitude}m · BAT ${item.battery}% · LINK ${item.link}%`).addTo(map!);
      });
      setAirspaceStatus(`AIP ENR 5.1 · ${airspace.metadata?.aipEffectiveDate ?? "2026-10-01"} · MOD SYNC`);
      window.requestAnimationFrame(() => map?.invalidateSize());
    }).catch(error => {
      console.error("Unable to load dashboard airspace", error);
      setAirspaceStatus("AIRSPACE DATA UNAVAILABLE");
    });
    return () => { cancelled = true; map?.remove(); };
  }, [manager]);

  return <div className="dashboard-map-shell">
    <div id="dashboard-live-map" ref={containerRef} className="dashboard-live-map" aria-label="Live UAV operations map" />
    <div className="dashboard-airspace-source">{airspaceStatus}</div>
    <div className="dashboard-map-status"><span>ZOOM LIVE · HDOP 0.82 · SAT 28 GNSS</span><span><b>●</b> ROUTE&nbsp;&nbsp; <i>●</i> GEOFENCE&nbsp;&nbsp; <em>●</em> CẤM BAY&nbsp;&nbsp; <mark>●</mark> HẠN CHẾ / NGUY HIỂM</span></div>
  </div>;
}
