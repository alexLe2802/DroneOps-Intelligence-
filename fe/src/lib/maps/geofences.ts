import type { Geofence } from "@/lib/portal/types";

export type GeofenceCategory = "prohibited" | "restricted";

export function describeGeofence(zone: Geofence) {
  const text = `${zone.name} ${zone.description ?? ""}`.toLowerCase();
  const category: GeofenceCategory = /vùng cấm bay|prohibited|^vvp|mod-cb/.test(text) ? "prohibited" : "restricted";
  const labels: Record<GeofenceCategory, string> = {
    prohibited: "Prohibited airspace", restricted: "Restricted airspace",
  };
  const colors: Record<GeofenceCategory, string> = {
    prohibited: "#ef3340", restricted: "#facc15",
  };
  const limits = zone.description?.match(/Vertical limits:\s*(.*?)\s+to\s+(.*?)\./i);
  const limitLabel = limits && !/^unspecified$/i.test(limits[1]) && !/^unspecified$/i.test(limits[2])
    ? `Altitude limits: ${limits[1]} → ${limits[2]}`
    : category === "restricted"
      ? "Boundary restrictions apply; altitude limits are not specified by the source."
      : "Altitude limits are not specified by the source.";
  return { category, label: labels[category], color: colors[category], limitLabel };
}

export function geofenceGeoJson(zones: Geofence[]) {
  return {
    type: "FeatureCollection" as const,
    features: zones.map(zone => ({
      zone,
      coordinates: zone.coordinates.filter(point => Number.isFinite(point.longitude) && Number.isFinite(point.latitude)
        && point.longitude >= 100 && point.longitude <= 110 && point.latitude >= 7 && point.latitude <= 24),
    })).filter(item => item.coordinates.length >= 3).map(({ zone, coordinates }) => {
      const presentation = describeGeofence(zone);
      const ring = coordinates.map(point => [point.longitude, point.latitude]);
      const first = ring[0];
      const last = ring.at(-1);
      if (first && last && (first[0] !== last[0] || first[1] !== last[1])) ring.push([...first]);
      return {
        type: "Feature" as const,
        properties: { id: zone.id, name: zone.name, category: presentation.category, categoryLabel: presentation.label, limitLabel: presentation.limitLabel },
        geometry: { type: "Polygon" as const, coordinates: [ring] },
      };
    }),
  };
}
