"use client";

import { useEffect, useRef } from "react";
import type * as Leaflet from "leaflet";

type Aircraft = { code: string; model: string; longitude: number; latitude: number; altitude: number; speed: number; battery: number; link: number };

const aircraft: Aircraft[] = [
  { code: "UAV-01", model: "M350 RTK", longitude: 106.7761, latitude: 10.8477, altitude: 120, speed: 14.2, battery: 82, link: 98 },
  { code: "UAV-02", model: "Inspire 3", longitude: 106.7708, latitude: 10.8522, altitude: 85, speed: 8.5, battery: 74, link: 94 },
];

const routeCoordinates: Leaflet.LatLngExpression[] = [
  [10.8462, 106.7658], [10.8484, 106.7681], [10.8505, 106.7712],
  [10.8496, 106.7737], [10.8477, 106.7761], [10.8501, 106.7790],
];

const restrictedCoordinates: Leaflet.LatLngExpression[] = [
  [10.8540, 106.7790], [10.8530, 106.7830], [10.8480, 106.7836], [10.8473, 106.7800],
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

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    let map: Leaflet.Map | undefined;
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled) return;
      map = L.map(container, { center: [10.849, 106.7725], zoom: 14, zoomControl: false, attributionControl: true });
      L.control.zoom({ position: "topright" }).addTo(map);
      L.control.scale({ position: "bottomleft", metric: true, imperial: false }).addTo(map);
      L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", { attribution: "&copy; OpenStreetMap contributors", maxZoom: 19, crossOrigin: true }).addTo(map);
      L.polyline(routeCoordinates, { color: "#29d7ff", weight: 4, dashArray: "10 7", opacity: 0.95 }).addTo(map);
      L.circle([10.8492, 106.7720], { radius: 780, color: "#57eac7", weight: 2, dashArray: "8 6", fillColor: "#38e1bc", fillOpacity: 0.08 })
        .bindTooltip("ZONE BRAVO GEOFENCE", { permanent: true, direction: "top", className: "geofence-tooltip" }).addTo(map);
      L.polygon(restrictedCoordinates, { color: "#ff8f96", weight: 2, fillColor: "#d8515d", fillOpacity: 0.3 })
        .bindTooltip("RESTRICTED FACILITY · MAX 120m AGL", { className: "restricted-tooltip" }).addTo(map);
      (manager ? aircraft : aircraft.slice(0, 1)).forEach(item => {
        L.marker([item.latitude, item.longitude], { icon: aircraftIcon(item, L) })
          .bindPopup(`${item.code} · ${item.model} · ALT ${item.altitude}m · BAT ${item.battery}% · LINK ${item.link}%`).addTo(map!);
      });
      window.requestAnimationFrame(() => map?.invalidateSize());
    });
    return () => { cancelled = true; map?.remove(); };
  }, [manager]);

  return <div className="dashboard-map-shell">
    <div id="dashboard-live-map" ref={containerRef} className="dashboard-live-map" aria-label="Live UAV operations map" />
    <div className="dashboard-map-status"><span>ZOOM LIVE · HDOP 0.82 · SAT 28 GNSS</span><span><b>●</b> ROUTE&nbsp;&nbsp; <i>●</i> GEOFENCE&nbsp;&nbsp; <em>●</em> RESTRICTED</span></div>
  </div>;
}
