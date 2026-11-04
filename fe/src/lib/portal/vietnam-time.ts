export const VIETNAM_TIME_ZONE = "Asia/Ho_Chi_Minh";
const offset = 7 * 60 * 60 * 1000;
type Instant = string | number | Date;

// API timestamps without a suffix are UTC; date-only fields remain calendar dates.
export function apiInstant(value: Instant): Date {
  if (value instanceof Date) return new Date(value.getTime());
  if (typeof value === "number") return new Date(value);
  if (/^\d{4}-\d{2}-\d{2}$/.test(value)) return new Date(`${value}T00:00:00+07:00`);
  return new Date(/(?:Z|[+-]\d{2}:?\d{2})$/i.test(value) ? value : `${value}Z`);
}
function format(value: Instant, options: Intl.DateTimeFormatOptions): string {
  const date = apiInstant(value);
  return Number.isFinite(date.getTime()) ? new Intl.DateTimeFormat("vi-VN", {
    timeZone: VIETNAM_TIME_ZONE, hourCycle: "h23", ...options,
  }).format(date) : "Unavailable";
}
export const formatVietnamDateTime = (value: Instant) => format(value, {
  day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", second: "2-digit",
}) + " (GMT+7)";
export const formatVietnamDate = (value: Instant) => format(value, { day: "2-digit", month: "2-digit", year: "numeric" });
export const formatVietnamTime = (value: Instant) => format(value, { hour: "2-digit", minute: "2-digit", second: "2-digit" });
export function toVietnamDateTimeInput(value: Instant): string {
  const date = apiInstant(value);
  return Number.isFinite(date.getTime()) ? new Date(date.getTime() + offset).toISOString().slice(0, 16) : "";
}
export function vietnamInputToUtcIso(value: string): string {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2})?$/.test(value)) throw new Error("Invalid Vietnam date/time.");
  const date = new Date(`${value}+07:00`);
  if (!Number.isFinite(date.getTime()) || toVietnamDateTimeInput(date) !== value.slice(0, 16)) throw new Error("Invalid Vietnam date/time.");
  return date.toISOString();
}
export function vietnamDateYearsAgo(years = 0, now: Instant = Date.now()): string {
  const date = new Date(apiInstant(now).getTime() + offset);
  date.setUTCFullYear(date.getUTCFullYear() - years);
  return date.toISOString().slice(0, 10);
}
