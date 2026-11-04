// The all-zero startup position must not place the aircraft in the Gulf of Guinea.
// This check does not establish GPS fix quality; that requires receiver metadata.
export function hasTelemetryPosition(value: { latitude: number | null; longitude: number | null } | null | undefined): value is { latitude: number; longitude: number } {
  return Boolean(value && Number.isFinite(value.latitude) && Number.isFinite(value.longitude)
    && Math.abs(value.latitude!) <= 90 && Math.abs(value.longitude!) <= 180
    && (value.latitude !== 0 || value.longitude !== 0));
}
