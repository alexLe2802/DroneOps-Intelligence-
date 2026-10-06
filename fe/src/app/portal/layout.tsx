import type { ReactNode } from "react";
import "./dashboard.css";
import "maplibre-gl/dist/maplibre-gl.css";
import "leaflet/dist/leaflet.css";

export default function PortalLayout({ children }: { children: ReactNode }) {
  return children;
}
