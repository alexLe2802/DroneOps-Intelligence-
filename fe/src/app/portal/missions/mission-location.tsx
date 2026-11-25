"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";

export function MissionLocation({ map, hasWaypoints }: { map: Leaflet.Map; hasWaypoints: boolean }) {
  const [request, setRequest] = useState(0);
  const [status, setStatus] = useState("");
  const [position, setPosition] = useState<GeolocationCoordinates | null>(null);

  const focusOnResult = useRef(!hasWaypoints);
  useEffect(() => {
    if (hasWaypoints) focusOnResult.current = false;
    const cancelFocus = () => { focusOnResult.current = false; };
    map.on("movestart click", cancelFocus);
    return () => { map.off("movestart click", cancelFocus); };
  }, [map, hasWaypoints]);

  useEffect(() => {
    let active = true;
    if (!navigator.geolocation) return;
    const locate = () => navigator.geolocation.getCurrentPosition(result => {
      if (!active) return;
      setPosition(result.coords);
      if (focusOnResult.current) map.setView([result.coords.latitude, result.coords.longitude], 16, { animate: false });
      setStatus("");
    }, error => {
      if (!active) return;
      setPosition(null);
      setStatus(error.code === 1 ? "Allow location access to show your position."
        : error.code === 3 ? "Location request timed out." : "Unable to determine your location.");
    }, { enableHighAccuracy: true, maximumAge: 30000, timeout: 15000 });
    if (request > 0) locate();
    else navigator.permissions?.query({ name: "geolocation" }).then(permission => {
      if (active && permission.state === "granted") locate();
    }).catch(() => { /* The location button also works without the Permissions API. */ });
    const deactivate = () => { active = false; };
    map.once("unload", deactivate);
    return () => { deactivate(); map.off("unload", deactivate); };
  }, [request, map]);

  useEffect(() => {
    if (!position) return;
    let active = true;
    let group: Leaflet.LayerGroup | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (!active) return;
      const point: Leaflet.LatLngTuple = [position.latitude, position.longitude];
      const accuracy = L.circle(point, { radius: position.accuracy, color: "#1977bf", weight: 1, fillOpacity: .12, interactive: false });
      const marker = L.circleMarker(point, { radius: 7, color: "#fff", fillColor: "#1977bf", fillOpacity: 1, weight: 2, interactive: false });
      marker.bindTooltip(`Your location · ±${Math.round(position.accuracy)} m`, { permanent: true, direction: "top" });
      group = L.layerGroup([accuracy, marker]).addTo(map);
    });
    return () => { active = false; group?.remove(); };
  }, [map, position]);

  return <div className="mission-location">
    <button type="button" disabled={status === "Locating…"} onClick={() => {
      if (!navigator.geolocation) {
        setStatus("Location is unavailable in this browser.");
        return;
      }
      focusOnResult.current = true;
      setStatus("Locating…");
      setPosition(null);
      setRequest(value => value + 1);
    }}>Show my location</button>
    <span role="status">{status}</span>
  </div>;
}
