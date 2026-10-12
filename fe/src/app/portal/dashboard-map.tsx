"use client";

import { useEffect, useRef } from "react";
import { Map, NavigationControl, ScaleControl, setWorkerUrl } from "maplibre-gl";
import { droneOpsMapStyle } from "@/lib/maps/style";

export default function DashboardMap() {
  const containerRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!containerRef.current) return;
    setWorkerUrl("/maplibre-gl-worker.mjs");
    const map = new Map({
      container: containerRef.current,
      style: droneOpsMapStyle(),
      center: [106.7725, 10.849],
      zoom: 12,
    });
    map.addControl(new NavigationControl(), "top-right");
    map.addControl(new ScaleControl({ unit: "metric" }), "bottom-left");
    map.on("error", () => {});
    return () => map.remove();
  }, []);
  return <div className="dashboard-map-shell"><div ref={containerRef} className="dashboard-live-map" /><div className="dashboard-airspace-source">OPENFREEMAP BASEMAP · LIVE UAV LAYER PENDING</div></div>;
}
