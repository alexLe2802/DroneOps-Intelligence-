import type { GeofenceCoordinate, MissionWaypoint } from "@/lib/portal/types";

export function validCoordinate(point: GeofenceCoordinate) {
  return Number.isFinite(point.latitude) && Math.abs(point.latitude) <= 90
    && Number.isFinite(point.longitude) && Math.abs(point.longitude) <= 180;
}

export function polygonError(points: GeofenceCoordinate[]): string | null {
  if (points.some(point => !validCoordinate(point))) return "Enter valid latitude (−90 to 90) and longitude (−180 to 180).";
  const ring = [...points];
  const same = (a: GeofenceCoordinate, b: GeofenceCoordinate) => a.latitude === b.latitude && a.longitude === b.longitude;
  if (ring.length > 1 && same(ring[0], ring[ring.length - 1])) ring.pop();
  if (ring.length < 3) return "An airspace boundary requires at least three distinct vertices.";
  if (new Set(ring.map(point => `${point.latitude},${point.longitude}`)).size !== ring.length) return "Each polygon vertex must be distinct.";
  const cross = (a: GeofenceCoordinate, b: GeofenceCoordinate, c: GeofenceCoordinate) =>
    (b.longitude - a.longitude) * (c.latitude - a.latitude) - (b.latitude - a.latitude) * (c.longitude - a.longitude);
  const between = (a: number, b: number, value: number) => value >= Math.min(a, b) && value <= Math.max(a, b);
  const onSegment = (a: GeofenceCoordinate, b: GeofenceCoordinate, c: GeofenceCoordinate) =>
    cross(a, b, c) === 0 && between(a.longitude, b.longitude, c.longitude) && between(a.latitude, b.latitude, c.latitude);
  for (let i = 0; i < ring.length; i++) {
    const a = ring[i], b = ring[(i + 1) % ring.length];
    for (let j = i + 1; j < ring.length; j++) {
      if (j === i + 1 || (i === 0 && j === ring.length - 1)) continue;
      const c = ring[j], d = ring[(j + 1) % ring.length];
      if ((cross(a, b, c) * cross(a, b, d) < 0 && cross(c, d, a) * cross(c, d, b) < 0)
        || onSegment(a, b, c) || onSegment(a, b, d) || onSegment(c, d, a) || onSegment(c, d, b)) return "Polygon edges must not cross or overlap.";
    }
  }
  const area = ring.reduce((sum, point, i) => {
    const next = ring[(i + 1) % ring.length];
    return sum + (point.longitude - ring[0].longitude) * (next.latitude - ring[0].latitude)
      - (next.longitude - ring[0].longitude) * (point.latitude - ring[0].latitude);
  }, 0);
  return Math.abs(area) < 1e-12 ? "The boundary must enclose an area; vertices cannot all be on one line." : null;
}

export function routeError(waypoints: MissionWaypoint[]): string | null {
  if (waypoints.length < 2) return "Add at least two waypoints to define a flight route.";
  if (waypoints.some(point => !validCoordinate(point))) return "Waypoint coordinates are outside the valid latitude or longitude range.";
  if (waypoints.some(point => point.altitude == null || !Number.isFinite(point.altitude) || point.altitude < 0)) return "Waypoint altitude must be a finite, non-negative number.";
  return null;
}
