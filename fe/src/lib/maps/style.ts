// OpenFreeMap is a MapLibre-compatible vector basemap that does not require
// an API key. Keep the URL in one place so production can replace it with a
// licensed or self-hosted style without changing map components.
export function droneOpsMapStyle(): string {
  return "https://tiles.openfreemap.org/styles/liberty";
}

/** Default map center — HCM City / FPT operational area (lng, lat). */
export const MAP_CENTER: [number, number] = [106.7725, 10.849];

/** Default zoom level for mission planning maps. */
export const MAP_ZOOM = 12;
