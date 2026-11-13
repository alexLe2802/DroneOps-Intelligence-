"use client";

import { useEffect, useRef, useState } from "react";
import type * as Leaflet from "leaflet";
import { describeGeofence } from "@/lib/maps/geofences";
import type { Geofence, MissionWaypoint, MissionValidationResult, PlannedHome, MissionFence, GeofenceCoordinate } from "@/lib/portal/types";

const valid = (latitude: number, longitude: number) => longitude >= 100 && longitude <= 110 && latitude >= 7 && latitude <= 24;

export default function MissionMap({ waypoints, geofences, onAdd, onUpdate, readOnly = false, findings, plannedHome = waypoints[0]?.navigation?.plannedHome ?? null, pickingHome = false, missionFences = waypoints[0]?.navigation?.missionFences ?? [], draftFence = [], drawingFence = null, onFenceUpdate }: {
  missionFences?: MissionFence[]; draftFence?: GeofenceCoordinate[]; drawingFence?: "Polygon" | "Circle" | null; onFenceUpdate?: (fence: MissionFence) => void;
  plannedHome?: PlannedHome | null; pickingHome?: boolean;
  waypoints: MissionWaypoint[]; geofences: Geofence[];
  onAdd?: (longitude: number, latitude: number) => void;
  onUpdate?: (index: number, patch: Partial<MissionWaypoint>) => void;
  readOnly?: boolean;
  findings?: MissionValidationResult["findings"];
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const mapRef = useRef<Leaflet.Map | null>(null);
  const highlightRef = useRef<Leaflet.Polyline[]>([]);
  const routeRef = useRef<Leaflet.Polyline | null>(null);
  const markersRef = useRef<Leaflet.Marker[]>([]);
  const readOnlyRef = useRef(readOnly);
  const fenceUpdateRef = useRef(onFenceUpdate);
  useEffect(() => { fenceUpdateRef.current = onFenceUpdate; }, [onFenceUpdate]);
  const onAddRef = useRef(onAdd);
  const onUpdateRef = useRef(onUpdate);
  const [error, setError] = useState("");
  const [mapReady, setMapReady] = useState<Leaflet.Map | null>(null);
  useEffect(() => { readOnlyRef.current = readOnly; }, [readOnly]);
  useEffect(() => { onAddRef.current = onAdd; }, [onAdd]);
  useEffect(() => { onUpdateRef.current = onUpdate; }, [onUpdate]);

  useEffect(() => {
    if (!containerRef.current) return;
    let cancelled = false;
    let resizeFrame: number | undefined;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || !containerRef.current) return;
      // SVG avoids queued Canvas redraws accessing a destroyed context on teardown.
      const map = L.map(containerRef.current, { center: [10.849, 106.7725], zoom: 12, wheelPxPerZoomLevel: 240, wheelDebounceTime: 80, zoomSnap: 0.25, preferCanvas: false, zoomAnimation: false, fadeAnimation: false, markerZoomAnimation: false });
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
      map.on("click", event => { if (!readOnlyRef.current) onAddRef.current?.(event.latlng.lng, event.latlng.lat); });
      resizeFrame = requestAnimationFrame(() => {
        if (!cancelled) map.invalidateSize({ animate: false, pan: false });
      });
      setMapReady(map);
      setError("");
    }).catch(reason => { if (!cancelled) setError(reason instanceof Error ? reason.message : "Map could not be loaded."); });
    return () => {
      cancelled = true;
      if (resizeFrame !== undefined) cancelAnimationFrame(resizeFrame);
      highlightRef.current.forEach(line => line.remove()); highlightRef.current = [];
      markersRef.current.forEach(marker => marker.remove());
      markersRef.current = [];
      routeRef.current = null;
      mapRef.current?.stop();
      mapRef.current?.remove();
      mapRef.current = null;
    };
  }, [geofences]);

  useEffect(() => {
    const map = mapRef.current;
    if (!map || mapReady !== map) return;
    let cancelled = false;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== map) return;
      highlightRef.current.forEach(line => line.remove()); highlightRef.current = [];
      markersRef.current.forEach(marker => marker.remove());
      markersRef.current = [];
      if (routeRef.current && map.hasLayer(routeRef.current)) routeRef.current.remove();
      routeRef.current = null;
      if (waypoints.some(point => !Number.isFinite(point.latitude) || Math.abs(point.latitude) > 90 || !Number.isFinite(point.longitude) || Math.abs(point.longitude) > 180)) return;
      markersRef.current = waypoints.map((point, index) => {
        const marker = L.marker([point.latitude, point.longitude], {
          draggable: !readOnly,
          icon: L.divIcon({ className: "waypoint-marker-shell", html: `<span class="waypoint-marker" ${findings?.some(f => f.waypointSequence === point.sequenceOrder) ? 'style="background:#ef3340;border-color:#ef3340"' : ""}><b>${index + 1}</b></span>`, iconSize: [28, 28], iconAnchor: [14, 28] }),
        }).bindTooltip(point.actionType === "TakeOff" ? "Takeoff/Home" : point.actionType === "Return" ? "Return/RTL · visit this point, then return to FC Home" : point.actionType).addTo(map);
        if (!readOnly) marker.on("dragend", () => { if (readOnlyRef.current) return; const value = marker.getLatLng(); onUpdateRef.current?.(index, { latitude: Number(value.lat.toFixed(6)), longitude: Number(value.lng.toFixed(6)) }); });
        return marker;
      });
      if (plannedHome && Number.isFinite(plannedHome.latitude) && Number.isFinite(plannedHome.longitude)) {
        const homeMarker = L.marker([plannedHome.latitude, plannedHome.longitude], {
          icon: L.divIcon({ className: "waypoint-marker-shell", html: '<span class="waypoint-marker" style="background:#ffc857;color:#101a27;border-color:#fff"><b>H</b></span>', iconSize: [30, 30], iconAnchor: [15, 30] }),
        }).bindTooltip("Planned Home").addTo(map);
        markersRef.current.push(homeMarker);
      }
      if (waypoints.length > 1) {
        const positions: Leaflet.LatLngTuple[] = waypoints.map(point => [point.latitude, point.longitude]);
        if (waypoints.at(-1)?.actionType === "Return") positions.push([waypoints[0].latitude, waypoints[0].longitude]);
        routeRef.current = L.polyline(positions, { color: "#25d6ff", weight: 4, opacity: .95 }).addTo(map);
      }
      for (const finding of findings ?? []) {
        if (finding.segmentFrom == null || finding.segmentTo == null) continue;
        const first = waypoints.find(point => point.sequenceOrder === finding.segmentFrom), last = waypoints.find(point => point.sequenceOrder === finding.segmentTo);
        if (first && last) { const note = document.createElement("span"); note.textContent = finding.reason; highlightRef.current.push(L.polyline([[first.latitude, first.longitude], [last.latitude, last.longitude]], { color: "#ef3340", weight: 6 }).bindTooltip(note).addTo(map)); }
      }
      if (waypoints.length > 0) {
        const bounds = L.latLngBounds(waypoints.map(point => [point.latitude, point.longitude] as Leaflet.LatLngTuple));
        map.fitBounds(bounds, { padding: [44, 44], maxZoom: 16, animate: false });
      }
    });
    return () => { cancelled = true; };
  }, [waypoints, mapReady, readOnly, findings, plannedHome]);

  useEffect(() => {
    if (!mapReady) return;
    let cancelled = false;
    let layer: Leaflet.LayerGroup | null = null;
    void import("leaflet").then(({ default: L }) => {
      if (cancelled || mapRef.current !== mapReady) return;
      layer = L.layerGroup().addTo(mapReady);
      const color = "#58e6b0";
      const handle = () => L.divIcon({className:"",html:'<span style="display:block;width:16px;height:16px;border:3px solid #fff;border-radius:50%;background:#14785e;box-shadow:0 1px 6px #0008"></span>',iconSize:[16,16],iconAnchor:[8,8]});
      const latlng = (p: GeofenceCoordinate): Leaflet.LatLngTuple => [p.latitude, p.longitude];
      for (const fence of missionFences) {
        if (fence.shape === "Circle" && fence.center && Number.isFinite(fence.center.latitude) && Number.isFinite(fence.center.longitude) && Number.isFinite(fence.radiusMeters) && fence.radiusMeters! > 0) {
          L.circle(latlng(fence.center), {radius: fence.radiusMeters!,color,fillOpacity:.1,weight:2}).bindTooltip("Permitted circle").addTo(layer);
          if(!readOnly) L.marker(latlng(fence.center),{draggable:true,icon:handle()}).bindTooltip("Drag fence center").on("dragend",event=>{
            if(readOnlyRef.current) return;
            const p=(event.target as Leaflet.Marker).getLatLng();
            fenceUpdateRef.current?.({...fence,center:{latitude:p.lat,longitude:p.lng}});
          }).addTo(layer);
        } else if(fence.shape === "Polygon" && fence.vertices?.length && fence.vertices.every(p=>Number.isFinite(p.latitude)&&Number.isFinite(p.longitude))) {
          L.polygon(fence.vertices.map(latlng),{color,fillOpacity:.1,weight:2}).bindTooltip("Permitted polygon").addTo(layer);
          if(!readOnly) fence.vertices.forEach((p,i)=> L.marker(latlng(p),{draggable:true,icon:handle()}).bindTooltip(`Fence vertex ${i+1}`).on("dragend",event=>{
            if(readOnlyRef.current) return;
            const location=(event.target as Leaflet.Marker).getLatLng();
            fenceUpdateRef.current?.({...fence,vertices:fence.vertices!.map((v,j)=>j===i?{latitude:location.lat,longitude:location.lng}:v)});
          }).addTo(layer!));
        }
      }
      if(draftFence.length) {
        L.polyline(draftFence.map(latlng),{color:"#ffc857",dashArray:"6 4"}).addTo(layer);
        draftFence.forEach(p=>L.circleMarker(latlng(p),{radius:5,color:"#ffc857"}).addTo(layer!));
      }
    });
    return ()=>{cancelled=true;layer?.remove();};
  }, [mapReady, missionFences, draftFence, readOnly]);

  return <div className="mission-map-wrap">
    <div ref={containerRef} className="mission-map" />
    <div className="map-hint">{readOnly ? "ROUTE PREVIEW" : drawingFence ? `DRAW ${drawingFence.toUpperCase()} FENCE` : pickingHome ? "CLICK TO SET PLANNED HOME" : "CLICK TO ADD · DRAG TO ADJUST"} · {waypoints.length} WAYPOINTS</div>
    <div className="airspace-map-legend"><span className="prohibited">Vùng cấm bay</span><span className="restricted">Vùng giới hạn bay</span></div>
    {error && <div className="mission-map-error" role="alert">{error}</div>}
  </div>;
}
