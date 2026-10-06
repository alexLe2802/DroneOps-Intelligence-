"use client";

import { useEffect, useRef } from "react";
import { GeoJSONSource, Map, Marker, NavigationControl, setWorkerUrl } from "maplibre-gl";
import { droneOpsMapStyle } from "@/lib/maps/style";
import type { MissionWaypoint } from "@/lib/portal/types";

function routeData(waypoints: MissionWaypoint[]) {
  return {
    type: "FeatureCollection" as const,
    features: waypoints.length > 1 ? [{
      type: "Feature" as const,
      properties: {},
      geometry: { type: "LineString" as const, coordinates: waypoints.map(point => [point.longitude, point.latitude]) },
    }] : [],
  };
}

export default function MissionMap({ waypoints, onAdd }: { waypoints: MissionWaypoint[]; onAdd: (longitude: number, latitude: number) => void }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Map | null>(null);
  const markerRef = useRef<Marker[]>([]);
  const waypointRef = useRef(waypoints);
  const onAddRef = useRef(onAdd);

  useEffect(() => { waypointRef.current = waypoints; }, [waypoints]);
  useEffect(() => { onAddRef.current = onAdd; }, [onAdd]);

  useEffect(() => {
    if (!containerRef.current) return;
    setWorkerUrl("/maplibre-gl-worker.mjs");
    const map = new Map({
      container: containerRef.current,
      style: droneOpsMapStyle(),
      center: [106.7725, 10.849],
      zoom: 13,
    });
    map.on("error", () => {});
    map.addControl(new NavigationControl(), "top-right");
    map.on("load", () => {
      map.addSource("mission-route", { type: "geojson", data: routeData(waypointRef.current) });
      map.addLayer({ id: "mission-route-line", type: "line", source: "mission-route", paint: { "line-color": "#25d6ff", "line-width": 4, "line-opacity": 0.9 } });
    });
    map.on("click", event => onAddRef.current(event.lngLat.lng, event.lngLat.lat));
    mapRef.current = map;
    return () => {
      markerRef.current.forEach(marker => marker.remove());
      map.remove();
      mapRef.current = null;
    };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    if (!map) return;
    const update = () => {
      (map.getSource("mission-route") as GeoJSONSource | undefined)?.setData(routeData(waypoints));
      markerRef.current.forEach(marker => marker.remove());
      markerRef.current = waypoints.map((point, index) => {
        const element = document.createElement("span");
        element.className = "waypoint-marker";
        const label = document.createElement("b");
        label.textContent = String(index + 1);
        element.append(label);
        return new Marker({ element }).setLngLat([point.longitude, point.latitude]).addTo(map);
      });
    };
    if (map.loaded()) update(); else map.once("load", update);
    return () => { map.off("load", update); };
  }, [waypoints]);

  return <div className="mission-map-wrap"><div ref={containerRef} className="mission-map" /><div className="map-hint">CLICK THE MAP TO ADD WAYPOINTS · {waypoints.length} POINTS</div></div>;
}
