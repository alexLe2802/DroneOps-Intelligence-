"use client";

import { useRef, useState, type ComponentProps } from "react";
import dynamic from "next/dynamic";
import MissionMap2D from "./mission-map-2d";

// Keep the existing Leaflet map as the default. Load WebGL only on demand.
const MissionMap3D = dynamic(() => import("./mission-map-3d"), { ssr: false });
export type MapView = { longitude: number; latitude: number; zoom: number };
export type MapViewRef = { current: MapView | null };
export type MissionMapProps = Omit<ComponentProps<typeof MissionMap2D>, "viewRef">;

export default function MissionMap(props: MissionMapProps) {
  const [mode, setMode] = useState<"2d" | "3d">("2d");
  const viewRef = useRef<MapView | null>(null);
  return <div className="mission-map-3d-wrap">
    {mode === "2d"
      ? <MissionMap2D {...props} viewRef={viewRef} />
      : <MissionMap3D {...props} viewRef={viewRef} />}
    <div className="map-toolbar-3d" role="group" aria-label="Map view mode">
      <div className="map-mode-toggle">
        <button type="button" aria-pressed={mode === "2d"} onClick={() => setMode("2d")}>2D</button>
        <button type="button" aria-pressed={mode === "3d"} onClick={() => setMode("3d")}>3D</button>
      </div>
    </div>
  </div>;
}
