import type { ReactNode } from "react";
import "./dashboard.css";
import "./portal-theme.css";
import "leaflet/dist/leaflet.css";
import "maplibre-gl/dist/maplibre-gl.css";

export default function PortalLayout({ children }: { children: ReactNode }) {
  return children;
}
