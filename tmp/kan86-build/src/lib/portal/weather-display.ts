import type { MissionWeather } from "./types";

type Point = MissionWeather["waypoints"][number];
const warningText = (text: string) => text.replace(/^WP\d+:\s*/, "");
const signature = (p: Point) => JSON.stringify([
  p.forecastTime, p.estimatedArrivalTime, p.arrivalTimeReason, p.sourceFetchedAt,
  p.temperatureCelsius, p.relativeHumidityPercent, p.windSpeedMetersPerSecond,
  p.windDirectionDegrees, p.windGustMetersPerSecond, p.windReferenceHeightMeters,
  p.precipitationProbabilityPercent, p.precipitationMillimeters, p.visibilityMeters,
  p.cloudCoverPercent, p.weatherCode, p.status, p.riskLevel,
  p.warnings.map(warningText).sort(),
]);
function distanceMeters(a: Point, b: Point) {
  if (![a.latitude, a.longitude, b.latitude, b.longitude].every(Number.isFinite)) return Infinity;
  const rad = (n: number) => n * Math.PI / 180;
  const h = Math.sin(rad(b.latitude - a.latitude) / 2) ** 2
    + Math.cos(rad(a.latitude)) * Math.cos(rad(b.latitude)) * Math.sin(rad(b.longitude - a.longitude) / 2) ** 2;
  return 6371000 * 2 * Math.asin(Math.sqrt(Math.min(1, h)));
}
export function groupWeatherWaypoints(points: Point[]) {
  const groups: { point: Point; sequences: number[] }[] = [];
  for (const point of [...points].sort((a, b) => a.sequenceOrder - b.sequenceOrder)) {
    const group = groups.find(g => signature(g.point) === signature(point) && distanceMeters(g.point, point) <= 1000);
    if (group) group.sequences.push(point.sequenceOrder);
    else groups.push({ point, sequences: [point.sequenceOrder] });
  }
  return groups;
}
export function weatherWarnings(weather: MissionWeather) {
  const representatives = new Map<number, number>();
  for (const group of groupWeatherWaypoints(weather.waypoints)) {
    for (const sequence of group.sequences) representatives.set(sequence, group.point.sequenceOrder);
  }
  return [...new Set(weather.warnings.map(text => text.replace(/^WP(\d+):/, (prefix, sequence) => {
    const representative = representatives.get(Number(sequence));
    return representative === undefined ? prefix : `WP${representative}:`;
  })))];
}
export function formatWeatherTime(value: string | null | undefined) {
  if (!value) return "Unavailable";
  // Backend forecast DateTimes are UTC, including ISO values without a suffix.
  const date = new Date(/(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`);
  if (!Number.isFinite(date.getTime())) return "Unavailable";
  return new Intl.DateTimeFormat("vi-VN", {
    timeZone: "Asia/Ho_Chi_Minh", day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23",
  }).format(date) + " (UTC+07:00)";
}
